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

---

## ADR-003 — Revisão da stack (baseline de 2026-11-10)
**Data:** 2026-11-10 · **Status:** aceita · **Pedido do líder:** "faça revisão das tecnologias que serão usadas".

Versões conferidas no nuget.org / npm nesta data. Só entra no projeto o que a etapa atual usa; o resto fica anotado para quando a dor/etapa chegar.

### Em uso agora
| Tecnologia | Escolha | Motivo / alternativa descartada |
|---|---|---|
| Runtime | **.NET 10 LTS**, SDK 10.0.204 fixado em `global.json` (`rollForward: latestFeature`) | .NET 11 (STS) saiu hoje, 10/11/2026. Com STS de 2 anos, 10 e 11 têm fim de suporte em nov/2028: não há ganho de suporte. 10 está instalado e maduro; 11.0.0 é dia zero. Migrar = 2 linhas. Reavaliar se um recurso do C# 15 for necessário. |
| Formato da solution | **`Demiurge.slnx`** (XML) | Padrão do SDK 10; substitui o `.sln`. Diff legível, sem GUIDs. |
| Testes | **xUnit v3, pacote 4.0.3**, sobre **Microsoft Testing Platform v2** (`global.json` → `test.runner`) | O SDK 10 não aceita mais o modo VSTest com MTP (erro de build). Descartados `Microsoft.NET.Test.Sdk` e `xunit.runner.visualstudio` (pertencem ao VSTest). |
| Versões de pacote | **Central Package Management** (`Directory.Packages.props`) | Uma versão por pacote na solution inteira. |
| Configuração comum | `Directory.Build.props`: `net10.0`, `Nullable`, `TreatWarningsAsErrors`, `InvariantGlobalization` | `InvariantGlobalization` remove dependência de cultura/ICU: formatação de números igual em qualquer máquina e imagem Docker menor. |
| PRNG | **SplitMix64 próprio** | `System.Random` sem garantia de algoritmo e sem estado exposto (ver CONCEPTS: Determinismo e seed). |

### Próximas etapas (verificado, não instalado)
| Tecnologia | Versão atual | Nota |
|---|---|---|
| ASP.NET Core (Minimal API, SignalR, static files) | parte do .NET 10 | Sem pacote extra. |
| Cliente JS SignalR `@microsoft/signalr` | 11.0.0 | Servido localmente em `wwwroot` (sem CDN), para subir offline. Conferir compatibilidade com servidor 10 ao adicionar. |
| `Microsoft.Data.Sqlite` | 11.0.0 | Conferir suporte a `net10.0` ao adicionar. Dapper (2.1.89) vs. ADO.NET puro: decidir na persistência. |
| `Microsoft.Extensions.AI` | 10.11.0 | `IChatClient`. Etapa 2. |
| Provider OpenAI-compatível `Microsoft.Extensions.AI.OpenAI` | 10.11.0 | O Ollama expõe endpoint compatível com OpenAI (`/v1`): um único provider pode cobrir Ollama e servidores compatíveis. Avaliar na etapa 2. |
| `OllamaSharp` | 5.5.2 | Implementa `IChatClient`; o antigo `Microsoft.Extensions.AI.Ollama` aparece como descontinuado. Alternativa ao provider OpenAI-compatível. |
| Provider Anthropic | — | Verificar SDK oficial e suporte a `IChatClient` na etapa 2. |
| GPU AMD no Docker | — | Docker Desktop no Windows: GPU só NVIDIA (doc oficial). AMD exige ROCm no WSL2 com Docker Engine dentro do WSL (não o Desktop). Plano: Ollama nativo no Windows; testar AMD em container na etapa 2. |
| `sqlite-vec` | — | Verificar maturidade na etapa 3. |
| Postgres, Marten, Orleans, Blazor, Unity | — | Não revisados: proibidos até a dor (regra 4). |

**Consequências:** `dotnet test` exige o `global.json` com `test.runner`; IDEs antigas sem suporte a MTP não descobrem os testes.

**Como explicar numa entrevista:** "Fixei o SDK no `global.json` e todas as versões de pacote num arquivo central, para o build ser reproduzível como o mundo. Fiquei no .NET 10 LTS mesmo com o 11 saindo no dia, porque o fim de suporte era o mesmo e migrar custa duas linhas. E o engine não tem nenhum pacote: zero dependência de IO é uma propriedade verificável no `.csproj`."

---

## ADR-004 — Persistência: SQLite + Dapper desde o início
**Data:** 2026-11-10 · **Status:** aceita · **Decisão do líder**

**Motivo:** event sourcing precisa de um event store (conceito 5). SQLite já estava na stack do dia 1 (arquivo em volume Docker, sem servidor).

**Opções de acesso a dados**
1. **ADO.NET puro** (`SqliteCommand`, `DataReader`): zero dependência extra; mais código repetitivo para parâmetros e leitura.
2. **Dapper**: micro-ORM; mapeia parâmetros de objetos anônimos e resultados para tipos; SQL continua escrito à mão.
3. **EF Core**: ORM completo com migrations e change tracking; pesado para 3 tabelas append-only e incompatível em espírito com "eventos imutáveis".

**Decisão:** opção 2. O Claude propôs ADO.NET puro até o SQL manual doer (regra 4); o líder decidiu usar Dapper de primeira. Versões: `Dapper` 2.1.89, `Microsoft.Data.Sqlite` 10.0.15 (linha 10.x, alinhada ao .NET 10 LTS do ADR-003; a 11.0.0 também suporta `net10.0`, mas é de dia zero).

**Consequências**
- SQL explícito e legível (`SqliteEventStore`), sem mapeamento mágico; Dapper só faz parâmetros e leitura.
- `seed` gravado como TEXT: `ulong` não cabe no INTEGER de 64 bits com sinal do SQLite.
- Inserção em lote é uma execução por linha dentro de uma transação (~7 linhas/tick, 0,52 ms/tick medido).

**Como explicar numa entrevista:** "O event store tem três tabelas e meia dúzia de queries, então EF Core seria excesso. Escolhi Dapper: escrevo o SQL — que num event store é simples e eu quero ver — e ele só cuida de parâmetros e mapeamento. A alternativa era ADO.NET puro; a diferença é pouco código, mas o Dapper deixa a classe do store com cara de SQL e nada mais."
