using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Configuration;

public static class ServiceCollectionExtensions {
	public static IServiceCollection AddRuntimeServices(
		this IServiceCollection services,
		IConfiguration configuration,
		string contentRootPath) {
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

		RuntimeSettings settings =
			configuration
				.GetSection(RuntimeSettings.SectionName)
				.Get<RuntimeSettings>()
			?? new RuntimeSettings();

		RuntimeConfig runtimeConfig = RuntimeConfigFactory.Create(settings, contentRootPath);

		services.AddSingleton(typeof(RuntimeConfig), runtimeConfig);
		services.AddSingleton<FraudRuntimeState>();
		services.AddSingleton<StartupState>();
		services.AddHostedService<StartupInitializationService>();

		return services;
	}
}
