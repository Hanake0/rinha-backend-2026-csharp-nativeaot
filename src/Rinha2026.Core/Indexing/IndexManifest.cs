namespace Rinha2026.Core.Indexing;

public sealed class IndexManifest {
	public int Dimension { get; init; }

	public string IndexKind { get; init; } = "FlatCorpus";

	public string LabelBitsetFile { get; init; } = "labels.bitset.bin";

	public string LabelEncoding { get; init; } = "bitset-lsb-first";

	public int PaddedDimension { get; init; }

	public float QuantizationMaxValue { get; init; } = 1f;

	public float QuantizationMinValue { get; init; } = -1f;

	public string QuantizationKind { get; init; } = "Q8Symmetric";

	public float QuantizationScale { get; init; } = 127f;

	public string QuantizedVectorFile { get; init; } = "vectors.q8.bin";

	public string RerankVectorFile { get; init; } = "vectors.f16.bin";

	public long VectorCount { get; init; }
}
