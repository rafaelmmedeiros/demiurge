# Banco de perguntas de entrevista

Respostas modelo curtas (até ~150 palavras), citando código e decisões reais. Níveis: **Fundamentos**, **Projeto**, **Profundidade**.

---

## Modelo do mundo

**[Projeto] Por que o espaço é um grafo de lugares e não um grid?** (ADR-001)
O requisito era social: deslocamento precisava custar tempo e um crime só podia ser visto por quem estivesse presente. Um grid exigiria pathfinding (BFS/A*), colisão e posição por célula — custo de engine sem ganho para a pergunta do projeto. No grafo, a posição do agente é um lugar ou uma aresta com ticks restantes; testemunha é "mesmo lugar, mesmo tick, fora de trânsito". No MVP o grafo é completo, então nem há busca de caminho. Se uma dor pedir granularidade maior, o grid continua possível.

**[Profundidade] Como você reseta o mundo sem perder histórico?** (ADR-002)
Cada mundo é (seed, config, fluxo de eventos) identificado por um `WorldId`. Criar um mundo novo gera outro `WorldId` e para o atual; nada é apagado. Como o engine é determinístico, a mesma seed com a mesma sequência de decisões reproduz o mesmo mundo, o que permite comparar rodadas (A/B). O custo é o banco crescer, vigiado pela dor `STORE_GROWTH`.

---

## Simulação e determinismo

**[Fundamentos] O que é uma simulação baseada em ticks e por que usá-la com agentes de LLM?**
O tempo avança em passos discretos; entre dois ticks nada muda. No DEMIURGE, 1 tick = 1 hora simulada, a cada 1000 ms. Em cada tick o engine aplica necessidades (fome +4, energia −2), depois coleta decisões de todos os agentes olhando o mesmo estado, e aplica uma a uma em ordem embaralhada pela seed. Com LLM isso é essencial: uma decisão leva 0,5–5 s, e o mundo precisa ficar congelado num estado consistente enquanto todos pensam. A alternativa, um loop assíncrono por agente, faz quem responde mais rápido agir primeiro — a ordem passa a depender da latência de rede e o determinismo se perde.

**[Projeto] Dois agentes tentam comprar a última comida no mesmo tick. O que acontece?**
Os dois perceberam "1 unidade" porque a percepção é tirada do estado do início do tick. As decisões são aplicadas sequencialmente numa ordem embaralhada pelo RNG da seed (Fisher–Yates): o primeiro compra, o segundo recebe `ActionRejected("OutOfStock")`, que entra na percepção dele no tick seguinte. Embaralhar evita que o agente de menor id sempre vença; usar a seed mantém o resultado reproduzível.

**[Projeto] Por que vocês implementaram o próprio PRNG em vez de usar `System.Random`?**
Dois motivos. Primeiro, a Microsoft não garante que o algoritmo do `System.Random(seed)` seja o mesmo entre versões do .NET: um upgrade poderia mudar todos os mundos salvos. Segundo, ele não expõe o estado interno, e um snapshot no tick 500 precisa salvar o estado do RNG para continuar a mesma sequência. O SplitMix64 tem 15 linhas, estado de um `ulong` (8 bytes), passa no BigCrush, e um teste fixa o vetor de referência da seed 0 para detectar qualquer mudança acidental.

**[Profundidade] O LLM não é determinístico. Como o mundo pode ser reproduzível?**
A regra é "mesma seed + mesma sequência de decisões = mesmo mundo", não "mesma seed = mesmo mundo". O LLM só propõe; cada decisão vira evento. No replay, o engine lê as decisões gravadas em vez de chamar o LLM. Assim a parte não determinística fica isolada na fronteira (o cérebro), e o engine — que é quem aplica regras e sorte — é uma função pura de (seed, decisões). Mesmo com temperatura 0 o LLM pode variar (batching, ponto flutuante na GPU), então gravar é a única garantia.

