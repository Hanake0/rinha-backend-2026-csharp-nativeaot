using System.Diagnostics;

using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Services;

public sealed class RequestProfileCollector {
	private readonly long[] bodyReadTicks;
	private readonly long[] parseTicks;
	private readonly long[] vectorizeTicks;
	private readonly long[] searchTicks;
	private readonly long[] responseWriteTicks;
	private readonly long[] totalTicks;
	private long requestCount;
	private long sampledRequestCount;
	private long sampleCursor;

	public RequestProfileCollector(RuntimeDiagnosticsConfig diagnostics) {
		this.IsEnabled = diagnostics.ProfileEnabled;
		this.SampleCapacity = diagnostics.ProfileSampleCapacity;
		this.SamplingStride = diagnostics.ProfileSamplingStride;
		this.bodyReadTicks = new long[this.SampleCapacity];
		this.parseTicks = new long[this.SampleCapacity];
		this.vectorizeTicks = new long[this.SampleCapacity];
		this.searchTicks = new long[this.SampleCapacity];
		this.responseWriteTicks = new long[this.SampleCapacity];
		this.totalTicks = new long[this.SampleCapacity];
	}

	public bool IsEnabled { get; }

	public int SampleCapacity { get; }

	public int SamplingStride { get; }

	public void Record(
		long bodyReadStageTicks,
		long parseStageTicks,
		long vectorizeStageTicks,
		long searchStageTicks,
		long responseWriteStageTicks,
		long totalStageTicks) {
		if (!this.IsEnabled) {
			return;
		}

		long currentRequestCount = Interlocked.Increment(ref this.requestCount);

		if ((currentRequestCount % this.SamplingStride) != 0L) {
			return;
		}

		long sampledRequestCount = Interlocked.Increment(ref this.sampledRequestCount);
		long sampleIndex = Interlocked.Increment(ref this.sampleCursor) - 1L;
		int slot = (int)(sampleIndex % this.SampleCapacity);
		this.bodyReadTicks[slot] = bodyReadStageTicks;
		this.parseTicks[slot] = parseStageTicks;
		this.vectorizeTicks[slot] = vectorizeStageTicks;
		this.searchTicks[slot] = searchStageTicks;
		this.responseWriteTicks[slot] = responseWriteStageTicks;
		this.totalTicks[slot] = totalStageTicks;
		_ = sampledRequestCount;
	}

	public RequestProfileSnapshot Snapshot() {
		int sampleCount = Math.Min(this.SampleCapacity, (int)Math.Min(Volatile.Read(ref this.sampledRequestCount), int.MaxValue));

		return new RequestProfileSnapshot(
			this.IsEnabled,
			Volatile.Read(ref this.requestCount),
			this.SamplingStride,
			sampleCount,
			BuildStageSummary(this.bodyReadTicks, sampleCount),
			BuildStageSummary(this.parseTicks, sampleCount),
			BuildStageSummary(this.vectorizeTicks, sampleCount),
			BuildStageSummary(this.searchTicks, sampleCount),
			BuildStageSummary(this.responseWriteTicks, sampleCount),
			BuildStageSummary(this.totalTicks, sampleCount));
	}

	public void Reset() {
		Array.Clear(this.bodyReadTicks);
		Array.Clear(this.parseTicks);
		Array.Clear(this.vectorizeTicks);
		Array.Clear(this.searchTicks);
		Array.Clear(this.responseWriteTicks);
		Array.Clear(this.totalTicks);
		Interlocked.Exchange(ref this.requestCount, 0L);
		Interlocked.Exchange(ref this.sampledRequestCount, 0L);
		Interlocked.Exchange(ref this.sampleCursor, 0L);
	}

	private static RequestProfileStageSummary BuildStageSummary(long[] source, int sampleCount) {
		if (sampleCount <= 0) {
			return new RequestProfileStageSummary(0d, 0d, 0d, 0d, 0d, 0d);
		}

		long[] samples = new long[sampleCount];
		Array.Copy(source, samples, sampleCount);
		Array.Sort(samples);

		return new RequestProfileStageSummary(
			ToMicroseconds(samples[0]),
			ToMicroseconds(GetPercentile(samples, 0.50d)),
			ToMicroseconds(GetPercentile(samples, 0.95d)),
			ToMicroseconds(GetPercentile(samples, 0.99d)),
			ToMicroseconds(samples[^1]),
			ToMicroseconds((long)Math.Round(samples.Average(), MidpointRounding.AwayFromZero)));
	}

	private static long GetPercentile(long[] sortedSamples, double percentile) {
		int index = (int)Math.Ceiling(sortedSamples.Length * percentile) - 1;
		return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
	}

	private static double ToMicroseconds(long ticks) {
		return Math.Round((ticks * 1_000_000d) / Stopwatch.Frequency, 3, MidpointRounding.AwayFromZero);
	}
}

public sealed record RequestProfileSnapshot(
	bool Enabled,
	long RequestCount,
	int SamplingStride,
	int SampleCount,
	RequestProfileStageSummary BodyReadUs,
	RequestProfileStageSummary ParseUs,
	RequestProfileStageSummary VectorizeUs,
	RequestProfileStageSummary SearchUs,
	RequestProfileStageSummary ResponseWriteUs,
	RequestProfileStageSummary TotalUs);

public sealed record RequestProfileStageSummary(
	double Min,
	double P50,
	double P95,
	double P99,
	double Max,
	double Mean);
