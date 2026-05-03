using BenchmarkDotNet.Attributes;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class CandidateSelectionBenchmarks {
	private const int Capacity = 48;

	private int[] candidateDistances = default!;
	private int[] candidateIds = default!;
	private int[] workingDistances = default!;
	private int[] workingIds = default!;

	[Params(50_000, 160_000)]
	public int CandidateCount { get; set; }

	[GlobalSetup]
	public void Setup() {
		Random random = new(20260503);
		this.candidateDistances = new int[this.CandidateCount];
		this.candidateIds = new int[this.CandidateCount];
		this.workingDistances = new int[Capacity];
		this.workingIds = new int[Capacity];

		for (int index = 0; index < this.CandidateCount; index++) {
			this.candidateIds[index] = index;
			this.candidateDistances[index] = random.Next(0, 200_000);
		}
	}

	[Benchmark(Baseline = true)]
	public int CurrentSortedInsertion() {
		Array.Fill(this.workingDistances, 0);
		Array.Fill(this.workingIds, 0);

		int count = 0;

		for (int index = 0; index < this.candidateDistances.Length; index++) {
			InsertSorted(this.workingIds, this.workingDistances, ref count, this.candidateIds[index], this.candidateDistances[index]);
		}

		return this.workingIds[0] ^ this.workingDistances[0] ^ count;
	}

	[Benchmark]
	public int UnsortedMaxScanReservoir() {
		Array.Fill(this.workingDistances, 0);
		Array.Fill(this.workingIds, 0);

		int count = 0;
		int currentMaxIndex = 0;
		int currentMaxDistance = int.MinValue;

		for (int index = 0; index < this.candidateDistances.Length; index++) {
			TryInsertUnsorted(
				this.workingIds,
				this.workingDistances,
				ref count,
				ref currentMaxIndex,
				ref currentMaxDistance,
				this.candidateIds[index],
				this.candidateDistances[index]);
		}

		return this.workingIds[0] ^ this.workingDistances[currentMaxIndex] ^ count;
	}

	[Benchmark]
	public int MaxHeapReservoir() {
		Array.Fill(this.workingDistances, 0);
		Array.Fill(this.workingIds, 0);

		int count = 0;

		for (int index = 0; index < this.candidateDistances.Length; index++) {
			TryInsertHeap(this.workingIds, this.workingDistances, ref count, this.candidateIds[index], this.candidateDistances[index]);
		}

		return this.workingIds[0] ^ this.workingDistances[0] ^ count;
	}

	private static void InsertSorted(
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		int id,
		int distance) {
		if ((count == destinationIds.Length) && (distance >= destinationDistances[destinationIds.Length - 1])) {
			return;
		}

		int insertAt = Math.Min(count, destinationIds.Length - 1);

		while ((insertAt > 0) && (distance < destinationDistances[insertAt - 1])) {
			if (insertAt < destinationIds.Length) {
				destinationIds[insertAt] = destinationIds[insertAt - 1];
				destinationDistances[insertAt] = destinationDistances[insertAt - 1];
			}

			insertAt--;
		}

		destinationIds[insertAt] = id;
		destinationDistances[insertAt] = distance;

		if (count < destinationIds.Length) {
			count++;
		}
	}

	private static void TryInsertHeap(
		Span<int> heapIds,
		Span<int> heapDistances,
		ref int count,
		int id,
		int distance) {
		if (count < heapIds.Length) {
			int insertIndex = count++;
			heapIds[insertIndex] = id;
			heapDistances[insertIndex] = distance;
			SiftUp(heapIds, heapDistances, insertIndex);
			return;
		}

		if (distance >= heapDistances[0]) {
			return;
		}

		heapIds[0] = id;
		heapDistances[0] = distance;
		SiftDown(heapIds, heapDistances, count, 0);
	}

	private static void TryInsertUnsorted(
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		ref int currentMaxIndex,
		ref int currentMaxDistance,
		int id,
		int distance) {
		if (count < destinationIds.Length) {
			destinationIds[count] = id;
			destinationDistances[count] = distance;

			if ((count == 0) || (distance > currentMaxDistance)) {
				currentMaxDistance = distance;
				currentMaxIndex = count;
			}

			count++;
			return;
		}

		if (distance >= currentMaxDistance) {
			return;
		}

		destinationIds[currentMaxIndex] = id;
		destinationDistances[currentMaxIndex] = distance;
		(currentMaxIndex, currentMaxDistance) = FindMax(destinationDistances, count);
	}

	private static (int Index, int Distance) FindMax(ReadOnlySpan<int> distances, int count) {
		int maxIndex = 0;
		int maxDistance = distances[0];

		for (int index = 1; index < count; index++) {
			if (distances[index] > maxDistance) {
				maxDistance = distances[index];
				maxIndex = index;
			}
		}

		return (maxIndex, maxDistance);
	}

	private static void SiftDown(
		Span<int> heapIds,
		Span<int> heapDistances,
		int count,
		int index) {
		while (true) {
			int left = (index * 2) + 1;

			if (left >= count) {
				return;
			}

			int right = left + 1;
			int largerChild = ((right < count) && (heapDistances[right] > heapDistances[left])) ? right : left;

			if (heapDistances[index] >= heapDistances[largerChild]) {
				return;
			}

			Swap(heapIds, heapDistances, index, largerChild);
			index = largerChild;
		}
	}

	private static void SiftUp(
		Span<int> heapIds,
		Span<int> heapDistances,
		int index) {
		while (index > 0) {
			int parent = (index - 1) >> 1;

			if (heapDistances[parent] >= heapDistances[index]) {
				return;
			}

			Swap(heapIds, heapDistances, parent, index);
			index = parent;
		}
	}

	private static void Swap(
		Span<int> ids,
		Span<int> distances,
		int left,
		int right) {
		(ids[left], ids[right]) = (ids[right], ids[left]);
		(distances[left], distances[right]) = (distances[right], distances[left]);
	}
}
