# Conceitos

Glossário vivo. Uma entrada por conceito, no formato de `docs/context/mentoring.md`. Todo conceito presente no código tem entrada aqui (senão é dor `UNDOCUMENTED_CONCEPT`).

---

## Simulação baseada em ticks
**Definição:** o tempo do mundo avança em passos discretos chamados ticks; entre dois ticks nada muda. No projeto, 1 tick = 1 hora simulada, executado a cada `TickIntervalMs` (default 1000 ms) de tempo real.

**Como funciona:**
1. Antes do tick, cada cérebro recebe `world.Perceive(id)`: uma cópia do estado ao fim do tick anterior. Todos veem o mesmo retrato.
2. `World.Step(decisions)` incrementa `Tick` e emite `TickStarted(t)`.
3. Fases em ordem fixa:
   1. **Decisões:** validar → aplicar ou rejeitar, uma a uma, em ordem embaralhada pela seed (ver *Engine autoritativo*).
   2. **A hora passa:** para cada agente vivo, em ordem de `AgentId`, `Hunger += 4` (teto 100), `Energy -= 2` (piso 0), evento `NeedsUpdated`. Fome em 100 por 12 ticks seguidos → `AgentDied(Starvation)`.
   3. **Mercado repõe** 1 unidade (teto 30) → `MarketRestocked`.
4. `Step()` devolve um `TickReport(t, eventos)`.

Ordem "ações antes do custo da hora": o agente age durante a hora e paga fome/energia no fim dela. Assim, quem decide comer com fome 96 come antes de a fome chegar a 100. (O primeiro esboço da explicação aplicava necessidades antes das decisões; mudou ao implementar o conceito 3.)

Dado real (seed 42, tick 1): `NeedsUpdated { Tick = 1, AgentId = agent-1, Hunger = 29, Energy = 96 }` — o agent-1 nasceu com fome 25 e energia 98. Sem ações, morre no tick 30: chega a 100 no tick ⌈75/4⌉ = 19 e completa 12 ticks no 30.

**Onde está no projeto:** `src/Demiurge.Engine/World.cs` (`Step`, `ApplyNeeds`, `RestockMarket`); `src/Demiurge.Engine/Domain/WorldRules.cs`; testes em `tests/Demiurge.Engine.Tests/TickTests.cs`.

**Por que usamos:** um LLM leva 0,5–5 s para decidir; com ticks, o mundo fica congelado num estado consistente enquanto todos pensam, e o replay é trivial (estado em `t` = inicial + eventos de 1..t). Requisito de modelo, não dor medida.

**Alternativas e trade-offs:**
- *Tempo real assíncrono* (um loop por agente): quem responde mais rápido age primeiro; a ordem depende da latência de rede → perde determinismo.
- *Simulação por eventos discretos* (DES, fila de prioridade com timestamps arbitrários): mais precisa no tempo, mas perde o "retrato" sincronizado que simplifica a percepção.

**Limitações conhecidas:** tudo dentro de um tick é simultâneo; "A fala e B responde na mesma hora" não é expressável — a resposta fica para `t+1`. Ticks curtos demais para o LLM geram a dor `TICK_OVERRUN`.

**Referências:** Park et al., *Generative Agents* (2023) — loop de simulação por passos; Robert Nystrom, *Game Programming Patterns*, capítulo "Game Loop".

---

## Determinismo e seed
**Definição:** mesma seed + mesma sequência de decisões = mesmo fluxo de eventos. A seed é um inteiro (default 42) que inicializa um gerador pseudoaleatório (PRNG): uma função pura estado → (número, novo estado). Toda aleatoriedade do mundo sai dele.

**Como funciona:**
1. `World.Create(config)` cria `new SplitMix64(config.Seed)`.
2. Cada chamada `NextULong()` soma a constante `0x9E3779B97F4A7C15` ao estado (8 bytes) e embaralha os bits com dois multiplicadores e três XOR-shifts.
3. `NextInt(min, max)` usa rejeição para evitar viés de módulo: descarta valores abaixo de 2^64 mod n.
4. `Shuffle` (Fisher–Yates) e `Chance(percent)` usam só `NextInt`; sem `float`.
5. `World.RngState` expõe o estado para snapshots: `new SplitMix64(state)` continua a mesma sequência.
6. Atributos iniciais dos agentes vêm do RNG; seed 42 gera sempre `agent-1: Hunger 25, Energy 98, Food 2, Money 33`.

Fontes de não-determinismo proibidas no engine: `DateTime.Now` (tempo é o tick), `Guid.NewGuid()` (IDs sequenciais `agent-N`), `string.GetHashCode()` (randomizado por processo no .NET), iteração de `HashSet`/`Dictionary` (iteramos por id ordenado), `float`/`double` em recursos (podem divergir entre plataformas).

O LLM não é determinístico (nem com temperatura 0); por isso a regra fala em "mesma sequência de **decisões**": decisões serão gravadas como eventos e o replay não chama o LLM.

**Onde está no projeto:** `src/Demiurge.Engine/Randomness/IRandom.cs`, `SplitMix64.cs`; `Domain/AgentId.cs`; testes em `tests/Demiurge.Engine.Tests/DeterminismTests.cs` (SHA-256 do fluxo de eventos de 500 ticks, seed 42 duas vezes = hash igual; seeds 42 e 43 = hashes diferentes) e `SplitMix64Tests.cs` (vetor de referência da seed 0: `0xE220A8397B1DCDAF`, ...).

**Por que usamos:** regra inviolável 2. Sem determinismo não dá para reproduzir um bug, escrever um teste com seed + cenário (ritual da seção 6.5), nem comparar mundos em A/B (etapa 6).

**Alternativas e trade-offs:**
- *`System.Random(seed)`*: a Microsoft não garante o algoritmo entre versões do .NET e ele não expõe o estado para snapshot. Descartado.
- *Seed pelo relógio*: cada rodada única; perde reprodutibilidade. Descartado.
- *xoshiro256\*\* / PCG*: qualidade estatística igual ou superior, mas estado maior (32 / 16 bytes). SplitMix64 passa no BigCrush e cabe num `ulong`; suficiente para simulação (não é criptográfico).

