# Decisões (ADRs)

Formato: dor/motivo → opções → decisão → consequência → como explicar numa entrevista.
Toda decisão aqui gera ao menos uma pergunta em `docs/INTERVIEW.md`.

---

## ADR-001 — Espaço como grafo de lugares, desde a etapa 1
**Data:** 2026-11-10 · **Status:** aceita

**Motivo:** a visão exige que agentes se desloquem e que deslocar custe tempo (`VISION.md` §4, P2.3), e que crimes só tenham testemunha se alguém estiver presente (P3.4). Não é uma dor medida em runtime; é requisito de modelo do mundo. Adiar significaria reescrever ações, validações e testes depois.

**Opções**
1. **Mundo abstrato** (sem espaço): todos interagem com todos. Mais simples; não há deslocamento nem "quem estava presente".
2. **Grid** (ex.: 40×30): posição (x, y), movimento célula a célula. Exige pathfinding (BFS/A*), regras de colisão e mais estado por tick; o visual é bom, mas o custo está no engine.
3. **Grafo de lugares** com tempo de viagem nas arestas: posição = lugar ou aresta em trânsito.

**Decisão:** opção 3, com grafo completo (toda dupla de lugares ligada) e 4 lugares no MVP. Sem pathfinding até o grafo deixar de ser completo.

**Consequências**
- Ações ganham requisito de lugar (`Work` no trabalho, `Buy` no mercado, `Talk`/`Steal` no mesmo lugar do alvo).
- Testemunha é uma regra simples e determinística: agentes no mesmo lugar, no mesmo tick, fora de trânsito.
- Dashboard desenha lugares como pontos fixos e interpola o pawn ao longo da aresta.
- Grid continua possível no futuro se uma dor pedir granularidade espacial maior.

**Como explicar numa entrevista:** "O requisito era que deslocamento custasse tempo e que crime só fosse visto por quem estivesse presente. Um grid resolveria, mas traria pathfinding e colisão sem nenhum ganho para a pergunta do projeto, que é social. Um grafo de lugares com tempo de viagem nas arestas dá exatamente as duas propriedades com o menor estado possível: a posição é um lugar ou uma aresta com ticks restantes."

---

## ADR-002 — Criar novo mundo pela interface, desde a etapa 1
**Data:** 2026-11-10 · **Status:** aceita

**Motivo:** critério de sucesso do líder: "conseguir resetar, criar uma nova" (`VISION.md` §7, P4.1). Sem isso, a única forma é `docker compose down -v`, que apaga tudo.

**Opções**
1. Só via `docker compose down -v` — apaga o histórico e exige terminal.
2. Reset que apaga o mundo atual no banco.
3. **Novo mundo com `WorldId`**: cada mundo tem seed e config; eventos carregam o `WorldId`; criar um novo para o atual e mantém os antigos.

**Decisão:** opção 3 (`POST /api/god/worlds { seed, agentCount }` + botão no dashboard).

**Consequências**
- Mundos antigos ficam disponíveis para replay e comparação (base para A/B com seed na etapa 6).
- O banco cresce com cada mundo — monitorado pela dor `STORE_GROWTH`.

**Como explicar numa entrevista:** "Como o engine é determinístico e todo estado vem de eventos, um mundo é só (seed, config, fluxo de eventos). Criar um mundo novo é gerar outro `WorldId`, e não apagar nada; isso me deu de graça a possibilidade de comparar duas rodadas com a mesma seed."
