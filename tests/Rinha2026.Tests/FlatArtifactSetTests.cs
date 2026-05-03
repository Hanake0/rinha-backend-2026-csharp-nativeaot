using Rinha2026.Core.Indexing;
using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class FlatArtifactSetTests {
	[Fact]
	public async Task LoadReadsManifestAndLabelBitset() {
		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			(CreateVector(0f, 0f, 0f), "legit"),
			(CreateVector(1f, 1f, 1f), "fraud"),
			(CreateVector(0.5f, 0.5f, 0.5f), "legit"));

		using FlatArtifactSet artifactSet = FlatArtifactSet.Load(corpus.IndexDirectory);

		Assert.Equal(3, artifactSet.VectorCount);
		Assert.Equal(16, artifactSet.PaddedDimension);
		Assert.False(artifactSet.IsFraud(0));
		Assert.True(artifactSet.IsFraud(1));
		Assert.False(artifactSet.IsFraud(2));
		Assert.Equal(48, artifactSet.GetQuantizedVectors().Length);
		Assert.Equal(96, artifactSet.GetRerankVectors().Length);
	}

	[Fact]
	public async Task LoadRejectsTruncatedRerankFile() {
		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			(CreateVector(0f, 0f, 0f), "legit"),
			(CreateVector(1f, 1f, 1f), "fraud"));

		string rerankPath = Path.Combine(corpus.IndexDirectory, "vectors.f16.bin");
		byte[] bytes = await File.ReadAllBytesAsync(rerankPath);
		await File.WriteAllBytesAsync(rerankPath, bytes.AsSpan(0, bytes.Length - 2).ToArray());

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => FlatArtifactSet.Load(corpus.IndexDirectory));
		Assert.Contains("rerank vector file length", exception.Message, StringComparison.Ordinal);
	}

	private static float[] CreateVector(float first, float second, float third) {
		float[] vector = new float[14];
		vector[0] = first;
		vector[1] = second;
		vector[2] = third;
		return vector;
	}
}
