# Stage 1 — Day 1 bootstrap

## Goal
```
docker compose up -d --build
```
Open **http://localhost:8080** and see: tick counter advancing, agent list with hunger/energy/money, live event feed, and the pains panel. UI in pt-BR and en.

## Done criteria
- [ ] `docker compose up -d --build` starts without errors on a clean machine (only Docker installed).
- [ ] `GET http://localhost:8080/health` returns 200.
- [ ] Dashboard at `/` updates in real time via SignalR.
- [ ] Dashboard switchable between pt-BR and en, no hardcoded strings.
- [ ] 5 agents living with `RuleBasedBrain`, no LLM.
- [ ] Pause / resume / step-1-tick buttons working.
- [ ] Pains panel shows at least the behavior pains (rule agents are weak on purpose; see `pain-system.md`).
- [ ] `dotnet test` green.
- [ ] `docs/CONCEPTS.md` has the stage 1 entries (see `roadmap.md`) and `docs/INTERVIEW.md` the matching questions.
- [ ] Stage 1 oral exam done with the lead (`mentoring.md`).

Stage 1 introduces many concepts at once. Rule 7 still applies: explain each before implementing it, in a sensible order (e.g. tick-based simulation → determinism/seed → authoritative engine → event sourcing → rule-based agent → harness/pains).
