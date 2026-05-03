using Rinha2026.Api.Configuration;
using Rinha2026.Api.Endpoints;
using Rinha2026.Api.Services;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
SocketTransportConfiguration.Configure(builder.WebHost, builder.Configuration);
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddRuntimeServices(builder.Configuration, builder.Environment.ContentRootPath);

WebApplication app = builder.Build();

app.MapGet(
	"/ready",
	static (StartupState startupState) => startupState.IsReady
		? Results.Ok()
		: Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapPost("/fraud-score", FraudScoreEndpoint.HandleAsync);

RequestProfileCollector requestProfileCollector = app.Services.GetRequiredService<RequestProfileCollector>();

if (requestProfileCollector.IsEnabled) {
	app.MapGet(
		"/debug/profile",
		static (RequestProfileCollector collector) => Results.Json(
			collector.Snapshot(),
			Rinha2026.Api.ApiJsonContext.Default.RequestProfileSnapshot));
	app.MapPost("/debug/profile/reset", static (RequestProfileCollector collector) => {
		collector.Reset();
		return Results.Ok();
	});
}

app.Run();

public partial class Program {
}