**Limitações conhecidas:** um único fluxo de RNG para tudo: adicionar uma chamada nova num sistema (ex.: chance de testemunha) desloca todos os números seguintes, mudando o mundo inteiro para a mesma seed. Se isso virar dor, separar fluxos por finalidade (seed derivada por subsistema). Não serve para criptografia.

**Referências:** Steele, Lea, Flood, *Fast Splittable Pseudorandom Number Generators* (OOPSLA 2014); Vigna, *prng.di.unimi.it*; documentação de `System.Random` (Microsoft Learn), seção sobre compatibilidade do algoritmo.

---

## Engine autoritativo
**Definição:** só o engine altera o estado do mundo. O cérebro de um agente (regras ou LLM) devolve uma **proposta** — uma ação de um conjunto fechado — e o engine valida: aplica e emite eventos do que mudou, ou rejeita com `ActionRejected(motivo)` sem mudar estado. Regra inviolável 1.

**Como funciona:**
1. O cérebro recebe `Perception`, um `record` imutável (fome, energia, comida, dinheiro, lugar, quem está presente, mensagens ouvidas, roubos vistos, estoque e preço do mercado, mapa, `LastRejection`). Ele nunca recebe o `Agent`, cujos setters são `internal` ao engine: sem referência a estado mutável, não há como alterá-lo.
2. O cérebro devolve `AgentDecision(AgentId, AgentAction)`. `AgentAction` é uma hierarquia fechada: `Rest`, `Eat`, `Work`, `Buy`, `MoveTo`, `Talk`, `Steal` (as três últimas e os requisitos de lugar: ver *Grafo de lugares*). Com LLM, essa hierarquia vira o JSON schema da tool call.
3. `World.Step(decisions)` ordena as decisões por `AgentId` (a ordem de chegada não pode importar) e embaralha com a seed.
4. Para cada uma: agente inexistente → `UnknownAgent`; morto → `AgentNotAlive`; mais de uma proposta do mesmo agente → todas `DuplicateDecision`; energia 0 e ação diferente de `Rest` → `Exhausted`, e `Rest` é aplicado no lugar.
5. `ActionRules.Validate` (função pura) confere a regra da ação; se passar, `ActionRules.Apply` muda o estado e devolve o evento.

| Ação | Regra (rejeição) | Efeito |
|---|---|---|
| `Rest` | sempre válida | Energy +10 |
| `Eat` | Food ≥ 1 (`NoFood`) | Food −1, Hunger −30 |
| `Work` | Energy ≥ 8 (`TooTired`) | Money +10, Energy −8 |
| `Buy` | Money ≥ 5 (`InsufficientMoney`); estoque ≥ 1 (`OutOfStock`) | Money −5 para o mercado, Food +1, estoque −1 |

6. O motivo da rejeição fica em `Agent.LastRejection` e aparece em `Perceive()` no tick seguinte; é limpo no início de cada tick.

Dado real (seed 42, tick 1): a ordem embaralhada foi agent-5, agent-4, agent-1, agent-2, agent-3 — `Worked { Tick = 1, AgentId = agent-5, Earned = 10, Money = 48, Energy = 74 }`. Exemplo de rejeição (teste): dois agentes compram com estoque 1 → um `FoodBought`, o outro `ActionRejected { Action = Buy { }, Reason = OutOfStock }`.

**Onde está no projeto:** `src/Demiurge.Engine/Actions/AgentAction.cs`, `Actions/ActionRules.cs`, `Perception.cs`, `World.cs` (`Perceive`, `ResolveDecisions`); testes em `tests/Demiurge.Engine.Tests/AuthoritativeEngineTests.cs`.

**Por que usamos:** (1) invariantes num só lugar — dinheiro nunca negativo, comida não surge do nada — mesmo com um LLM que alucina; (2) determinismo — a sorte é sorteada no engine com a seed; (3) segurança — um sussurro/prompt injection influencia a escolha, mas não quebra regras. Requisito de arquitetura (regra 1), não dor medida.

**Alternativas e trade-offs:**
- *LLM devolve o novo estado* (`"money": 50`): sem invariantes, sem replay. Descartado.
- *Tools com efeito colateral* (comum em frameworks de agentes): cada tool muta na hora; sem resolução simultânea por tick e com validação espalhada. Descartado.
- *LLM como mestre do jogo* (Concordia, DeepMind): um LLM resolve as ações em texto livre. Expressivo, mas não determinístico nem verificável. Descartado por ora; pode voltar para ações que o schema não cobre.

**Limitações conhecidas:** o agente só pode fazer o que o schema prevê — comportamento novo exige ação nova no engine (o preço da verificabilidade). A rejeição informa só o motivo, não como corrigir. Observação da primeira rodada (seed 42, 300 ticks, política de teste): zero rejeições, zero mortes e dinheiro acumulando até ~800 por agente — a economia está generosa demais; vira pressão a criar quando a dor aparecer (ex.: `RESOURCE_COLLAPSE` ou escassez do modo deus).

**Referências:** Vezhnevets et al., *Generative agent-based modeling with actions grounded in physical, social, or digital space using Concordia* (DeepMind, 2023); Yao et al., *ReAct* (2022); documentação de tool calling e structured outputs dos providers.

---

## Grafo de lugares
**Definição:** o espaço do mundo é um grafo: nós são lugares (`Place`: id, tipo, coordenadas só para desenho) e arestas não direcionadas têm um custo em ticks de viagem. A posição de um agente é `AtPlace(lugar)` ou `InTransit(origem, destino, ticksRestantes)`. Decisão registrada no ADR-001.

