namespace Rinha2026.Core.Search;

public readonly record struct HierarchicalSearchTrace(
	int SelectedParentCount,
	int SelectedLeafCount,
	int CandidateScanCount,
	int CandidateRerankCount,
	int MaxSelectedLeafSize,
	int MinSelectedLeafSize,
	int PrunedLeafCount,
	int PrunedCandidateCount,
	int SecondaryCandidateScanCount);
