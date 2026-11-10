# Stage 1 — Day 1 bootstrap

## Goal
```
docker compose up -d --build
```
Open **http://localhost:8080** and see: tick counter advancing, a canvas with places and pawns moving between them, agent list with hunger/energy/money, live event feed, and the pains panel. UI in pt-BR and en.

## Done criteria
- [ ] `docker compose up -d --build` starts without errors on a clean machine (only Docker installed).
- [ ] `GET http://localhost:8080/health` returns 200.
- [ ] Dashboard at `/` updates in real time via SignalR.
- [ ] Dashboard switchable between pt-BR and en, no hardcoded strings.
- [ ] Places graph (4 places, complete graph with travel times); pawns drawn on canvas, interpolated along edges while in transit.
- [ ] 5 agents living with `RuleBasedBrain`, no LLM; actions respect location rules (`world.md`).
- [ ] Pause / resume / step-1-tick buttons working.
- [ ] "New world" (seed + agent count) creates and switches to a fresh world; same seed reproduces the same world.
- [ ] Pains panel shows at least the behavior pains (rule agents are weak on purpose; see `pain-system.md`).
- [ ] `dotnet test` green, including a determinism test (same seed + N ticks = same event stream).
- [ ] `docs/CONCEPTS.md` has the stage 1 entries (see `roadmap.md`) and `docs/INTERVIEW.md` the matching questions.
- [ ] Stage 1 oral exam done with the lead (`mentoring.md`).

## Concept order
Rule 7 applies: explain each concept before implementing it.
1. Tick-based simulation
2. Determinism and seed
3. Authoritative engine (propose → validate → apply)
4. Places graph (spatial model)
5. Event sourcing
6. Agent vs. environment, perception → decision → action loop
7. Rule-based agent / utility AI
8. Harness, observability, pain detectors
9. Dashboard: SignalR streaming, canvas, i18n