**Como funciona:**
1. Mapa padrão (`PlacesGraph.Default`): `home`, `workplace`, `market`, `square`. Casa–Trabalho e Mercado–Trabalho = 2 ticks; demais pares = 1. O construtor exige grafo **completo** (todo par com aresta ≥ 1 tick), então não há pathfinding.
2. Todos nascem em `home` (`WorldConfig.StartPlace`).
3. `MoveTo(destino)`: valida (`UnknownPlace`, `AlreadyThere`), muda para `InTransit(aqui, destino, custo)` e emite `TravelStarted`. Na fase "a hora passa", `AdvanceTravel` decrementa: chegou a 0 → `AtPlace(destino)` + `Arrived`; senão `TravelProgressed`. Custo 1 = chega no fim do mesmo tick; custo 2 = passa um tick inteiro na estrada.
4. Em trânsito: proposta rejeitada com `InTransit`, não pode ser alvo, não testemunha; fome e energia continuam correndo.
5. Requisitos de lugar: `Work` só em `workplace`, `Buy` só em `market` (`NotAtRequiredPlace`); `Rest` dá +10 em casa e +5 fora; `Eat` em qualquer lugar.
6. `Talk(alvo, texto)`: alvo vivo no mesmo lugar (`TargetNotPresent`), não pode ser si mesmo (`InvalidTarget`), texto não vazio e ≤ 200 caracteres. O texto entra em `MessagesHeard` do ouvinte, visível no tick seguinte.
7. `Steal(alvo)`: mesmo lugar, alvo com comida (`NothingToSteal`); move 1 comida. O evento `Stole` é sempre emitido. Depois, cada agente presente — vítima inclusive, ladrão excluído — sorteia `Chance(50%)` em ordem de id; quem acerta recebe `TheftWitnessed` e o roubo entra em `TheftsWitnessed` da sua percepção.
8. **Informação assimétrica:** o log de eventos é a visão de deus (todo `Stole`); a percepção é a visão do agente (só o que viu). A vítima que não viu percebe a comida a menos, mas não sabe quem levou.

Dado real (seed 42, 300 ticks, política de teste em que o agent-3 rouba quando está sem comida e há alguém por perto):
```
TravelStarted { Tick = 1, AgentId = agent-5, From = home, To = workplace, Ticks = 2 }
TravelProgressed { Tick = 1, AgentId = agent-1, From = home, To = workplace, TicksRemaining = 1 }
Arrived { Tick = 2, AgentId = agent-1, Place = workplace }
Stole { Tick = 40, Thief = agent-3, Victim = agent-4, Item = Food }      ← ninguém viu este
```
14 roubos, 13 testemunhos ao todo; 4 mortes por fome; só o ladrão sobreviveu.

**Onde está no projeto:** `src/Demiurge.Engine/Domain/Places.cs` (`PlaceId`, `Place`, `Edge`, `PlacesGraph`, `Location`), `Actions/ActionRules.cs` (requisitos, `ApplySteal`), `World.cs` (`AdvanceTravel`, `PresentAt`, `IsPresentWith`, `Perceive`), `Domain/Agent.cs` (`MessagesHeard`, `TheftsWitnessed`); testes em `tests/Demiurge.Engine.Tests/PlacesGraphTests.cs`.

**Por que usamos:** requisitos da visão — deslocamento custa tempo (P2.3) e crime só é punido se alguém vê (P3.4). Ver ADR-001.

**Alternativas e trade-offs:**
- *Grid* (ex.: 40×30): pathfinding (BFS/A*), colisão, mais estado por tick; nenhum ganho para a pergunta social. Descartado.
- *Mundo abstrato*: sem deslocamento e sem "quem estava presente". Descartado.
- *Grafo não completo + Dijkstra*: mais realista (rotas passando por lugares), mas pathfinding sem necessidade com 4 lugares. Adiado até doer.

**Limitações conhecidas:** agentes na mesma estrada não se encontram; tempo de viagem igual para todos; grafo completo cresce em n(n−1)/2 arestas (4 lugares = 6; 20 = 190); resolução sequencial dentro do tick faz o resultado depender da ordem sorteada (se o alvo sai antes do ladrão agir, `TargetNotPresent`).

**Referências:** Park et al., *Generative Agents* (2023) — mundo Smallville como árvore de áreas; Cormen et al., *Introduction to Algorithms*, cap. 22 (representação de grafos).

---

## Event sourcing
**Definição:** a fonte da verdade é a lista append-only de eventos (só INSERT, nunca UPDATE/DELETE). O estado do mundo é derivado aplicando os eventos em ordem: `estado(t) = fold(Apply, vazio, eventos[0..t])`. Snapshots são cache, não verdade.

**Como funciona:**
1. **Estado só muda por evento.** Toda regra do engine calcula eventos (`ActionRules.Resolve`, `ApplyNeeds`, `AdvanceTravel`...) e chama `Emit(e)`, que faz `Apply(e)` e guarda `e`. `Apply` é o único lugar com atribuições de estado. O caminho ao vivo e o de reconstrução executam o mesmo `Apply`.
2. **Eventos carregam o resultado.** `Worked { Money = 48, Energy = 74 }`, `NeedsUpdated { ..., TicksStarving }`, `Stole { ThiefFood, VictimFood }`, `FoodBought { ..., MarketMoney }`. `Apply` só atribui — nenhuma regra roda na reconstrução, então o passado não muda se as regras mudarem.
3. **RNG também é estado.** Cada tick termina com `TickEnded { Tick, RngState }` (o gênesis também). Reconstruído o mundo, a sequência aleatória continua do mesmo ponto: o futuro é idêntico, não só o presente.
4. **Gravação (harness):** `SqliteEventStore` com Dapper. Tabelas `worlds` (seed, config JSON), `events` (world_id, seq, tick, type, payload JSON) e `snapshots` (world_id, tick, estado JSON). Cada tick = 1 transação. Payload com discriminador: `{"$type":"FoodBought","agentId":3,"price":5,"money":275,"food":1,"marketFoodStock":29,"marketMoney":35,"tick":40}`. O formato JSON fica no harness (`EventJson`); o engine não conhece formato de arquivo.
5. **Snapshot a cada 100 ticks** (`SimulationSession`). `Resume` = último snapshot + eventos com `tick > snapshot.Tick`; sem snapshot, replay do gênesis.

