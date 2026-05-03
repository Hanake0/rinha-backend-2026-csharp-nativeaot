using System.Text;

namespace Rinha2026.Benchmarks.Support;

internal sealed class TemporaryRuntimeData : IDisposable {
	private TemporaryRuntimeData(
		TemporaryArtifactCorpus corpus,
		string mccRiskPath,
		string normalizationPath) {
		this.Corpus = corpus;
		this.MccRiskPath = mccRiskPath;
		this.NormalizationPath = normalizationPath;
	}

	public TemporaryArtifactCorpus Corpus { get; }

	public string IndexDirectory => this.Corpus.IndexDirectory;

	public string MccRiskPath { get; }

	public string NormalizationPath { get; }

	public static async Task<TemporaryRuntimeData> CreateAsync(params (float[] Vector, string Label)[] records) {
		TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(records);

		try {
			string mccRiskPath = Path.Combine(corpus.RootPath, "mcc_risk.json");
			string normalizationPath = Path.Combine(corpus.RootPath, "normalization.json");

			await File.WriteAllTextAsync(
				mccRiskPath,
				"""
				{
				  "5411": 0.15,
				  "5812": 0.30,
				  "5912": 0.20,
				  "5944": 0.45,
				  "7801": 0.80,
				  "7802": 0.75,
				  "7995": 0.85,
				  "4511": 0.35,
				  "5311": 0.25,
				  "5999": 0.50
				}
				""",
				Encoding.UTF8);
			await File.WriteAllTextAsync(
				normalizationPath,
				"""
				{
				  "max_amount": 10000,
				  "max_installments": 12,
				  "amount_vs_avg_ratio": 10,
				  "max_minutes": 1440,
				  "max_km": 1000,
				  "max_tx_count_24h": 20,
				  "max_merchant_avg_amount": 10000
				}
				""",
				Encoding.UTF8);

			return new TemporaryRuntimeData(corpus, mccRiskPath, normalizationPath);
		} catch {
			corpus.Dispose();
			throw;
		}
	}

	public void Dispose() => this.Corpus.Dispose();
}
