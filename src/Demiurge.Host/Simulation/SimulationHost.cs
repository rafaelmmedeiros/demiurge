using System.Threading.Channels;
using Demiurge.Agents;
using Demiurge.Engine.Domain;
using Demiurge.Harness;
using Demiurge.Harness.Pains;
using Demiurge.Harness.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Demiurge.Host.Simulation;

/// <summary>A god-mode command. Completed by the simulation loop with the resulting view.</summary>
public abstract record GodCommand
{
    internal TaskCompletionSource<WorldView> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record PauseCommand : GodCommand;

public sealed record ResumeCommand : GodCommand;

public sealed record StepCommand : GodCommand;

public sealed record NewWorldCommand(ulong Seed, int AgentCount) : GodCommand;

/// <summary>
/// Owns the current world and runs the simulation loop: tick → publish view → wait (interval − tick
/// duration) → repeat. God-mode commands arrive through a single-reader channel and are executed
/// by this loop only, so the world has exactly one writer: no locks, no race between a "step"
/// request and a running tick. Readers get the latest immutable <see cref="WorldView"/>.
/// Concept: Dashboard and state streaming — docs/CONCEPTS.md#dashboard-e-streaming-de-estado
/// </summary>
public sealed class SimulationHost : BackgroundService
{
    private readonly Channel<GodCommand> _commands = Channel.CreateUnbounded<GodCommand>();
    private readonly SqliteEventStore _store;
    private readonly DemiurgeOptions _options;
    private readonly TimeProvider _clock;
    private readonly IHubContext<WorldHub> _hub;
    private readonly ILogger<SimulationHost> _logger;

    private SimulationSession? _session;
    private TickRunner? _runner;
    private bool _paused;
    private volatile WorldView? _view;

    public SimulationHost(
        SqliteEventStore store, IOptions<DemiurgeOptions> options, TimeProvider clock,
        IHubContext<WorldHub> hub, ILogger<SimulationHost> logger)
    {
        _store = store;
        _options = options.Value;
        _clock = clock;
        _hub = hub;
        _logger = logger;
        _paused = _options.StartPaused;
    }

    /// <summary>Latest published view; null until the loop has started.</summary>
    public WorldView? View => _view;

    public SqliteEventStore Store => _store;

    /// <summary>Queues a command for the loop and waits for its result.</summary>
    public async Task<WorldView> SendAsync(GodCommand command, CancellationToken ct)
    {
        await _commands.Writer.WriteAsync(command, ct);
        return await command.Completion.Task.WaitAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // let the host finish starting before the first tick
        Load(_store.LatestWorldId() is { } id ? SimulationSession.Resume(_store, id) : StartNew(_options.Seed, _options.AgentCount));
        await PublishAsync([], stoppingToken);

        var interval = TimeSpan.FromMilliseconds(_options.TickIntervalMs);
        var nextTickAt = _clock.GetUtcNow() + interval;
        Task<bool>? waitForCommand = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            waitForCommand ??= _commands.Reader.WaitToReadAsync(stoppingToken).AsTask();
            var delay = nextTickAt - _clock.GetUtcNow();
            var timer = _paused
                ? Task.Delay(Timeout.Infinite, stoppingToken)
                : Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, _clock, stoppingToken);

            try
            {
                await Task.WhenAny(waitForCommand, timer);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            if (waitForCommand.IsCompleted)
            {
                waitForCommand = null;
                while (_commands.Reader.TryRead(out var command))
                    await ExecuteAsync(command, stoppingToken);
                if (!_paused && nextTickAt < _clock.GetUtcNow())
                    nextTickAt = _clock.GetUtcNow();
                continue;
            }

            var started = _clock.GetUtcNow();
            await TickAsync(stoppingToken);
            // Overrun mitigation: if the tick took longer than the interval, the next one starts now.
            nextTickAt = started + interval > _clock.GetUtcNow() ? started + interval : _clock.GetUtcNow();
        }
    }

    private async Task ExecuteAsync(GodCommand command, CancellationToken ct)
    {
        try
        {
            switch (command)
            {
                case PauseCommand:
                    _paused = true;
                    await PublishAsync([], ct);
                    break;
                case ResumeCommand:
                    _paused = false;
                    await PublishAsync([], ct);
                    break;
                case StepCommand:
                    await TickAsync(ct);
                    break;
                case NewWorldCommand n:
                    Load(StartNew(n.Seed, n.AgentCount));
                    _logger.LogInformation("New world {WorldId} (seed {Seed}, {Agents} agents)", _session!.WorldId, n.Seed, n.AgentCount);
                    await PublishAsync([], ct);
                    break;
            }

            command.Completion.TrySetResult(_view!);
        }
        catch (Exception ex)
        {
            command.Completion.TrySetException(ex);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await _runner!.RunTickAsync(ct);
            await PublishAsync(outcome.Report.Events, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Rule 6: a failing tick (e.g. the database) is logged and the loop keeps running.
            _logger.LogError(ex, "Tick failed for world {WorldId}", _session?.WorldId);
            _runner!.Pains.Record([new PainSignal(PainCodes.TickException, PainCategory.Technical, 4,
                _session!.World.Tick, null, $"Tick failed: {ex.GetType().Name}: {ex.Message}", "Tick skipped; loop continued.")]);
        }
    }

    private SimulationSession StartNew(ulong seed, int agentCount) =>
        SimulationSession.Start(_store, new WorldConfig { Seed = seed, AgentCount = agentCount }, _clock);

    private void Load(SimulationSession session)
    {
        _session = session;
        var pains = new PainRegistry(_store.LoadPains(session.WorldId), changed => _store.SavePains(session.WorldId, changed));
        _runner = new TickRunner(
            session,
            _ => CreateBrain(),
            TickRunnerOptions.FromInterval(TimeSpan.FromMilliseconds(_options.TickIntervalMs)),
            _clock,
            PainDetectors.CreateDefault(),
            pains);
    }

    private IAgentBrain CreateBrain() => _options.Brain switch
    {
        "Rules" => new RuleBasedBrain(),
        _ => throw new InvalidOperationException($"Brain '{_options.Brain}' is not available yet (stage 2). Use 'Rules'."),
    };

    private async Task PublishAsync(IReadOnlyList<Demiurge.Engine.Events.WorldEvent> events, CancellationToken ct)
    {
        var view = WorldView.From(_session!.WorldId, _session.World, _paused, _options.TickIntervalMs, _runner!.Pains.Ranked());
        _view = view;
        try
        {
            await _hub.Clients.All.SendAsync("tick", new TickMessage(view, EventViews.ToJson(events)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to push tick {Tick} to dashboard clients", view.Tick);
        }
    }
}

/// <summary>SignalR hub: server-to-client only for now (message "tick"). Commands go through the HTTP API.</summary>
public sealed class WorldHub : Hub;