Números reais (seed 42, 5 agentes, 1000 ticks): 7.334 eventos (7,3/tick), payload médio 72 bytes, arquivo de 944 KB com 10 snapshots; escrita 0,52 ms/tick (0,05% do intervalo de 1000 ms); `Resume` pelo snapshot 15 ms; replay completo do gênesis 31 ms. A 1 tick/s isso dá ~3,4 MB/hora (a estimativa inicial de ~7 MB/h superestimou tamanho e quantidade de eventos).

**Onde está no projeto:** `src/Demiurge.Engine/World.cs` (`Emit`, `Apply`, `FromEvents`, `FromSnapshot`, `ToSnapshot`), `WorldSnapshot.cs`, `Events/WorldEvent.cs`; `src/Demiurge.Harness/Persistence/SqliteEventStore.cs`, `EventJson.cs`, `SimulationSession.cs`. Testes: `tests/Demiurge.Engine.Tests/EventSourcingTests.cs` (cobertura de todos os tipos de evento; reconstrução do gênesis e do snapshot com `Assert.Equivalent` estrito; mundo reconstruído continua igual por 250 ticks) e `tests/Demiurge.Harness.Tests/` (round-trip JSON de cada evento, store, e o teste de restart: parar no tick 250, retomar do banco, rodar mais 250 e comparar com 500 ticks ininterruptos).

**Por que usamos:** regra inviolável 3. Histórico completo ("o que aconteceu no tick 40?"), mundo que sobrevive a restart, replay e comparação A/B de mundos, auditoria (o log é a visão de deus; base para medir dores e, mais tarde, avaliar decisões).

**Alternativas e trade-offs:**
- *Persistir só o estado atual* (UPDATE por tick): simples, mas sem história nem replay. Descartado.
- *Command sourcing* (gravar seed + decisões e reexecutar o engine): muito mais compacto, mas o mundo reconstruído muda quando as regras mudam no código. Mantemos as decisões reproduzíveis pelo determinismo, mas a verdade é o evento.
- *Mutação direta + evento como "log"* (como era até o conceito 4): dois caminhos de escrita que podem divergir; um estado mudado sem evento só seria pego por teste. Trocado por "só `Apply` muda estado".
- *Marten / EventStoreDB*: projeções, versionamento e concorrência prontos, mas exigem Postgres ou servidor dedicado. Proibidos até a dor (etapa 7).

**Limitações conhecidas:** evolução de schema — renomear um evento ou campo quebra a leitura de eventos antigos (exigirá *upcasting* ou versão no `$type`); o log cresce sem limite (dor `STORE_GROWTH`); `Append` lê `MAX(seq)` a cada tick, aceitável com um único escritor, errado com escritores concorrentes; snapshot e eventos não são gravados na mesma transação (se cair entre os dois, retoma-se do snapshot anterior — correto, só mais lento).

**Referências:** Martin Fowler, *Event Sourcing* (martinfowler.com, 2005); Greg Young, *CQRS Documents* (2010); Microsoft Learn, *Event Sourcing pattern* (Azure Architecture Center).

---

## Agente e o laço percepção → decisão → ação
**Definição:** um agente percebe o ambiente por sensores e age sobre ele por atuadores; a função do agente mapeia percepções em ações (Russell & Norvig). A diferença para um chatbot é o **laço**: o agente age, o ambiente responde com consequências, e a consequência volta como nova percepção — sem um humano iniciando cada passo.

**Como funciona:**

| Conceito | No DEMIURGE |
|---|---|
| Ambiente | `World` (engine) |
| Sensor | `World.Perceive(id)` → `Perception` (cópia imutável, só o que o agente pode saber) |
| Programa do agente | `IAgentBrain.DecideAsync(Perception, CancellationToken)` → `AgentAction` |
| Atuador | `World.Step(decisions)`: valida e aplica ou rejeita |
| Consequência | novo estado + `LastRejection`, mensagens ouvidas, roubos vistos na próxima percepção |

Um tick no harness (`TickRunner.RunTickAsync`):
1. Seleciona quem pode agir: vivo e `AtPlace` (em trânsito não decide).
2. Tira a percepção de cada um (todos do mesmo estado).
3. Chama todos os cérebros **em paralelo** (`Task.WhenAll`), com um prazo (`CancellationTokenSource(timeout, TimeProvider)`).
4. `DecideSafelyAsync` isola falhas: exceção → `BrainFailure(Exception)`; prazo estourado → `Timeout` (com `WaitAsync`, o tick para de esperar mesmo se o cérebro ignorar o token); `null` → `NullAction`. O agente com falha só fica parado naquele tick.
5. Monta `AgentDecision(perception.Self, ação)` — o cérebro devolve só a ação, então não consegue agir em nome de outro agente.
6. `SimulationSession.Step(decisions)` aplica e persiste; devolve `TickOutcome(Report, Failures)`.

Cada agente tem sua instância de cérebro, criada uma vez por uma fábrica `Func<AgentId, IAgentBrain>` — permite misturar cérebros (regras, LLM, personalidades) no mesmo mundo.

Dado real (teste): com o cérebro do agent-2 lançando `InvalidOperationException("brain exploded")`, o tick fecha com `TickEnded`, os outros 4 agem e o resultado traz `BrainFailure { AgentId = agent-2, Kind = Exception, Detail = "InvalidOperationException: brain exploded" }`. Com um cérebro que nunca responde e prazo de 100 ms, o tick fecha em ~100 ms com `Kind = Timeout`.

**Onde está no projeto:** `src/Demiurge.Agents/IAgentBrain.cs`; `src/Demiurge.Harness/TickRunner.cs` (`RunTickAsync`, `DecideSafelyAsync`, `TickOutcome`, `BrainFailure`). Testes: `tests/Demiurge.Harness.Tests/TickRunnerTests.cs` (equivalência com chamar o engine direto; isolamento de exceção, timeout e null; agentes em trânsito não são consultados; paralelismo provado com uma barreira de 5 cérebros que só passa se todos começarem juntos; uma instância de cérebro por agente).

