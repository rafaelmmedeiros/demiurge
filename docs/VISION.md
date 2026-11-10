# Visão do projeto — DEMIURGE

> Documento vivo. Construído a partir da entrevista de visão (`docs/interviews/2026-11-10-visao.md`); a referência `(Px.y)` aponta a pergunta de origem.
> Mudanças na visão exigem nova pergunta ao líder, registrada em `docs/interviews/`.

**Em uma frase:** um experimento social em que agentes de IA vivem numa pequena sociedade simulada, e um operador "deus" perturba o mundo e sussurra nos agentes para ver se surgem justiça, leis e governo — construído para transformar um engenheiro .NET/arquiteto em AI Engineer capaz de explicar cada peça.

## 1. Por que este projeto existe
- **Transição de carreira para AI Engineer.** O líder já atua como engenheiro backend .NET e arquiteto; a lacuna é a parte de IA. (P1.1)
- Aprender agentes de IA na prática, a ponto de conversar com segurança sobre AI engineering e agentes em entrevistas. (P1.1, P4.1)
- **Implicação:** a profundidade didática vai para os conceitos de IA (LLM como política de decisão, tool calling, memória, embeddings, planejamento, evals). Conceitos de engenharia que o líder já domina (DI, Minimal API, Docker) são documentados de forma curta.

## 2. Para quem
- **O próprio líder:** estudo, diversão, curiosidade. (P1.2)
- **Portfólio público:** repositório aberto, apresentável para recrutadores. (P1.2)
- É uma **gamificação séria**, inspirada no gênero "god mode"/simulação de colônia (ex.: RimWorld): o operador observa e perturba uma sociedade que vive sozinha. Inspiração de gênero e mecânica, não recriação de nenhum jogo existente. (P1.2)
- **Implicação:** README público em inglês, demo reproduzível com um comando, dashboard agradável de assistir.

## 3. Pergunta central
- **Interações que só existem entre humanos, expressas em ações e não só em texto:** traição, assassinato, respeito, política, empatia. (P1.3)
- **Desfecho desejado:** pawns criando um governo justo e vivendo em harmonia — sistema de justiça, leis de trabalho etc. (P4.2)
- Sob pressão (escassez, crime, eventos do modo deus, personalidades extremas), surgem regras, leis e instituições? São aplicadas?
- **Implicação:** o mundo precisa de relações entre agentes, memória do que aconteceu e ações com peso social. E precisa medir: um sentimento que não muda nenhuma ação não conta. O desfecho "harmonia" é hipótese a testar, não resultado garantido.

## 4. O que o mundo é
- **Experimento social, não jogo, não life sim, não produto comercial.** (P2.3, P4.3) Rotina diária (morar, trabalhar, estudar, votar, comer, deslocar-se) existe como substrato para a dinâmica social, não como foco.
- **Iterativo:** começa básico e ganha complexidade por dor. (P2.1)
- **Espaço:** grafo de lugares (casa, trabalho, mercado, escola, praça, prefeitura…) com arestas que custam N ticks de viagem. Dashboard em canvas com pawns andando ao longo das arestas. Sem grid; grid só se uma dor pedir. (P2.3, P3.4)
- **Crime e violência:** o engine oferece a ação (`Steal`, depois `Kill`) com consequência física e testemunhas; o motivo emerge do estado e do cérebro do agente; **o mundo não pune**. Nas palavras do líder: "eu posso roubar alguém e matar, somente serei punido se alguém ver". Testemunha = agente no mesmo lugar no mesmo tick. Punição, se existir, é reação dos outros agentes. `Kill` entra com LLM + relações (etapa 5). (P2.2, P3.4)
  - Futuro: descoberta posterior (corpo encontrado, objeto roubado visto com o ladrão, boato), quando a dor aparecer.
