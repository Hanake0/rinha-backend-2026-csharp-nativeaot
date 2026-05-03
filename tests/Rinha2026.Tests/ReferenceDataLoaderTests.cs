using Rinha2026.Core.Detection;

namespace Rinha2026.Tests;

public sealed class ReferenceDataLoaderTests {
	[Fact]
	public async Task LoadersReadOfficialJsonShapes() {
		string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try {
			string mccRiskPath = Path.Combine(tempRoot, "mcc_risk.json");
			string normalizationPath = Path.Combine(tempRoot, "normalization.json");

			await File.WriteAllTextAsync(mccRiskPath, """{ "5411": 0.15, "7802": 0.75 }""");
			await File.WriteAllTextAsync(
				normalizationPath,
				"""{ "max_amount": 10000, "max_installments": 12, "amount_vs_avg_ratio": 10, "max_minutes": 1440, "max_km": 1000, "max_tx_count_24h": 20, "max_merchant_avg_amount": 10000 }""");

			MccRiskTable mccRiskTable = ReferenceDataLoader.LoadMccRiskTable(mccRiskPath);
			NormalizationConstants normalization = ReferenceDataLoader.LoadNormalizationConstants(normalizationPath);

			Assert.Equal(0.15f, mccRiskTable.GetRisk(5411));
			Assert.Equal(0.5f, mccRiskTable.GetRisk(9999));
			Assert.Equal(10_000d, normalization.MaxAmount);
			Assert.Equal(1_440d, normalization.MaxMinutes);
		} finally {
			Directory.Delete(tempRoot, recursive: true);
		}
	}
}