**Por que usamos:** é o laço que transforma "um modelo que responde" em "um agente que vive". Separa o que muda entre etapas (o cérebro: regras → LLM → memória → planejamento) do que não muda (ambiente e laço). Regra 6: uma falha não para a simulação.

**Alternativas e trade-offs:**
- *Decisão síncrona* (`AgentAction Decide(...)`): bloqueia uma thread por agente durante a espera do LLM e não pode ser cancelada no meio. Descartado.
- *Cérebro recebe o `World`*: quebra o engine autoritativo e a informação assimétrica. Descartado.
- *Cérebro devolve `AgentDecision`* (como estava no CLAUDE.md original): permitiria um cérebro propor ação para outro agente; o harness teria de verificar. Trocado por `AgentAction`, garantia por tipo.
- *Um loop/ator por agente, em tempo real*: perde determinismo (ver *Simulação baseada em ticks*).
- *Microsoft Agent Framework / Semantic Kernel*: orquestram agentes de LLM com tools que mutam estado — conflita com o engine autoritativo. Só com dor.

**Limitações conhecidas:** o prazo é tempo real, então numa execução ao vivo um cérebro lento muda quais decisões chegam ao engine — o resultado continua reproduzível pelos eventos gravados, mas não por "seed + cérebros". Um cérebro que ignora o token continua rodando em segundo plano depois do prazo (o tick só para de esperar). `BrainFailure` ainda não é persistido; vira dor (`TICK_EXCEPTION`, `TICK_OVERRUN`) com os detectores. Nenhuma mitigação ainda: o agente com falha simplesmente não age.

**Referências:** Russell & Norvig, *Artificial Intelligence: A Modern Approach*, cap. 2 (Intelligent Agents); Microsoft Learn, *Task cancellation* e `Task.WaitAsync`.

---

## Agente baseado em regras e utility AI
**Definição:** cérebros sem aprendizado, escritos à mão. *Lista de regras* (decision list): "se X então Y" em ordem fixa, a primeira que casa vence. *Utility AI*: toda opção recebe uma nota calculada da percepção e vence a maior. O `RuleBasedBrain` é utility AI.

**Como funciona:**
1. `RuleBasedBrain.Score(perception)` dá nota (inteiro 0–100) a cada objetivo disponível, sempre nesta ordem:

| Objetivo | Disponível se | Nota |
|---|---|---|
| `Eat` | Food ≥ 1 | `Hunger` |
| `GetFood` | Food = 0 e Money ≥ preço | `Hunger − 10` (custo de viagem) |
| `Work` | Energy ≥ 8 | `70 − Money` (piso 0) |
| `Rest` | sempre | `100 − Energy` |

2. Vence a maior nota; empate fica com o primeiro da tabela (`MaxBy` mantém o primeiro) — determinístico.
3. Objetivo → ação: `Eat` em qualquer lugar; os outros fazem a ação se o agente já está no lugar (`Buy` no mercado, `Work` no trabalho, `Rest` em casa), senão `MoveTo` até lá.
4. Fraco de propósito: sem memória, ignora `LastRejection` e tempo de viagem, nunca conversa, nunca rouba.

Exemplo (teste): no Trabalho, Hunger 72, Energy 40, Food 0, Money 12 → `GetFood 62`, `Work 58`, `Rest 60` → `MoveTo(market)`.

**Medição real** (1000 ticks, seeds 1–10, 5 agentes). Primeira versão feita com script fora do repositório; mortes e dores reproduzidas pelos detectores com `dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000 --agents 5` (ver *Harness, observabilidade e detectores de dor*). As linhas de troca de objetivo e trabalho por viagem são métricas internas do script original, não reproduzidas pela ferramenta:

| Sinal | Resultado |
|---|---|
| Mortes | 2 a 5 por seed (41 de 50 agentes) |
| Morte com dinheiro e comida disponível (`STARVATION_PARADOX`) | 0 — morrem sem dinheiro (0–3, preço 5) |
| Ações rejeitadas (`ACTION_REJECTION_RATE`) | 0% — a utility só pontua objetivos disponíveis |
| Mesma ação seguida (`REPETITION_LOOP`) | no máximo 3 ticks |
| `Talk` (`NO_SOCIAL_CONTACT`) | 0 |
| Troca de objetivo entre decisões consecutivas | **55%** |
| `Work` por viagem iniciada | **0,13** (675 trabalhos, 5.090 viagens) |

Sequência real do agent-1 (seed 1, ticks 100–113): `MoveTo Rest MoveTo Buy Eat MoveTo Rest MoveTo Work MoveTo MoveTo Buy Eat MoveTo`.

O modo de falha é **oscilação de objetivo** (*dithering*): cada `Work` derruba a nota de trabalhar em 10 e sobe a de descansar em 10, então o agente troca de objetivo após 1–2 ações e passa o tempo viajando. Morre a caminho do trabalho, sem dinheiro. A causa é estrutural da utility AI sem compromisso: a decisão é refeita do zero a cada tick.

**Onde está no projeto:** `src/Demiurge.Agents/RuleBasedBrain.cs` (`Decide`, `Score`, `Goal`, `GoalScore`); testes em `tests/Demiurge.Agents.Tests/RuleBasedBrainTests.cs`.

**Por que usamos:** linha de base barata (microssegundos, sem custo de LLM) e explicável (as notas dizem por que cada decisão foi tomada). Gera as dores que justificam a etapa 2. Contraste medido com a lista de regras: a `ScriptedPolicy` de teste (lista de regras) ficava presa no mercado com `InsufficientMoney` repetido; a utility nunca propõe ação impossível.

