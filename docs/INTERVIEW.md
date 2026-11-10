# Banco de perguntas de entrevista

Respostas modelo curtas (até ~150 palavras), citando código e decisões reais. Níveis: **Fundamentos**, **Projeto**, **Profundidade**.

---

## Modelo do mundo

**[Projeto] Por que o espaço é um grafo de lugares e não um grid?** (ADR-001)
O requisito era social: deslocamento precisava custar tempo e um crime só podia ser visto por quem estivesse presente. Um grid exigiria pathfinding (BFS/A*), colisão e posição por célula — custo de engine sem ganho para a pergunta do projeto. No grafo, a posição do agente é um lugar ou uma aresta com ticks restantes; testemunha é "mesmo lugar, mesmo tick, fora de trânsito". No MVP o grafo é completo, então nem há busca de caminho. Se uma dor pedir granularidade maior, o grid continua possível.

**[Profundidade] Como você reseta o mundo sem perder histórico?** (ADR-002)
Cada mundo é (seed, config, fluxo de eventos) identificado por um `WorldId`. Criar um mundo novo gera outro `WorldId` e para o atual; nada é apagado. Como o engine é determinístico, a mesma seed com a mesma sequência de decisões reproduz o mesmo mundo, o que permite comparar rodadas (A/B). O custo é o banco crescer, vigiado pela dor `STORE_GROWTH`.
