using System.Globalization;
using System.IO.Compression;
using System.Text;

using Rinha2026.IndexBuilder.Build;

namespace Rinha2026.Tests.Support;

internal sealed class TemporaryArtifactCorpus : IDisposable {
	private TemporaryArtifactCorpus(string rootPath, string indexDirectory) {
		this.RootPath = rootPath;
		this.IndexDirectory = indexDirectory;
	}

	public string IndexDirectory { get; }

	public string RootPath { get; }

	public static async Task<TemporaryArtifactCorpus> CreateAsync(params (float[] Vector, string Label)[] records) {
		ArgumentNullException.ThrowIfNull(records);

		string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string inputPath = Path.Combine(rootPath, "references.json.gz");
		string indexDirectory = Path.Combine(rootPath, "artifacts");
		Directory.CreateDirectory(rootPath);

		await WriteCompressedReferenceJsonAsync(inputPath, records);

		IndexBuildOptions options = new() {
			InputPath = inputPath,
			OutputDirectory = indexDirectory,
		};

		await ReferenceCorpusBuilder.BuildAsync(options, CancellationToken.None);
		return new TemporaryArtifactCorpus(rootPath, indexDirectory);
	}

	public void Dispose() {
		if (Directory.Exists(this.RootPath)) {
			Directory.Delete(this.RootPath, recursive: true);
		}
	}

	private static async Task WriteCompressedReferenceJsonAsync(
		string path,
		(float[] Vector, string Label)[] records) {
		StringBuilder builder = new();
		builder.AppendLine("[");

		for (int recordIndex = 0; recordIndex < records.Length; recordIndex++) {
			(float[] vector, string label) = records[recordIndex];
			builder.Append("  { \"vector\": [");

			for (int valueIndex = 0; valueIndex < vector.Length; valueIndex++) {
				if (valueIndex > 0) {
					builder.Append(", ");
				}

				builder.Append(vector[valueIndex].ToString("G9", CultureInfo.InvariantCulture));
			}

			builder.Append("], \"label\": \"");
			builder.Append(label);
			builder.Append("\" }");

			if (recordIndex < (records.Length - 1)) {
				builder.Append(',');
			}

			builder.AppendLine();
		}

		builder.Append(']');

		await using FileStream output = File.Create(path);
		await using GZipStream gzip = new(output, CompressionMode.Compress);
		byte[] payload = Encoding.UTF8.GetBytes(builder.ToString());
		await gzip.WriteAsync(payload);
	}
}
