using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;
using Rinha2026.Core.Detection;
using Rinha2026.Core.Model;
using Rinha2026.Core.Parsing;
using Rinha2026.Core.Search;
using Rinha2026.Core.Vectorization;

EvaluatorOptions options = EvaluatorOptions.Parse(args);
RuntimeConfig runtimeConfig = new(
	new RuntimeDetectionConfig(options.TopK, options.ApprovalThreshold),
	new RuntimeDatasetConfig(
		Path.Combine(options.RuntimeDataRoot, "index"),
		Path.Combine(options.RuntimeDataRoot, "mcc_risk.json"),
		Path.Combine(options.RuntimeDataRoot, "normalization.json")),
	new RuntimeSearchConfig(
		options.BeamLevel1,
		options.BeamLevel2,
		Dimension: 14,
		DistanceMetric.SquaredL2,
		options.IndexKind,
		PaddedDimension: 16,
		options.RerankCount),
	new RuntimeHttpConfig(
		GetRuntimeParserMode(options.ParseMode),
		ResponseMode.PrecomputedTable,
		TransportMode.Tcp));

NormalizationConstants normalizationConstants = ReferenceDataLoader.LoadNormalizationConstants(runtimeConfig.Dataset.NormalizationPath);
MccRiskTable mccRiskTable = ReferenceDataLoader.LoadMccRiskTable(runtimeConfig.Dataset.MccRiskPath);

using VectorSearchRuntime searchRuntime = VectorSearchRuntime.Load(runtimeConfig);
using FraudDetectionService? detectionService = UsesFraudDetectionService(options.ParseMode)
	? FraudDetectionService.Create(runtimeConfig)
	: null;
using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(options.TestDataPath));

JsonElement root = document.RootElement;
JsonElement stats = root.GetProperty("stats");
JsonElement.ArrayEnumerator entries = root.GetProperty("entries").EnumerateArray();
int availableCount = stats.GetProperty("total").GetInt32();
int remainingCount = Math.Max(availableCount - options.StartIndex, 0);
int evaluationCount = (options.Limit > 0) ? Math.Min(options.Limit, remainingCount) : remainingCount;

if (evaluationCount <= 0) {
	throw new ArgumentOutOfRangeException(nameof(args), "The requested evaluation window is empty.");
}

long[] vectorizationTicks = new long[evaluationCount];
long[] searchTicks = new long[evaluationCount];
long[] totalTicks = new long[evaluationCount];
List<int>? traceCandidateScanCounts = options.TraceEvery > 0 ? new List<int>() : null;
List<int>? traceCandidateRerankCounts = options.TraceEvery > 0 ? new List<int>() : null;
List<int>? traceSelectedLeafCounts = options.TraceEvery > 0 ? new List<int>() : null;
List<int>? traceMaxLeafSizes = options.TraceEvery > 0 ? new List<int>() : null;
List<EvaluatorMismatch> mismatches = [];
SearchHit[] hits = new SearchHit[options.TopK];
float[] vectorBuffer = new float[FraudVectorizer.PaddedDimension];

int truePositiveCount = 0;
int trueNegativeCount = 0;
int falsePositiveCount = 0;
int falseNegativeCount = 0;
int absoluteIndex = 0;
int evaluatedCount = 0;
Stopwatch wallClock = Stopwatch.StartNew();

