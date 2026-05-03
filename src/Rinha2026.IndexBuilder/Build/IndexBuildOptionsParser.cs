namespace Rinha2026.IndexBuilder.Build;

public static class IndexBuildOptionsParser {
	public static IndexBuildOptions? TryParse(string[] args, out string? errorMessage) {
		ArgumentNullException.ThrowIfNull(args);

		string? inputPath = null;
		string? outputDirectory = null;
		int dimension = 14;
		int kMeansIterations = 8;
		int level1ClusterCount = 32;
		int level2ClustersPerLevel1 = 8;
		int paddedDimension = 16;
		int trainingSampleSize = 16_384;
		bool useLastTransactionPartitioning = false;

		for (int index = 0; index < args.Length; index++) {
			switch (args[index]) {
				case "--input":
					if (!TryReadValue(args, ref index, out inputPath)) {
						errorMessage = "Missing value for --input.";
						return null;
					}

					break;
				case "--output":
					if (!TryReadValue(args, ref index, out outputDirectory)) {
						errorMessage = "Missing value for --output.";
						return null;
					}

					break;
				case "--dimension":
					if (!TryReadIntValue(args, ref index, out dimension)) {
						errorMessage = "Missing or invalid value for --dimension.";
						return null;
					}

					break;
				case "--padded-dimension":
					if (!TryReadIntValue(args, ref index, out paddedDimension)) {
						errorMessage = "Missing or invalid value for --padded-dimension.";
						return null;
					}

					break;
				case "--level1-clusters":
					if (!TryReadIntValue(args, ref index, out level1ClusterCount)) {
						errorMessage = "Missing or invalid value for --level1-clusters.";
						return null;
					}

					break;
				case "--level2-per-level1":
					if (!TryReadIntValue(args, ref index, out level2ClustersPerLevel1)) {
						errorMessage = "Missing or invalid value for --level2-per-level1.";
						return null;
					}

					break;
				case "--training-sample-size":
					if (!TryReadIntValue(args, ref index, out trainingSampleSize)) {
						errorMessage = "Missing or invalid value for --training-sample-size.";
						return null;
					}

					break;
				case "--kmeans-iterations":
					if (!TryReadIntValue(args, ref index, out kMeansIterations)) {
						errorMessage = "Missing or invalid value for --kmeans-iterations.";
						return null;
					}

					break;
				case "--use-last-transaction-partitioning":
					if (!TryReadBooleanValue(args, ref index, out useLastTransactionPartitioning)) {
						errorMessage = "Missing or invalid value for --use-last-transaction-partitioning.";
						return null;
					}

					break;
				default:
					errorMessage = $"Unknown argument '{args[index]}'.";
					return null;
			}
		}

		if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputDirectory)) {
			errorMessage = "Usage: --input <path-to-references.json.gz> --output <artifact-directory> [--dimension 14] [--padded-dimension 16] [--level1-clusters 32] [--level2-per-level1 8] [--training-sample-size 16384] [--kmeans-iterations 8] [--use-last-transaction-partitioning true|false]";
			return null;
		}

		if (dimension <= 0) {
			errorMessage = "--dimension must be greater than zero.";
			return null;
		}

		if (paddedDimension < dimension) {
			errorMessage = "--padded-dimension must be greater than or equal to --dimension.";
			return null;
		}

		if (level1ClusterCount <= 0) {
			errorMessage = "--level1-clusters must be greater than zero.";
			return null;
		}

		if (level2ClustersPerLevel1 <= 0) {
			errorMessage = "--level2-per-level1 must be greater than zero.";
			return null;
		}

		if (trainingSampleSize <= 0) {
			errorMessage = "--training-sample-size must be greater than zero.";
			return null;
		}

		if (kMeansIterations <= 0) {
			errorMessage = "--kmeans-iterations must be greater than zero.";
			return null;
		}

		errorMessage = null;
		return new IndexBuildOptions {
			Dimension = dimension,
			InputPath = inputPath,
			KMeansIterations = kMeansIterations,
			Level1ClusterCount = level1ClusterCount,
			Level2ClustersPerLevel1 = level2ClustersPerLevel1,
			OutputDirectory = outputDirectory,
			PaddedDimension = paddedDimension,
			TrainingSampleSize = trainingSampleSize,
			UseLastTransactionPartitioning = useLastTransactionPartitioning,
		};
	}

	private static bool TryReadBooleanValue(string[] args, ref int index, out bool value) {
		value = default;
		return TryReadValue(args, ref index, out string? text) && bool.TryParse(text, out value);
	}

	private static bool TryReadIntValue(string[] args, ref int index, out int value) {
		value = default;
		return TryReadValue(args, ref index, out string? text) && int.TryParse(text, out value);
	}

	private static bool TryReadValue(string[] args, ref int index, out string? value) {
		value = null;
		int nextIndex = index + 1;

		if (nextIndex >= args.Length) {
			return false;
		}

		value = args[nextIndex];
		index = nextIndex;
		return true;
	}
}
