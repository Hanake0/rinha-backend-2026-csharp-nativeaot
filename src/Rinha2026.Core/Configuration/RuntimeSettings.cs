namespace Rinha2026.Core.Configuration;

public sealed class RuntimeSettings {
	public const string SectionName = "Runtime";

	public DetectionSettings Detection { get; init; } = new();

	public DatasetSettings Dataset { get; init; } = new();

	public SearchSettings Search { get; init; } = new();

	public HttpSettings Http { get; init; } = new();

	public DiagnosticsSettings Diagnostics { get; init; } = new();
}

public sealed class DetectionSettings {
	public int TopK { get; init; } = 5;

	public double ApprovalThreshold { get; init; } = 0.6d;
}

public sealed class DatasetSettings {
	public string IndexDirectory { get; init; } = "./data/index";

	public string MccRiskPath { get; init; } = "./data/mcc_risk.json";

	public string NormalizationPath { get; init; } = "./data/normalization.json";
}

public sealed class SearchSettings {
	public int BeamLevel1 { get; init; } = 10;

	public int BeamLevel2 { get; init; } = 32;

	public int Dimension { get; init; } = 14;

	public DistanceMetric DistanceMetric { get; init; } = DistanceMetric.SquaredL2;

	public IndexKind IndexKind { get; init; } = IndexKind.HierarchicalBeamIvf;

	public int PaddedDimension { get; init; } = 16;

	public int RerankCount { get; init; } = 48;

	public int BoundaryRerankCount { get; init; } = 48;

	public bool UseLeafRadiusPruning { get; init; }

	public bool UseLastTransactionPartitionPruning { get; init; } = true;
}

public sealed class HttpSettings {
	public ParserMode ParserMode { get; init; } = ParserMode.Manual;

	public ResponseMode ResponseMode { get; init; } = ResponseMode.PrecomputedTable;

	public TransportMode TransportMode { get; init; } = TransportMode.Tcp;

	public string? UnixSocketPath { get; init; }
}

public sealed class DiagnosticsSettings {
	public bool ProfileEnabled { get; init; }

	public int ProfileSampleCapacity { get; init; } = 8192;

	public int ProfileSamplingStride { get; init; } = 64;
}
