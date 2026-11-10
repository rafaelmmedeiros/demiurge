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

---

## Espaço e informação

**[Projeto] Como funciona o deslocamento no seu mundo?**
O espaço é um grafo completo de 4 lugares com custo em ticks nas arestas (Casa–Trabalho = 2, a maioria = 1). `MoveTo` coloca o agente em `InTransit(origem, destino, ticksRestantes)` e emite `TravelStarted`; ao fim de cada tick a viagem avança e, ao chegar a 0, vira `AtPlace(destino)` com `Arrived`. Em trânsito o agente não decide, não pode ser alvo e não testemunha nada, mas a fome continua subindo. Por isso deslocar-se custa tempo de verdade: no mesmo seed, a política simples que sobrevivia sem espaço passou a morrer de fome com ele.

**[Projeto] Um agente rouba outro. Como o mundo decide quem fica sabendo?**
O engine sempre emite `Stole` — é o log de eventos, a visão de deus. Depois, cada agente presente no mesmo lugar, inclusive a vítima e exceto o ladrão, sorteia 50% com o RNG da seed, em ordem de id. Quem acerta recebe `TheftWitnessed` e o roubo aparece em `TheftsWitnessed` na sua percepção do tick seguinte. Quem não viu não sabe — a vítima percebe a comida a menos, mas não quem levou. O mundo nunca pune: só quem viu pode reagir.

**[Profundidade] O que é informação assimétrica no seu projeto e por que ela importa?**
É a diferença entre o log de eventos (tudo que aconteceu) e a percepção de cada agente (só o que ele viu, ouviu e o que está no lugar dele). Ela é construída de propósito: `Perceive()` devolve apenas lugar, presentes, mensagens ouvidas e roubos testemunhados no último tick. Sem ela, todo crime seria conhecido por todos e não haveria impunidade, boato, investigação ou mentira — exatamente os fenômenos sociais que o projeto quer observar. Também é um ponto de avaliação: um agente de LLM que "sabe" algo que não estava na percepção denuncia vazamento de contexto.

**[Profundidade] Por que um grafo completo e não Dijkstra/A*?**
Com 4 lugares, um grafo completo tem 6 arestas e responde "quanto custa ir de A a B" com uma consulta. Pathfinding só é necessário quando há rotas que passam por lugares intermediários. O construtor do `PlacesGraph` rejeita grafo incompleto, então a hipótese está garantida em código. O limite é o crescimento n(n−1)/2: com 20 lugares seriam 190 arestas a configurar — aí entra Dijkstra e o grafo deixa de precisar ser completo.

---

## Event sourcing e persistência

**[Fundamentos] O que é event sourcing?**
É guardar como fonte da verdade a sequência de fatos que aconteceram, só com inserções, e derivar o estado aplicando esses fatos em ordem: estado(t) = fold(Apply, vazio, eventos até t). No DEMIURGE, toda mudança passa por `World.Emit(e)`, que aplica e registra o evento; o SQLite guarda a tabela `events` append-only. O estado atual nunca é a verdade — é um cálculo, e snapshots a cada 100 ticks são só cache para não reaplicar do gênesis.

**[Projeto] Como você garante que nenhum estado muda sem gerar evento?**
Por construção e por teste. Por construção: `Apply(evento)` é o único método com atribuições de estado no `World`; as regras só calculam eventos, e `Emit` aplica e registra. O caminho ao vivo e a reconstrução usam o mesmo `Apply`. Por teste: rodo 500 ticks, reconstruo um mundo só com os eventos e comparo os snapshots com `Assert.Equivalent` estrito; outro teste garante que o cenário produz todos os tipos de evento, para que nenhum fique sem cobertura.

**[Projeto] Por que os eventos carregam o valor final (`Money = 48`) e não a variação (`+10`)?**
Para que aplicar um evento seja atribuição, não regra. Se o evento fosse "trabalhou", a reconstrução precisaria reexecutar a regra do salário — e, se o salário mudar no código, o passado mudaria junto. Com o valor final, o passado fica fixo. É também a diferença entre event sourcing e command sourcing: gravar só as decisões é mais compacto, mas o resultado depende da versão do código.

