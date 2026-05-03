using System.Runtime.InteropServices;

namespace Rinha2026.Core.Indexing;

public sealed class HierarchicalArtifactSet : IDisposable {
	private readonly MemoryMappedReadOnlyBuffer leafCentroids;
	private readonly MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts;
	private readonly MemoryMappedReadOnlyBuffer leafPostingIds;
	private readonly MemoryMappedReadOnlyBuffer leafPostingOffsets;
	private readonly MemoryMappedReadOnlyBuffer? leafRadii;
	private readonly MemoryMappedReadOnlyBuffer level1Centroids;
	private readonly sbyte[] quantizedLeafCentroids;
	private bool disposed;

	private HierarchicalArtifactSet(
		FlatArtifactSet flatArtifacts,
		MemoryMappedReadOnlyBuffer level1Centroids,
		MemoryMappedReadOnlyBuffer leafCentroids,
		MemoryMappedReadOnlyBuffer leafPostingOffsets,
		MemoryMappedReadOnlyBuffer leafPostingIds,
		MemoryMappedReadOnlyBuffer? leafRadii,
		MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts) {
		this.FlatArtifacts = flatArtifacts;
		this.level1Centroids = level1Centroids;
		this.leafCentroids = leafCentroids;
		this.leafPostingOffsets = leafPostingOffsets;
		this.leafPostingIds = leafPostingIds;
		this.leafRadii = leafRadii;
		this.leafWithoutHistoryCounts = leafWithoutHistoryCounts;
		this.quantizedLeafCentroids = QuantizeLeafCentroids(MemoryMarshal.Cast<byte, float>(leafCentroids.GetSpan()));
	}

	public FlatArtifactSet FlatArtifacts { get; }

	public int LeafCount => checked(this.FlatArtifacts.Manifest.Level1ClusterCount * this.FlatArtifacts.Manifest.Level2ClustersPerLevel1);

	public int Level1ClusterCount => this.FlatArtifacts.Manifest.Level1ClusterCount;

	public int Level2ClustersPerLevel1 => this.FlatArtifacts.Manifest.Level2ClustersPerLevel1;

	public bool HasLeafRadiusBounds => this.leafRadii is not null;

	public bool HasLastTransactionPartitioning => this.leafWithoutHistoryCounts is not null;

	public bool UsesIdentityPostings => string.Equals(
		this.FlatArtifacts.Manifest.PostingLayout,
		"IdentityLeafOrder",
		StringComparison.Ordinal);

