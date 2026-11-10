using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Demiurge.Engine.Actions;
using Demiurge.Engine.Domain;
using Demiurge.Engine.Events;
using Demiurge.Harness.Pains;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Demiurge.Host.Tests;

/// <summary>
/// The whole process in memory: API, simulation loop, SignalR hub, static dashboard.
/// The clock starts paused with a 1-hour interval, so ticks only happen on "step".
/// Concept: Dashboard and state streaming — docs/CONCEPTS.md#dashboard-e-streaming-de-estado
/// </summary>
public sealed class HostTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"demiurge-host-{Guid.NewGuid():N}.db");
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> NewFactory()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DEMIURGE:DatabasePath", _db);
            b.UseSetting("DEMIURGE:StartPaused", "true");
            b.UseSetting("DEMIURGE:TickIntervalMs", "3600000");
        });
        _factories.Add(factory);
        return factory;
    }

    private static async Task<JsonElement> WorldAsync(HttpClient client)
    {
        for (int i = 0; i < 100; i++)
        {
            var res = await client.GetAsync("/api/world", Ct);
            if (res.StatusCode == HttpStatusCode.OK)
                return await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("Simulation did not publish a view.");
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object? body = null)
    {
        var res = body is null ? await client.PostAsync(path, null, Ct) : await client.PostAsJsonAsync(path, body, Ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task Health_returns_200_once_the_loop_runs()
    {
        var client = NewFactory().CreateClient();
        await WorldAsync(client);

        var res = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task World_starts_at_genesis_and_paused()
    {
        var world = await WorldAsync(NewFactory().CreateClient());

        Assert.Equal(0, world.GetProperty("tick").GetInt64());
        Assert.True(world.GetProperty("paused").GetBoolean());
        Assert.Equal(5, world.GetProperty("agents").GetArrayLength());
        Assert.Equal(4, world.GetProperty("places").GetArrayLength());
    }

    [Fact]
    public async Task Step_advances_one_tick_and_its_events_are_readable()
    {
        var client = NewFactory().CreateClient();
        await WorldAsync(client);

        var view = await PostAsync(client, "/api/god/step");
        var events = await client.GetFromJsonAsync<JsonElement>("/api/events?sinceTick=1", Ct);

        Assert.Equal(1, view.GetProperty("tick").GetInt64());
        Assert.Equal("TickStarted", events[0].GetProperty("$type").GetString());
        Assert.Equal("TickEnded", events[events.GetArrayLength() - 1].GetProperty("$type").GetString());
    }

    [Fact]
    public async Task Pause_and_resume_toggle_the_clock()
    {
        var client = NewFactory().CreateClient();
        await WorldAsync(client);

        Assert.False((await PostAsync(client, "/api/god/resume")).GetProperty("paused").GetBoolean());
        Assert.True((await PostAsync(client, "/api/god/pause")).GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task New_world_gets_a_new_id_seed_and_size()
    {
        var client = NewFactory().CreateClient();
        var first = await WorldAsync(client);

        var second = await PostAsync(client, "/api/god/worlds", new { seed = 7, agentCount = 3 });

        Assert.NotEqual(first.GetProperty("worldId").GetInt64(), second.GetProperty("worldId").GetInt64());
        Assert.Equal("7", second.GetProperty("seed").GetString());
        Assert.Equal(3, second.GetProperty("agents").GetArrayLength());
        Assert.Equal(0, second.GetProperty("tick").GetInt64());
    }

    [Fact]
    public async Task Invalid_agent_count_is_a_400()
    {
        var client = NewFactory().CreateClient();
        await WorldAsync(client);

        var res = await client.PostAsJsonAsync("/api/god/worlds", new { agentCount = 0 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Event_injection_is_a_stub_until_stage_6()
    {
        var client = NewFactory().CreateClient();

        var res = await client.PostAsync("/api/god/events", null, Ct);

        Assert.Equal(HttpStatusCode.NotImplemented, res.StatusCode);
    }

    [Fact]
    public async Task Pains_are_exposed_ranked()
    {
        var client = NewFactory().CreateClient();
        await WorldAsync(client);
        for (int i = 0; i < 50; i++) await PostAsync(client, "/api/god/step"); // 48 silent ticks → NO_SOCIAL_CONTACT

        var pains = await client.GetFromJsonAsync<JsonElement>("/api/pains", Ct);

        Assert.Contains(pains.EnumerateArray(), p => p.GetProperty("code").GetString() == PainCodes.NoSocialContact);
        Assert.Equal("Open", pains[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task World_survives_a_process_restart()
    {
        var first = NewFactory().CreateClient();
        long worldId = (await WorldAsync(first)).GetProperty("worldId").GetInt64();
        for (int i = 0; i < 3; i++) await PostAsync(first, "/api/god/step");
        await _factories[0].DisposeAsync();

        var resumed = await WorldAsync(NewFactory().CreateClient());

        Assert.Equal(worldId, resumed.GetProperty("worldId").GetInt64());
        Assert.Equal(3, resumed.GetProperty("tick").GetInt64());
    }

    [Fact]
    public async Task Dashboard_is_served_at_root()
    {
        var html = await NewFactory().CreateClient().GetStringAsync("/", Ct);

        Assert.Contains("DEMIURGE", html);
        Assert.Contains("lib/signalr.min.js", html);
    }

    [Fact]
    public async Task Hub_pushes_every_tick()
    {
        var factory = NewFactory();
        var client = factory.CreateClient();
        await WorldAsync(client);

        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(client.BaseAddress!, "/hubs/world"), o =>
            {
                o.Transports = HttpTransportType.LongPolling; // TestServer has no real sockets
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();
        hub.On<JsonElement>("tick", msg => received.TrySetResult(msg));
        await hub.StartAsync(Ct);

        await PostAsync(client, "/api/god/step");
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(1, message.GetProperty("world").GetProperty("tick").GetInt64());
        Assert.Contains(message.GetProperty("events").EnumerateArray(), e => e.GetProperty("$type").GetString() == "TickEnded");
    }

    [Fact]
    public void Both_dictionaries_have_the_same_keys_and_cover_every_domain_identifier()
    {
        var env = NewFactory().Services.GetRequiredService<IWebHostEnvironment>();
        Dictionary<string, string> Load(string lang)
        {
            using var stream = env.WebRootFileProvider.GetFileInfo($"i18n/{lang}.json").CreateReadStream();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }

        var pt = Load("pt-BR");
        var en = Load("en");
        Assert.Equal(en.Keys.Order(StringComparer.Ordinal), pt.Keys.Order(StringComparer.Ordinal));

        var required = new List<string>();
        required.AddRange(typeof(WorldEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(WorldEvent)) && !t.IsAbstract).Select(t => $"event.{t.Name}"));
        required.AddRange(typeof(AgentAction).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AgentAction)) && !t.IsAbstract).Select(t => $"action.{t.Name}"));
        required.AddRange(Enum.GetNames<RejectionReason>().Select(n => $"reason.{n}"));
        required.AddRange(Enum.GetNames<DeathCause>().Select(n => $"cause.{n}"));
        required.AddRange(Enum.GetNames<StolenItem>().Select(n => $"item.{n}"));
        required.AddRange(Enum.GetNames<PainStatus>().Select(n => $"status.{n}"));
        required.AddRange(Enum.GetNames<PainCategory>().Select(n => $"category.{n}"));
        required.AddRange(typeof(PainCodes).GetFields().Select(f => $"pain.{f.GetValue(null)}"));
        required.AddRange(PlacesGraph.Default.Places.Select(p => $"place.{p.Id}"));

        Assert.All(required, key => Assert.True(en.ContainsKey(key), $"missing i18n key {key}"));
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _db, _db + "-wal", _db + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
