using System.IO.Compression;
using System.Text.Json;

using Rinha2026.Core.Indexing;

namespace Rinha2026.IndexBuilder.Build;

public static class ReferenceCorpusBuilder {
	private static readonly JsonSerializerOptions StreamingJsonOptions = new() {
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	public static async Task<IndexManifest> BuildAsync(IndexBuildOptions options, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(options);
		Directory.CreateDirectory(options.OutputDirectory);

		string q8Path = Path.Combine(options.OutputDirectory, "vectors.q8.bin");
		string f16Path = Path.Combine(options.OutputDirectory, "vectors.f16.bin");
		string labelsPath = Path.Combine(options.OutputDirectory, "labels.bitset.bin");

		await using FileStream inputStream = File.OpenRead(options.InputPath);
		await using GZipStream gzipStream = new(inputStream, CompressionMode.Decompress);
		await using FileStream q8Stream = CreateOutputStream(q8Path);
		await using FileStream f16Stream = CreateOutputStream(f16Path);
		await using LabelBitWriter labelWriter = new(CreateOutputStream(labelsPath));

		float[] paddedVector = new float[options.PaddedDimension];
		byte[] q8Buffer = new byte[options.PaddedDimension];
		byte[] f16Buffer = new byte[checked(options.PaddedDimension * sizeof(ushort))];
		long vectorCount = 0;

		await foreach (ReferenceVectorRecord? record in JsonSerializer.DeserializeAsyncEnumerable<ReferenceVectorRecord>(
			gzipStream,
			StreamingJsonOptions,
			cancellationToken)) {
			if (record is null) {
				continue;
			}

			WritePaddedVector(record.Vector, paddedVector, options.Dimension, options.PaddedDimension);
			VectorEncoding.EncodeQ8Symmetric(paddedVector, q8Buffer);
			VectorEncoding.EncodeFloat16(paddedVector, f16Buffer);

			await q8Stream.WriteAsync(q8Buffer, cancellationToken);
			await f16Stream.WriteAsync(f16Buffer, cancellationToken);
			await labelWriter.WriteAsync(IsFraud(record.Label), cancellationToken);

			vectorCount++;
		}

		await labelWriter.FlushAsync(cancellationToken);

		IndexManifest manifest = new() {
			Dimension = options.Dimension,
			PaddedDimension = options.PaddedDimension,
			VectorCount = vectorCount,
		};

		string manifestPath = Path.Combine(options.OutputDirectory, "manifest.json");
		await File.WriteAllTextAsync(
			manifestPath,
			JsonSerializer.Serialize(manifest, ReferenceCorpusJsonContext.Default.IndexManifest),
			cancellationToken);

		return manifest;
	}

	private static FileStream CreateOutputStream(string path) => new(
		path,
		FileMode.Create,
		FileAccess.Write,
		FileShare.None,
		bufferSize: 64 * 1024,
		FileOptions.SequentialScan);

	private static bool IsFraud(string label) => string.Equals(label, "fraud", StringComparison.OrdinalIgnoreCase);

	private static void WritePaddedVector(float[] source, float[] destination, int dimension, int paddedDimension) {
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);

		if ((source.Length != dimension) && (source.Length != paddedDimension)) {
			throw new InvalidDataException($"Expected vectors with {dimension} or {paddedDimension} elements, received {source.Length}.");
		}

		Array.Clear(destination);
		source.AsSpan().CopyTo(destination);
	}
}
