using System.Text.Json;

namespace Rinha2026.Core.Indexing;

public sealed class FlatArtifactSet : IDisposable {
	private readonly MemoryMappedReadOnlyBuffer quantizedVectors;
	private readonly MemoryMappedReadOnlyBuffer rerankVectors;
	private readonly MemoryMappedReadOnlyBuffer labelBitset;
	private bool disposed;

	private FlatArtifactSet(
		string indexDirectory,
		IndexManifest manifest,
		MemoryMappedReadOnlyBuffer quantizedVectors,
		MemoryMappedReadOnlyBuffer rerankVectors,
		MemoryMappedReadOnlyBuffer labelBitset) {
		this.IndexDirectory = indexDirectory;
		this.Manifest = manifest;
		this.quantizedVectors = quantizedVectors;
		this.rerankVectors = rerankVectors;
		this.labelBitset = labelBitset;
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
		MemoryMappedReadOnlyBuffer labelBitset = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.LabelBitsetFile));

		try {
			ValidateBufferLengths(manifest, quantizedVectors, rerankVectors, labelBitset);
			return new FlatArtifactSet(indexDirectory, manifest, quantizedVectors, rerankVectors, labelBitset);
		} catch {
			quantizedVectors.Dispose();
			rerankVectors.Dispose();
			labelBitset.Dispose();
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

	public void Dispose() {
		if (this.disposed) {
			return;
		}

		this.quantizedVectors.Dispose();
		this.rerankVectors.Dispose();
		this.labelBitset.Dispose();
		this.disposed = true;
	}

	private static void ValidateBufferLengths(
		IndexManifest manifest,
		MemoryMappedReadOnlyBuffer quantizedVectors,
		MemoryMappedReadOnlyBuffer rerankVectors,
		MemoryMappedReadOnlyBuffer labelBitset) {
		long expectedQuantizedLength = checked(manifest.VectorCount * manifest.PaddedDimension);
		long expectedRerankLength = checked(manifest.VectorCount * manifest.PaddedDimension * sizeof(ushort));
		long expectedLabelLength = checked((manifest.VectorCount + 7) / 8);

		if (quantizedVectors.Length != expectedQuantizedLength) {
			throw new InvalidDataException(
				$"Unexpected quantized vector file length. Expected {expectedQuantizedLength}, found {quantizedVectors.Length}.");
		}

		if (rerankVectors.Length != expectedRerankLength) {
			throw new InvalidDataException(
				$"Unexpected rerank vector file length. Expected {expectedRerankLength}, found {rerankVectors.Length}.");
		}

		if (labelBitset.Length != expectedLabelLength) {
			throw new InvalidDataException(
				$"Unexpected label bitset file length. Expected {expectedLabelLength}, found {labelBitset.Length}.");
		}
	}
}
