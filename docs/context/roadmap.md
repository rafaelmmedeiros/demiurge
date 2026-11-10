# Pain-driven roadmap

A stage starts only when the pain that justifies it has been measured. Every stage ends with an oral exam (`mentoring.md`).

| Stage | Unlocking pain | What enters | Concepts to master |
|---|---|---|---|
| 1. Day 1 | — | engine, places graph, rules, SQLite, dashboard (pt-BR/en, canvas), new-world button, detectors | agent vs. environment, spatial model (places graph), perception→decision→action loop, tick-based simulation, determinism and seed, event sourcing, authoritative engine, rule-based agent, utility AI, harness, observability, pain-driven development, i18n |
| 2. LLM | behavior pains measured with `RuleBasedBrain`: `RESOURCE_COLLAPSE`, `GOAL_OSCILLATION`, `NO_SOCIAL_CONTACT` (ADR-005) | `LlmBrain` via `IChatClient`, fallback to rules | LLM as decision policy, tool calling, structured output and JSON schema, system prompt vs. context, context window and tokens, temperature, validation and retry, fallback, circuit breaker, cost and latency |
| 3. Memory | `REPETITION_LOOP` persists with LLM | memory stream, retrieval by recency × relevance × importance, `sqlite-vec` | short vs. long-term memory, embeddings, cosine similarity, vector search, RAG, retrieval scoring, summarization |
| 4. Reflection & planning | reactive agents, no mid-term goals | periodic reflection and daily plan | reflection, hierarchical planning, ReAct, goal decomposition, replanning |
| 5. Social & laws | `NO_SOCIAL_CONTACT`, theft without consequence | laws as world objects, punishment mechanics with cost | multi-agent systems, agent communication, asymmetric information, reputation, basic game theory, emergent behavior vs. model prior |
| 6. God mode | — | plague, drought, agent injection, planted personalities, whisper (perception injection) | experimental perturbation, reproducible scenarios, A/B with seed, society metrics (Gini, mortality, crime rate) |
| 7. Scale | `TICK_OVERRUN`, `STORE_GROWTH` | Channels, Postgres + pgvector, Marten, Orleans | concurrency and backpressure, actor model and virtual actors, event projections, snapshots |
| 8. Observability | hard to explain decisions | OpenTelemetry, Aspire dashboard, trace per decision | traces, spans, metrics, agent evaluation (evals), LLM-as-judge |
| 9. Visualization | dashboard insufficient | Blazor; later Unity consuming the same stream | simulation/presentation separation, state streaming |

References: Generative Agents (Park et al., Stanford, 2023), Project Sid (Altera, 2024), AI Town (a16z), Concordia (Google DeepMind), ReAct (Yao et al., 2022).
