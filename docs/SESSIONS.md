# Diário de sessões

Uma entrada por sessão, a mais recente no topo. Serve para a próxima sessão retomar sem perder contexto.
Formato: o que foi feito · decisões · pendências.

---

## 2026-11-10 — Sessão 1: organização e visão

**Feito**
- CLAUDE.md original (19 KB) reduzido para ~4,5 KB; detalhes movidos para `docs/context/*.md` (lidos sob demanda).
- Repositório criado: https://github.com/rafaelmmedeiros/demiurge (conta pessoal, identidade git configurada só localmente).
- `.gitignore` (`.env`, `*.db`, `bin/`, `obj/`) e `.gitattributes` (LF; `.sln` em CRLF).
- Entrevista de visão concluída (4 rodadas, `docs/interviews/2026-11-10-visao.md`) e `docs/VISION.md` consolidado.

**Decisões**
- Idioma: código, tooling, `CLAUDE.md` e `docs/context/` em inglês; dashboard bilíngue pt-BR + en; chat e documentos de estudo (`CONCEPTS`, `INTERVIEW`, `PAINS`, `DECISIONS`, `VISION`, este diário) em pt-BR.
- Docs de contexto referenciados por tabela "leia quando", não por `@import` (import carrega tudo em toda sessão).
- Primeiro passo do projeto: definir a visão por entrevista, antes de qualquer código.
- Visão: experimento social (não jogo, não life sim, não comercial); espaço como grafo de lugares com tempo de viagem; o mundo não pune crimes, só agentes reagem (testemunha = mesmo lugar, mesmo tick); modo deus com perturbações, criar/resetar mundo, personalidades plantadas e sussurro na percepção; LLM local por padrão, até US$ 20/mês.

- Líder aprovou incluir na etapa 1: grafo de lugares (ADR-001) e botão "Novo mundo" (ADR-002). `world.md`, `stage-1.md`, `roadmap.md` atualizados; criados `DECISIONS.md` e `INTERVIEW.md`.

- Conceitos 1 (simulação baseada em ticks) e 2 (determinismo e seed) explicados, aprovados, implementados e documentados (`CONCEPTS.md`, `INTERVIEW.md`).
- Primeiro código: `Demiurge.slnx`, `src/Demiurge.Engine` (`World.Step`, necessidades, morte por fome, `IRandom`/`SplitMix64`), `tests/Demiurge.Engine.Tests` — 10 testes verdes (vetor de referência do PRNG, determinismo por SHA-256 de 500 ticks, regras de necessidade e fome).
- Conceito 3 (engine autoritativo) explicado, aprovado e implementado: ações `Rest/Eat/Work/Buy`, mercado, `Perception` imutável, validação com `ActionRejected` e motivo na percepção seguinte. Ordem do tick mudou para decisões → necessidades → mercado. 25 testes verdes.
- Conceito 4 (grafo de lugares) explicado, aprovado e implementado: 4 lugares em grafo completo, `MoveTo` com tempo de viagem, requisitos de lugar, `Talk`, `Steal` com testemunhas (50%, vítima inclusa), percepção com lugar/presentes/mensagens/roubos vistos. 42 testes verdes.
- Conceito 5 (event sourcing) explicado, aprovado e implementado: estado muda só em `World.Apply(evento)`, eventos com valor final, `TickEnded` com estado do RNG, `Demiurge.Harness` com `SqliteEventStore` (Dapper, decisão do líder — ADR-004), snapshots a cada 100 ticks, `SimulationSession` com Start/Resume. Teste de restart: parar no 250, retomar, igual a 500 ininterruptos. 56 testes verdes. Medido: 7,3 eventos/tick, 0,52 ms/tick de escrita, ~1 MB/1000 ticks.
- Conceito 6 (agente e laço) explicado, aprovado e implementado: `Demiurge.Agents` com `IAgentBrain` devolvendo `AgentAction` (contrato ajustado com ok do líder), `TickRunner` no harness (cérebros em paralelo, prazo com `TimeProvider`, falhas isoladas como `BrainFailure`). 63 testes verdes, estáveis em 3 repetições.
- Conceito 7 (rule-based / utility AI) explicado, aprovado e implementado: `RuleBasedBrain` com 4 objetivos pontuados, notas públicas (`Score`), fraco de propósito. Novo projeto `tests/Demiurge.Agents.Tests`. 71 testes verdes. Medição em 10 seeds × 1000 ticks: 41/50 mortes, 0 paradoxo da fome, 0% rejeição, 0 conversas, 55% de troca de objetivo, 0,13 trabalho por viagem.
- ADR-005 aprovado: nova dor `GOAL_OSCILLATION`; gatilho da etapa 2 = dores comportamentais medidas (`RESOURCE_COLLAPSE`, `GOAL_OSCILLATION`, `NO_SOCIAL_CONTACT`); medições reproduzíveis via detectores no conceito 8.
- Conceito 8 (harness, observabilidade, detectores) explicado, aprovado e implementado: `IPainDetector.Inspect(TickContext)` (contrato ajustado com ok do líder), 8 detectores com disparo por borda, `PainRegistry` com ranking e tabela `pains`, `InMemorySimulation`, ferramenta `tools/Demiurge.Probe`. Medição reproduzida: `GOAL_OSCILLATION` 8/10, `NO_SOCIAL_CONTACT` 10/10, `RESOURCE_COLLAPSE` 9/10 seeds; paradoxo, loop e rejeição = 0. Criado `docs/PAINS.md`. 86 testes verdes.
- Conceito 9 (dashboard, streaming, Docker) explicado, aprovado e implementado: `Demiurge.Host` (SimulationHost com escritor único por `Channel`, `WorldView`, SignalR, API do modo deus, health), dashboard com canvas + i18n pt-BR/en, Dockerfile + compose (ADR-006). `docker compose up -d --build` verificado: health 200, ticks a ~1/s, pause/step/resume, novo mundo, restart mantém o mundo. Conceitos i18n e pain-driven development documentados. 98 testes verdes. Critérios do dia 1 cumpridos, exceto a arguição.
- Revisão da stack (ADR-003): .NET 10 LTS (11 saiu hoje, mesmo fim de suporte), `.slnx`, xUnit v3 4.0.3 em Microsoft Testing Platform v2, Central Package Management.

**Pendências**
- Líder revisar o rascunho do pitch (`VISION.md` §8).
- Próximo passo: **arguição da etapa 1** (5–10 perguntas do `INTERVIEW.md`, uma por vez). Depois, etapa 2 (LLM), já liberada pelas dores medidas (ADR-005).
- Dashboard conferido visualmente pelo líder: ok.
- Verificar `docker compose up` numa máquina limpa.
- Medições dos conceitos 3–6 vieram de um console descartável no scratchpad (exemplos ilustrativos). As do conceito 7 foram reproduzidas pelo `tools/Demiurge.Probe`.
- Economia: sem espaço, generosa demais (~800 de dinheiro por agente). Com o grafo, a mesma política simples entra em colapso: 4 de 5 mortos em 300 ticks, presos no mercado sem dinheiro e exaustos (`InsufficientMoney` 56×, `Exhausted` 73×) — só o ladrão sobrevive. É comportamento da política, não bug do engine; é o tipo de sinal que os detectores `REPETITION_LOOP`/`ACTION_REJECTION_RATE` devem capturar.
