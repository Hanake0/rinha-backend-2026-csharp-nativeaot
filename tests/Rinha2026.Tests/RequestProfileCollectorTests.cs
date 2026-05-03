using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Tests;

public sealed class RequestProfileCollectorTests {
	[Fact]
	public void SnapshotReflectsRecordedSamples() {
		RequestProfileCollector collector = new(new RuntimeDiagnosticsConfig(ProfileEnabled: true, ProfileSampleCapacity: 8, ProfileSamplingStride: 1));

		collector.Record(10, 20, 30, 40, 50, 60);
		collector.Record(15, 25, 35, 45, 55, 65);

		RequestProfileSnapshot snapshot = collector.Snapshot();

		Assert.True(snapshot.Enabled);
		Assert.Equal(2L, snapshot.RequestCount);
		Assert.Equal(1, snapshot.SamplingStride);
		Assert.Equal(2, snapshot.SampleCount);
		Assert.True(snapshot.SearchUs.Max >= snapshot.SearchUs.Min);
		Assert.True(snapshot.TotalUs.Max >= snapshot.TotalUs.Min);
	}

	[Fact]
	public void ResetClearsCollectedSamples() {
		RequestProfileCollector collector = new(new RuntimeDiagnosticsConfig(ProfileEnabled: true, ProfileSampleCapacity: 4, ProfileSamplingStride: 1));
		collector.Record(10, 20, 30, 40, 50, 60);

		collector.Reset();

		RequestProfileSnapshot snapshot = collector.Snapshot();
		Assert.Equal(0L, snapshot.RequestCount);
		Assert.Equal(0, snapshot.SampleCount);
	}

	[Fact]
	public void SamplingStrideDropsIntermediateRequests() {
		RequestProfileCollector collector = new(new RuntimeDiagnosticsConfig(ProfileEnabled: true, ProfileSampleCapacity: 8, ProfileSamplingStride: 2));
		collector.Record(10, 20, 30, 40, 50, 60);
		collector.Record(11, 21, 31, 41, 51, 61);
		collector.Record(12, 22, 32, 42, 52, 62);

		RequestProfileSnapshot snapshot = collector.Snapshot();

		Assert.Equal(3L, snapshot.RequestCount);
		Assert.Equal(2, snapshot.SamplingStride);
		Assert.Equal(1, snapshot.SampleCount);
	}
}
