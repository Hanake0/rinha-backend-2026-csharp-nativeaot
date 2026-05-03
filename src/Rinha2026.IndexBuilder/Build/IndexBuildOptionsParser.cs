namespace Rinha2026.IndexBuilder.Build;

public static class IndexBuildOptionsParser {
	public static IndexBuildOptions? TryParse(string[] args, out string? errorMessage) {
		ArgumentNullException.ThrowIfNull(args);

		string? inputPath = null;
		string? outputDirectory = null;
		int dimension = 14;
		int paddedDimension = 16;

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
				default:
					errorMessage = $"Unknown argument '{args[index]}'.";
					return null;
			}
		}

		if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputDirectory)) {
			errorMessage = "Usage: --input <path-to-references.json.gz> --output <artifact-directory> [--dimension 14] [--padded-dimension 16]";
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

		errorMessage = null;
		return new IndexBuildOptions {
			Dimension = dimension,
			InputPath = inputPath,
			OutputDirectory = outputDirectory,
			PaddedDimension = paddedDimension,
		};
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
