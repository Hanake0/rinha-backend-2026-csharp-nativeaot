using BenchmarkDotNet.Attributes;

using Rinha2026.Benchmarks.Support;
using Rinha2026.Core.Indexing;
using Rinha2026.Core.Search;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ExactFlatSearchBenchmarks {
	private TemporaryArtifactCorpus corpus = default!;
	private ExactFlatSearchEngine engine = default!;
	private SearchHit[] hits = default!;
	private float[] query = default!;
	private FlatArtifactSet artifactSet = default!;

	[GlobalSetup]
	public async Task Setup() {
		Random random = new(20260502);
		var records = new (float[] Vector, string Label)[4096];

		for (int index = 0; index < records.Length; index++) {
			float[] vector = new float[14];

			for (int dimension = 0; dimension < vector.Length; dimension++) {
				vector[dimension] = (float)random.NextDouble();
			}

			records[index] = (vector, (index & 1) == 0 ? "legit" : "fraud");
		}

		this.corpus = await TemporaryArtifactCorpus.CreateAsync(records);
		this.artifactSet = FlatArtifactSet.Load(this.corpus.IndexDirectory);
		this.engine = new ExactFlatSearchEngine(this.artifactSet);
		this.hits = new SearchHit[5];
		this.query = new float[16];

		for (int dimension = 0; dimension < 14; dimension++) {
			this.query[dimension] = (float)random.NextDouble();
		}
	}

	[GlobalCleanup]
	public void Cleanup() {
		this.artifactSet.Dispose();
		this.corpus.Dispose();
	}

	[Benchmark]
	public int SearchTop5() => this.engine.CountFraud(this.query, this.hits);
}
