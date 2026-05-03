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
