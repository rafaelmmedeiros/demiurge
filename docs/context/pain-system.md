# Pain system (pain-driven development)

The harness must **sense** what is wrong, **mitigate** at runtime what it can, and **expose** the rest so the next development session fixes it structurally.

## What a pain is
A measured signal, never an opinion.

| Field | Example |
|---|---|
| `Code` | `LLM_INVALID_ACTION` |
| `Category` | `Technical`, `Behavior` or `Knowledge` |
| `Severity` | 1–5 |
| `Count`, `FirstTick`, `LastTick` | |
| `Sample` | context of one occurrence (agent, tick, summarized input/output) |
| `Mitigation` | what the runtime did automatically |
| `Status` | `Open`, `Mitigated`, `Resolved` |

Detectors implement `IPainDetector` (see `world.md`), run at the end of every tick; pains are persisted in the `pains` table and exposed at `/api/pains` (sorted by severity × frequency) and on the dashboard. `Knowledge` pains live in `docs/PAINS.md`, not in the runtime.

## Technical pains (the harness)

| Code | Signal | Automatic runtime mitigation |
|---|---|---|
| `TICK_EXCEPTION` | unhandled exception processing an agent | isolate the agent for the tick (`Idle`), log stack, tick continues |
| `TICK_OVERRUN` | tick took longer than `TickIntervalMs` | next tick doesn't wait for late decisions; agents without a decision repeat their last valid action or `Rest` |
| `LLM_INVALID_ACTION` | unparseable or out-of-schema response | 1 retry with the error in the prompt; if it fails, `RuleBasedBrain` decides |
| `LLM_UNAVAILABLE` | timeout / network error | circuit breaker: after 3 failures all agents use rules for 60 ticks, then probe again |
| `LLM_LATENCY` | decision p95 > 50% of tick interval | reduce frequency: agent queries the LLM only every N ticks or on a new event |
| `LLM_COST` | tokens per tick above configured budget | same frequency reduction; record tokens per agent |
| `STORE_GROWTH` | DB size or query latency above limit | force snapshot and read compaction |

## Behavior pains (decision quality)
They show where the brain is weak. On Day 1, with `RuleBasedBrain`, **they must appear** — that is what justifies the LLM, then memory, then planning.

| Code | Signal | Indicates |
|---|---|---|
| `STARVATION_PARADOX` | agent starved while having money and the market having food | bad decision, no prioritization |
| `REPETITION_LOOP` | same action for N ticks without improving any resource | no memory or planning |
| `ACTION_REJECTION_RATE` | > 20% of an agent's actions rejected | incomplete perception or bad prompt |
| `NO_SOCIAL_CONTACT` | no `Talk` for N ticks while other agents are alive | no social dimension |
| `RESOURCE_COLLAPSE` | mortality or Gini above limit | unbalanced world dynamics |

## Knowledge pains (the lead)

| Code | Signal | Resolution |
|---|---|---|
| `KNOWLEDGE_GAP` | weak answer in the oral exam, or the lead can't explain a part | review the CONCEPTS.md entry, re-explain with real code, new oral exam on the point |
| `UNDOCUMENTED_CONCEPT` | concept in code with no CONCEPTS.md entry | document before any other task |

## Two loops

**Runtime loop (seconds):** the mitigations above. They keep the simulation alive but don't fix the cause. Every applied mitigation is also recorded as a `Mitigated` pain; a recurring mitigation signals a structural problem.

**Development loop (per Claude Code session) — mandatory ritual:**
1. **Start:** `docker compose up -d --build`, check `/health`, read `GET /api/pains` and `docs/PAINS.md`.
2. **Pick the highest severity × frequency pain**, unless the lead asks for something else. `UNDOCUMENTED_CONCEPT` has priority.
3. **Explain** the pain and the proposed fix, including concepts involved (`mentoring.md`).
4. **Reproduce:** write a failing test (seed + scenario) before fixing.
5. **Fix the cause** with the smallest possible change. New technology → record in `docs/DECISIONS.md`.
6. **Verify:** test green, `docker compose up` still works, the pain disappears or falls below threshold over N ticks with the same seed.
7. **Document:** update `docs/PAINS.md`, `docs/CONCEPTS.md`, `docs/INTERVIEW.md` and, if architecture changed, `CLAUDE.md` / `docs/context/*`.
8. **Didactic summary** (`mentoring.md`).

No relevant open pain → **create pressure**: more agents, more scarcity, or a god-mode event, until a new pain appears.
