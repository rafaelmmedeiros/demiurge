# World rules (MVP), contracts and god-mode API

## Resources per agent
`Food` (stock), `Money`, `Energy` (0–100), `Hunger` (0–100).

## Per tick
- `Hunger +4`, `Energy -2`.
- `Hunger >= 100` for 12 consecutive ticks = death by starvation.
- `Energy = 0` forces `Rest`.

## Actions (fixed schema)
`Work`, `Eat`, `Buy`, `Talk(targetId, text)`, `Steal(targetId)`, `Rest`.

## Engine validation
- `Buy` requires money and market stock.
- `Steal` requires a living target and has a chance of being seen (event `TheftWitnessed`).
- `Eat` requires food.
- A rejected action emits `ActionRejected` with a reason; that reason enters the agent's perception on the next tick.

## Core contracts
```csharp
public interface IAgentBrain
{
    Task<AgentDecision> DecideAsync(Perception perception, CancellationToken ct);
}

public interface IPainDetector
{
    IEnumerable<PainSignal> Inspect(TickReport report);
}
```

## God-mode API (Day 1)
- `GET  /health`
- `GET  /api/world` — current snapshot
- `GET  /api/events?sinceTick=N`
- `GET  /api/pains` — aggregated pains, sorted by severity × frequency
- `POST /api/god/pause` · `POST /api/god/resume` · `POST /api/god/step`
- `POST /api/god/events` — event injection (stub on Day 1; plague and drought come later in the roadmap)
- SignalR hub: `/hubs/world` (tick events, agent changes, new pains)
