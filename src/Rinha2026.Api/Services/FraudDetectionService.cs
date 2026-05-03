using System.Diagnostics;
using System.Text.Json;

using Rinha2026.Core.Configuration;
using Rinha2026.Core.Detection;
using Rinha2026.Core.Model;
using Rinha2026.Core.Parsing;
using Rinha2026.Core.Search;
using Rinha2026.Core.Vectorization;

namespace Rinha2026.Api.Services;

public sealed class FraudDetectionService : IDisposable {
	private const int WarmUpPassCount = 4;

	private static readonly byte[][] warmupPayloads = [
		"""
		{"id":"warm-1","transaction":{"amount":41.12,"installments":2,"requested_at":"2026-03-11T18:45:53Z"},"customer":{"avg_amount":82.24,"tx_count_24h":3,"known_merchants":["MERC-003","MERC-016"]},"merchant":{"id":"MERC-016","mcc":"5411","avg_amount":60.25},"terminal":{"is_online":false,"card_present":true,"km_from_home":29.23},"last_transaction":null}
		"""u8.ToArray(),
		"""
		{"id":"warm-2","transaction":{"amount":3200.0,"installments":9,"requested_at":"2026-03-17T02:04:06Z"},"customer":{"avg_amount":68.88,"tx_count_24h":18,"known_merchants":["MERC-004","MERC-007","MERC-015"]},"merchant":{"id":"MERC-062","mcc":"7801","avg_amount":25.55},"terminal":{"is_online":true,"card_present":false,"km_from_home":881.61},"last_transaction":{"timestamp":"2026-03-17T01:58:06Z","km_from_current":660.92}}
		"""u8.ToArray(),
		"""
		{"id":"warm-3","transaction":{"amount":384.88,"installments":3,"requested_at":"2026-03-11T20:23:35Z"},"customer":{"avg_amount":769.76,"tx_count_24h":3,"known_merchants":["MERC-009","MERC-001"]},"merchant":{"id":"MERC-001","mcc":"5912","avg_amount":298.95},"terminal":{"is_online":false,"card_present":true,"km_from_home":13.71},"last_transaction":{"timestamp":"2026-03-11T14:58:35Z","km_from_current":18.86}}
		"""u8.ToArray(),
		"""
		{"id":"warm-4","transaction":{"amount":912.45,"installments":1,"requested_at":"2026-03-12T09:14:11Z"},"customer":{"avg_amount":110.0,"tx_count_24h":6,"known_merchants":["MERC-002","MERC-003"]},"merchant":{"id":"MERC-077","mcc":"5999","avg_amount":140.0},"terminal":{"is_online":true,"card_present":false,"km_from_home":245.5},"last_transaction":null}
		"""u8.ToArray(),
	];

	[ThreadStatic]
	private static SearchHit[]? hitScratchBuffer;

	[ThreadStatic]
	private static float[]? vectorScratchBuffer;

	private readonly MccRiskTable mccRiskTable;
	private readonly NormalizationConstants normalizationConstants;
	private readonly RuntimeConfig runtimeConfig;
	private readonly FraudResponseCache responseCache;
	private readonly VectorSearchRuntime searchRuntime;

	private FraudDetectionService(
		RuntimeConfig runtimeConfig,
		NormalizationConstants normalizationConstants,
		MccRiskTable mccRiskTable,
		VectorSearchRuntime searchRuntime,
		FraudResponseCache responseCache) {
		this.runtimeConfig = runtimeConfig;
		this.normalizationConstants = normalizationConstants;
		this.mccRiskTable = mccRiskTable;
		this.searchRuntime = searchRuntime;
		this.responseCache = responseCache;
	}

	public static FraudDetectionService Create(RuntimeConfig runtimeConfig) {
		NormalizationConstants normalizationConstants = ReferenceDataLoader.LoadNormalizationConstants(
			runtimeConfig.Dataset.NormalizationPath);
		MccRiskTable mccRiskTable = ReferenceDataLoader.LoadMccRiskTable(runtimeConfig.Dataset.MccRiskPath);
		VectorSearchRuntime searchRuntime = VectorSearchRuntime.Load(runtimeConfig);
		FraudResponseCache responseCache = new(
			runtimeConfig.Detection.TopK,
			runtimeConfig.Detection.ApprovalThreshold);

		return new FraudDetectionService(
			runtimeConfig,
			normalizationConstants,
			mccRiskTable,
			searchRuntime,
			responseCache);
	}

	public bool TryHandle(ReadOnlySpan<byte> payload, out ReadOnlyMemory<byte> response) {
		if (!this.TryHandleCore(payload, out int _, out response, out FraudDetectionProfile? _)) {
			return false;
		}

		return true;
	}