foreach (JsonElement entry in entries) {
	if (absoluteIndex < options.StartIndex) {
		absoluteIndex++;
		continue;
	}

	if (evaluatedCount >= evaluationCount) {
		break;
	}

	bool expectedApproved = entry.GetProperty("expected_approved").GetBoolean();
	bool approved;
	int fraudCount;
	double fraudScore;

	if (UsesFraudDetectionService(options.ParseMode)) {
		byte[] payload = Encoding.UTF8.GetBytes(entry.GetProperty("request").GetRawText());
		long requestStart = Stopwatch.GetTimestamp();

		if ((detectionService is null) || !detectionService.TryHandle(payload, out ReadOnlyMemory<byte> responsePayload)) {
			throw new InvalidDataException("FraudDetectionService could not handle the official payload.");
		}

		long requestEnd = Stopwatch.GetTimestamp();
		vectorizationTicks[evaluatedCount] = 0;
		searchTicks[evaluatedCount] = requestEnd - requestStart;
		totalTicks[evaluatedCount] = requestEnd - requestStart;

		(approved, fraudScore) = ParseApprovedResponse(responsePayload);
		fraudCount = checked((int)Math.Round(fraudScore * options.TopK));
	} else {
		FraudRequest request = ParseRequest(entry.GetProperty("request"), options.ParseMode);

		long vectorizationStart = Stopwatch.GetTimestamp();
		FraudVectorizer.WriteVector(request, normalizationConstants, mccRiskTable, vectorBuffer);
		long searchStart = Stopwatch.GetTimestamp();
		fraudCount = searchRuntime.CountFraud(vectorBuffer, hits);
		long requestEnd = Stopwatch.GetTimestamp();

		vectorizationTicks[evaluatedCount] = searchStart - vectorizationStart;
		searchTicks[evaluatedCount] = requestEnd - searchStart;
		totalTicks[evaluatedCount] = requestEnd - vectorizationStart;

		if ((options.TraceEvery > 0) && ((evaluatedCount % options.TraceEvery) == 0) && searchRuntime.TryTrace(vectorBuffer, out HierarchicalSearchTrace trace)) {
			traceCandidateScanCounts!.Add(trace.CandidateScanCount);
			traceCandidateRerankCounts!.Add(trace.CandidateRerankCount);
			traceSelectedLeafCounts!.Add(trace.SelectedLeafCount);
			traceMaxLeafSizes!.Add(trace.MaxSelectedLeafSize);
		}

		fraudScore = fraudCount / (double)options.TopK;
		approved = fraudScore < options.ApprovalThreshold;
	}

	if (approved == expectedApproved) {
		if (approved) {
			trueNegativeCount++;
		} else {
			truePositiveCount++;
		}
	} else if (approved) {
		falseNegativeCount++;
	} else {
		falsePositiveCount++;
	}

	if ((approved != expectedApproved) && (mismatches.Count < options.MismatchLimit)) {
		mismatches.Add(new EvaluatorMismatch(
			absoluteIndex,
			expectedApproved,
			approved,
			fraudCount,
			fraudScore));
	}

	absoluteIndex++;
	evaluatedCount++;
}

wallClock.Stop();

EvaluatorSummary summary = new(
	new EvaluatorSettings(
		options.TestDataPath,
		options.RuntimeDataRoot,
		options.IndexKind.ToString(),
		options.ParseMode.ToString(),
		options.BeamLevel1,
		options.BeamLevel2,
		options.RerankCount,
		options.TopK,
		options.ApprovalThreshold,
		options.StartIndex,
		options.TraceEvery,
		options.MismatchLimit,
		evaluatedCount),
	new EvaluatorDatasetStats(
		stats.GetProperty("total").GetInt32(),
		stats.GetProperty("fraud_count").GetInt32(),
		stats.GetProperty("legit_count").GetInt32(),
		stats.GetProperty("edge_case_count").GetInt32(),
		stats.GetProperty("fraud_rate").GetDouble(),
		stats.GetProperty("legit_rate").GetDouble(),
		stats.GetProperty("edge_case_rate").GetDouble()),
	new EvaluatorBreakdown(
		truePositiveCount,
		trueNegativeCount,
		falsePositiveCount,
		falseNegativeCount),
	BuildDetectionScoring(evaluatedCount, falsePositiveCount, falseNegativeCount),
	mismatches,
	new EvaluatorLatencySummary(
		BuildMetricSummary(vectorizationTicks),
		BuildMetricSummary(searchTicks),
		BuildMetricSummary(totalTicks),
		Math.Round(evaluatedCount / wallClock.Elapsed.TotalSeconds, 2)),
	BuildTraceSummary(
		traceCandidateScanCounts,
		traceCandidateRerankCounts,
		traceSelectedLeafCounts,
		traceMaxLeafSizes));

Console.WriteLine(JsonSerializer.Serialize(
	summary,
	EvaluatorJsonContext.Default.EvaluatorSummary));

static EvaluatorDetectionScoring BuildDetectionScoring(int requestCount, int falsePositiveCount, int falseNegativeCount) {
	const double K = 1000d;
	const double EpsilonMin = 0.001d;
	const double Beta = 300d;
	const double FailureRateCut = 0.15d;
	const double CutScore = -3000d;

	int weightedErrors = falsePositiveCount + (falseNegativeCount * 3);
	int totalFailures = falsePositiveCount + falseNegativeCount;
	double epsilon = requestCount > 0 ? (weightedErrors / (double)requestCount) : 0d;
	double failureRate = requestCount > 0 ? (totalFailures / (double)requestCount) : 0d;

	if (failureRate > FailureRateCut) {
		return new EvaluatorDetectionScoring(
			weightedErrors,
			epsilon,
			failureRate,
			CutScore,
			RateComponent: null,
			AbsolutePenalty: null,
			CutTriggered: true);
	}

	double rateComponent = K * Math.Log10(1d / Math.Max(epsilon, EpsilonMin));
	double absolutePenalty = -Beta * Math.Log10(1d + weightedErrors);
	double score = rateComponent + absolutePenalty;

	return new EvaluatorDetectionScoring(
		weightedErrors,
		epsilon,
		failureRate,
		score,
		rateComponent,
		absolutePenalty,
		CutTriggered: false);
}

