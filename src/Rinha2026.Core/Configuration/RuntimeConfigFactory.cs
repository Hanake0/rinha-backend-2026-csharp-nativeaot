namespace Rinha2026.Core.Configuration;

public static class RuntimeConfigFactory {
	public static RuntimeConfig Create(RuntimeSettings settings, string contentRootPath) {
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

		ValidateDetection(settings.Detection);
		ValidateSearch(settings.Search);
		ValidateDataset(settings.Dataset);
		ValidateDiagnostics(settings.Diagnostics);

		RuntimeDetectionConfig detection = new(
			settings.Detection.TopK,
			settings.Detection.ApprovalThreshold);

		RuntimeDatasetConfig dataset = new(
			GetAbsolutePath(contentRootPath, settings.Dataset.IndexDirectory),
			GetAbsolutePath(contentRootPath, settings.Dataset.MccRiskPath),
			GetAbsolutePath(contentRootPath, settings.Dataset.NormalizationPath));

		RuntimeSearchConfig search = new(
			settings.Search.BeamLevel1,
			settings.Search.BeamLevel2,
			settings.Search.Dimension,
			settings.Search.DistanceMetric,
			settings.Search.IndexKind,
			settings.Search.PaddedDimension,
			settings.Search.RerankCount,
			settings.Search.UseLastTransactionPartitionPruning);

		RuntimeHttpConfig http = new(
			settings.Http.ParserMode,
			settings.Http.ResponseMode,
			settings.Http.TransportMode);

		RuntimeDiagnosticsConfig diagnostics = new(
			settings.Diagnostics.ProfileEnabled,
			settings.Diagnostics.ProfileSampleCapacity,
			settings.Diagnostics.ProfileSamplingStride);

		return new RuntimeConfig(detection, dataset, search, http, diagnostics);
	}

	private static string GetAbsolutePath(string contentRootPath, string path) {
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return Path.GetFullPath(path, contentRootPath);
	}

	private static void ValidateDataset(DatasetSettings settings) {
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentException.ThrowIfNullOrWhiteSpace(settings.IndexDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(settings.MccRiskPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(settings.NormalizationPath);
	}

	private static void ValidateDetection(DetectionSettings settings) {
		ArgumentNullException.ThrowIfNull(settings);

		if (settings.TopK <= 0) {
			throw new ArgumentOutOfRangeException(nameof(settings.TopK), settings.TopK, "TopK must be greater than zero.");
		}

		if ((settings.ApprovalThreshold < 0d) || (settings.ApprovalThreshold > 1d)) {
			throw new ArgumentOutOfRangeException(
				nameof(settings.ApprovalThreshold),
				settings.ApprovalThreshold,
				"ApprovalThreshold must be between 0.0 and 1.0.");
		}
	}

	private static void ValidateSearch(SearchSettings settings) {
		ArgumentNullException.ThrowIfNull(settings);

		if (settings.Dimension <= 0) {
			throw new ArgumentOutOfRangeException(nameof(settings.Dimension), settings.Dimension, "Dimension must be greater than zero.");
		}

		if (settings.PaddedDimension < settings.Dimension) {
			throw new ArgumentOutOfRangeException(
				nameof(settings.PaddedDimension),
				settings.PaddedDimension,
				"PaddedDimension must be greater than or equal to Dimension.");
		}

		if (settings.BeamLevel1 <= 0) {
			throw new ArgumentOutOfRangeException(nameof(settings.BeamLevel1), settings.BeamLevel1, "BeamLevel1 must be greater than zero.");
		}

		if (settings.BeamLevel2 <= 0) {
			throw new ArgumentOutOfRangeException(nameof(settings.BeamLevel2), settings.BeamLevel2, "BeamLevel2 must be greater than zero.");
		}

		if (settings.RerankCount <= 0) {
			throw new ArgumentOutOfRangeException(nameof(settings.RerankCount), settings.RerankCount, "RerankCount must be greater than zero.");
		}
	}

	private static void ValidateDiagnostics(DiagnosticsSettings settings) {
		ArgumentNullException.ThrowIfNull(settings);

		if (settings.ProfileSampleCapacity <= 0) {
			throw new ArgumentOutOfRangeException(
				nameof(settings.ProfileSampleCapacity),
				settings.ProfileSampleCapacity,
				"ProfileSampleCapacity must be greater than zero.");
		}

		if (settings.ProfileSamplingStride <= 0) {
			throw new ArgumentOutOfRangeException(
				nameof(settings.ProfileSamplingStride),
				settings.ProfileSamplingStride,
				"ProfileSamplingStride must be greater than zero.");
		}
	}
}