	public bool TryHandle(ReadOnlySpan<byte> payload, out int fraudCount, out ReadOnlyMemory<byte> response) {
		if (!this.TryHandleCore(payload, out fraudCount, out response, out FraudDetectionProfile? _)) {
			return false;
		}

		return true;
	}

	public bool TryHandle(
		ReadOnlySpan<byte> payload,
		out ReadOnlyMemory<byte> response,
		out FraudDetectionProfile detectionProfile) {
		if (!this.TryHandleCore(payload, out int _, out response, out FraudDetectionProfile? profile) || (profile is null)) {
			detectionProfile = default;
			return false;
		}

		detectionProfile = profile.Value;
		return true;
	}

	public bool WarmUp() {
		_ = this.searchRuntime.Warm();
		_ = this.responseCache.GetResponse(0);
		_ = this.responseCache.GetResponse(Math.Max(0, this.runtimeConfig.Detection.MaxApprovedCount));
		_ = this.responseCache.GetResponse(this.runtimeConfig.Detection.MinDeniedCount);
		_ = this.responseCache.GetResponse(this.runtimeConfig.Detection.TopK);

		try {
			for (int pass = 0; pass < WarmUpPassCount; pass++) {
				foreach (byte[] payload in warmupPayloads) {
					if (!this.TryHandle(payload, out ReadOnlyMemory<byte> _)) {
						return false;
					}
				}
			}

			return true;
		} catch (ArgumentException) {
			return false;
		}
	}

	public bool TryHandle(
		ReadOnlySpan<byte> payload,
		out int fraudCount,
		out ReadOnlyMemory<byte> response,
		out FraudDetectionProfile detectionProfile) {
		if (!this.TryHandleCore(payload, out fraudCount, out response, out FraudDetectionProfile? profile) || (profile is null)) {
			detectionProfile = default;
			return false;
		}

		detectionProfile = profile.Value;
		return true;
	}

	private bool TryHandleCore(
		ReadOnlySpan<byte> payload,
		out int fraudCount,
		out ReadOnlyMemory<byte> response,
		out FraudDetectionProfile? detectionProfile) {
		fraudCount = 0;
		response = default;
		detectionProfile = default;
		long parseStart = Stopwatch.GetTimestamp();

		if (!TryParse(payload, this.runtimeConfig.Http.ParserMode, out FraudRequest request)) {
			return false;
		}

		long vectorizationStart = Stopwatch.GetTimestamp();
		float[] vectorBuffer = GetVectorScratchBuffer(FraudVectorizer.PaddedDimension);
		Span<float> vector = vectorBuffer.AsSpan(0, FraudVectorizer.PaddedDimension);
		FraudVectorizer.WriteVector(request, this.normalizationConstants, this.mccRiskTable, vector);

		long searchStart = Stopwatch.GetTimestamp();
		int topK = this.runtimeConfig.Detection.TopK;
		SearchHit[] hitsBuffer = GetHitScratchBuffer(topK);
		Span<SearchHit> hits = hitsBuffer.AsSpan(0, topK);
		fraudCount = this.searchRuntime.CountFraud(vector, hits);
		response = this.responseCache.GetResponse(fraudCount);
		long end = Stopwatch.GetTimestamp();
		detectionProfile = new FraudDetectionProfile(
			vectorizationStart - parseStart,
			searchStart - vectorizationStart,
			end - searchStart);
		return true;
	}

	public void Dispose() => this.searchRuntime.Dispose();

	private static SearchHit[] GetHitScratchBuffer(int length) {
		SearchHit[] buffer = hitScratchBuffer ?? Array.Empty<SearchHit>();

		if (buffer.Length < length) {
			buffer = new SearchHit[length];
			hitScratchBuffer = buffer;
		}

		return buffer;
	}

	private static float[] GetVectorScratchBuffer(int length) {
		float[] buffer = vectorScratchBuffer ?? Array.Empty<float>();

		if (buffer.Length < length) {
			buffer = new float[length];
			vectorScratchBuffer = buffer;
		}

		return buffer;
	}

	private static bool TryParse(ReadOnlySpan<byte> payload, ParserMode parserMode, out FraudRequest request) {
		try {
			return parserMode switch {
				ParserMode.Manual => ManualFraudRequestParser.TryParse(payload, out request),
				ParserMode.ReferenceStj => ReferenceFraudRequestParser.TryParse(payload, out request),
				_ => throw new NotSupportedException($"Unsupported parser mode '{parserMode}'."),
			};
		} catch (JsonException) {
			request = default;
			return false;
		}
	}
}

public readonly record struct FraudDetectionProfile(
	long ParseTicks,
	long VectorizeTicks,
	long SearchTicks);
