using BenchmarkDotNet.Attributes;

using Rinha2026.Benchmarks.Support;
using Rinha2026.Core.Indexing;
using Rinha2026.Core.Search;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class HierarchicalBeamSearchBenchmarks {
	private TemporaryArtifactCorpus corpus = default!;
	private ExactFlatSearchEngine exactEngine = default!;
	private FlatArtifactSet flatArtifacts = default!;
	private SearchHit[] hits = default!;
	private HierarchicalArtifactSet hierarchicalArtifacts = default!;
	private HierarchicalBeamSearchEngine hierarchicalEngine = default!;
	private float[] query = default!;

	[GlobalSetup]
	public async Task Setup() {
		Random random = new(20260502);
		var records = new (float[] Vector, string Label)[4096];

		for (int index = 0; index < records.Length; index++) {
			float[] vector = new float[14];
			float baseValue = (index % 8) / 7f;

			for (int dimension = 0; dimension < vector.Length; dimension++) {
				float jitter = (float)random.NextDouble() * 0.02f;
				vector[dimension] = Math.Clamp(baseValue + jitter, 0f, 1f);
			}

			records[index] = (vector, (index & 1) == 0 ? "legit" : "fraud");
		}

		this.corpus = await TemporaryArtifactCorpus.CreateAsync(records);
		this.flatArtifacts = FlatArtifactSet.Load(this.corpus.IndexDirectory);
		this.hierarchicalArtifacts = HierarchicalArtifactSet.Load(this.corpus.IndexDirectory);
		this.exactEngine = new ExactFlatSearchEngine(this.flatArtifacts);
		this.hierarchicalEngine = new HierarchicalBeamSearchEngine(this.hierarchicalArtifacts);
		this.hits = new SearchHit[5];
		this.query = new float[16];

		for (int dimension = 0; dimension < 14; dimension++) {
			this.query[dimension] = 0.43f;
		}
	}

	[GlobalCleanup]
	public void Cleanup() {
		this.hierarchicalArtifacts.Dispose();
		this.flatArtifacts.Dispose();
		this.corpus.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int ExactSearchTop5() => this.exactEngine.CountFraud(this.query, this.hits);

	[Benchmark]
	public int ApproximateSearchTop5() => this.hierarchicalEngine.CountFraud(
		this.query,
		beamLevel1: Math.Min(2, this.hierarchicalArtifacts.Level1ClusterCount),
		beamLevel2: Math.Min(8, this.hierarchicalArtifacts.LeafCount),
		rerankCount: 48,
		this.hits);
}
