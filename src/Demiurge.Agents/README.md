# Demiurge.Agents

**Responsibility:** agent programs (brains): map a `Perception` to a proposed `AgentAction`. Today the contract `IAgentBrain`; next `RuleBasedBrain`, later `LlmBrain`.

**May depend on it:** `Demiurge.Harness`, `Demiurge.Host`, tests.

**Depends on:** `Demiurge.Engine` (only `Perception` and `AgentAction` types).

**Must not:**
- receive or hold a `World` or `Agent` reference (only the immutable `Perception`);
- write state, persist anything or call the database;
- block on IO without honouring the `CancellationToken`.

**Concepts implemented** (see `docs/CONCEPTS.md`):
- Agent and the perception → decision → action loop — `IAgentBrain`
- Rule-based agent / utility AI (weak on purpose) — `RuleBasedBrain`