static MetricSummary BuildMetricSummary(long[] ticks) {
	Array.Sort(ticks);

	return new MetricSummary(
		ToMicroseconds(ticks[0]),
		ToMicroseconds(GetPercentileTicks(ticks, 0.50d)),
		ToMicroseconds(GetPercentileTicks(ticks, 0.95d)),
		ToMicroseconds(GetPercentileTicks(ticks, 0.99d)),
		ToMicroseconds(ticks[^1]),
		ToMicroseconds((long)Math.Round(ticks.Average())));
}

static TraceSummary? BuildTraceSummary(
	List<int>? candidateScanCounts,
	List<int>? candidateRerankCounts,
	List<int>? selectedLeafCounts,
	List<int>? maxLeafSizes) {
	if ((candidateScanCounts is null) ||
		(candidateRerankCounts is null) ||
		(selectedLeafCounts is null) ||
		(maxLeafSizes is null) ||
		(candidateScanCounts.Count == 0)) {
		return null;
	}

	return new TraceSummary(
		candidateScanCounts.Count,
		BuildIntMetricSummary(candidateScanCounts),
		BuildIntMetricSummary(candidateRerankCounts),
		BuildIntMetricSummary(selectedLeafCounts),
		BuildIntMetricSummary(maxLeafSizes));
}

static IntMetricSummary BuildIntMetricSummary(List<int> values) {
	int[] data = [.. values];
	Array.Sort(data);

	return new IntMetricSummary(
		data[0],
		GetPercentileValue(data, 0.50d),
		GetPercentileValue(data, 0.95d),
		GetPercentileValue(data, 0.99d),
		data[^1],
		(int)Math.Round(values.Average()));
}

static FraudRequest ParseRequest(JsonElement requestElement, EvaluationParseMode parseMode) {
	if (UsesFraudDetectionService(parseMode)) {
		throw new InvalidOperationException("Service-backed parse modes do not expose a direct FraudRequest model.");
	}

	if (parseMode == EvaluationParseMode.ManualParser) {
		byte[] payload = Encoding.UTF8.GetBytes(requestElement.GetRawText());

		if (!ManualFraudRequestParser.TryParse(payload, out FraudRequest parsedRequest)) {
			throw new InvalidDataException("Manual parser could not parse the official payload.");
		}

		return parsedRequest;
	}

	if (parseMode == EvaluationParseMode.StjRoundTrip) {
		ArrayBufferWriter<byte> bufferWriter = new();

		using (Utf8JsonWriter writer = new(bufferWriter)) {
			requestElement.WriteTo(writer);
		}

		byte[] payload = bufferWriter.WrittenSpan.ToArray();

		if (!ManualFraudRequestParser.TryParse(payload, out FraudRequest parsedRequest)) {
			throw new InvalidDataException("Manual parser could not parse the System.Text.Json round-tripped payload.");
		}

		return parsedRequest;
	}

	JsonElement transactionElement = requestElement.GetProperty("transaction");
	JsonElement customerElement = requestElement.GetProperty("customer");
	JsonElement merchantElement = requestElement.GetProperty("merchant");
	JsonElement terminalElement = requestElement.GetProperty("terminal");
	JsonElement lastTransactionElement = requestElement.GetProperty("last_transaction");

	TransactionData transaction = new(
		transactionElement.GetProperty("amount").GetDouble(),
		transactionElement.GetProperty("installments").GetInt32(),
		ParseTimestamp(transactionElement.GetProperty("requested_at").GetString()));

	MerchantIdList knownMerchants = default;

	foreach (JsonElement knownMerchant in customerElement.GetProperty("known_merchants").EnumerateArray()) {
		knownMerchants.Add(ParseMerchantId(knownMerchant.GetString()));
	}

	CustomerData customer = new(
		customerElement.GetProperty("avg_amount").GetDouble(),
		knownMerchants,
		customerElement.GetProperty("tx_count_24h").GetInt32());

	MerchantData merchant = new(
		merchantElement.GetProperty("avg_amount").GetDouble(),
		ParseMerchantId(merchantElement.GetProperty("id").GetString()),
		int.Parse(merchantElement.GetProperty("mcc").GetString()!, System.Globalization.CultureInfo.InvariantCulture));

	TerminalData terminal = new(
		terminalElement.GetProperty("card_present").GetBoolean(),
		terminalElement.GetProperty("is_online").GetBoolean(),
		terminalElement.GetProperty("km_from_home").GetDouble());

	LastTransactionData lastTransaction = lastTransactionElement.ValueKind == JsonValueKind.Null
		? new LastTransactionData(false, 0d, default)
		: new LastTransactionData(
			true,
			lastTransactionElement.GetProperty("km_from_current").GetDouble(),
			ParseTimestamp(lastTransactionElement.GetProperty("timestamp").GetString()));

	return new FraudRequest(transaction, customer, merchant, terminal, lastTransaction);
}

