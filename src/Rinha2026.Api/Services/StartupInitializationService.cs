using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Services;

public sealed class StartupInitializationService : IHostedService {
	private readonly FraudRuntimeState fraudRuntimeState;
	private readonly RuntimeConfig runtimeConfig;
	private readonly StartupState startupState;

	public StartupInitializationService(
		FraudRuntimeState fraudRuntimeState,
		RuntimeConfig runtimeConfig,
		StartupState startupState) {
		this.fraudRuntimeState = fraudRuntimeState;
		this.runtimeConfig = runtimeConfig;
		this.startupState = startupState;
	}

	public async Task StartAsync(CancellationToken cancellationToken) {
		FraudDetectionService detectionService = FraudDetectionService.Create(this.runtimeConfig);
		this.fraudRuntimeState.Set(detectionService);

		this.startupState.MarkReady();
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		this.fraudRuntimeState.Dispose();
		return Task.CompletedTask;
	}
}
