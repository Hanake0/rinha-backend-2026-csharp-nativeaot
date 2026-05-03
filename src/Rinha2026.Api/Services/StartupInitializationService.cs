using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Services;

public sealed class StartupInitializationService : IHostedService {
	private readonly FraudRuntimeState fraudRuntimeState;
	private readonly ILogger<StartupInitializationService> logger;
	private readonly RuntimeConfig runtimeConfig;
	private readonly StartupState startupState;

	public StartupInitializationService(
		FraudRuntimeState fraudRuntimeState,
		ILogger<StartupInitializationService> logger,
		RuntimeConfig runtimeConfig,
		StartupState startupState) {
		this.fraudRuntimeState = fraudRuntimeState;
		this.logger = logger;
		this.runtimeConfig = runtimeConfig;
		this.startupState = startupState;
	}

	public Task StartAsync(CancellationToken cancellationToken) {
		FraudDetectionService detectionService = FraudDetectionService.Create(this.runtimeConfig);
		this.fraudRuntimeState.Set(detectionService);

		this.logger.LogInformation(
			"Runtime configured with TopK={TopK}, ParserMode={ParserMode}, IndexKind={IndexKind}",
			this.runtimeConfig.Detection.TopK,
			this.runtimeConfig.Http.ParserMode,
			this.runtimeConfig.Search.IndexKind);

		this.startupState.MarkReady();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		this.fraudRuntimeState.Dispose();
		return Task.CompletedTask;
	}
}
