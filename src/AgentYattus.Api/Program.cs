using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Missions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services
    .AddMissionsModule()
    .AddAgentsModule()
    .AddAuditModule();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMissionsEndpoints();

app.Run();
