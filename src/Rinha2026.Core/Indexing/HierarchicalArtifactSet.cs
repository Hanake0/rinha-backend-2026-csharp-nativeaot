using System.Runtime.InteropServices;

namespace Rinha2026.Core.Indexing;

public sealed class HierarchicalArtifactSet : IDisposable {
	private readonly MemoryMappedReadOnlyBuffer leafCentroids;
	private readonly MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts;
	private readonly MemoryMappedReadOnlyBuffer leafPostingIds;
	private readonly MemoryMappedReadOnlyBuffer leafPostingOffsets;
	private readonly MemoryMappedReadOnlyBuffer level1Centroids;
	private bool disposed;

	private HierarchicalArtifactSet(
		FlatArtifactSet flatArtifacts,
		MemoryMappedReadOnlyBuffer level1Centroids,
		MemoryMappedReadOnlyBuffer leafCentroids,
		MemoryMappedReadOnlyBuffer leafPostingOffsets,
		MemoryMappedReadOnlyBuffer leafPostingIds,
		MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts) {
		this.FlatArtifacts = flatArtifacts;
		this.level1Centroids = level1Centroids;
		this.leafCentroids = leafCentroids;
		this.leafPostingOffsets = leafPostingOffsets;
		this.leafPostingIds = leafPostingIds;
		this.leafWithoutHistoryCounts = leafWithoutHistoryCounts;
	}

	public FlatArtifactSet FlatArtifacts { get; }

	public int LeafCount => checked(this.FlatArtifacts.Manifest.Level1ClusterCount * this.FlatArtifacts.Manifest.Level2ClustersPerLevel1);

	public int Level1ClusterCount => this.FlatArtifacts.Manifest.Level1ClusterCount;

	public int Level2ClustersPerLevel1 => this.FlatArtifacts.Manifest.Level2ClustersPerLevel1;

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
		MemoryMappedReadOnlyBuffer? leafWithoutHistoryCounts = null;

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
				leafWithoutHistoryCounts);
			return new HierarchicalArtifactSet(
				flatArtifacts,
				level1Centroids,
				leafCentroids,
				leafPostingOffsets,
				leafPostingIds,
				leafWithoutHistoryCounts);
		} catch {
			level1Centroids.Dispose();
			leafCentroids.Dispose();
			leafPostingOffsets.Dispose();
			leafPostingIds.Dispose();
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

	public void Dispose() {
		if (this.disposed) {
			return;
		}

		this.level1Centroids.Dispose();
		this.leafCentroids.Dispose();
		this.leafPostingOffsets.Dispose();
		this.leafPostingIds.Dispose();
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

		if ((leafWithoutHistoryCounts is not null) && (leafWithoutHistoryCounts.Length != (leafCount * sizeof(int)))) {
			throw new InvalidDataException(
				$"Unexpected last-transaction partition count file length. Expected {leafCount * sizeof(int)}, found {leafWithoutHistoryCounts.Length}.");
		}
	}
}
