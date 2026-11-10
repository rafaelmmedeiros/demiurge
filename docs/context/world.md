# World rules (MVP), contracts and god-mode API

## Space: places graph
The world is a graph of places, not a grid (see `docs/VISION.md` §4, `docs/DECISIONS.md` ADR-001).

- MVP places (`PlacesGraph.Default`): `home`, `workplace`, `market`, `square`. Home–Workplace and Market–Workplace = 2 ticks; every other pair = 1 tick. Everyone starts at `home`.
- **Complete graph:** every pair of places has a direct edge with a travel time in ticks. No pathfinding in the MVP; it enters only if the graph stops being complete.
- The graph is world configuration (deterministic, part of the seed scenario). Each place also has display coordinates `(x, y)` used **only** by the dashboard, never by engine logic.
- An agent is either **at a place** or **in transit** on an edge `(from, to, ticksRemaining)`.
- In transit: needs keep ticking; the agent makes no decision (proposals rejected with `InTransit`), cannot be targeted, cannot witness.
- Trip mechanics: `MoveTo` emits `TravelStarted`; at the end of each tick travel advances (`TravelProgressed`, or `Arrived` when it reaches 0). Cost 1 arrives at the end of the same tick.

## Resources per agent
`Food` (stock), `Money`, `Energy` (0–100), `Hunger` (0–100).

## Per tick
- `Hunger +4`, `Energy -2`.
- `Hunger >= 100` for 12 consecutive ticks = death by starvation.
- `Energy = 0` forces `Rest`.
- Order inside a tick: decisions are resolved first (the agent acts during the hour), then needs decay, then the market restocks.

| Value | Constant / config | Default |
|---|---|---|
| Wage per `Work` | `WorldRules.WagePerWork` | 10 |
| Energy cost of `Work` | `WorldRules.WorkEnergyCost` | 8 |
| Hunger relief per `Eat` | `WorldRules.MealHungerRelief` | 30 |
| Energy per `Rest` | `WorldRules.RestEnergyGain` | 10 |
| Food price | `WorldConfig.FoodPrice` | 5 |
| Market stock: initial / restock per tick / capacity | `WorldConfig` | 20 / 1 / 30 |

## Actions (fixed schema)

| Action | Where | Notes |
|---|---|---|
| `MoveTo(placeId)` | anywhere | starts transit; arrives after the edge's travel time |
| `Work` | `Workplace` | earns money; requires Energy ≥ `WorkEnergyCost` |
| `Buy` | `Market` | requires money and market stock |
| `Eat` | anywhere | requires food |
| `Rest` | anywhere | +10 energy at `Home`, +5 elsewhere |
| `Talk(targetId, text)` | same place as target | text non-empty, ≤ 200 chars; listener hears it next tick |
| `Steal(targetId)` | same place as target | moves 1 food; target must have food |

## Engine validation
- Location requirements above; target must be alive, at the same place, not in transit.
- `Steal`: `Stole` is always emitted (god's view). Then each agent at the same place (victim included, thief excluded, not in transit) rolls `TheftWitnessChancePercent` (default 50) in id order → `TheftWitnessed` for each one who saw it. **The world never punishes**; consequences come only from other agents.
- Engine-level rejections: `UnknownAgent`, `AgentNotAlive`, `DuplicateDecision` (all proposals of an agent that sent more than one), `Exhausted` (energy 0 and action other than `Rest`: refused, `Rest` applied instead).
- Decisions are sorted by `AgentId` and then shuffled with the seeded RNG: submission order never matters.
- Brains receive an immutable `Perception`, never the mutable `Agent`.
- A rejected action emits `ActionRejected` with a reason (full list: `RejectionReason` enum); that reason enters the agent's perception on the next tick.
- Perception contains only what the agent can know: own resources, location, people present, messages heard and thefts witnessed in the last tick, market stock/price, the map. Never the whole world (asymmetric information).

## Worlds
- Each world has a `WorldId`, a seed and a config (agent count, places graph). All events carry the `WorldId`.
- Creating a new world stops the current one; old worlds stay in the database (replay, comparison).

## Event sourcing rules
- State changes only in `World.Apply(event)`; rules compute events and `Emit` applies + records them.
- Events carry resulting values (assignment on apply, never a rule re-run).
- Every tick ends with `TickEnded { RngState }`; genesis too. Snapshots include the RNG state.
- Adding an event type: add it to `World.Apply`, make the test scenario produce it (`EventSourcingTests.Scenario_covers_every_event_type`), and keep its JSON round-trip green.

## Core contracts
```csharp
// Demiurge.Agents. Returns only the action; the harness binds it to perception.Self.
public interface IAgentBrain
{
    Task<AgentAction> DecideAsync(Perception perception, CancellationToken ct);
}

// Demiurge.Harness.Pains. TickContext = events + decisions + brain failures + duration + snapshot after.
public interface IPainDetector
{
    IEnumerable<PainSignal> Inspect(TickContext ctx);
}
```

## God-mode API (Day 1)
- `GET  /health`
- `GET  /api/world` — current snapshot (agents with location/transit, places graph)
- `GET  /api/events?sinceTick=N`
- `GET  /api/pains` — aggregated pains, sorted by severity × frequency
- `POST /api/god/pause` · `POST /api/god/resume` · `POST /api/god/step`
- `POST /api/god/worlds` — create a new world `{ seed, agentCount }` and switch to it
- `POST /api/god/events` — event injection (stub on Day 1; plague, drought, whisper, planted personalities come later)
- SignalR hub: `/hubs/world` (tick events, agent changes incl. positions, new pains)
