namespace Rinha2026.Api.Services;

public sealed class FraudRuntimeState : IDisposable {
	private FraudDetectionService? detectionService;

	public bool TryGet(out FraudDetectionService? detectionService) {
		detectionService = Volatile.Read(ref this.detectionService);
		return detectionService is not null;
	}

	public void Set(FraudDetectionService detectionService) {
		ArgumentNullException.ThrowIfNull(detectionService);

		FraudDetectionService? previous = Interlocked.Exchange(ref this.detectionService, detectionService);
		previous?.Dispose();
	}

	public void Dispose() {
		FraudDetectionService? service = Interlocked.Exchange(ref this.detectionService, null);
		service?.Dispose();
	}
}
