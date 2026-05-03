namespace Rinha2026.IndexBuilder.Build;

public sealed class IndexBuildOptions {
	public int Dimension { get; init; } = 14;

	public required string InputPath { get; init; }

	public int KMeansIterations { get; init; } = 8;

	public int Level1ClusterCount { get; init; } = 32;

	public int Level2ClustersPerLevel1 { get; init; } = 8;

	public required string OutputDirectory { get; init; }

	public int PaddedDimension { get; init; } = 16;

	public int TrainingSampleSize { get; init; } = 16_384;
}
