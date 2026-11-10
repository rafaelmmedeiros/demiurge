# Demiurge.Engine

**Responsibility:** the world and its rules — entities, resources, ticks, (soon) actions and validation. The single authority over world state.

**May depend on it:** `Demiurge.Agents`, `Demiurge.Harness`, `Demiurge.Host`, tests.

**Must not:**
- reference any IO, database, HTTP or LLM package (no `PackageReference` at all);
- use `DateTime.Now`, `Guid.NewGuid()`, `System.Random`, `string.GetHashCode()` or unordered iteration;
- use floating point for resources.

**Concepts implemented** (see `docs/CONCEPTS.md`):
- Tick-based simulation — `World.Step()`
- Determinism and seed — `Randomness/IRandom`, `Randomness/SplitMix64`
- Authoritative engine — `Actions/AgentAction`, `Actions/ActionRules`, `Perception`, `World.ResolveDecisions`
- Places graph — `Domain/Places`, `World.AdvanceTravel`, `ActionRules.ApplySteal`
