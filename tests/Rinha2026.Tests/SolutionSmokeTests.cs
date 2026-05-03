namespace Rinha2026.Tests;

public sealed class SolutionSmokeTests {
	[Fact]
	public void CoreAssemblyMarkerIsLoadable() {
		Assert.NotNull(typeof(Rinha2026.Core.CoreAssemblyMarker));
	}
}
