using System.Text.Json;

using Rinha2026.Api.Configuration;
using Rinha2026.Api.Endpoints;
using Rinha2026.Api.Raw;
using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
RuntimeConfig runtimeConfig = ServiceCollectionExtensions.LoadRuntimeConfig(builder.Configuration, builder.Environment.ContentRootPath);

if (runtimeConfig.Http.ServerMode == ServerMode.RawSockets) {
	RequestProfileCollector rawRequestProfileCollector = new(runtimeConfig.Diagnostics);
	await using RawHttpServer rawHttpServer = RawHttpServer.Create(
		runtimeConfig,
		builder.Configuration["ASPNETCORE_URLS"],
		rawRequestProfileCollector);
	await rawHttpServer.RunAsync();
	return;
}

SocketTransportConfiguration.Configure(builder.WebHost, builder.Configuration);
builder.Services.AddRuntimeServices(runtimeConfig);

WebApplication app = builder.Build();
runtimeConfig = app.Services.GetRequiredService<RuntimeConfig>();
SocketTransportConfiguration.ConfigureApplication(app.Lifetime, runtimeConfig.Http);

app.MapGet(
	"/ready",
	static (StartupState startupState) => startupState.IsReady
		? Results.Ok()
		: Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapPost("/fraud-score", FraudScoreEndpoint.HandleAsync);

RequestProfileCollector requestProfileCollector = app.Services.GetRequiredService<RequestProfileCollector>();

if (requestProfileCollector.IsEnabled) {
	app.MapGet("/debug/profile", static async (
		HttpContext context,
		RequestProfileCollector collector,
		CancellationToken cancellationToken) => {
			context.Response.StatusCode = StatusCodes.Status200OK;
			context.Response.ContentType = "application/json";
			await JsonSerializer.SerializeAsync(
				context.Response.Body,
				collector.Snapshot(),
				Rinha2026.Api.ApiJsonContext.Default.RequestProfileSnapshot,
				cancellationToken);
		});
	app.MapPost("/debug/profile/reset", static (RequestProfileCollector collector) => {
		collector.Reset();
		return Results.Ok();
	});
}

app.Run();

public partial class Program {
}