**[Profundidade] Seu mundo usa números aleatórios. Como a reconstrução continua a mesma sequência?**
O estado do RNG é estado do mundo, então vira evento: cada tick termina com `TickEnded { Tick, RngState }`, e o snapshot também guarda o `RngState`. Ao aplicar `TickEnded`, o mundo recria o SplitMix64 naquele estado. Um teste para a simulação no tick 250, retoma do SQLite, roda mais 250 ticks e exige que o log seja idêntico ao de 500 ticks ininterruptos.

**[Profundidade] Quais os custos de event sourcing no seu projeto?**
Três. Crescimento: 7,3 eventos por tick, ~1 MB por 1000 ticks com 5 agentes, ~3,4 MB por hora a 1 tick/s — vigiado pela dor `STORE_GROWTH`. Evolução de schema: renomear um evento quebra a leitura do histórico; precisarei de *upcasting* ou versão no discriminador. Concorrência: o `Append` lê `MAX(seq)`, o que só é correto com um escritor — suficiente hoje, errado com vários processos.

**[Projeto] Por que Dapper e não EF Core?** (ADR-004)
O event store tem três tabelas append-only e meia dúzia de queries. EF Core traria change tracking e migrations, pensados para entidades mutáveis — o oposto de eventos imutáveis. Dapper me deixa escrever o SQL, que num event store é simples e eu quero controlar, e só cuida de parâmetros e mapeamento. A alternativa considerada foi ADO.NET puro; o Dapper venceu por deixar o código do store só com SQL.

---

## Agente e laço

**[Fundamentos] O que é um agente de IA? Qual a diferença para um chatbot?**
Um agente percebe um ambiente, decide e age sobre ele, em laço: a consequência da ação volta como nova percepção. Um chatbot responde a uma pergunta e para; não tem ambiente nem continuidade própria. No DEMIURGE o ambiente é o `World`, o sensor é `Perceive()`, o programa é `IAgentBrain` e o atuador é a ação que o engine valida. Se o agente tenta comprar sem dinheiro, no tick seguinte ele "vê" `InsufficientMoney` — o erro vira entrada. O LLM, quando entrar, é só a função de decisão dentro desse laço.

**[Projeto] Por que `IAgentBrain` devolve `AgentAction` e não `AgentDecision`?**
Porque `AgentDecision` carrega o `AgentId`. Se o cérebro devolvesse a decisão completa, poderia — por bug ou alucinação de um LLM — propor uma ação em nome de outro agente, e o harness teria de conferir. Devolvendo só a ação, o harness monta `AgentDecision(perception.Self, ação)` e a impossibilidade é garantida pelo tipo, no mesmo espírito da `Perception` imutável.

**[Profundidade] O que acontece se o LLM ficar 30 s sem responder no meio de um tick?**
O tick não espera. O `TickRunner` cria um `CancellationTokenSource` com o prazo da decisão e aguarda cada cérebro com `WaitAsync(token)`: no prazo, para de esperar mesmo que o cérebro ignore o token. Aquele agente recebe `BrainFailure(Timeout)` e fica parado no tick; os outros agem normalmente e o tick fecha com `TickEnded`. Um teste usa um cérebro que nunca responde, com prazo de 100 ms, e verifica que o tick fecha. Depois, o circuit breaker da etapa 2 faz o LLM cair para o cérebro de regras.

**[Profundidade] Como você prova que os cérebros rodam em paralelo?**
Com uma barreira, não com cronômetro. Cada um dos 5 cérebros de teste incrementa um contador e espera até que os 5 tenham chegado. Se o harness chamasse em sequência, o primeiro esperaria para sempre pelos outros e estouraria o prazo. O teste exige zero falhas — só passa se as 5 chamadas estiverem em andamento ao mesmo tempo. Testes por tempo ("demorou menos que X") são instáveis em CI.

**[Projeto] Os cérebros respondem em ordem aleatória. Isso não quebra o determinismo?**
Não, porque a ordem de chegada é descartada: o engine ordena as decisões por `AgentId` e só depois embaralha com a seed. O que pode mudar o resultado é o prazo: numa execução ao vivo, um cérebro lento pode ficar de fora. Por isso a garantia é "mesma seed + mesmas decisões", e o mundo é reproduzível pelo log de eventos, que registra o que de fato aconteceu.

