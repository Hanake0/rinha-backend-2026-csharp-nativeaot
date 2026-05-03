using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

ReplayOptions options = ReplayOptions.Parse(args);
ReplayEntry[] entries = LoadEntries(options.TestDataPath, options.StartIndex, options.Limit);
HttpClient client = CreateClient(options);
RequestResult[] results = new RequestResult[entries.Length];
Stopwatch wallClock = Stopwatch.StartNew();

await Parallel.ForEachAsync(
	Enumerable.Range(0, entries.Length),
	new ParallelOptions {
		MaxDegreeOfParallelism = options.Concurrency,
	},
	async (entryOffset, cancellationToken) => {
		ReplayEntry entry = entries[entryOffset];
		long requestStart = Stopwatch.GetTimestamp();

		using ByteArrayContent content = new(entry.Payload);
		content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

		try {
			using HttpResponseMessage response = await client.PostAsync(options.Url, content, cancellationToken);
			byte[] responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
			long requestEnd = Stopwatch.GetTimestamp();

			bool? approved = null;
			double? fraudScore = null;

			if (response.StatusCode == HttpStatusCode.OK && TryParseApproved(responseBody, out bool parsedApproved, out double parsedFraudScore)) {
				approved = parsedApproved;
				fraudScore = parsedFraudScore;
			}

			results[entryOffset] = new RequestResult(
				entry.Index,
				entry.ExpectedApproved,
				(int)response.StatusCode,
				approved,
				fraudScore,
				requestEnd - requestStart,
				(response.StatusCode == HttpStatusCode.OK) ? null : GetBodyPreview(responseBody));
		} catch (Exception exception) {
			long requestEnd = Stopwatch.GetTimestamp();
			results[entryOffset] = new RequestResult(
				entry.Index,
				entry.ExpectedApproved,
				StatusCode: 0,
				Approved: null,
				FraudScore: null,
				requestEnd - requestStart,
				exception.GetType().Name + ": " + exception.Message);
		}
	});

wallClock.Stop();

ReplaySummary summary = BuildSummary(options, entries.Length, results, wallClock.Elapsed);
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions {
	WriteIndented = true,
}));

static ReplaySummary BuildSummary(
	ReplayOptions options,
	int requestCount,
	RequestResult[] results,
	TimeSpan wallClockElapsed) {
	List<RequestMismatch> mismatches = [];
	long[] latencyTicks = new long[results.Length];
	int truePositiveCount = 0;
	int trueNegativeCount = 0;
	int falsePositiveCount = 0;
	int falseNegativeCount = 0;
	int httpErrorCount = 0;

	for (int index = 0; index < results.Length; index++) {
		RequestResult result = results[index];
		latencyTicks[index] = result.LatencyTicks;

		if (result.StatusCode != 200 || !result.Approved.HasValue) {
			httpErrorCount++;

			if (mismatches.Count < options.MismatchLimit) {
				mismatches.Add(new RequestMismatch(
					result.Index,
					result.ExpectedApproved,
					result.Approved,
					result.StatusCode,
					result.FraudScore,
					result.Error));
			}

			continue;
		}

		bool approved = result.Approved.Value;

		if (approved == result.ExpectedApproved) {
			if (approved) {
				trueNegativeCount++;
			} else {
				truePositiveCount++;
			}
		} else {
			if (approved) {
				falseNegativeCount++;
			} else {
				falsePositiveCount++;
			}

			if (mismatches.Count < options.MismatchLimit) {
				mismatches.Add(new RequestMismatch(
					result.Index,
					result.ExpectedApproved,
					approved,
					result.StatusCode,
					result.FraudScore,
					result.Error));
			}
		}
	}

	Array.Sort(latencyTicks);

	return new ReplaySummary(
		new ReplaySettings(
			options.Url,
			options.TestDataPath,
			options.StartIndex,
			requestCount,
			options.Concurrency,
			options.TimeoutMs,
			options.MismatchLimit),
		new ReplayBreakdown(
			truePositiveCount,
			trueNegativeCount,
			falsePositiveCount,
			falseNegativeCount,
			httpErrorCount),
		new ReplayLatency(
			ToMicroseconds(latencyTicks[0]),
			ToMicroseconds(GetPercentileTicks(latencyTicks, 0.50d)),
			ToMicroseconds(GetPercentileTicks(latencyTicks, 0.95d)),
			ToMicroseconds(GetPercentileTicks(latencyTicks, 0.99d)),
			ToMicroseconds(latencyTicks[^1]),
			ToMicroseconds((long)Math.Round(latencyTicks.Average())),
			Math.Round(requestCount / wallClockElapsed.TotalSeconds, 2)),
		mismatches);
}

static HttpClient CreateClient(ReplayOptions options) {
	SocketsHttpHandler handler = new() {
		AutomaticDecompression = DecompressionMethods.None,
		ConnectTimeout = TimeSpan.FromMilliseconds(options.TimeoutMs),
		MaxConnectionsPerServer = options.Concurrency,
		PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
		PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
		UseCookies = false,
	};

	return new HttpClient(handler) {
		Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs),
	};
}

static string GetBodyPreview(byte[] body) {
	if (body.Length == 0) {
		return string.Empty;
	}

	string text = System.Text.Encoding.UTF8.GetString(body);
	return text.Length <= 256 ? text : text[..256];
}

static long GetPercentileTicks(long[] sortedTicks, double percentile) {
	int index = (int)Math.Ceiling(sortedTicks.Length * percentile) - 1;
	return sortedTicks[Math.Clamp(index, 0, sortedTicks.Length - 1)];
}

