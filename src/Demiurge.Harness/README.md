# Demiurge.Harness

**Responsibility:** everything around the engine that keeps the simulation alive and observable: persistence (event store, snapshots), and soon tick orchestration, pain detectors and runtime mitigations.

**May depend on it:** `Demiurge.Host`, tests.

**Depends on:** `Demiurge.Engine`, `Demiurge.Agents`; packages `Dapper`, `Microsoft.Data.Sqlite` (ADR-004).

**Must not:**
- change world state except through `World.Step` / `World.FromEvents` / `World.FromSnapshot`;
- put storage formats (JSON attributes, SQL types) into the engine;
- UPDATE or DELETE rows in `events`.

**Concepts implemented** (see `docs/CONCEPTS.md`):
- Event sourcing — `Persistence/SqliteEventStore`, `Persistence/EventJson`, `SimulationSession`
- Agent loop (perceive → decide in parallel with deadline → act), failure isolation — `TickRunner`
- Harness, observability and pain detectors — `Pains/` (`TickContext`, detectors, `EdgeTrigger`, `PainRegistry`), `ISimulation`/`InMemorySimulation`
