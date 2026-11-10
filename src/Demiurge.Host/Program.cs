using System.Text.Json.Serialization;
using Demiurge.Agents;
using Demiurge.Harness.Persistence;
using Demiurge.Host;
using Demiurge.Host.Simulation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

// Composition root: API, SignalR hub, static dashboard and the simulation loop in one process.
// Concept: Dashboard and state streaming — docs/CONCEPTS.md#dashboard-e-streaming-de-estado

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DemiurgeOptions>(builder.Configuration.GetSection(DemiurgeOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var path = sp.GetRequiredService<IOptions<DemiurgeOptions>>().Value.DatabasePath;
    var directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);
    return SqliteEventStore.ForFile(path);
});
builder.Services.AddSingleton<SimulationHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationHost>());

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddCheck<SimulationHealthCheck>("simulation");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapHub<WorldHub>("/hubs/world");

var api = app.MapGroup("/api");

api.MapGet("/world", (SimulationHost sim) =>
    sim.View is { } view ? Results.Ok(view) : Results.Problem("Simulation not started yet.", statusCode: 503));

api.MapGet("/events", (SimulationHost sim, long sinceTick = 0, int limit = 500) =>
{
    if (sim.View is not { } view)
        return Results.Problem("Simulation not started yet.", statusCode: 503);
    var events = sim.Store.ReadEvents(view.WorldId, afterTick: sinceTick - 1, limit: Math.Clamp(limit, 1, 5000));
    return Results.Ok(EventViews.ToJson(events));
});

api.MapGet("/pains", (SimulationHost sim) =>
    sim.View is { } view ? Results.Ok(view.Pains) : Results.Problem("Simulation not started yet.", statusCode: 503));

var god = api.MapGroup("/god");
god.MapPost("/pause", (SimulationHost sim, CancellationToken ct) => sim.SendAsync(new PauseCommand(), ct));
god.MapPost("/resume", (SimulationHost sim, CancellationToken ct) => sim.SendAsync(new ResumeCommand(), ct));
god.MapPost("/step", (SimulationHost sim, CancellationToken ct) => sim.SendAsync(new StepCommand(), ct));
god.MapPost("/worlds", async (NewWorldRequest request, SimulationHost sim, IOptions<DemiurgeOptions> options, CancellationToken ct) =>
{
    int agents = request.AgentCount ?? options.Value.AgentCount;
    if (agents is < 1 or > 100)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["agentCount"] = ["Must be between 1 and 100."] });
    return Results.Ok(await sim.SendAsync(new NewWorldCommand(request.Seed ?? options.Value.Seed, agents), ct));
});
god.MapPost("/events", () => Results.Problem(
    "Event injection (plague, drought, whisper) arrives in stage 6.", statusCode: StatusCodes.Status501NotImplemented));

app.Run();

public sealed record NewWorldRequest(ulong? Seed, int? AgentCount);

/// <summary>Healthy once the simulation loop has published its first view.</summary>
public sealed class SimulationHealthCheck(SimulationHost sim) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(sim.View is { } view
            ? HealthCheckResult.Healthy($"world {view.WorldId}, tick {view.Tick}")
            : HealthCheckResult.Degraded("simulation starting"));
}

public partial class Program; // exposes the entry point to WebApplicationFactory in tests
