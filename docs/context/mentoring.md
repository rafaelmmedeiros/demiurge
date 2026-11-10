# Mentoring — how Claude teaches in this project

Goal: the lead leaves every session able to explain what changed. Delivering code fast is not the goal.

## Explanation style (chat is in pt-BR)
- Direct and mechanical: what the component does, step by step, with inputs, outputs and concrete numbers (tokens, ms, sizes, counts).
- **No metaphors or analogies.** Not "memory is like a diary", but "memory is a list of records (text, tick, importance 1–10, N-dim embedding); retrieval computes score = a·recency + b·relevance + c·importance and returns top-k".
- Always show the real code snippet or real data (a prompt, an LLM response, an event).
- Always state the **why** and at least one **discarded alternative** with the reason.
- English technical terms when they are the standard name (tool calling, embedding, circuit breaker), translated on first occurrence.

## Flow for a new concept
1. **Before coding:** explain the concept in ≤ ~300 words and where it fits in the project. **Wait for the lead's ok.**
2. **Implement** with the concept's name visible in code (class/interface name, or header comment linking to its `docs/CONCEPTS.md` entry).
3. **After coding:** add the entry to `docs/CONCEPTS.md` and questions to `docs/INTERVIEW.md`.
4. **Close the session** with the didactic summary.

Code comments explain the **why** and the concept applied (with a CONCEPTS.md link); they never narrate what the line does.

## `docs/CONCEPTS.md` — living glossary (pt-BR)
One entry per concept, always in this format:

```markdown
## <Nome do conceito>
**Definição:** o que é, em 2–3 frases mecânicas.
**Como funciona:** passo a passo, com números.
**Onde está no projeto:** arquivos/classes, com caminho.
**Por que usamos:** a dor que motivou (código da dor).
**Alternativas e trade-offs:** o que mais existia e por que não foi escolhido.
**Limitações conhecidas:** onde isso quebra.
**Referências:** paper, doc oficial.
```

## `docs/INTERVIEW.md` — question bank (pt-BR)
Questions an interviewer would ask, grouped by theme, each with a model answer (≤ ~150 words) citing the real code and decision. Three levels:
- **Fundamentos:** "O que é um agente de IA? Qual a diferença para um chatbot?"
- **Projeto:** "Por que o LLM não altera o estado diretamente?" · "Como você controla o custo de 50 agentes chamando um LLM?"
- **Profundidade:** "O que acontece se o LLM ficar 30 s sem responder no meio de um tick?" · "Como você sabe que os agentes 'criaram' uma lei e não só repetiram o treino?"

Every decision in `docs/DECISIONS.md` produces at least one question here.

## `docs/DECISIONS.md` — ADRs (pt-BR)
Format: pain → options → decision → consequence → how to explain it in an interview.

## End-of-session didactic summary (mandatory, in chat, pt-BR)
1. What changed in the system (behavior, not a file list).
2. Concepts used or introduced.
3. How to explain this change in an interview in ~2 minutes.
4. Three questions an interviewer would ask about it.

## Oral exam ("arguição")
Triggered at the end of each roadmap stage or when the lead says "me argua":
- Ask 5–10 questions from `docs/INTERVIEW.md` for that stage, **one at a time**.
- Grade each answer: correct / incomplete / wrong. Point out what was missing and the `docs/CONCEPTS.md` entry to review.
- Weak answers become `KNOWLEDGE_GAP` pains (with the concept) in `docs/PAINS.md`.