**Alternativas e trade-offs:**
- *Lista de regras*: mais simples; prioridade fixa esconde necessidades (vimos agentes presos no mercado).
- *FSM (máquina de estados)*: boa para estados visíveis; transições explodem com o número de necessidades.
- *Behavior tree*: padrão em jogos, hierárquica; estrutura demais para 4 objetivos.
- *GOAP* (planejamento A* sobre ações): resolveria a oscilação planejando a sequência, mas é planejamento — etapa 4, e entraria antes da dor.
- *Histerese/compromisso* (bônus para manter o objetivo atual): conserto clássico da oscilação. Não aplicado: o cérebro é fraco de propósito, e a oscilação é a dor que queremos medir.

**Limitações conhecidas:** curvas lineares e fixas; sem horizonte (não considera o custo da viagem nem o que vem depois); oscila sem histerese; mesmos pesos para todos (personalidades viriam de pesos por agente); conhece só 4 objetivos — nada social.

**Referências:** Dave Mark, *Behavioral Mathematics for Game AI* (2009); Kevin Dill, "Introducing GAIA" / Infinite Axis Utility System (GDC 2010–2015); Jeff Orkin, *Three States and a Plan: The A.I. of F.E.A.R.* (GDC 2006) para GOAP; Damian Isla, *Handling Complexity in the Halo 2 AI* (GDC 2005) para behavior trees.

---

## Harness, observabilidade e detectores de dor
**Definição:** *harness* é tudo em volta do engine que mantém a simulação viva e observável (laço, persistência, medição, mitigação). *Observabilidade* é responder "o que está acontecendo e por quê" só com os dados que o sistema emite. Uma *dor* é um sinal medido sobre esses dados, nunca uma opinião.

**Como funciona:**
1. Ao fim de cada tick, o `TickRunner` monta um `TickContext`: eventos, decisões enviadas, falhas de cérebro, duração (relógio de parede via `TimeProvider`), intervalo do tick, snapshot depois do tick e config. O contrato ficou `IPainDetector.Inspect(TickContext)` (o original recebia só `TickReport`, que não tem falhas nem duração).
2. Cada detector guarda a própria janela de histórico e olha só o observável — nunca o "objetivo" interno de um cérebro —, então mede regras e LLM do mesmo jeito.
3. **Disparo por borda** (`EdgeTrigger`): o sinal sai quando a condição passa de falsa para verdadeira e se rearma quando volta a falsa. `Count` = episódios. Sem isso, `NO_SOCIAL_CONTACT` geraria um sinal por agente por tick.
4. `PainRegistry` agrega por código: `Count`, `FirstTick`, `LastTick`, maior severidade, última amostra, mitigação, `Status` (técnica com mitigação → `Mitigated`; comportamental → `Open`; `Resolved` que volta → `Open`). Ranking por `Severity × Count`. Com store, cada agregado alterado vai para a tabela `pains` (upsert — é agregado, não evento).
5. Um detector que lança exceção é pulado e vira `TICK_EXCEPTION`; nunca derruba o tick.

| Dor | Sinal | Sev. |
|---|---|---|
| `STARVATION_PARADOX` | morreu de fome com Money ≥ preço e estoque ≥ 1 | 5 |
| `RESOURCE_COLLAPSE` | ≥ 50% da população inicial morta (5) / Gini do dinheiro dos vivos > 0,6 (3) | 5/3 |
| `GOAL_OSCILLATION` | ≥ 5 chegadas resolvidas nos últimos 50 ticks e > 40% "chegou e partiu sem agir" | 3 |
| `REPETITION_LOOP` | mesma ação proposta 8 ticks seguidos sem nenhum recurso melhorar tick a tick | 3 |
| `ACTION_REJECTION_RATE` | > 20% das últimas 50 decisões rejeitadas (mín. 10) | 2 |
| `NO_SOCIAL_CONTACT` | 48 ticks sem falar nem ouvir, com outro agente vivo | 2 |
| `TICK_EXCEPTION` | cérebro lançou exceção ou devolveu null → `Mitigated` | 4 |
| `TICK_OVERRUN` | tick > intervalo, ou cérebro estourou o prazo → `Mitigated` | 3 |

`GOAL_OSCILLATION` é a versão observável de "trocou de objetivo antes de concluir": uma chegada é desperdiçada quando o próximo evento do agente é `TravelStarted` saindo dali, sem `Worked`/`FoodBought`/`Rested`/`Ate`/`Talked`/`Stole` antes.

**Medição reproduzível** — `dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000 --agents 5` (4 s, mesmo caminho do harness, sem SQLite):
```
seed | deaths | talks | pains (code×episodes)
   1 |      3 |     0 | RESOURCE_COLLAPSE×4, NO_SOCIAL_CONTACT×5
   2 |      5 |     0 | GOAL_OSCILLATION×22, NO_SOCIAL_CONTACT×5, RESOURCE_COLLAPSE×1
   ...
code                   | seeds | episodes | earliest tick | severity
GOAL_OSCILLATION       |  8/10 |      126 |            16 |        3
NO_SOCIAL_CONTACT      | 10/10 |       50 |            48 |        2
RESOURCE_COLLAPSE      |  9/10 |       17 |           114 |        5
```
`STARVATION_PARADOX`, `REPETITION_LOOP` e `ACTION_REJECTION_RATE`: zero em todas as seeds — confirma o ADR-005. As três dores do novo gatilho da etapa 2 estão medidas.

**Onde está no projeto:** `src/Demiurge.Harness/Pains/` (`PainModel.cs`: `TickContext`, `IPainDetector`, `PainSignal`, `Pain`, `EdgeTrigger`; `TechnicalDetectors.cs`; `BehaviorDetectors.cs`; `PainRegistry.cs`), `TickRunner.cs` (`Detect`), `ISimulation.cs` (`InMemorySimulation`), `Persistence/SqliteEventStore.cs` (`SavePains`, `LoadPains`); `tools/Demiurge.Probe/Program.cs`. Testes: `tests/Demiurge.Harness.Tests/PainDetectorTests.cs` (cada detector com cenário que dispara e, quando útil, um que não dispara; agregação e ranking; persistência; mesma seed = mesmas dores).

