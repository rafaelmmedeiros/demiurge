# Diário de sessões

Uma entrada por sessão, a mais recente no topo. Serve para a próxima sessão retomar sem perder contexto.
Formato: como retomar · feito · decisões · pendências.

---

## 2026-11-10 — Sessão 1: organização, visão e etapa 1 completa (falta a arguição)

### Como retomar (próxima sessão)
1. Ler este diário, `docs/VISION.md` e `docs/context/mentoring.md` (seção *Oral exam*).
2. Subir o sistema: `docker compose up -d --build` → http://localhost:8080 (`/health` = 200).
3. **Fazer a arguição da etapa 1**: 5–10 perguntas de `docs/INTERVIEW.md` sobre a etapa 1, **uma por vez**; avaliar cada resposta (correta / incompleta / errada), apontar o que faltou e a entrada de `docs/CONCEPTS.md` a revisar; respostas fracas viram `KNOWLEDGE_GAP` em `docs/PAINS.md`.
4. Fechada a arguição: marcar o último item de `docs/context/stage-1.md` e iniciar a **etapa 2 (LLM)**, já liberada pelas dores medidas (ADR-005). Primeiro conceito da etapa 2: LLM como política de decisão (ver `docs/context/roadmap.md`).

### Feito
- **Organização:** CLAUDE.md de 19 KB → ~4,5 KB; detalhes em `docs/context/*.md` (lidos sob demanda). Repositório https://github.com/rafaelmmedeiros/demiurge (conta pessoal, identidade git só local). `.gitignore`, `.gitattributes` (LF).
- **Visão:** entrevista em 4 rodadas (`docs/interviews/2026-11-10-visao.md`) → `docs/VISION.md`.
- **Etapa 1, conceitos 1–9** — cada um explicado, aprovado, implementado, testado e documentado (`CONCEPTS.md`, `INTERVIEW.md`):
  1. Simulação baseada em ticks · 2. Determinismo e seed (SplitMix64) · 3. Engine autoritativo · 4. Grafo de lugares e informação assimétrica · 5. Event sourcing (SQLite + Dapper, snapshots, restart) · 6. Agente e laço (`IAgentBrain`, `TickRunner`) · 7. Utility AI (`RuleBasedBrain`, fraco de propósito) · 8. Detectores de dor (`PainRegistry`, `tools/Demiurge.Probe`) · 9. Dashboard (Host, SignalR, canvas, i18n) + Docker.
  - Também documentados: i18n e pain-driven development.
- **Estado:** 98 testes verdes; `docker compose up -d --build` funcionando; dashboard conferido visualmente pelo líder; todos os critérios do dia 1 cumpridos **exceto a arguição**.
- **Medições** (reproduzíveis com `dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000`): 41/50 mortes; `GOAL_OSCILLATION` 8/10 seeds, `NO_SOCIAL_CONTACT` 10/10, `RESOURCE_COLLAPSE` 9/10; paradoxo da fome, repetição e rejeição = 0. Persistência: 7,3 eventos/tick, 0,52 ms/tick, ~1 MB/1000 ticks.

### Decisões
- Idioma: código, tooling, `CLAUDE.md` e `docs/context/` em inglês; dashboard pt-BR + en; chat e docs de estudo em pt-BR.
- Docs de contexto por tabela "leia quando", não `@import`.
- ADR-001 grafo de lugares · ADR-002 novo mundo por `WorldId` · ADR-003 stack (.NET 10 LTS, `.slnx`, xUnit v3/MTP) · ADR-004 Dapper (decisão do líder) · ADR-005 gatilho da etapa 2 pelas dores medidas + `GOAL_OSCILLATION` · ADR-006 Host (escritor único, SignalR, Ollama adiado).
- Contratos ajustados com ok do líder: `IAgentBrain` devolve `AgentAction`; `IPainDetector.Inspect(TickContext)`.

### Pendências
- **Arguição da etapa 1** (próximo passo).
- Líder revisar o rascunho do pitch (`VISION.md` §8).
- Verificar `docker compose up` numa máquina limpa.
- Etapa 2: verificar GPU AMD (RX 7900 XTX) em container antes de decidir Ollama nativo vs. container.

### Observações
- Medições dos conceitos 3–6 vieram de um console descartável (exemplos ilustrativos); as do conceito 7 foram reproduzidas pelo Probe.
- Erros corrigidos no caminho, bons para a arguição: ordem do tick invertida ao implementar; estimativa de armazenamento (7 → 3,4 MB/h); roadmap com dores supostas erradas (ADR-005); dois testes errados (agentes morriam antes do tick 48; no tick 0 todos conversam).
