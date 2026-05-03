using Rinha2026.Core.Search;

namespace Rinha2026.IndexBuilder.Build;

internal static class KMeansTrainer {
	public static float[] Train(
		ReadOnlySpan<float> samples,
		int sampleCount,
		int dimension,
		int clusterCount,
		int iterations) {
		if (sampleCount <= 0) {
			throw new ArgumentOutOfRangeException(nameof(sampleCount));
		}

		if (dimension <= 0) {
			throw new ArgumentOutOfRangeException(nameof(dimension));
		}

		if (clusterCount <= 0) {
			throw new ArgumentOutOfRangeException(nameof(clusterCount));
		}

		if (iterations <= 0) {
			throw new ArgumentOutOfRangeException(nameof(iterations));
		}

		float[] centroids = new float[clusterCount * dimension];
		InitializeFarthestPoint(samples, sampleCount, dimension, centroids);

		float[] sums = new float[clusterCount * dimension];
		int[] counts = new int[clusterCount];

		for (int iteration = 0; iteration < iterations; iteration++) {
			Array.Clear(sums);
			Array.Clear(counts);

			for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
				ReadOnlySpan<float> sample = samples.Slice(sampleIndex * dimension, dimension);
				int nearestCentroid = FindNearestCentroid(sample, centroids, clusterCount, dimension);
				counts[nearestCentroid]++;
				AccumulateSample(sample, sums.AsSpan(nearestCentroid * dimension, dimension));
			}

			for (int centroidIndex = 0; centroidIndex < clusterCount; centroidIndex++) {
				Span<float> centroid = centroids.AsSpan(centroidIndex * dimension, dimension);

				if (counts[centroidIndex] == 0) {
					int fallbackSampleIndex = (centroidIndex * sampleCount) / clusterCount;
					samples.Slice(fallbackSampleIndex * dimension, dimension).CopyTo(centroid);
					continue;
				}

				Span<float> centroidSums = sums.AsSpan(centroidIndex * dimension, dimension);
				float inverseCount = 1f / counts[centroidIndex];

				for (int dimensionIndex = 0; dimensionIndex < dimension; dimensionIndex++) {
					centroid[dimensionIndex] = centroidSums[dimensionIndex] * inverseCount;
				}
			}
		}

		return centroids;
	}

	public static int FindNearestCentroid(
		ReadOnlySpan<float> vector,
		ReadOnlySpan<float> centroids,
		int clusterCount,
		int dimension) {
		int nearestIndex = 0;
		float nearestDistance = float.MaxValue;

		for (int centroidIndex = 0; centroidIndex < clusterCount; centroidIndex++) {
			float distance = DistanceComputations.SquaredL2(
				vector,
				centroids.Slice(centroidIndex * dimension, dimension));

			if (distance < nearestDistance) {
				nearestDistance = distance;
				nearestIndex = centroidIndex;
			}
		}

		return nearestIndex;
	}

	private static void AccumulateSample(ReadOnlySpan<float> sample, Span<float> sums) {
		for (int dimensionIndex = 0; dimensionIndex < sample.Length; dimensionIndex++) {
			sums[dimensionIndex] += sample[dimensionIndex];
		}
	}

	private static void InitializeFarthestPoint(
		ReadOnlySpan<float> samples,
		int sampleCount,
		int dimension,
		Span<float> centroids) {
		int clusterCount = centroids.Length / dimension;
		samples[..dimension].CopyTo(centroids[..dimension]);
		int initialized = 1;

		for (; initialized < Math.Min(clusterCount, sampleCount); initialized++) {
			int bestSampleIndex = 0;
			float bestDistance = float.MinValue;

			for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
				ReadOnlySpan<float> sample = samples.Slice(sampleIndex * dimension, dimension);
				float nearestDistance = float.MaxValue;

				for (int centroidIndex = 0; centroidIndex < initialized; centroidIndex++) {
					float distance = DistanceComputations.SquaredL2(
						sample,
						centroids.Slice(centroidIndex * dimension, dimension));

					if (distance < nearestDistance) {
						nearestDistance = distance;
					}
				}

				if (nearestDistance > bestDistance) {
					bestDistance = nearestDistance;
					bestSampleIndex = sampleIndex;
				}
			}

			samples.Slice(bestSampleIndex * dimension, dimension)
				.CopyTo(centroids.Slice(initialized * dimension, dimension));
		}

		for (; initialized < clusterCount; initialized++) {
			int fallbackSampleIndex = initialized % sampleCount;
			samples.Slice(fallbackSampleIndex * dimension, dimension)
				.CopyTo(centroids.Slice(initialized * dimension, dimension));
		}
	}
}
