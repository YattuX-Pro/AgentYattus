using AgentYattus.Api;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Missions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddTelemetry(builder.Configuration);
builder.Services
    .AddMissionsModule()
    .AddAgentsModule(builder.Configuration)
    .AddAuditModule();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMissionsEndpoints();

app.Run();
