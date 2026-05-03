using Rinha2026.Core.Configuration;
using Rinha2026.Core.Indexing;

namespace Rinha2026.Core.Search;

public sealed class VectorSearchRuntime : IDisposable {
	private readonly ExactFlatSearchEngine? exactEngine;
	private readonly FlatArtifactSet? exactFlatArtifacts;
	private readonly HierarchicalArtifactSet? hierarchicalArtifacts;
	private readonly HierarchicalBeamSearchEngine? hierarchicalEngine;
	private readonly RuntimeSearchConfig searchConfig;

	private VectorSearchRuntime(
		RuntimeSearchConfig searchConfig,
		FlatArtifactSet exactFlatArtifacts,
		ExactFlatSearchEngine exactEngine) {
		this.searchConfig = searchConfig;
		this.exactFlatArtifacts = exactFlatArtifacts;
		this.exactEngine = exactEngine;
	}

	private VectorSearchRuntime(
		RuntimeSearchConfig searchConfig,
		HierarchicalArtifactSet hierarchicalArtifacts,
		HierarchicalBeamSearchEngine hierarchicalEngine) {
		this.searchConfig = searchConfig;
		this.hierarchicalArtifacts = hierarchicalArtifacts;
		this.hierarchicalEngine = hierarchicalEngine;
	}

	public static VectorSearchRuntime Load(RuntimeConfig runtimeConfig) {
		return runtimeConfig.Search.IndexKind switch {
			IndexKind.ExactSampleOnly => LoadExact(runtimeConfig),
			IndexKind.FlatIvf => LoadExact(runtimeConfig),
			IndexKind.HierarchicalBeamIvf => LoadHierarchical(runtimeConfig),
			_ => throw new NotSupportedException($"Unsupported index kind '{runtimeConfig.Search.IndexKind}'."),
		};
	}

	public int CountFraud(ReadOnlySpan<float> query, Span<SearchHit> scratch) {
		if (this.hierarchicalEngine is not null) {
			return this.hierarchicalEngine.CountFraud(
				query,
				this.searchConfig.BeamLevel1,
				this.searchConfig.BeamLevel2,
				this.searchConfig.RerankCount,
				scratch);
		}

		if (this.exactEngine is not null) {
			return this.exactEngine.CountFraud(query, scratch);
		}

		throw new InvalidOperationException("Search runtime was not initialized.");
	}

	public void Dispose() {
		this.hierarchicalArtifacts?.Dispose();
		this.exactFlatArtifacts?.Dispose();
	}

	private static VectorSearchRuntime LoadExact(RuntimeConfig runtimeConfig) {
		FlatArtifactSet artifacts = FlatArtifactSet.Load(runtimeConfig.Dataset.IndexDirectory);
		ExactFlatSearchEngine engine = new(artifacts);
		return new VectorSearchRuntime(runtimeConfig.Search, artifacts, engine);
	}

	private static VectorSearchRuntime LoadHierarchical(RuntimeConfig runtimeConfig) {
		HierarchicalArtifactSet artifacts = HierarchicalArtifactSet.Load(runtimeConfig.Dataset.IndexDirectory);
		HierarchicalBeamSearchEngine engine = new(artifacts);
		return new VectorSearchRuntime(runtimeConfig.Search, artifacts, engine);
	}
}
