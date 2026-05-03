using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rinha2026.Core.Indexing;

public sealed class FlatArtifactSet : IDisposable {
	private readonly MemoryMappedReadOnlyBuffer quantizedVectors;
	private readonly MemoryMappedReadOnlyBuffer rerankVectors;
	private readonly MemoryMappedReadOnlyBuffer? fullPrecisionRerankVectors;
	private readonly MemoryMappedReadOnlyBuffer labelBitset;
	private readonly MemoryMappedReadOnlyBuffer? originalVectorIds;
	private bool disposed;

	private FlatArtifactSet(
		string indexDirectory,
		IndexManifest manifest,
		MemoryMappedReadOnlyBuffer quantizedVectors,
		MemoryMappedReadOnlyBuffer rerankVectors,
		MemoryMappedReadOnlyBuffer? fullPrecisionRerankVectors,
		MemoryMappedReadOnlyBuffer labelBitset,
		MemoryMappedReadOnlyBuffer? originalVectorIds) {
		this.IndexDirectory = indexDirectory;
		this.Manifest = manifest;
		this.quantizedVectors = quantizedVectors;
		this.rerankVectors = rerankVectors;
		this.fullPrecisionRerankVectors = fullPrecisionRerankVectors;
		this.labelBitset = labelBitset;
		this.originalVectorIds = originalVectorIds;
	}

	public string IndexDirectory { get; }

	public IndexManifest Manifest { get; }

	public int PaddedDimension => this.Manifest.PaddedDimension;

	public long VectorCount => this.Manifest.VectorCount;

	public static FlatArtifactSet Load(string indexDirectory) {
		ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);

		string manifestPath = Path.Combine(indexDirectory, "manifest.json");
		IndexManifest? manifest = JsonSerializer.Deserialize(
			File.ReadAllText(manifestPath),
			IndexManifestJsonContext.Default.IndexManifest);

		if (manifest is null) {
			throw new InvalidDataException("Index manifest could not be deserialized.");
		}

