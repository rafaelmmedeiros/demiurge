# Visão do projeto — DEMIURGE

> Documento vivo. Construído a partir da entrevista de visão (`docs/interviews/2026-11-10-visao.md`).
> Itens marcados **[hipótese]** vieram do CLAUDE.md original e ainda não foram confirmados pelo líder.

## 1. Por que este projeto existe
- **Transição de carreira para AI Engineer.** O líder já atua como engenheiro backend .NET e arquiteto; a lacuna é a parte de IA. (P1.1)
- Aprender agentes de IA na prática, a ponto de explicar cada decisão numa entrevista técnica.
- **Implicação:** a profundidade didática vai para os conceitos de IA (LLM como política de decisão, tool calling, memória, embeddings, planejamento, evals). Conceitos de engenharia que o líder já domina (DI, Minimal API, Docker) são documentados mais curtos.

## 2. Para quem
- **O próprio líder:** estudo, diversão, curiosidade. (P1.2)
- **Portfólio público:** o repositório é aberto e precisa ser apresentável para recrutadores. (P1.2)
- É uma **gamificação séria**, inspirada no gênero de jogos "god mode"/simulação de colônia (ex.: RimWorld): o operador observa e perturba uma sociedade que vive sozinha. Inspiração de gênero e mecânica, não recriação de nenhum jogo existente.
- **Implicação:** README público em inglês, demo reproduzível com um comando, dashboard agradável de assistir.

## 3. Pergunta central (o que queremos observar)
- **Interações que só existem entre humanos, expressas em ações e não só em texto:** traição, assassinato, respeito, política, empatia. (P1.3)
- **[hipótese]** Se, sob pressão (praga, seca, escassez), agentes criam regras, leis e instituições, e se as aplicam. Política e leis passam a ser um caso particular da pergunta acima.
- **Implicação:** o mundo precisa de relações entre agentes (quem confia em quem, quem deve a quem), memória do que aconteceu e ações com peso social. E precisa medir isso: um sentimento que não muda nenhuma ação não conta.

## 4. O que o mundo é (e não é)
- **Iterativo:** começa básico e ganha complexidade por dor. (P2.1)
- **Pessoas:** no início, só necessidades básicas. Direção de longo prazo: personalidades diferentes fundamentadas em psicologia, neurologia e psiquiatria (candidato natural: traços Big Five/OCEAN como base). (P2.1)
- **Não é um jogo, é uma simulação de vida:** o agente se desloca, mora (ou não tem casa), trabalha, estuda, vota, come. Deslocamento custa tempo desde o modelo inicial. (P2.3)
- **Espaço (confirmado, P3.4):** grafo de lugares (casa, trabalho, mercado, escola, praça, prefeitura…) com arestas que custam N ticks de viagem. Dashboard em canvas com pawns andando ao longo das arestas. Sem grid; grid só se uma dor pedir.
- **Violência e crime (confirmado, P3.4):** o engine oferece a ação (`Steal`, depois `Kill`) com consequência física e testemunhas; o motivo emerge do estado e do cérebro do agente; **o mundo não pune**. Nas palavras do líder: "eu posso roubar alguém e matar, somente serei punido se alguém ver". Testemunha = agente no mesmo lugar no mesmo tick. Punição, se existir, é reação dos outros agentes. `Kill` entra com LLM + relações (etapa 5).
- **Desdobramento futuro:** além de testemunha direta, descoberta posterior (corpo encontrado, objeto roubado visto com o ladrão, boato). Fica para quando a dor aparecer.
- **População:** pequena; 25 agentes como meta inicial, condicionada ao custo de LLM. MVP continua com 5. (P2.4)
- **Gerações:** haverá nascimento e gerações, mas não no início. (P2.4)

## 5. Restrições
- **[hipótese]** Sobe com um comando, sem chave de API. LLM opcional (local via Ollama ou API paga).
- **Hardware do líder:** i9-13900K, 64 GB RAM, AMD RX 7900 XTX (24 GB VRAM). Comporta modelos locais de ~8B a ~30B parâmetros quantizados.
- **Risco técnico:** a GPU no Docker Desktop do Windows é suportada para NVIDIA, não AMD. O Ollama em container rodaria só em CPU; para usar a GPU, Ollama nativo no Windows com o container apontando para `host.docker.internal:11434`. O líder aceita essa alternativa, mas quer verificar antes se a AMD não roda em Docker. Verificar na etapa 2. (P3.3)
- **Tempo:** 6–12 h por semana. (P3.1) Cerca de 80–150 h por trimestre. Entregas devem caber em sessões de 2–4 h, cada uma deixando o sistema subindo e documentado.
- **Orçamento de LLM:** até ~US$ 20/mês, "quanto menos, melhor". (P3.2) Consequências:
  - LLM local (Ollama) é o padrão para desenvolvimento e rodadas longas; API paga só para experimentos pontuais e comparação.
  - Controle de custo é requisito desde a etapa 2: contagem de tokens por agente/tick, orçamento configurável, consulta ao LLM só em evento novo ou a cada N ticks (dor `LLM_COST`).
  - A meta de 25 agentes depende desse orçamento.

## 6. Como saberemos que deu certo
- _(pendente — rodada 4)_

## 7. Não-objetivos
- _(pendente — rodada 4)_
