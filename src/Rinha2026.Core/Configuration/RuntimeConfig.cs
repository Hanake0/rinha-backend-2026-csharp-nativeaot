namespace Rinha2026.Core.Configuration;

public readonly record struct RuntimeConfig(
	RuntimeDetectionConfig Detection,
	RuntimeDatasetConfig Dataset,
	RuntimeSearchConfig Search,
	RuntimeHttpConfig Http,
	RuntimeDiagnosticsConfig Diagnostics = default);

public readonly record struct RuntimeDetectionConfig(
	int TopK,
	double ApprovalThreshold) {
	public int ResponseCount => checked(this.TopK + 1);

	public int MinDeniedCount => ComputeMinDeniedCount(this.TopK, this.ApprovalThreshold);

	public int MaxApprovedCount => this.MinDeniedCount - 1;

	public static int ComputeMinDeniedCount(int topK, double approvalThreshold) {
		for (int fraudCount = 0; fraudCount <= topK; fraudCount++) {
			if ((fraudCount / (double)topK) >= approvalThreshold) {
				return fraudCount;
			}
		}

		return topK;
	}
}

public readonly record struct RuntimeDatasetConfig(
	string IndexDirectory,
	string MccRiskPath,
	string NormalizationPath);

public readonly record struct RuntimeSearchConfig(
	int BeamLevel1,
	int BeamLevel2,
	int Dimension,
	DistanceMetric DistanceMetric,
	IndexKind IndexKind,
	int PaddedDimension,
	int RerankCount,
	int BoundaryRerankCount,
	bool UseLeafRadiusPruning,
	bool UseLastTransactionPartitionPruning);

public readonly record struct RuntimeHttpConfig(
	ParserMode ParserMode,
	ResponseMode ResponseMode,
	TransportMode TransportMode,
	string? UnixSocketPath);

public readonly record struct RuntimeDiagnosticsConfig(
	bool ProfileEnabled,
	int ProfileSampleCapacity,
	int ProfileSamplingStride);
