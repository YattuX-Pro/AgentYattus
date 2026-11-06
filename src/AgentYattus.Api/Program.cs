using AgentYattus.Api;
using AgentYattus.Api.Modules.Agents;
using AgentYattus.Api.Modules.Audit;
using AgentYattus.Api.Modules.Identity;
using AgentYattus.Api.Modules.Llm;
using AgentYattus.Api.Modules.Missions;
using AgentYattus.Api.Modules.Projects;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddTelemetry(builder.Configuration);
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment);
builder.Services
    .AddIdentityModule()
    .AddProjectsModule()
    .AddMissionsModule()
    .AddAgentsModule(builder.Configuration)
    .AddAuditModule()
    .AddLlmModule(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseUserDirectory();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapMissionsEndpoints();
app.MapAgentsEndpoints();
app.MapLlmEndpoints();
app.MapAuditEndpoints();
app.MapIdentityEndpoints();
app.MapProjectsEndpoints();

app.Run();