---

## Cérebro de regras

**[Fundamentos] Qual a diferença entre um agente baseado em regras e utility AI?**
Na lista de regras, as condições são testadas em ordem e a primeira que casa decide — a prioridade é fixa. Na utility AI, toda opção recebe uma nota calculada da percepção e vence a maior. No DEMIURGE vi a diferença na prática: uma lista de regras ("fome ≥ 60 → comprar" antes de "trabalhar") deixava agentes famintos e sem dinheiro presos no mercado, rejeitados tick após tick. O `RuleBasedBrain`, com utility, só pontua objetivos disponíveis: 0% de ações rejeitadas em 10 seeds.

**[Projeto] Seu cérebro de regras é fraco de propósito. Como você sabe onde ele falha?**
Medindo, não supondo. Rodei 1000 ticks em 10 seeds: 41 de 50 agentes morreram, mas nenhum com dinheiro no bolso — então não era o "paradoxo da fome" que o roadmap previa. O que apareceu foi oscilação: o objetivo muda em 55% das decisões e há 0,13 trabalho por viagem. Cada `Work` reduz a nota de trabalhar em 10 e aumenta a de descansar em 10, e o agente vive na estrada. Também zero conversas. São esses números que justificam o próximo cérebro.

**[Profundidade] Como você corrigiria a oscilação sem LLM?**
Três caminhos clássicos. Histerese: bônus de nota para o objetivo atual, então só troca se outro superar por uma margem. Compromisso: depois de escolher, mantém por N ticks ou até concluir. Planejamento (GOAP): busca a sequência de ações que satisfaz o objetivo, considerando o custo da viagem. Não apliquei nenhum de propósito: o cérebro de regras é a linha de base e a oscilação é a dor medida. A pergunta da etapa 2 é se um LLM, só com a mesma percepção, decide melhor — e a que custo.

**[Projeto] Por que as notas são públicas (`Score`)?**
Explicabilidade. Para qualquer decisão consigo mostrar "foi ao mercado porque GetFood = 62 superou Rest = 60 e Work = 58". Isso é a linha de base para observabilidade: quando o LLM entrar, vou querer a mesma pergunta respondida — por que ele decidiu isso? — e a comparação justa exige o mesmo nível de inspeção nos dois cérebros.

**[Projeto] Já aconteceu de a medição contradizer o seu plano?** (ADR-005)
Sim. O roadmap dizia que o cérebro de regras geraria "paradoxo da fome" (morrer com dinheiro e comida disponível) e loops de repetição, e que isso justificaria o LLM. Em 10 seeds, as duas deram zero. A falha real foi oscilação: o objetivo mudava em 55% das decisões e o agente passava a vida viajando. Criei a dor `GOAL_OSCILLATION` e mudei o gatilho da etapa 2 para as dores efetivamente medidas. Também percebi que esses números vinham de um script fora do repo e os marquei como provisórios até os detectores os reproduzirem com um comando.

---

## Dores e observabilidade

**[Fundamentos] O que é observabilidade e como ela aparece no seu projeto?**
É conseguir responder "o que está acontecendo e por quê" só com os dados que o sistema emite. No DEMIURGE há três fontes: o log de eventos (o que aconteceu), o `TickOutcome` (falhas de cérebro e duração) e as dores — sinais calculados por detectores ao fim de cada tick, agregados e ranqueados por severidade × episódios. A pergunta "por que tantos agentes morrem?" é respondida com `GOAL_OSCILLATION` em 8 de 10 seeds, não com uma opinião.

**[Projeto] Como seus detectores medem um cérebro de LLM, se não sabem o que ele "pensa"?**
Eles só olham o observável: eventos, decisões enviadas, falhas e o snapshot do mundo. Por isso a oscilação não é medida como "trocou de objetivo" — objetivo é interno, e um LLM nem tem um explícito —, mas como "chegou a um lugar e partiu sem fazer nada lá". Isso vale igual para regras e para LLM, o que torna a comparação da etapa 2 justa.

