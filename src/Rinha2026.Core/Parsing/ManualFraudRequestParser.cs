using Rinha2026.Core.Model;

namespace Rinha2026.Core.Parsing;

public static class ManualFraudRequestParser {
	public static bool TryParse(ReadOnlySpan<byte> utf8Json, out FraudRequest request) =>
		ReferenceFraudRequestParser.TryParse(utf8Json, out request);
}
