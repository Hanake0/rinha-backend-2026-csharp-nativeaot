namespace Rinha2026.Core.Search;

public readonly record struct SearchHit(
	int Index,
	float Distance,
	bool IsFraud);
