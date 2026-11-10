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

- Líder aprovou incluir na etapa 1: grafo de lugares (ADR-001) e botão "Novo mundo" (ADR-002). `world.md`, `stage-1.md`, `roadmap.md` atualizados; criados `DECISIONS.md` e `INTERVIEW.md`.

- Conceitos 1 (simulação baseada em ticks) e 2 (determinismo e seed) explicados, aprovados, implementados e documentados (`CONCEPTS.md`, `INTERVIEW.md`).
- Primeiro código: `Demiurge.slnx`, `src/Demiurge.Engine` (`World.Step`, necessidades, morte por fome, `IRandom`/`SplitMix64`), `tests/Demiurge.Engine.Tests` — 10 testes verdes (vetor de referência do PRNG, determinismo por SHA-256 de 500 ticks, regras de necessidade e fome).
- Conceito 3 (engine autoritativo) explicado, aprovado e implementado: ações `Rest/Eat/Work/Buy`, mercado, `Perception` imutável, validação com `ActionRejected` e motivo na percepção seguinte. Ordem do tick mudou para decisões → necessidades → mercado. 25 testes verdes.
- Revisão da stack (ADR-003): .NET 10 LTS (11 saiu hoje, mesmo fim de suporte), `.slnx`, xUnit v3 4.0.3 em Microsoft Testing Platform v2, Central Package Management.

**Pendências**
- Líder revisar o rascunho do pitch (`VISION.md` §8).
- Ainda não existe `docker-compose.yml`/Host: a regra 5 (sobe com um comando) passa a valer quando o Host entrar (conceito 9 / dashboard). Avaliar antecipar um Host mínimo com `/health`.
- Próximo conceito: 4 — grafo de lugares (MoveTo, Talk, Steal, testemunhas). Ordem em `docs/context/stage-1.md`.
- Economia generosa demais (300 ticks, seed 42: zero mortes, ~800 de dinheiro por agente com política simples). Não ajustar agora; usar como pressão quando a dor aparecer.