static long GetPercentileTicks(long[] sortedTicks, double percentile) {
	int index = (int)Math.Ceiling(sortedTicks.Length * percentile) - 1;
	return sortedTicks[Math.Clamp(index, 0, sortedTicks.Length - 1)];
}

static int GetPercentileValue(int[] sortedValues, double percentile) {
	int index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
	return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
}

static int ParseMerchantId(string? merchantId) {
	ArgumentException.ThrowIfNullOrWhiteSpace(merchantId);

	ReadOnlySpan<char> span = merchantId.AsSpan();

	if ((span.Length != 8) || !span.StartsWith("MERC-".AsSpan(), StringComparison.Ordinal)) {
		throw new InvalidDataException($"Unexpected merchant id '{merchantId}'.");
	}

	return int.Parse(span[5..], System.Globalization.CultureInfo.InvariantCulture);
}

static CompactTimestamp ParseTimestamp(string? value) {
	ArgumentException.ThrowIfNullOrWhiteSpace(value);

	ReadOnlySpan<char> span = value.AsSpan();

	if (span.Length != 20) {
		throw new InvalidDataException($"Unexpected timestamp '{value}'.");
	}

	return new CompactTimestamp(
		ReadTwoOrFourDigits(span[0..4]),
		ReadTwoOrFourDigits(span[5..7]),
		ReadTwoOrFourDigits(span[8..10]),
		ReadTwoOrFourDigits(span[11..13]),
		ReadTwoOrFourDigits(span[14..16]),
		ReadTwoOrFourDigits(span[17..19]));
}

static int ReadTwoOrFourDigits(ReadOnlySpan<char> span) {
	int value = 0;

	for (int index = 0; index < span.Length; index++) {
		int digit = span[index] - '0';

		if ((uint)digit > 9u) {
			throw new InvalidDataException("Unexpected non-digit while parsing compact timestamp.");
		}

		value = (value * 10) + digit;
	}

	return value;
}

static double ToMicroseconds(long ticks) => Math.Round((ticks * 1_000_000d) / Stopwatch.Frequency, 3);

static ParserMode GetRuntimeParserMode(EvaluationParseMode parseMode) {
	return parseMode switch {
		EvaluationParseMode.ServiceReferenceStj => ParserMode.ReferenceStj,
		_ => ParserMode.Manual,
	};
}

static (bool Approved, double FraudScore) ParseApprovedResponse(ReadOnlyMemory<byte> payload) {
	using JsonDocument document = JsonDocument.Parse(payload);
	JsonElement root = document.RootElement;
	return (
		root.GetProperty("approved").GetBoolean(),
		root.GetProperty("fraud_score").GetDouble());
}

static bool UsesFraudDetectionService(EvaluationParseMode parseMode) {
	return (parseMode == EvaluationParseMode.ServiceManual) ||
		(parseMode == EvaluationParseMode.ServiceReferenceStj);
}

internal sealed record EvaluatorSummary(
	EvaluatorSettings Settings,
	EvaluatorDatasetStats Dataset,
	EvaluatorBreakdown Breakdown,
	EvaluatorDetectionScoring Detection,
	List<EvaluatorMismatch> Mismatches,
	EvaluatorLatencySummary Latency,
	TraceSummary? Trace);

internal sealed record EvaluatorSettings(
	string TestDataPath,
	string RuntimeDataRoot,
	string IndexKind,
	string ParseMode,
	int BeamLevel1,
	int BeamLevel2,
	int RerankCount,
	int TopK,
	double ApprovalThreshold,
	int StartIndex,
	int TraceEvery,
	int MismatchLimit,
	int EvaluatedRequests);

internal sealed record EvaluatorDatasetStats(
	int Total,
	int FraudCount,
	int LegitCount,
	int EdgeCaseCount,
	double FraudRate,
	double LegitRate,
	double EdgeCaseRate);

internal sealed record EvaluatorBreakdown(
	int TruePositiveDetections,
	int TrueNegativeDetections,
	int FalsePositiveDetections,
	int FalseNegativeDetections);