**[Profundidade] Por que disparo por borda e não por nível?**
Uma condição como "48 ticks sem conversar" fica verdadeira por centenas de ticks. Por nível, gera um sinal por agente por tick — 5 agentes × 950 ticks soterram qualquer dor rara. Por borda, o sinal sai quando a condição começa e o detector se rearma quando ela termina; `Count` passa a ser número de episódios. É o mesmo raciocínio dos alertas do Prometheus, que notificam na transição de estado, não a cada avaliação.

**[Projeto] Como você garante que a medição é reproduzível?**
Duas garantias. Determinismo: a mesma seed produz as mesmas dores (há um teste que roda 300 ticks duas vezes e compara os sinais). Ferramenta: `dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000` roda o mesmo caminho do harness, em memória, e imprime a tabela. Antes disso eu tinha números de um script descartável — marquei como provisórios até a ferramenta reproduzi-los.

**[Profundidade] Um detector com bug pode derrubar a simulação?**
Não. O `TickRunner` executa cada detector dentro de try/catch; se um lança exceção, ele é pulado naquele tick e a falha vira um sinal `TICK_EXCEPTION` com a mitigação "Detector skipped for this tick". Observabilidade não pode ser um ponto de falha do sistema observado.

---

## Dashboard, host e i18n

**[Projeto] Como o dashboard recebe o estado e por que ele não altera o mundo?**
A simulação roda num `BackgroundService` que é o único escritor do mundo. Ao fim de cada tick ele publica um `WorldView` imutável e empurra pelo SignalR uma mensagem `tick` com a view e os eventos do tick (~2 KB com 5 agentes). O browser só desenha. Comandos do modo deus vão por HTTP e viram mensagens numa fila; quem executa é o laço. Assim a apresentação pode ser trocada — Blazor, Unity — sem mexer na simulação.

**[Profundidade] O que acontece se eu clicar "avançar" enquanto um tick está rodando?**
Nada conflita. O endpoint não toca no mundo: escreve um `StepCommand` num `Channel` e espera o `TaskCompletionSource` dele. O laço da simulação só lê o canal entre ticks, então o step roda depois do tick atual, e a resposta HTTP traz a view resultante. É o padrão de ator: um dono do estado, uma caixa de entrada, processamento sequencial. Com `lock`, a trava ficaria presa durante os segundos de espera de um LLM.

**[Projeto] Por que SignalR e não polling ou SSE?**
Polling a cada segundo dá até um segundo de atraso e uma requisição por cliente por segundo, mesmo sem mudança. SSE resolveria hoje — o fluxo é só servidor→browser —, é nativo e mais simples. Escolhi SignalR porque já estava na stack do projeto, trata reconexão e fallback de transporte, e permite mensagens nos dois sentidos se o modo deus precisar. Se só houvesse push, SSE seria uma escolha igualmente boa.

**[Projeto] Como você garante que o sistema sobe com um comando?**
`docker compose up -d --build`: Dockerfile multi-stage (SDK compila, runtime roda como não-root), um serviço, porta 8080, volume para o SQLite, `.env` opcional — sem chave nem configuração. Defaults de tudo em `DemiurgeOptions`. Ao subir, retoma o último mundo do volume. Testei: `/health` 200, ticks avançando, e um `docker compose restart` mantém o mesmo mundo.

**[Fundamentos] Como funciona a i18n do dashboard?**
Dois dicionários JSON (pt-BR e en) com chaves como `event.Worked` ou `pain.GOAL_OSCILLATION`. A API e os eventos ficam em inglês; o browser traduz identificadores em rótulos e preenche modelos como "{agentId} trabalhou e ganhou {earned}". O idioma vem da escolha salva, senão do navegador. Um teste por reflexão garante que todo tipo de evento, ação, motivo de rejeição e código de dor tem tradução nos dois idiomas — um evento novo sem tradução quebra os testes.

**[Profundidade] Que cuidado de segurança o dashboard já precisa ter, mesmo sem LLM?**
O texto de `Talked` vem de um agente e, com LLM, será texto gerado — pode conter HTML ou script. O feed insere esse texto com `textContent`, nunca `innerHTML`, então ele é exibido literalmente. É a mesma lição da prompt injection, ao contrário: saída de modelo é entrada não confiável para o resto do sistema.
