using Rinha2026.Core.Configuration;
using Rinha2026.Core.Detection;
using Rinha2026.Core.Model;
using Rinha2026.Core.Parsing;
using Rinha2026.Core.Search;
using Rinha2026.Core.Vectorization;

namespace Rinha2026.Api.Services;

public sealed class FraudDetectionService : IDisposable {
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
		response = default;

		if (!TryParse(payload, this.runtimeConfig.Http.ParserMode, out FraudRequest request)) {
			return false;
		}

		Span<float> vector = stackalloc float[FraudVectorizer.PaddedDimension];
		FraudVectorizer.WriteVector(request, this.normalizationConstants, this.mccRiskTable, vector);

		int topK = this.runtimeConfig.Detection.TopK;
		Span<SearchHit> hits = topK <= 16 ? stackalloc SearchHit[topK] : new SearchHit[topK];
		int fraudCount = this.searchRuntime.CountFraud(vector, hits);
		response = this.responseCache.GetResponse(fraudCount);
		return true;
	}

	public void Dispose() => this.searchRuntime.Dispose();

	private static bool TryParse(ReadOnlySpan<byte> payload, ParserMode parserMode, out FraudRequest request) {
		return parserMode switch {
			ParserMode.Manual => ManualFraudRequestParser.TryParse(payload, out request),
			ParserMode.ReferenceStj => ReferenceFraudRequestParser.TryParse(payload, out request),
			_ => throw new NotSupportedException($"Unsupported parser mode '{parserMode}'."),
		};
	}
}