		MemoryMappedReadOnlyBuffer quantizedVectors = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.QuantizedVectorFile));
		MemoryMappedReadOnlyBuffer rerankVectors = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.RerankVectorFile));
		MemoryMappedReadOnlyBuffer? fullPrecisionRerankVectors = null;
		MemoryMappedReadOnlyBuffer labelBitset = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.LabelBitsetFile));
		MemoryMappedReadOnlyBuffer? originalVectorIds = null;

		if (!string.IsNullOrWhiteSpace(manifest.FullPrecisionRerankVectorFile)) {
			fullPrecisionRerankVectors = MemoryMappedReadOnlyBuffer.OpenRead(
				Path.Combine(indexDirectory, manifest.FullPrecisionRerankVectorFile));
		}

		if (!string.IsNullOrWhiteSpace(manifest.OriginalVectorIdFile)) {
			originalVectorIds = MemoryMappedReadOnlyBuffer.OpenRead(
				Path.Combine(indexDirectory, manifest.OriginalVectorIdFile));
		}

		try {
			ValidateBufferLengths(
				manifest,
				quantizedVectors,
				rerankVectors,
				fullPrecisionRerankVectors,
				labelBitset,
				originalVectorIds);
			return new FlatArtifactSet(
				indexDirectory,
				manifest,
				quantizedVectors,
				rerankVectors,
				fullPrecisionRerankVectors,
				labelBitset,
				originalVectorIds);
		} catch {
			quantizedVectors.Dispose();
			rerankVectors.Dispose();
			fullPrecisionRerankVectors?.Dispose();
			labelBitset.Dispose();
			originalVectorIds?.Dispose();
			throw;
		}
	}

	public ReadOnlySpan<byte> GetQuantizedVectors() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return this.quantizedVectors.GetSpan();
	}

	public ReadOnlySpan<byte> GetRerankVectors() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return this.rerankVectors.GetSpan();
	}

	public bool HasFullPrecisionRerankVectors => this.fullPrecisionRerankVectors is not null;

	public ReadOnlySpan<byte> GetFullPrecisionRerankVectors() {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if (this.fullPrecisionRerankVectors is null) {
			throw new InvalidOperationException("The loaded artifact set does not include full-precision rerank vectors.");
		}

		return this.fullPrecisionRerankVectors.GetSpan();
	}

	public bool IsFraud(long vectorIndex) {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if ((vectorIndex < 0) || (vectorIndex >= this.VectorCount)) {
			throw new ArgumentOutOfRangeException(nameof(vectorIndex));
		}

		ReadOnlySpan<byte> labels = this.labelBitset.GetSpan();
		int byteOffset = checked((int)(vectorIndex >> 3));
		int bitOffset = (int)(vectorIndex & 0b111);
		return (labels[byteOffset] & (1 << bitOffset)) != 0;
	}

	public int GetStableOrderKey(int vectorIndex) {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if ((vectorIndex < 0) || (vectorIndex >= this.VectorCount)) {
			throw new ArgumentOutOfRangeException(nameof(vectorIndex));
		}

		if (this.originalVectorIds is null) {
			return vectorIndex;
		}

		ReadOnlySpan<int> originalIds = MemoryMarshal.Cast<byte, int>(this.originalVectorIds.GetSpan());
		return originalIds[vectorIndex];
	}

	public void Dispose() {
		if (this.disposed) {
			return;
		}

		this.quantizedVectors.Dispose();
		this.rerankVectors.Dispose();
		this.fullPrecisionRerankVectors?.Dispose();
		this.labelBitset.Dispose();
		this.originalVectorIds?.Dispose();
		this.disposed = true;
	}

	private static void ValidateBufferLengths(
		IndexManifest manifest,
		MemoryMappedReadOnlyBuffer quantizedVectors,
		MemoryMappedReadOnlyBuffer rerankVectors,
		MemoryMappedReadOnlyBuffer? fullPrecisionRerankVectors,
		MemoryMappedReadOnlyBuffer labelBitset,
		MemoryMappedReadOnlyBuffer? originalVectorIds) {
		long expectedQuantizedLength = checked(manifest.VectorCount * manifest.PaddedDimension);
		long expectedRerankLength = checked(manifest.VectorCount * manifest.PaddedDimension * sizeof(ushort));
		long expectedFullPrecisionRerankLength = checked(manifest.VectorCount * manifest.PaddedDimension * sizeof(float));
		long expectedOriginalVectorIdLength = checked(manifest.VectorCount * sizeof(int));
		long expectedLabelLength = checked((manifest.VectorCount + 7) / 8);

		if (quantizedVectors.Length != expectedQuantizedLength) {
			throw new InvalidDataException(
				$"Unexpected quantized vector file length. Expected {expectedQuantizedLength}, found {quantizedVectors.Length}.");
		}

		if (rerankVectors.Length != expectedRerankLength) {
			throw new InvalidDataException(
				$"Unexpected rerank vector file length. Expected {expectedRerankLength}, found {rerankVectors.Length}.");
		}

		if ((fullPrecisionRerankVectors is not null) && (fullPrecisionRerankVectors.Length != expectedFullPrecisionRerankLength)) {
			throw new InvalidDataException(
				$"Unexpected full-precision rerank vector file length. Expected {expectedFullPrecisionRerankLength}, found {fullPrecisionRerankVectors.Length}.");
		}

		if (labelBitset.Length != expectedLabelLength) {
			throw new InvalidDataException(
				$"Unexpected label bitset file length. Expected {expectedLabelLength}, found {labelBitset.Length}.");
		}

		if ((originalVectorIds is not null) && (originalVectorIds.Length != expectedOriginalVectorIdLength)) {
			throw new InvalidDataException(
				$"Unexpected original-vector-id file length. Expected {expectedOriginalVectorIdLength}, found {originalVectorIds.Length}.");
		}
	}
}
