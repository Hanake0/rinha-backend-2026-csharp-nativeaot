using System.Net.Http.Headers;

using Rinha2026.Api.Endpoints;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Services;

public sealed class StartupInitializationService : IHostedService {
	private const int HttpWarmUpPassCount = 4;

	private static readonly byte[][] warmupPayloads = [
		"""
		{"id":"warm-http-1","transaction":{"amount":41.12,"installments":2,"requested_at":"2026-03-11T18:45:53Z"},"customer":{"avg_amount":82.24,"tx_count_24h":3,"known_merchants":["MERC-003","MERC-016"]},"merchant":{"id":"MERC-016","mcc":"5411","avg_amount":60.25},"terminal":{"is_online":false,"card_present":true,"km_from_home":29.23},"last_transaction":null}
		"""u8.ToArray(),
		"""
		{"id":"warm-http-2","transaction":{"amount":3200.0,"installments":9,"requested_at":"2026-03-17T02:04:06Z"},"customer":{"avg_amount":68.88,"tx_count_24h":18,"known_merchants":["MERC-004","MERC-007","MERC-015"]},"merchant":{"id":"MERC-062","mcc":"7801","avg_amount":25.55},"terminal":{"is_online":true,"card_present":false,"km_from_home":881.61},"last_transaction":{"timestamp":"2026-03-17T01:58:06Z","km_from_current":660.92}}
		"""u8.ToArray(),
		"""
		{"id":"warm-http-3","transaction":{"amount":384.88,"installments":3,"requested_at":"2026-03-11T20:23:35Z"},"customer":{"avg_amount":769.76,"tx_count_24h":3,"known_merchants":["MERC-009","MERC-001"]},"merchant":{"id":"MERC-001","mcc":"5912","avg_amount":298.95},"terminal":{"is_online":false,"card_present":true,"km_from_home":13.71},"last_transaction":{"timestamp":"2026-03-11T14:58:35Z","km_from_current":18.86}}
		"""u8.ToArray(),
	];

	private readonly FraudRuntimeState fraudRuntimeState;
	private readonly IHostApplicationLifetime hostApplicationLifetime;
	private readonly IConfiguration configuration;
	private readonly RuntimeConfig runtimeConfig;
	private readonly StartupState startupState;
	private Task? startupWarmUpTask;

	public StartupInitializationService(
		FraudRuntimeState fraudRuntimeState,
		IHostApplicationLifetime hostApplicationLifetime,
		IConfiguration configuration,
		RuntimeConfig runtimeConfig,
		StartupState startupState) {
		this.fraudRuntimeState = fraudRuntimeState;
		this.hostApplicationLifetime = hostApplicationLifetime;
		this.configuration = configuration;
		this.runtimeConfig = runtimeConfig;
		this.startupState = startupState;
	}

	public Task StartAsync(CancellationToken cancellationToken) {
		FraudDetectionService detectionService = FraudDetectionService.Create(this.runtimeConfig);
		// Keep /ready false until the mmap-backed artifacts and hot request path are faulted in.
		bool warmUpSupportsHttpPath = detectionService.WarmUp();
		this.fraudRuntimeState.Set(detectionService);

		if (!warmUpSupportsHttpPath) {
			this.startupState.MarkReady();
			return Task.CompletedTask;
		}

		this.hostApplicationLifetime.ApplicationStarted.Register(() => this.startupWarmUpTask = Task.Run(this.CompleteStartupWarmUpAsync));
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken) {
		if (this.startupWarmUpTask is not null) {
			await this.startupWarmUpTask.ConfigureAwait(false);
		}

		this.fraudRuntimeState.Dispose();
	}

	private async Task CompleteStartupWarmUpAsync() {
		try {
			string? warmupUrl = this.GetStartupWarmUpUrl();

			if (!string.IsNullOrWhiteSpace(warmupUrl)) {
				using HttpClient client = new() {
					BaseAddress = new Uri(warmupUrl, UriKind.Absolute),
				};

				for (int pass = 0; pass < HttpWarmUpPassCount; pass++) {
					foreach (byte[] payload in warmupPayloads) {
						await SendWarmUpRequestAsync(client, payload).ConfigureAwait(false);
					}
				}
			}

			this.startupState.MarkReady();
		} catch (Exception exception) {
			Environment.FailFast("Startup warm-up failed before readiness.", exception);
		}
	}

	private static async Task SendWarmUpRequestAsync(HttpClient client, byte[] payload) {
		using ByteArrayContent content = new(payload);
		content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		using HttpRequestMessage request = new(HttpMethod.Post, "/fraud-score") {
			Content = content,
		};
		request.Headers.TryAddWithoutValidation(FraudScoreEndpoint.StartupWarmupHeaderName, FraudScoreEndpoint.StartupWarmupHeaderValue);
		using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode) {
			throw new InvalidOperationException($"Startup HTTP warm-up failed with status code {(int)response.StatusCode}.");
		}
	}

	private string? GetStartupWarmUpUrl() {
		if (this.runtimeConfig.Http.TransportMode != TransportMode.Tcp) {
			return null;
		}

		string? urls = this.configuration["ASPNETCORE_URLS"];

		if (string.IsNullOrWhiteSpace(urls)) {
			return null;
		}

		foreach (string candidate in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (candidate.StartsWith("http://+:", StringComparison.OrdinalIgnoreCase)) {
				return $"http://127.0.0.1:{candidate[9..]}";
			}

			if (candidate.StartsWith("http://*:", StringComparison.OrdinalIgnoreCase)) {
				return $"http://127.0.0.1:{candidate[9..]}";
			}

			if (candidate.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) ||
				candidate.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)) {
				return candidate;
			}
		}

		return null;
	}

}
