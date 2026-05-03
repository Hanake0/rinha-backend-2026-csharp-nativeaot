namespace Rinha2026.IndexBuilder.Build;

public sealed class IndexBuildOptions {
	public int Dimension { get; init; } = 14;

	public required string InputPath { get; init; }

	public required string OutputDirectory { get; init; }

	public int PaddedDimension { get; init; } = 16;
}
