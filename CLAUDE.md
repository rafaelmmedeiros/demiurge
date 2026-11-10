# DEMIURGE — AI-agent civilization simulator

Personal, **didactic** project for learning AI agents. Agents control entities that are born, work, sustain themselves, interact and die in a simulated world. A human operator ("god mode") watches and injects events (plague, drought, scarcity) to see whether rules, laws and institutions emerge, and whether they are enforced.

Code is half the product. The other half is **understanding**: the project lead must be able to answer, in a technical interview, any question about what was built, why, and which alternatives were discarded. Claude Code is implementer **and technical mentor** — see `docs/context/mentoring.md`.

This file is the source of truth. Details live in on-demand context docs (table below): read the relevant one **before** acting on that area.

## Language policy

| Context | Language |
|---|---|
| Code, identifiers, event names, pain codes, commits, config, scripts, this file, `docs/context/*` | English |
| Dashboard UI | Bilingual **pt-BR + en**, switchable at runtime. No hardcoded user-facing strings (see `docs/context/architecture.md`) |
| Chat with the lead, explanations, session summaries | Portuguese (pt-BR). Standard English technical terms are kept, with a translation on first use |
| Learning/product docs: `docs/CONCEPTS.md`, `INTERVIEW.md`, `PAINS.md`, `DECISIONS.md`, `VISION.md`, `SESSIONS.md`, `interviews/` | pt-BR (study material for the lead) |

## Session continuity (nothing may be lost between sessions)

- **Start:** read `docs/SESSIONS.md` (latest entry) and `docs/VISION.md`.
- **During:** when the lead answers questions (vision, requirements, oral exams), record answers verbatim in `docs/interviews/` right away, then update the derived doc. Commit and push at each milestone.
- **End:** add an entry to the top of `docs/SESSIONS.md` (done · decisions · open items), commit and push.
- Git: use the repo-local personal identity (`rafaelmmedeiros`), never the global work one.

## Inviolable rules

1. **The engine is authoritative.** The LLM never mutates state. It proposes an action (tool call with a fixed schema); the engine validates and applies it, or rejects it with a reason.
2. **Determinism.** Same seed + same decision sequence = same world. All randomness goes through a seeded `IRandom`. No `DateTime.Now` in the engine; time is the tick. No non-deterministic iteration order.
3. **Everything is an event.** Every state change emits an append-only event. World state is rebuildable from events.
4. **The harness grows by pain, not anticipation.** No technology, layer, dependency or compose service without a recorded pain justifying it. Postgres, Marten, Orleans, Blazor, Unity are on the roadmap but forbidden until their pain appears.
5. **Always starts with one command.** `docker compose up -d --build` works at any time, with no API key and no manual config.
6. **A failure never stops the simulation.** An agent error isolates that agent for the tick; an LLM error falls back to the rule brain. The tick always closes; it never waits indefinitely for an LLM.
7. **Nothing enters unexplained.** No concept, pattern or technology is implemented before it is explained to the lead and documented in `docs/CONCEPTS.md`. A part the lead cannot explain is a `KNOWLEDGE_GAP` pain.

Also never: let the LLM touch the database; commit API keys, `.env` or the SQLite DB; end a session without the didactic summary.

## On-demand context

| Read | When |
|---|---|
| `docs/VISION.md` | Every session; any scope or priority decision |
| `docs/context/mentoring.md` | Introducing any concept; writing CONCEPTS/INTERVIEW entries; ending a session (didactic summary); "me argua" / end of a roadmap stage |
| `docs/context/pain-system.md` | **Start of every session** (dev-loop ritual); working on detectors, mitigations, `PAINS.md` |
| `docs/context/architecture.md` | Creating projects/files, adding dependencies, persistence, dashboard/i18n, configuration, Docker |
| `docs/context/world.md` | Engine rules, resources, actions, validation, brain/detector contracts, god-mode API |
| `docs/context/stage-1.md` | Bootstrapping the system (Day 1) and checking its done criteria |
| `docs/context/roadmap.md` | Deciding what comes next; checking whether a stage is unlocked |

## Repo map

```
src/Demiurge.Engine/    world, entities, actions, validation, tick — zero IO/LLM deps
src/Demiurge.Agents/    IAgentBrain, RuleBasedBrain, LlmBrain, perception, prompts
src/Demiurge.Harness/   tick orchestration, pain detectors, mitigations, persistence
src/Demiurge.Host/      ASP.NET Core: API, SignalR hub, wwwroot (dashboard), DI
tools/Demiurge.Probe/   batch pain measurement (N seeds × T ticks)
tests/                  xUnit: Engine.Tests, Agents.Tests, Harness.Tests, Host.Tests
docs/                   learning docs (pt-BR) + docs/context/ (Claude context, en)
```

## Commands

```bash
docker compose up -d --build        # everything at http://localhost:8080
docker compose logs -f demiurge
docker compose down                 # stop (data kept in volume)
docker compose down -v              # stop and wipe the world
# docker compose --profile llm up -d  # Ollama: stage 2 (ADR-006)
dotnet build && dotnet test
dotnet run --project src/Demiurge.Host   # outside Docker, port 8080
dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000   # measure pains
```