**[Profundidade] Que fontes escondidas de não-determinismo existem em C#?**
`DateTime.Now` e `Guid.NewGuid()` são as óbvias. As escondidas: `string.GetHashCode()` é randomizado por processo no .NET Core, então qualquer coisa ordenada por hash muda a cada execução; iterar `HashSet`/`Dictionary` não tem ordem garantida; `float`/`double` podem divergir entre plataformas e otimizações; `Parallel.ForEach` aplicando efeitos muda a ordem. No engine: IDs sequenciais, iteração por id ordenado, recursos inteiros, efeitos aplicados em sequência. Um teste compara o SHA-256 do fluxo de eventos de duas execuções de 500 ticks.

---

## Stack

**[Projeto] Por que .NET 10 e não .NET 11, lançado no mesmo dia em que o projeto começou?** (ADR-003)
O .NET 10 é LTS e o 11 é STS; com a nova política de suporte de dois anos para STS, os dois terminam em novembro de 2028, então não há vantagem de suporte. O 10 já estava instalado, com ecossistema maduro; o 11 estava no 11.0.0 do dia zero. Nada no projeto precisa de recurso do C# 15 hoje. Migrar é trocar o `TargetFramework` no `Directory.Build.props` e o SDK no `global.json` — duas linhas — então a decisão é barata de reverter.

---

## Engine autoritativo

**[Fundamentos] Por que o LLM não altera o estado do mundo diretamente?**
Porque um LLM alucina: pode "comprar" com dinheiro que não tem ou inventar comida. No DEMIURGE o cérebro só devolve uma proposta de um conjunto fechado (`Rest`, `Eat`, `Work`, `Buy`); o engine valida em `ActionRules.Validate` e aplica ou rejeita com `ActionRejected(motivo)`. Assim as invariantes — dinheiro nunca negativo, comida conservada — ficam num só arquivo, a sorte é sorteada no engine com a seed (determinismo) e uma prompt injection pode mudar a escolha do agente, mas nunca as regras do mundo.

**[Projeto] Como você garante, no código, que o cérebro não consegue mudar o estado?**
Por tipo, não por convenção. O cérebro recebe `Perception`, um `record` imutável com cópias dos valores; nunca recebe o `Agent`, cujos setters são `internal` ao assembly do engine. Sem referência a nada mutável, não há o que alterar. A única saída do cérebro é um `AgentDecision(AgentId, AgentAction)`. Um teste confirma que a percepção é uma cópia: depois de um tick, a percepção antiga mantém os valores antigos.

**[Projeto] O que acontece com uma ação inválida? O agente fica sabendo?**
Nenhum estado muda; o engine emite `ActionRejected { AgentId, Action, Reason }`, grava o motivo em `Agent.LastRejection` e, no tick seguinte, `Perceive()` devolve esse motivo. É o laço de feedback: o agente vê `NoFood` e pode decidir comprar. O motivo dura um tick. Se mais de 20% das ações de um agente forem rejeitadas, a dor `ACTION_REJECTION_RATE` aponta percepção incompleta ou prompt ruim.

**[Profundidade] Por que não usar tools com efeito colateral, como a maioria dos frameworks de agentes?**
Com tools que mutam, cada chamada altera o mundo no momento em que acontece: quem responde primeiro age primeiro, a validação fica espalhada por cada tool e não existe resolução simultânea no tick. Aqui a tool call é só uma proposta: todas as decisões do tick são coletadas, ordenadas por id, embaralhadas pela seed e validadas num único lugar. Perde-se a possibilidade de encadear várias ações num tick; ganha-se determinismo e invariantes verificáveis.

**[Profundidade] Por que ordenar as decisões por id antes de embaralhar?**
Porque os cérebros respondem em ordem arbitrária — em paralelo, com latência de rede variável. Se o embaralhamento partisse da ordem de chegada, essa ordem não determinística vazaria para o resultado. Ordenando por `AgentId` primeiro, a entrada do shuffle é sempre a mesma e o resultado depende só da seed. Um teste envia as mesmas cinco compras em ordem direta e invertida e exige eventos idênticos.

**[Profundidade] Qual a diferença para o "Game Master" do Concordia?**
No Concordia (DeepMind) um LLM "mestre do jogo" lê a ação em texto livre e narra o resultado. É muito expressivo — qualquer ação é possível — mas o resultado não é determinístico nem verificável por código. No DEMIURGE o engine é código: só existem as ações do schema, mas cada resultado é reproduzível e testável. O custo é que comportamento novo exige ação nova no engine.
