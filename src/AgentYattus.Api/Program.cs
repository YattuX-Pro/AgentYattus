using AgentYattus.Api.Modules.Missions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddMissionsModule();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMissionsEndpoints();

app.Run();
