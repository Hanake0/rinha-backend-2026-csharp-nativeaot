namespace Rinha2026.Core.Configuration;

public enum DistanceMetric {
	SquaredL2 = 0,
}

public enum IndexKind {
	ExactSampleOnly = 0,
	FlatIvf = 1,
	HierarchicalBeamIvf = 2,
}

public enum ParserMode {
	Manual = 0,
	ReferenceStj = 1,
}

public enum ResponseMode {
	PrecomputedTable = 0,
}

public enum TransportMode {
	Tcp = 0,
	UnixDomainSocket = 1,
}
