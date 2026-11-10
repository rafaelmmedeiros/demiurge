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
- **Espaço — proposta (aguardando ok, P3.4):** grafo de lugares com tempo de viagem em ticks, desenhado em canvas com pawns se movendo entre lugares. Sem grid.
- **Violência — proposta (aguardando ok, P3.4):** "depende do que leva uma pessoa a matar" (P2.2). O engine oferece a ação com consequência física; o motivo emerge do estado e do cérebro; o mundo não pune, os agentes reagem. Entra junto com LLM + relações (etapa 5).
- **População:** pequena; 25 agentes como meta inicial, condicionada ao custo de LLM. MVP continua com 5. (P2.4)
- **Gerações:** haverá nascimento e gerações, mas não no início. (P2.4)

## 5. Restrições
- **[hipótese]** Sobe com um comando, sem chave de API. LLM opcional (local via Ollama ou API paga).
- **Hardware do líder:** i9-13900K, 64 GB RAM, AMD RX 7900 XTX (24 GB VRAM). Comporta modelos locais de ~8B a ~30B parâmetros quantizados.
- **Risco técnico:** a GPU no Docker Desktop do Windows é suportada para NVIDIA, não AMD. O Ollama em container rodaria só em CPU; para usar a GPU, Ollama nativo no Windows com o container apontando para `host.docker.internal:11434`. Verificar na etapa 2.
- _(pendente — rodada 3: tempo por semana, orçamento de LLM)_

## 6. Como saberemos que deu certo
- _(pendente — rodada 4)_

## 7. Não-objetivos
- _(pendente — rodada 4)_
