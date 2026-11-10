# Architecture, stack and conventions

Minimal stack; it grows only by recorded pain (CLAUDE.md rule 4).

## Stack (Day 1)
Baseline and versions reviewed in `docs/DECISIONS.md` ADR-003.
- .NET 10 LTS, C#, a single process. SDK pinned in `global.json`; shared settings in `Directory.Build.props`; package versions only in `Directory.Packages.props` (Central Package Management).
- ASP.NET Core: Minimal API + SignalR + static files.
- Engine runs as a `BackgroundService` inside the same host.
- Persistence: SQLite on a Docker volume (`/data/demiurge.db`) via **Dapper** + `Microsoft.Data.Sqlite` 10.x (ADR-004). Tables `worlds`, `events` (append-only, JSON payload with `$type`), `snapshots` (every 100 ticks). JSON format lives in `Demiurge.Harness/Persistence/EventJson.cs`, never in the engine.
- Dashboard: plain HTML + JS in `wwwroot` with the SignalR JS client. No front-end framework on Day 1.
- LLM access: `Microsoft.Extensions.AI` (`IChatClient`). Agent Framework only if a pain justifies it.
- Running one test project: `dotnet test --project tests/<Name>/<Name>.csproj` (MTP syntax); `dotnet test` runs the whole solution.
- Tests: xUnit v3 (package line 4.x) on Microsoft Testing Platform v2 (`global.json` → `test.runner`). Test projects are `OutputType Exe`.

## Layout
```
/
├── CLAUDE.md
├── docker-compose.yml
├── Dockerfile                  # multi-stage: sdk:10.0 -> aspnet:10.0, exposes 8080
├── .env.example
├── Demiurge.slnx              # XML solution format (SDK 10 default)
├── global.json                 # pinned SDK + test runner
├── Directory.Build.props       # shared build settings
├── Directory.Packages.props    # central package versions
├── docs/
│   ├── context/                # Claude on-demand context (en)
│   ├── CONCEPTS.md             # living glossary (pt-BR)
│   ├── INTERVIEW.md            # question bank (pt-BR)
│   ├── PAINS.md                # open/resolved pains (pt-BR)
│   └── DECISIONS.md            # ADRs (pt-BR)
├── src/
│   ├── Demiurge.Engine/        # world, entities, resources, actions, validation, tick. Zero IO/LLM deps.
│   ├── Demiurge.Agents/        # IAgentBrain, RuleBasedBrain, LlmBrain, perception, prompts
│   ├── Demiurge.Harness/       # tick orchestration, pain detectors, mitigations, persistence
│   └── Demiurge.Host/          # ASP.NET Core: API, SignalR hub, wwwroot, DI composition
├── tools/
│   └── Demiurge.Probe/         # batch pain measurement: N seeds × T ticks, in memory
└── tests/
    ├── Demiurge.Engine.Tests/
    ├── Demiurge.Agents.Tests/
    ├── Demiurge.Harness.Tests/
    └── Demiurge.Host.Tests/
```

Dependency direction: `Host → Harness → Agents → Engine`. Engine depends on nothing.

## Conventions
- Code, identifiers, event names, pain codes, logs, commits: English.
- Comments explain the **why** and the concept applied, linking to `docs/CONCEPTS.md`; they don't narrate code.
- Each `src/` project has a short `README.md` (en): responsibility, who may depend on it, what it must not do, concepts it implements.

## Dashboard i18n (pt-BR + en)
- Every user-facing string comes from a locale dictionary: `wwwroot/i18n/en.json`, `wwwroot/i18n/pt-BR.json`. Same keys in both; a test or startup check flags missing keys.
- Initial locale: saved choice (localStorage) → `navigator.language` → `en`. Language toggle in the header.
- Domain identifiers (event types, pain codes, action names) stay English in data and API; the UI maps them to translated labels via keys (e.g. `event.TheftWitnessed`, `pain.STARVATION_PARADOX`). Unknown keys fall back to the raw identifier.
- API responses and logs are not localized.
- Implemented: `wwwroot/i18n/*.json`, `app.js` (`t`, `describeEvent`); key coverage enforced by `HostTests`.

## Host
- `SimulationHost` (BackgroundService) is the single writer of the world; god-mode endpoints enqueue `GodCommand`s on a `Channel` (ADR-006). Readers use the immutable `WorldView`.
- On start: resume the latest world in the database, else create one from config.
- SignalR hub `/hubs/world`, message `tick` = `{ world, events }`. JS client vendored at `wwwroot/lib/signalr.min.js` (10.0.11).
- Agent-provided text goes into the DOM with `textContent` only.

## Docker
- `Dockerfile` multi-stage (`sdk:10.0` → `aspnet:10.0`, non-root `app` user, `DEMIURGE__DatabasePath=/data/demiurge.db`); `docker-compose.yml` single service, port 8080, volume `demiurge-data`, optional `.env`. Ollama profile arrives in stage 2 (ADR-006).

## Configuration
All via environment variables (prefix `DEMIURGE__`), with working defaults:

| Variable | Default | Description |
|---|---|---|
| `DEMIURGE__Seed` | `42` | World seed |
| `DEMIURGE__AgentCount` | `5` | Initial agents |
| `DEMIURGE__TickIntervalMs` | `1000` | Real time between ticks (1 tick = 1 simulated hour) |
| `DEMIURGE__Brain` | `Rules` | `Rules` (`Llm` in stage 2) |
| `DEMIURGE__DatabasePath` | `data/demiurge.db` (container: `/data/demiurge.db`) | SQLite file |
| `DEMIURGE__StartPaused` | `false` | Start with the clock paused |
| `DEMIURGE__Llm__Provider` | — | `Ollama`, `Anthropic` or `OpenAICompatible` |
| `DEMIURGE__Llm__Endpoint` | — | Provider URL |
| `DEMIURGE__Llm__Model` | — | Model name |
| `DEMIURGE__Llm__ApiKey` | — | Never commit; use `.env` (gitignored) |

Optional local LLM via compose profile (`docker compose --profile llm up -d`) arrives in stage 2 (ADR-006). Day 1 does not depend on it.