**Por que usamos:** seção 6 do CLAUDE.md — o harness precisa sentir o que está ruim, mitigar em runtime e expor o resto para a próxima sessão. Sem medição, a escolha da próxima etapa seria opinião (o ADR-005 nasceu justamente de uma suposição errada).

**Alternativas e trade-offs:**
- *Só logs*: texto; exigiria parsing para virar sinal, sem agregação nem ranking.
- *OpenTelemetry (métricas/traces) agora*: padrão da indústria, mas é a etapa 8; sem dor que justifique.
- *Disparo por nível*: um sinal por tick enquanto a condição dura — afoga as dores raras.
- *Detectores lendo o estado interno do cérebro* (ex.: o objetivo escolhido): mais preciso para regras, impossível para LLM; quebraria a comparação entre cérebros.

**Limitações conhecidas:** limites são chutes iniciais; o detector usa o snapshot do fim do tick (aproximação para "estado no momento da morte"); a janela dos detectores fica em memória e se perde num restart (os agregados persistem); rodam dentro do tick e somam à sua duração; `RESOURCE_COLLAPSE` por Gini pode oscilar perto do limite e contar vários episódios.

**Referências:** Charity Majors, Liz Fong-Jones, George Miranda, *Observability Engineering* (O'Reilly, 2022); Google SRE Book, cap. 6 *Monitoring Distributed Systems*; Prometheus docs, *Alerting rules* (`for:` e transições de estado).

---

## Dashboard e streaming de estado
**Definição:** separação simulação/apresentação. A simulação roda num laço próprio no servidor e é a única que escreve no mundo; a apresentação recebe o estado empurrado (*push*) a cada tick, desenha, e envia comandos do modo deus por HTTP. A interface nunca altera o mundo.

**Como funciona:**
1. `SimulationHost : BackgroundService` é dono do mundo atual. Ao subir, retoma o último mundo do SQLite (`LatestWorldId` → `SimulationSession.Resume`) ou cria um com a config. Laço: tick (`TickRunner`) → publica view → espera `intervalo − duração` → repete. Se o tick estourou o intervalo, o próximo começa na hora (mitigação de `TICK_OVERRUN`).
2. **Escritor único:** `POST /api/god/pause|resume|step|worlds` não tocam no mundo; colocam um `GodCommand` num `Channel` e aguardam o `TaskCompletionSource` do comando. Só o laço lê o canal e executa. Sem locks, sem corrida entre "step" e um tick em andamento.
3. **Leitura sem trava:** ao fim de cada tick o laço publica um `WorldView` imutável (tick, pausado, lugares, arestas, agentes com lugar/viagem, mercado, dores ranqueadas) num campo `volatile`; `GET /api/world` e `/api/pains` só leem a referência. `GET /api/events?sinceTick=N&limit=500` lê do SQLite.
4. **Push:** hub SignalR `/hubs/world`, mensagem `tick` = `{ world: WorldView, events: [...] }` (eventos no mesmo JSON do store, com `$type`). ~2 KB/tick com 5 agentes.
5. **Dashboard** (`wwwroot`, HTML/JS/CSS puros, cliente SignalR 10.0.11 versionado em `lib/`, sem CDN): canvas com lugares nos `(x, y)` do grafo; pawn em trânsito em `lerp(origem, destino, progresso)`, com `progresso = (viagem − restantes) / viagem`; `requestAnimationFrame` interpola da posição anterior à atual ao longo de um intervalo de tick. Painéis: tick e hora simulada (dia = tick/24 + 1), pausar/retomar/avançar, novo mundo (seed + agentes), tabela de agentes, feed (esconde eventos ruidosos por padrão), dores. Texto vindo de agentes (`Talked`) entra por `textContent`, nunca `innerHTML` (com LLM, um agente poderia "falar" HTML).
6. `/health` (health check do ASP.NET Core): `Healthy` depois que o laço publicou a primeira view.
7. **Docker:** Dockerfile multi-stage (`sdk:10.0` restaura e publica; `aspnet:10.0` roda como usuário não-root `app`), restore em camada própria; `docker-compose.yml` com um serviço, porta 8080, volume `demiurge-data:/data`, `.env` opcional.

Dados reais (container, `docker compose up -d --build`): `/health` → `200 Healthy`; tick 8 → 12 em 3 s; `pause` → `(24, True)`, `step` → `(25, True)`, `resume` → `(25, False)`; `docker compose restart` → mesmo `worldId`, tick continua; `worlds {seed 7, agentCount 3}` → `worldId 2`, tick 0, 3 agentes.

**Onde está no projeto:** `src/Demiurge.Host/` (`Program.cs`, `DemiurgeOptions.cs`, `Simulation/SimulationHost.cs`, `Simulation/WorldView.cs`, `wwwroot/`), `Dockerfile`, `docker-compose.yml`, `.env.example`. Testes: `tests/Demiurge.Host.Tests/HostTests.cs` (`WebApplicationFactory`: health, gênese pausada, step + eventos, pause/resume, novo mundo, 400, 501, dores, restart do processo, dashboard servido, push do hub via cliente SignalR, i18n).

**Por que usamos:** critério do dia 1 ("abrir o navegador e ver a simulação") e regra 5 (sobe com um comando). A separação permite trocar a apresentação (Blazor, Unity — etapa 9) sem tocar na simulação.

**Alternativas e trade-offs:**
- *Polling* de `/api/world` a cada segundo: mais latência e requisições; descartado.
- *SSE (Server-Sent Events)*: mais simples, nativo do browser, só servidor→cliente — suficiente hoje. SignalR escolhido por estar na stack e permitir mensagens nos dois sentidos depois.
- *`lock` em volta do mundo*: ficaria preso durante a espera de um LLM; o canal de escritor único é a semente do modelo de atores (etapa 7).
- *Framework de front (React/Blazor)*: proibido até a dor (etapa 9).
- *Ollama no compose agora*: adiado para a etapa 2 (sem código que use LLM; GPU AMD a verificar) — ADR-006.

**Limitações conhecidas:** a view vai inteira a cada tick (não é delta) — bom para 5–25 agentes, não para milhares; o `/api/events` lê do banco a cada chamada; um mundo existente no banco tem prioridade sobre a seed da config (para trocar, botão "Novo mundo" ou `docker compose down -v`); os textos de amostra das dores vêm em inglês do harness; o visual do dashboard não tem teste automatizado (só a API, o hub e as chaves de i18n).

**Referências:** Microsoft Learn — *Background tasks with hosted services*, *ASP.NET Core SignalR*, *System.Threading.Channels*; Docker docs — *Multi-stage builds*; MDN — `requestAnimationFrame`, *Server-sent events*.

---

## Internacionalização (i18n)
**Definição:** separar todo texto visível da interface do código, em dicionários por idioma, e escolher o dicionário em tempo de execução. No DEMIURGE, a interface é bilíngue (pt-BR e en); dados, API, eventos e códigos continuam em inglês.

**Como funciona:**
1. `wwwroot/i18n/pt-BR.json` e `en.json`: mapa chave → texto. Famílias de chave: `ui.*`, `event.<Tipo>`, `pain.<CÓDIGO>`, `place.<id>`, `action.<Tipo>`, `reason.<Motivo>`, `cause.*`, `item.*`, `status.*`, `category.*`.
2. Eventos usam modelos com marcadores: `"event.Worked": "{agentId} trabalhou e ganhou {earned} (total {money})"`. `describeEvent` traduz os campos antes (ids → "Agente 1", lugares → "Mercado", motivos → "dinheiro insuficiente") e preenche o modelo.
3. Idioma inicial: escolha salva (`localStorage`) → idioma do navegador (`pt*` → pt-BR) → `en`. Troca pelo seletor, sem recarregar.
4. Chave ausente cai para o identificador cru (`event.Foo` → `Foo`), nunca quebra a tela.
5. Teste `Both_dictionaries_have_the_same_keys_and_cover_every_domain_identifier`: mesmas chaves nos dois arquivos e uma chave para cada tipo de evento, ação, motivo de rejeição, causa, item, status, categoria, código de dor e lugar — obtidos por reflexão. Um evento novo sem tradução quebra o build de testes.

**Onde está no projeto:** `src/Demiurge.Host/wwwroot/i18n/`, `wwwroot/app.js` (`i18n`, `t`, `describeEvent`), `tests/Demiurge.Host.Tests/HostTests.cs`.

**Por que usamos:** requisito do líder (dashboard pt-BR e en; código em inglês) e portfólio público.

**Alternativas e trade-offs:** *i18next* ou similar — pluralização, formatação de datas, interpolação rica; dependência e CDN para ~30 linhas de necessidade. *Textos no HTML* — impossível alternar idioma sem duplicar a página. *Traduzir no servidor* (API devolve texto pronto) — acopla API a idioma e quebra a regra de API em inglês.

**Limitações conhecidas:** sem pluralização ("1 episódio(s)"); números e horas não formatados por cultura; amostras das dores vêm do harness em inglês.

**Referências:** W3C, *Internationalization techniques: Authoring HTML & CSS*; MDN, `Intl` e `navigator.language`.

---

## Pain-driven development
**Definição:** método do projeto: nenhuma tecnologia, camada ou etapa entra por antecipação; entra quando uma dor **medida** a justifica. O harness sente (detectores), mitiga o que dá em runtime e expõe o resto; cada sessão de desenvolvimento ataca a dor de maior severidade × frequência.

**Como funciona:**
1. *Laço de runtime (segundos):* mitigações automáticas (falha de cérebro → agente parado no tick; tick atrasado → próximo começa na hora). Toda mitigação vira dor `Mitigated` — recorrência indica problema estrutural.
2. *Laço de desenvolvimento (por sessão):* subir o sistema, ler `/api/pains` e `docs/PAINS.md`, escolher a dor, explicar, escrever teste que falha, corrigir a causa com a menor mudança, verificar que a dor some com a mesma seed, documentar (`docs/context/pain-system.md`).
3. *Roadmap guiado por dor:* cada etapa tem uma dor que a libera; sem dor medida, cria-se pressão (mais agentes, escassez, evento do modo deus).
4. *Evidência reproduzível:* toda dor citada tem um comando que a reproduz (`tools/Demiurge.Probe`).

Exemplo real: o roadmap supunha que o cérebro de regras geraria `STARVATION_PARADOX` e `REPETITION_LOOP`. A medição deu zero nas duas e mostrou oscilação de objetivo, que nem tinha código. Resultado: ADR-005 — nova dor `GOAL_OSCILLATION` e gatilho da etapa 2 trocado pelas dores medidas. Exemplo de recusa por falta de dor: Ollama no compose adiado (ADR-006).

**Onde está no projeto:** CLAUDE.md regra 4; `docs/context/pain-system.md`, `docs/context/roadmap.md`, `docs/PAINS.md`, `docs/DECISIONS.md`; `src/Demiurge.Harness/Pains/`; `tools/Demiurge.Probe/`.

**Por que usamos:** projeto didático com tempo limitado (6–12 h/semana): cada peça precisa ter um motivo explicável numa entrevista. Evita arquitetura especulativa (Postgres, Orleans, Blazor antes da hora).

**Alternativas e trade-offs:** *roadmap fixo por funcionalidade* — previsível, mas constrói o que talvez não seja necessário e não aprende com o sistema; *YAGNI sem medição* — mesmo espírito, mas sem critério objetivo de quando algo passa a ser necessário. Custo do método: às vezes adia algo que se sabe que virá (o líder antecipou Dapper por decisão própria — ADR-004 — e isso também fica registrado).

**Limitações conhecidas:** só mede o que tem detector — uma falha sem detector é invisível; limites de detecção são arbitrários até calibrados; dores de conhecimento dependem da arguição.

**Referências:** Kent Beck, *Extreme Programming Explained* (YAGNI); Google SRE Book, cap. 6 (*symptoms vs. causes*); Eric Ries, *The Lean Startup* (medir → aprender).