static ReplayEntry[] LoadEntries(string testDataPath, int startIndex, int limit) {
	using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(testDataPath));
	JsonElement.ArrayEnumerator entries = document.RootElement.GetProperty("entries").EnumerateArray();
	List<ReplayEntry> selectedEntries = [];
	int index = 0;
	int remaining = (limit > 0) ? limit : int.MaxValue;

	foreach (JsonElement entry in entries) {
		if (index < startIndex) {
			index++;
			continue;
		}

		if (remaining == 0) {
			break;
		}

		byte[] payload = System.Text.Encoding.UTF8.GetBytes(entry.GetProperty("request").GetRawText());
		bool expectedApproved = entry.GetProperty("expected_approved").GetBoolean();
		selectedEntries.Add(new ReplayEntry(index, expectedApproved, payload));
		index++;
		remaining--;
	}

	return [.. selectedEntries];
}

static double ToMicroseconds(long ticks) => Math.Round((ticks * 1_000_000d) / Stopwatch.Frequency, 3);

static bool TryParseApproved(ReadOnlySpan<byte> responseBody, out bool approved, out double fraudScore) {
	approved = default;
	fraudScore = default;

	Utf8JsonReader reader = new(responseBody, isFinalBlock: true, state: default);
	bool hasApproved = false;
	bool hasFraudScore = false;

	while (reader.Read()) {
		if (reader.TokenType != JsonTokenType.PropertyName) {
			continue;
		}

		if (reader.ValueTextEquals("approved"u8)) {
			if (!reader.Read() || ((reader.TokenType != JsonTokenType.True) && (reader.TokenType != JsonTokenType.False))) {
				return false;
			}

			approved = reader.GetBoolean();
			hasApproved = true;
		} else if (reader.ValueTextEquals("fraud_score"u8)) {
			if (!reader.Read() || (reader.TokenType != JsonTokenType.Number) || !reader.TryGetDouble(out fraudScore)) {
				return false;
			}

			hasFraudScore = true;
		} else if (!reader.Read()) {
			return false;
		}
	}

	return hasApproved && hasFraudScore;
}

internal sealed record ReplayEntry(int Index, bool ExpectedApproved, byte[] Payload);

internal sealed record RequestMismatch(
	int Index,
	bool ExpectedApproved,
	bool? ActualApproved,
	int StatusCode,
	double? FraudScore,
	string? Error);

internal sealed record ReplaySummary(
	ReplaySettings Settings,
	ReplayBreakdown Breakdown,
	ReplayLatency Latency,
	List<RequestMismatch> Mismatches);

internal sealed record ReplaySettings(
	string Url,
	string TestDataPath,
	int StartIndex,
	int RequestCount,
	int Concurrency,
	int TimeoutMs,
	int MismatchLimit);

internal sealed record ReplayBreakdown(
	int TruePositiveDetections,
	int TrueNegativeDetections,
	int FalsePositiveDetections,
	int FalseNegativeDetections,
	int HttpErrors);

internal sealed record ReplayLatency(
	double MinUs,
	double P50Us,
	double P95Us,
	double P99Us,
	double MaxUs,
	double MeanUs,
	double ThroughputPerSecond);

internal sealed record RequestResult(
	int Index,
	bool ExpectedApproved,
	int StatusCode,
	bool? Approved,
	double? FraudScore,
	long LatencyTicks,
	string? Error);

internal sealed class ReplayOptions {
	private ReplayOptions(
		string url,
		string testDataPath,
		int startIndex,
		int limit,
		int concurrency,
		int timeoutMs,
		int mismatchLimit) {
		this.Url = url;
		this.TestDataPath = testDataPath;
		this.StartIndex = startIndex;
		this.Limit = limit;
		this.Concurrency = concurrency;
		this.TimeoutMs = timeoutMs;
		this.MismatchLimit = mismatchLimit;
	}

	public int Concurrency { get; }

	public int Limit { get; }

	public int MismatchLimit { get; }

	public int StartIndex { get; }

	public string TestDataPath { get; }

	public int TimeoutMs { get; }

	public string Url { get; }

	public static ReplayOptions Parse(string[] args) {
		string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		string defaultTestDataPath = Path.GetFullPath(Path.Combine(repoRoot, "..", "rinha-de-backend-2026", "test", "test-data.json"));
		Dictionary<string, string> values = ParseArguments(args);

		string url = GetValue(values, "--url", null)
			?? throw new ArgumentException("Missing required --url value.");

		return new ReplayOptions(
			url,
			Path.GetFullPath(GetValue(values, "--test-data", defaultTestDataPath)!),
			int.Parse(GetValue(values, "--start-index", "0")!, CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--limit", "0")!, CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--concurrency", "1")!, CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--timeout-ms", "2001")!, CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--mismatch-limit", "32")!, CultureInfo.InvariantCulture));
	}

	private static string? GetValue(Dictionary<string, string> values, string key, string? fallback) =>
		values.TryGetValue(key, out string? value) ? value : fallback;

	private static Dictionary<string, string> ParseArguments(string[] args) {
		Dictionary<string, string> values = new(StringComparer.Ordinal);

		for (int index = 0; index < args.Length; index++) {
			string argument = args[index];

			if (!argument.StartsWith("--", StringComparison.Ordinal)) {
				throw new ArgumentException($"Unexpected positional argument '{argument}'.");
			}

			if ((index + 1) >= args.Length) {
				throw new ArgumentException($"Missing value for '{argument}'.");
			}

			values[argument] = args[++index];
		}

		return values;
	}
}