	public static HierarchicalArtifactSet Load(string indexDirectory) {
		FlatArtifactSet flatArtifacts = FlatArtifactSet.Load(indexDirectory);
		IndexManifest manifest = flatArtifacts.Manifest;

		if ((manifest.Level1ClusterCount <= 0) || (manifest.Level2ClustersPerLevel1 <= 0)) {
			flatArtifacts.Dispose();
			throw new InvalidDataException("The index manifest does not describe a hierarchical coarse index.");
		}

		MemoryMappedReadOnlyBuffer level1Centroids = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.Level1CentroidFile));
		MemoryMappedReadOnlyBuffer leafCentroids = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.LeafCentroidFile));
		MemoryMappedReadOnlyBuffer leafPostingOffsets = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.LeafPostingOffsetsFile));
		MemoryMappedReadOnlyBuffer leafPostingIds = MemoryMappedReadOnlyBuffer.OpenRead(
			Path.Combine(indexDirectory, manifest.LeafPostingIdsFile));
		MemoryMappedReadOnlyBuffer? leafRadii = null;
		MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts = null;

		if (!string.IsNullOrWhiteSpace(manifest.LeafRadiusFile)) {
			leafRadii = MemoryMappedReadOnlyBuffer.OpenRead(
				Path.Combine(indexDirectory, manifest.LeafRadiusFile));
		}

		if (!string.IsNullOrWhiteSpace(manifest.LeafWithoutHistoryCountFile)) {
			leafWithoutHistoryCounts = MemoryMappedReadOnlyBuffer.OpenRead(
				Path.Combine(indexDirectory, manifest.LeafWithoutHistoryCountFile));
		}

		try {
			ValidateBufferLengths(
				manifest,
				level1Centroids,
				leafCentroids,
				leafPostingOffsets,
				leafPostingIds,
				leafRadii,
				leafWithoutHistoryCounts);
			return new HierarchicalArtifactSet(
				flatArtifacts,
				level1Centroids,
				leafCentroids,
				leafPostingOffsets,
				leafPostingIds,
				leafRadii,
				leafWithoutHistoryCounts);
		} catch {
			level1Centroids.Dispose();
			leafCentroids.Dispose();
			leafPostingOffsets.Dispose();
			leafPostingIds.Dispose();
			leafRadii?.Dispose();
			leafWithoutHistoryCounts?.Dispose();
			flatArtifacts.Dispose();
			throw;
		}
	}

	public ReadOnlySpan<float> GetLeafCentroids() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return MemoryMarshal.Cast<byte, float>(this.leafCentroids.GetSpan());
	}

	public ReadOnlySpan<int> GetLeafPostingIds() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return MemoryMarshal.Cast<byte, int>(this.leafPostingIds.GetSpan());
	}

	public ReadOnlySpan<int> GetLeafPostingOffsets() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return MemoryMarshal.Cast<byte, int>(this.leafPostingOffsets.GetSpan());
	}

	public ReadOnlySpan<sbyte> GetQuantizedLeafCentroids() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return this.quantizedLeafCentroids;
	}

	public ReadOnlySpan<float> GetLeafRadiusBounds() {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if (this.leafRadii is null) {
			throw new InvalidOperationException("The loaded artifact set does not include leaf radius metadata.");
		}

		return MemoryMarshal.Cast<byte, float>(this.leafRadii.GetSpan());
	}

	public ReadOnlySpan<int> GetLeafWithoutHistoryCounts() {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if (this.leafWithoutHistoryCounts is null) {
			throw new InvalidOperationException("The loaded artifact set does not include last-transaction partition metadata.");
		}

		return MemoryMarshal.Cast<byte, int>(this.leafWithoutHistoryCounts.GetSpan());
	}

	public ReadOnlySpan<float> GetLevel1Centroids() {
		ObjectDisposedException.ThrowIf(this.disposed, this);
		return MemoryMarshal.Cast<byte, float>(this.level1Centroids.GetSpan());
	}

	public int Warm() {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		int checksum = this.FlatArtifacts.Warm();
		checksum ^= this.level1Centroids.TouchEveryPage();
		checksum ^= this.leafCentroids.TouchEveryPage();
		checksum ^= this.leafPostingOffsets.TouchEveryPage();
		checksum ^= this.leafPostingIds.TouchEveryPage();

		if (this.leafRadii is not null) {
			checksum ^= this.leafRadii.TouchEveryPage();
		}

		if (this.leafWithoutHistoryCounts is not null) {
			checksum ^= this.leafWithoutHistoryCounts.TouchEveryPage();
		}

		ReadOnlySpan<byte> quantizedCentroids = MemoryMarshal.AsBytes<sbyte>(this.quantizedLeafCentroids);

		for (int index = 0; index < quantizedCentroids.Length; index += 4096) {
			checksum ^= quantizedCentroids[index];
		}

		if (!quantizedCentroids.IsEmpty) {
			checksum ^= quantizedCentroids[^1];
		}

		return checksum;
	}

	public void Dispose() {
		if (this.disposed) {
			return;
		}

		this.level1Centroids.Dispose();
		this.leafCentroids.Dispose();
		this.leafPostingOffsets.Dispose();
		this.leafPostingIds.Dispose();
		this.leafRadii?.Dispose();
		this.leafWithoutHistoryCounts?.Dispose();
		this.FlatArtifacts.Dispose();
		this.disposed = true;
	}

	private static void ValidateBufferLengths(
		IndexManifest manifest,
		MemoryMappedReadOnlyBuffer level1Centroids,
		MemoryMappedReadOnlyBuffer leafCentroids,
		MemoryMappedReadOnlyBuffer leafPostingOffsets,
		MemoryMappedReadOnlyBuffer leafPostingIds,
		MemoryMappedReadOnlyBuffer? leafRadii,
		MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts) {
		int leafCount = checked(manifest.Level1ClusterCount * manifest.Level2ClustersPerLevel1);
		long expectedLevel1CentroidsLength = checked(manifest.Level1ClusterCount * manifest.PaddedDimension * sizeof(float));
		long expectedLeafCentroidsLength = checked(leafCount * manifest.PaddedDimension * sizeof(float));
		long expectedLeafPostingOffsetsLength = checked((leafCount + 1L) * sizeof(int));
		long expectedLeafPostingIdsLength = checked(manifest.VectorCount * sizeof(int));

		if (level1Centroids.Length != expectedLevel1CentroidsLength) {
			throw new InvalidDataException(
				$"Unexpected level1 centroid file length. Expected {expectedLevel1CentroidsLength}, found {level1Centroids.Length}.");
		}

		if (leafCentroids.Length != expectedLeafCentroidsLength) {
			throw new InvalidDataException(
				$"Unexpected leaf centroid file length. Expected {expectedLeafCentroidsLength}, found {leafCentroids.Length}.");
		}

		if (leafPostingOffsets.Length != expectedLeafPostingOffsetsLength) {
			throw new InvalidDataException(
				$"Unexpected posting offset file length. Expected {expectedLeafPostingOffsetsLength}, found {leafPostingOffsets.Length}.");
		}

		if (leafPostingIds.Length != expectedLeafPostingIdsLength) {
			throw new InvalidDataException(
				$"Unexpected posting id file length. Expected {expectedLeafPostingIdsLength}, found {leafPostingIds.Length}.");
		}

		if ((leafRadii is not null) && (leafRadii.Length != (leafCount * sizeof(float)))) {
			throw new InvalidDataException(
				$"Unexpected leaf radius file length. Expected {leafCount * sizeof(float)}, found {leafRadii.Length}.");
		}

		if ((leafWithoutHistoryCounts is not null) && (leafWithoutHistoryCounts.Length != (leafCount * sizeof(int)))) {
			throw new InvalidDataException(
				$"Unexpected last-transaction partition count file length. Expected {leafCount * sizeof(int)}, found {leafWithoutHistoryCounts.Length}.");
		}
	}

	private static sbyte[] QuantizeLeafCentroids(ReadOnlySpan<float> leafCentroids) {
		sbyte[] quantized = new sbyte[leafCentroids.Length];
		VectorEncoding.EncodeQ8Symmetric(leafCentroids, quantized);
		return quantized;
	}
}
