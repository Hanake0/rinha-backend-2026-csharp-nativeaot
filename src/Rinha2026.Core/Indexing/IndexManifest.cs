namespace Rinha2026.Core.Indexing;

public sealed class IndexManifest {
	public int Dimension { get; init; }

	public string IndexKind { get; init; } = "FlatCorpus";

	public int KMeansIterations { get; init; }

	public string LabelBitsetFile { get; init; } = "labels.bitset.bin";

	public string LabelEncoding { get; init; } = "bitset-lsb-first";

	public string LeafCentroidFile { get; init; } = "leaf.centroids.f32.bin";

	public string LeafRadiusFile { get; init; } = string.Empty;

	public string LeafWithoutHistoryCountFile { get; init; } = string.Empty;

	public string LeafPostingIdsFile { get; init; } = "leaf.postings.ids.bin";

	public string LeafPostingOffsetsFile { get; init; } = "leaf.postings.offsets.bin";

	public int Level1ClusterCount { get; init; }

	public string Level1CentroidFile { get; init; } = "level1.centroids.f32.bin";

	public int Level2ClustersPerLevel1 { get; init; }

	public int PaddedDimension { get; init; }

	public string PostingLayout { get; init; } = "ExplicitIds";

	public float QuantizationMaxValue { get; init; } = 1f;

	public float QuantizationMinValue { get; init; } = -1f;

	public string QuantizationKind { get; init; } = "Q8Symmetric";

	public float QuantizationScale { get; init; } = 127f;

	public string QuantizedVectorFile { get; init; } = "vectors.q8.bin";

	public string RerankVectorFile { get; init; } = "vectors.f16.bin";

	public int TrainingSampleSize { get; init; }

	public long VectorCount { get; init; }
}