- **Pessoas:** no início, só necessidades básicas. Depois, personalidades diferentes fundamentadas em psicologia, neurologia e psiquiatria (candidato: traços Big Five/OCEAN). (P2.1)
- **Personalidades plantadas:** ao criar um mundo, o operador pode inserir pawns com tendências definidas (ex.: psicopata, boçal) para observar a reação da sociedade. (P4.3)
- **População:** pequena; 25 agentes como meta, condicionada ao custo de LLM. MVP com 5. (P2.4)
- **Gerações:** haverá nascimento e gerações, mas não no início. (P2.4)

## 5. Modo deus
O operador tem um conjunto amplo e crescente de intervenções; pragas e secas são só exemplos. (P4.4)
- **Perturbações no mundo:** praga, seca, escassez, injeção de agentes.
- **Criar e resetar mundos:** novo mundo com seed e configuração (população, personalidades plantadas); resetar o atual. (P4.1)
- **Sussurro ("diabinho"/"anjinho"):** o operador injeta um pensamento na percepção de um agente — "vai lá e queima a casa dela", "se desculpa pelo que fez" — e observa se ele obedece ou resiste. (P4.4)
  - Coerente com a regra 1: o sussurro altera a **percepção**, não o estado. O agente decide.
  - Experimento mensurável: taxa de obediência ao sussurro por personalidade, por relação com o alvo, por tipo de sugestão.
  - Conexão com AI engineering: é uma injeção de instrução controlada no contexto do LLM — o mesmo mecanismo de prompt injection — tema forte de entrevista.

## 6. Restrições
- Sobe com um comando, sem chave de API. LLM opcional (local ou pago). (CLAUDE.md)
- **Tempo:** 6–12 h por semana (~80–150 h por trimestre). Entregas cabem em sessões de 2–4 h, cada uma deixando o sistema subindo e documentado. (P3.1)
- **Orçamento de LLM:** até ~US$ 20/mês, "quanto menos, melhor". (P3.2)
  - LLM local (Ollama) é o padrão; API paga só para experimentos pontuais e comparação.
  - Controle de custo desde a etapa 2: tokens por agente/tick, orçamento configurável, LLM consultado só em evento novo ou a cada N ticks (dor `LLM_COST`).
  - A meta de 25 agentes depende desse orçamento.
- **Hardware do líder:** i9-13900K, 64 GB RAM, AMD RX 7900 XTX (24 GB VRAM) — comporta modelos locais de ~8B a ~30B quantizados.
- **Risco técnico:** GPU no Docker Desktop do Windows é suportada para NVIDIA, não AMD. Ollama em container rodaria em CPU; alternativa: Ollama nativo no Windows, container apontando para `host.docker.internal:11434`. O líder aceita, mas quer verificar antes. Verificar na etapa 2. (P3.3)

## 7. Critérios de sucesso (horizonte de ~3 meses)
1. Abrir o navegador e ver a simulação acontecendo. (P4.1)
2. Resetar o mundo e criar um novo pela interface. (P4.1)
3. O líder conversa com segurança sobre AI engineering e agentes em entrevistas — verificado pelas arguições de cada etapa (`docs/context/mentoring.md`). (P4.1)

## 8. Pitch de entrevista (rascunho — o líder ajusta)
O pitch não pode depender de os pawns terem dado certo; a história é o método, e o resultado entra no fim, seja ele qual for.

> "Construí um simulador em que agentes com LLM vivem numa pequena sociedade. O LLM só propõe ações; um engine determinístico valida e aplica, então o mundo é reproduzível por seed. Cada técnica de agentes entrou para resolver um problema medido: agentes de regras morriam de fome com dinheiro no bolso, então entrou o LLM; o LLM repetia ações, então entrou memória. Depois pus pressão — crime sem punição automática, seca, um psicopata plantado, sussurros do 'diabinho' — para ver se surgiam leis e justiça. O que aconteceu foi: ___."

## 9. Não-objetivos
- Não é produto comercial. (P4.3)
- Não é jogo: não há jogador controlando pawn nem condição de vitória. (P4.3)
- Não é life sim: rotina individual não é o foco. (P4.3)
- Sem gráficos 3D ou visual elaborado no horizonte inicial (Unity só na etapa 9, por dor).
