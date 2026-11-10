# Diário de sessões

Uma entrada por sessão, a mais recente no topo. Serve para a próxima sessão retomar sem perder contexto.
Formato: o que foi feito · decisões · pendências.

---

## 2026-11-10 — Sessão 1: organização e visão

**Feito**
- CLAUDE.md original (19 KB) reduzido para ~4,5 KB; detalhes movidos para `docs/context/*.md` (lidos sob demanda).
- Repositório criado: https://github.com/rafaelmmedeiros/demiurge (conta pessoal, identidade git configurada só localmente).
- `.gitignore` (`.env`, `*.db`, `bin/`, `obj/`) e `.gitattributes` (LF; `.sln` em CRLF).
- Entrevista de visão concluída (4 rodadas, `docs/interviews/2026-11-10-visao.md`) e `docs/VISION.md` consolidado.

**Decisões**
- Idioma: código, tooling, `CLAUDE.md` e `docs/context/` em inglês; dashboard bilíngue pt-BR + en; chat e documentos de estudo (`CONCEPTS`, `INTERVIEW`, `PAINS`, `DECISIONS`, `VISION`, este diário) em pt-BR.
- Docs de contexto referenciados por tabela "leia quando", não por `@import` (import carrega tudo em toda sessão).
- Primeiro passo do projeto: definir a visão por entrevista, antes de qualquer código.
- Visão: experimento social (não jogo, não life sim, não comercial); espaço como grafo de lugares com tempo de viagem; o mundo não pune crimes, só agentes reagem (testemunha = mesmo lugar, mesmo tick); modo deus com perturbações, criar/resetar mundo, personalidades plantadas e sussurro na percepção; LLM local por padrão, até US$ 20/mês.

**Pendências**
- Líder revisar o rascunho do pitch (`VISION.md` §8).
- Ajustar `docs/context/world.md` e `stage-1.md` ao que a visão mudou (grafo de lugares, criar/resetar mundo). Decidir o que entra já na etapa 1.
- Depois: Dia 1 (etapa 1), começando pela explicação de simulação baseada em ticks.
