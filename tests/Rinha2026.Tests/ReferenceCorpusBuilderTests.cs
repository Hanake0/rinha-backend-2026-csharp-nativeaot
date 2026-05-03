using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Rinha2026.Core.Indexing;
using Rinha2026.IndexBuilder.Build;

namespace Rinha2026.Tests;

public sealed class ReferenceCorpusBuilderTests {
	[Fact]
	public async Task BuildAsyncWritesManifestVectorsAndLabels() {
		string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try {
			string inputPath = Path.Combine(tempRoot, "references.json.gz");
			string outputDirectory = Path.Combine(tempRoot, "artifacts");

			await WriteCompressedSampleAsync(inputPath);

			IndexBuildOptions options = new() {
				InputPath = inputPath,
				KMeansIterations = 2,
				Level1ClusterCount = 2,
				Level2ClustersPerLevel1 = 2,
				OutputDirectory = outputDirectory,
				TrainingSampleSize = 2,
			};

			IndexManifest manifest = await ReferenceCorpusBuilder.BuildAsync(options, CancellationToken.None);

			Assert.Equal(14, manifest.Dimension);
			Assert.Equal(16, manifest.PaddedDimension);
			Assert.Equal(2, manifest.VectorCount);

			string q8Path = Path.Combine(outputDirectory, manifest.QuantizedVectorFile);
			string f16Path = Path.Combine(outputDirectory, manifest.RerankVectorFile);
			string labelPath = Path.Combine(outputDirectory, manifest.LabelBitsetFile);
			string level1CentroidPath = Path.Combine(outputDirectory, manifest.Level1CentroidFile);
			string leafCentroidPath = Path.Combine(outputDirectory, manifest.LeafCentroidFile);
			string postingOffsetsPath = Path.Combine(outputDirectory, manifest.LeafPostingOffsetsFile);
			string postingIdsPath = Path.Combine(outputDirectory, manifest.LeafPostingIdsFile);
			string manifestPath = Path.Combine(outputDirectory, "manifest.json");

			Assert.True(File.Exists(manifestPath));
			Assert.Equal("HierarchicalBeamIvf", manifest.IndexKind);
			Assert.Equal(2, manifest.Level1ClusterCount);
			Assert.Equal(2, manifest.Level2ClustersPerLevel1);
			Assert.Equal(32, new FileInfo(q8Path).Length);
			Assert.Equal(64, new FileInfo(f16Path).Length);
			Assert.Equal(1, new FileInfo(labelPath).Length);
			Assert.Equal(128, new FileInfo(level1CentroidPath).Length);
			Assert.Equal(256, new FileInfo(leafCentroidPath).Length);
			Assert.Equal(20, new FileInfo(postingOffsetsPath).Length);
			Assert.Equal(8, new FileInfo(postingIdsPath).Length);

			byte[] labels = await File.ReadAllBytesAsync(labelPath);
			Assert.Equal(0b0000_0010, labels[0]);

			IndexManifest? reloadedManifest = JsonSerializer.Deserialize<IndexManifest>(
				await File.ReadAllTextAsync(manifestPath),
				new JsonSerializerOptions {
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				});
			Assert.NotNull(reloadedManifest);
			Assert.Equal(2, reloadedManifest.VectorCount);
		} finally {
			Directory.Delete(tempRoot, recursive: true);
		}
	}

	private static async Task WriteCompressedSampleAsync(string path) {
		string json = """
		[
		  {
		    "vector": [0.01, 0.0833, 0.05, 0.8261, 0.1667, -1, -1, 0.0432, 0.25, 0, 1, 0, 0.2, 0.0416],
		    "label": "legit"
		  },
		  {
		    "vector": [0.5796, 0.9167, 1.0, 0.0435, 0, 0.0056, 0.4394, 0.4598, 0.4, 1, 0, 1, 0.85, 0.0032],
		    "label": "fraud"
		  }
		]
		""";

		await using FileStream output = File.Create(path);
		await using GZipStream gzip = new(output, CompressionMode.Compress);
		byte[] payload = Encoding.UTF8.GetBytes(json);
		await gzip.WriteAsync(payload);
	}
}
