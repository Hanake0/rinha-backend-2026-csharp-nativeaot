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

		RuntimeConfig runtimeConfig = LoadRuntimeConfig(configuration, contentRootPath);
		return services.AddRuntimeServices(runtimeConfig);
	}

	public static IServiceCollection AddRuntimeServices(
		this IServiceCollection services,
		RuntimeConfig runtimeConfig) {
		ArgumentNullException.ThrowIfNull(services);

		services.AddSingleton(typeof(RuntimeConfig), runtimeConfig);
		services.AddSingleton<FraudRuntimeState>();
		services.AddSingleton<RequestProfileCollector>(_ => new RequestProfileCollector(runtimeConfig.Diagnostics));
		services.AddSingleton<StartupState>();
		services.AddHostedService<StartupInitializationService>();

		return services;
	}

	public static RuntimeConfig LoadRuntimeConfig(IConfiguration configuration, string contentRootPath) {
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
		RuntimeSettings settings = ReadRuntimeSettings(configuration);
		return RuntimeConfigFactory.Create(settings, contentRootPath);
	}

	private static RuntimeSettings ReadRuntimeSettings(IConfiguration configuration) {
		RuntimeSettings defaults = new();

		return new RuntimeSettings {
			Detection = new DetectionSettings {
				ApprovalThreshold = GetDouble(
					configuration,
					"Runtime:Detection:ApprovalThreshold",
					defaults.Detection.ApprovalThreshold),
				TopK = GetInt32(
					configuration,
					"Runtime:Detection:TopK",
					defaults.Detection.TopK),
			},
			Dataset = new DatasetSettings {
				IndexDirectory = GetString(
					configuration,
					"Runtime:Dataset:IndexDirectory",
					defaults.Dataset.IndexDirectory),
				MccRiskPath = GetString(
					configuration,
					"Runtime:Dataset:MccRiskPath",
					defaults.Dataset.MccRiskPath),
				NormalizationPath = GetString(
					configuration,
					"Runtime:Dataset:NormalizationPath",
					defaults.Dataset.NormalizationPath),
			},
			Http = new HttpSettings {
				ParserMode = GetEnum(
					configuration,
					"Runtime:Http:ParserMode",
					defaults.Http.ParserMode),
				ResponseMode = GetEnum(
					configuration,
					"Runtime:Http:ResponseMode",
					defaults.Http.ResponseMode),
				ServerMode = GetEnum(
					configuration,
					"Runtime:Http:ServerMode",
					defaults.Http.ServerMode),
				TransportMode = GetEnum(
					configuration,
					"Runtime:Http:TransportMode",
					defaults.Http.TransportMode),
				UnixSocketPath = GetOptionalString(
					configuration,
					"Runtime:Http:UnixSocketPath"),
			},
			Diagnostics = new DiagnosticsSettings {
				ProfileEnabled = GetBoolean(
					configuration,
					"Runtime:Diagnostics:ProfileEnabled",
					defaults.Diagnostics.ProfileEnabled),
				ProfileSampleCapacity = GetInt32(
					configuration,
					"Runtime:Diagnostics:ProfileSampleCapacity",
					defaults.Diagnostics.ProfileSampleCapacity),
				ProfileSamplingStride = GetInt32(
					configuration,
					"Runtime:Diagnostics:ProfileSamplingStride",
					defaults.Diagnostics.ProfileSamplingStride),
			},
			Search = new SearchSettings {
				BeamLevel1 = GetInt32(
					configuration,
					"Runtime:Search:BeamLevel1",
					defaults.Search.BeamLevel1),
				BeamLevel2 = GetInt32(
					configuration,
					"Runtime:Search:BeamLevel2",
					defaults.Search.BeamLevel2),
				Dimension = GetInt32(
					configuration,
					"Runtime:Search:Dimension",
					defaults.Search.Dimension),
				DistanceMetric = GetEnum(
					configuration,
					"Runtime:Search:DistanceMetric",
					defaults.Search.DistanceMetric),
				IndexKind = GetEnum(
					configuration,
					"Runtime:Search:IndexKind",
					defaults.Search.IndexKind),
				PaddedDimension = GetInt32(
					configuration,
					"Runtime:Search:PaddedDimension",
					defaults.Search.PaddedDimension),
				RerankCount = GetInt32(
					configuration,
					"Runtime:Search:RerankCount",
					defaults.Search.RerankCount),
				BoundaryRerankCount = GetInt32(
					configuration,
					"Runtime:Search:BoundaryRerankCount",
					defaults.Search.BoundaryRerankCount),
				UseLeafRadiusPruning = GetBoolean(
					configuration,
					"Runtime:Search:UseLeafRadiusPruning",
					defaults.Search.UseLeafRadiusPruning),
				UseLastTransactionPartitionPruning = GetBoolean(
					configuration,
					"Runtime:Search:UseLastTransactionPartitionPruning",
					defaults.Search.UseLastTransactionPartitionPruning),
			},
		};
	}

	private static bool GetBoolean(IConfiguration configuration, string key, bool fallback) {
		string? value = configuration[key];
		return bool.TryParse(value, out bool parsedValue) ? parsedValue : fallback;
	}

	private static double GetDouble(IConfiguration configuration, string key, double fallback) {
		string? value = configuration[key];
		return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedValue)
			? parsedValue
			: fallback;
	}

	private static TEnum GetEnum<TEnum>(IConfiguration configuration, string key, TEnum fallback) where TEnum : struct {
		string? value = configuration[key];
		return Enum.TryParse<TEnum>(value, ignoreCase: true, out TEnum parsedValue)
			? parsedValue
			: fallback;
	}

	private static int GetInt32(IConfiguration configuration, string key, int fallback) {
		string? value = configuration[key];
		return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedValue)
			? parsedValue
			: fallback;
	}

	private static string GetString(IConfiguration configuration, string key, string fallback) {
		string? value = configuration[key];
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}

	private static string? GetOptionalString(IConfiguration configuration, string key) {
		string? value = configuration[key];
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}
}
