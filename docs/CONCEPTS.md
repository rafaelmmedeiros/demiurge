# Conceitos

Glossário vivo. Uma entrada por conceito, no formato de `docs/context/mentoring.md`. Todo conceito presente no código tem entrada aqui (senão é dor `UNDOCUMENTED_CONCEPT`).

---

## Simulação baseada em ticks
**Definição:** o tempo do mundo avança em passos discretos chamados ticks; entre dois ticks nada muda. No projeto, 1 tick = 1 hora simulada, executado a cada `TickIntervalMs` (default 1000 ms) de tempo real.

**Como funciona:**
1. `World.Step()` incrementa `Tick` e emite `TickStarted(t)`.
2. Fases em ordem fixa. Hoje só existe a fase de necessidades: para cada agente vivo, em ordem de `AgentId`, `Hunger += 4` (teto 100), `Energy -= 2` (piso 0), evento `NeedsUpdated`. Fome em 100 por 12 ticks seguidos → `AgentDied(Starvation)`.
3. Fases futuras entram no mesmo lugar: percepção (todos veem o estado de `t`), decisões em paralelo com timeout, validação e aplicação uma a uma em ordem embaralhada pela seed.
4. `Step()` devolve um `TickReport(t, eventos)`.

Dado real (seed 42, tick 1): `NeedsUpdated { Tick = 1, AgentId = agent-1, Hunger = 29, Energy = 96 }` — o agent-1 nasceu com fome 25 e energia 98. Sem ações, morre no tick 30: chega a 100 no tick ⌈75/4⌉ = 19 e completa 12 ticks no 30.

**Onde está no projeto:** `src/Demiurge.Engine/World.cs` (`Step`, `ApplyNeeds`); `src/Demiurge.Engine/Domain/WorldRules.cs`; testes em `tests/Demiurge.Engine.Tests/TickTests.cs`.

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
