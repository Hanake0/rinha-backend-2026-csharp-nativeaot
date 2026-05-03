using Rinha2026.Api.Configuration;
using Rinha2026.Api.Services;

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddRuntimeServices(builder.Configuration, builder.Environment.ContentRootPath);

WebApplication app = builder.Build();

app.MapGet(
	"/ready",
	static (StartupState startupState) => startupState.IsReady
		? Results.Ok()
		: Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program {
}
