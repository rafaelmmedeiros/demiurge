# Demiurge.Host

**Responsibility:** composition root and presentation: Minimal API (god mode), SignalR hub, static dashboard (`wwwroot`), and the `SimulationHost` background loop that owns the current world.

**May depend on it:** tests only.

**Depends on:** `Demiurge.Harness`, `Demiurge.Agents`, `Demiurge.Engine`.

**Must not:**
- mutate the world outside the `SimulationHost` loop (endpoints enqueue `GodCommand`s);
- put domain rules in endpoints or in the dashboard;
- insert agent-provided text into the DOM as HTML.

**Concepts implemented** (see `docs/CONCEPTS.md`):
- Dashboard and state streaming — `Simulation/SimulationHost`, `Simulation/WorldView`, `wwwroot/app.js`
- Internationalization (i18n) — `wwwroot/i18n/`, `wwwroot/app.js`
