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
		string f32Path = Path.Combine(options.OutputDirectory, "vectors.f32.bin");
		string originalIdsPath = Path.Combine(options.OutputDirectory, "vectors.original.ids.bin");
		string labelsPath = Path.Combine(options.OutputDirectory, "labels.bitset.bin");
		long vectorCount = 0;

		await using (FileStream inputStream = File.OpenRead(options.InputPath))
		await using (GZipStream gzipStream = new(inputStream, CompressionMode.Decompress))
		await using (FileStream q8Stream = CreateOutputStream(q8Path))
		await using (FileStream f16Stream = CreateOutputStream(f16Path))
		await using (FileStream f32Stream = CreateOutputStream(f32Path))
		await using (FileStream originalIdStream = CreateOutputStream(originalIdsPath))
		await using (LabelBitWriter labelWriter = new(CreateOutputStream(labelsPath))) {
			float[] paddedVector = new float[options.PaddedDimension];
			byte[] q8Buffer = new byte[options.PaddedDimension];
			byte[] f16Buffer = new byte[checked(options.PaddedDimension * sizeof(ushort))];
			byte[] f32Buffer = new byte[checked(options.PaddedDimension * sizeof(float))];
			byte[] originalIdBuffer = new byte[sizeof(int)];

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
				Buffer.BlockCopy(paddedVector, 0, f32Buffer, 0, f32Buffer.Length);

				await q8Stream.WriteAsync(q8Buffer, cancellationToken);
				await f16Stream.WriteAsync(f16Buffer, cancellationToken);
				await f32Stream.WriteAsync(f32Buffer, cancellationToken);
				BitConverter.TryWriteBytes(originalIdBuffer, checked((int)vectorCount));
				await originalIdStream.WriteAsync(originalIdBuffer, cancellationToken);
				await labelWriter.WriteAsync(IsFraud(record.Label), cancellationToken);

				vectorCount++;
			}

			await labelWriter.FlushAsync(cancellationToken);
		}

		IndexManifest manifest = new() {
			Dimension = options.Dimension,
			FullPrecisionRerankVectorFile = "vectors.f32.bin",
			OriginalVectorIdFile = "vectors.original.ids.bin",
			PaddedDimension = options.PaddedDimension,
			VectorCount = vectorCount,
		};

		manifest = await HierarchicalIndexBuilder.BuildAsync(options, manifest, cancellationToken);

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