internal sealed record EvaluatorDetectionScoring(
	int WeightedErrors,
	double ErrorRateEpsilon,
	double FailureRate,
	double Score,
	double? RateComponent,
	double? AbsolutePenalty,
	bool CutTriggered);

internal sealed record EvaluatorMismatch(
	int Index,
	bool ExpectedApproved,
	bool ActualApproved,
	int FraudCount,
	double FraudScore);

internal sealed record EvaluatorLatencySummary(
	MetricSummary VectorizationUs,
	MetricSummary SearchUs,
	MetricSummary TotalUs,
	double ThroughputPerSecond);

internal sealed record MetricSummary(
	double Min,
	double P50,
	double P95,
	double P99,
	double Max,
	double Mean);

internal sealed record TraceSummary(
	int SampleCount,
	IntMetricSummary CandidateScanCount,
	IntMetricSummary CandidateRerankCount,
	IntMetricSummary SelectedLeafCount,
	IntMetricSummary MaxSelectedLeafSize);

internal sealed record IntMetricSummary(
	int Min,
	int P50,
	int P95,
	int P99,
	int Max,
	int Mean);

internal sealed class EvaluatorOptions {
	private EvaluatorOptions(
		string testDataPath,
		string runtimeDataRoot,
		IndexKind indexKind,
		EvaluationParseMode parseMode,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		int topK,
		double approvalThreshold,
		int startIndex,
		int limit,
		int traceEvery,
		int mismatchLimit) {
		this.TestDataPath = testDataPath;
		this.RuntimeDataRoot = runtimeDataRoot;
		this.IndexKind = indexKind;
		this.ParseMode = parseMode;
		this.BeamLevel1 = beamLevel1;
		this.BeamLevel2 = beamLevel2;
		this.RerankCount = rerankCount;
		this.TopK = topK;
		this.ApprovalThreshold = approvalThreshold;
		this.StartIndex = startIndex;
		this.Limit = limit;
		this.TraceEvery = traceEvery;
		this.MismatchLimit = mismatchLimit;
	}

	public double ApprovalThreshold { get; }

	public int BeamLevel1 { get; }

	public int BeamLevel2 { get; }

	public IndexKind IndexKind { get; }

	public int Limit { get; }

	public int MismatchLimit { get; }

	public EvaluationParseMode ParseMode { get; }

	public int RerankCount { get; }

	public string RuntimeDataRoot { get; }

	public string TestDataPath { get; }

	public int TopK { get; }

	public int StartIndex { get; }

	public int TraceEvery { get; }

	public static EvaluatorOptions Parse(string[] args) {
		string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		string defaultRuntimeDataRoot = Path.Combine(repoRoot, "runtime-data");
		string defaultTestDataPath = Path.GetFullPath(Path.Combine(repoRoot, "..", "rinha-de-backend-2026", "test", "test-data.json"));
		Dictionary<string, string> values = ParseArguments(args);

		string runtimeDataRoot = Path.GetFullPath(GetValue(values, "--runtime-data", defaultRuntimeDataRoot));
		string testDataPath = Path.GetFullPath(GetValue(values, "--test-data", defaultTestDataPath));
		IndexKind indexKind = Enum.TryParse(GetValue(values, "--index-kind", nameof(IndexKind.HierarchicalBeamIvf)), ignoreCase: true, out IndexKind parsedIndexKind)
			? parsedIndexKind
			: throw new ArgumentOutOfRangeException(nameof(args), "Invalid --index-kind value.");

		return new EvaluatorOptions(
			testDataPath,
			runtimeDataRoot,
			indexKind,
			Enum.TryParse(GetValue(values, "--parse-mode", nameof(EvaluationParseMode.DirectModel)), ignoreCase: true, out EvaluationParseMode parsedParseMode)
				? parsedParseMode
				: throw new ArgumentOutOfRangeException(nameof(args), "Invalid --parse-mode value."),
			int.Parse(GetValue(values, "--beam-level1", "10"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--beam-level2", "32"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--rerank-count", "48"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--top-k", "5"), System.Globalization.CultureInfo.InvariantCulture),
			double.Parse(GetValue(values, "--approval-threshold", "0.6"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--start-index", "0"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--limit", "0"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--trace-every", "0"), System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(GetValue(values, "--mismatch-limit", "32"), System.Globalization.CultureInfo.InvariantCulture));
	}

	private static string GetValue(Dictionary<string, string> values, string key, string fallback) =>
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

internal enum EvaluationParseMode {
	DirectModel = 0,
	ManualParser = 1,
	StjRoundTrip = 2,
	ServiceManual = 3,
	ServiceReferenceStj = 4,
}
