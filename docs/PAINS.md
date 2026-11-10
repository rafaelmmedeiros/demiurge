# Dores

Dores abertas e resolvidas. As de runtime (técnicas e comportamentais) vêm dos detectores (`/api/pains` quando o Host existir; hoje via `tools/Demiurge.Probe`). Dores `Knowledge` (do líder) são registradas só aqui.

Formato: código · categoria · severidade · evidência (comando que reproduz) · status · próximo passo.

---

## Abertas

### GOAL_OSCILLATION · Behavior · 3
- **Evidência:** `dotnet run --project tools/Demiurge.Probe -- --brain rules --seeds 1-10 --ticks 1000 --agents 5` → 8/10 seeds, 126 episódios, primeiro no tick 16.
- **Causa:** `RuleBasedBrain` refaz a decisão do zero a cada tick, sem compromisso nem histerese (ver CONCEPTS: *Agente baseado em regras e utility AI*).
- **Status:** Open — gatilho da etapa 2 (ADR-005). Não corrigir no cérebro de regras (fraco de propósito).

### NO_SOCIAL_CONTACT · Behavior · 2
- **Evidência:** mesmo comando → 10/10 seeds, 50 episódios (todos os agentes), tick 48.
- **Causa:** `RuleBasedBrain` não tem objetivo social; nunca propõe `Talk`.
- **Status:** Open — gatilho da etapa 2 (ADR-005).

### RESOURCE_COLLAPSE · Behavior · 5
- **Evidência:** mesmo comando → 9/10 seeds, 17 episódios, primeiro no tick 114; 41/50 agentes mortos.
- **Causa:** consequência da oscilação (vivem viajando, não acumulam dinheiro) somada ao custo de deslocamento.
- **Status:** Open — gatilho da etapa 2 (ADR-005).

## Não observadas com `RuleBasedBrain`
`STARVATION_PARADOX`, `REPETITION_LOOP`, `ACTION_REJECTION_RATE`: 0 em 10 seeds × 1000 ticks.

## Resolvidas
_(nenhuma)_

## Knowledge
_(nenhuma registrada; a arguição da etapa 1 ainda não foi feita)_
