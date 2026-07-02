# Current Task State — spaarke-ai-platform-unification-r7

> **Last Updated**: 2026-07-02 (by context-handoff, mid-Wave-12 Doc Upload debugging + Linear AI Consumer decision)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Session** | R7 Wave 12 Doc Upload UAT surfaced a class of Playbook Engine interpreter bugs. Operator + I concluded: two-path architecture — Linear code for linear consumers, Playbook Engine for dynamic ones. Doc Upload is the first migration. |
| **Status** | Design approved. Docs written. Ready to execute the migration. |
| **Branch** | `work/spaarke-ai-platform-unification-r7` — HEAD is `15511117b` (last engine bandaid — to be reverted per plan) |
| **Worktree** | `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7/` |
| **Next Action** | Read the three companion docs (below). Execute Phase A of the task plan: revert engine bandaid commits. Then Phase B: build shared primitives + Doc Upload consumer service. |

---

## Three companion docs (READ IN THIS ORDER)

1. **Architecture** — [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
   The pattern: two-path classifier, shared primitives, consumer service shape, data model, coexistence guardrails.

2. **Work spec** — [`notes/wave12-linear-consumer-migration.md`](notes/wave12-linear-consumer-migration.md)
   The goal + scope, per-consumer reference info (current wire, target service, Action rows kept, Playbook rows retired), tonight's revert + preservation list, verification checklist.

3. **Task plan** — [`notes/wave12-linear-consumer-tasks.md`](notes/wave12-linear-consumer-tasks.md)
   Phase A (revert bandaids), Phase B (Doc Upload — tonight's target), Phase C (File Summarize), Phase D (Prefills), Phase E (data cleanup), Phase F (coexistence check), Phase G (docs + wrap-up). Mark tasks complete inline.

---

## Session summary (2026-07-01 into 2026-07-02)

Started with R7 Wave 12 UAT of the Doc Upload wizard. Cascading 500 errors during Update Record. I chased each surface symptom with a fix; operator stepped back and asked whether the R7 architecture was actually right for this use case.

Root-cause conclusion: **the Playbook Engine (data-driven interpreter) is over-applied to linear workflows**. Same lesson as Wave 11 Daily Briefing narrator — for `Start → LLM → deterministic steps → return` flows, a code-defined service is ~10× less code + 0 vs ~6 bug classes. Two paths coexist; each consumer sits on exactly one.

Six consumers are migrating off the engine onto the Linear pattern:

1. Document Upload / Profile Document (tonight)
2. File Summarize
3. Matter Prefill
4. Project Prefill
5. Work Assignment Prefill
6. Document Create Profile

Chat, Insight Engine, Daily Briefing narration stay on their existing paths.

The Playbook Engine (`PlaybookOrchestrationService`, node executors, template engine) remains for its rightful consumers. When Playbook Engine consumers hit the same interpreter tax in future UAT (operator flagged: summarize Assistant), we address via playbook-to-code compilation — deferred to R7 W12+ or R8.

## Commits from tonight's Doc Upload debugging cycle

Kept (correct regardless of path):

- `17f432b13` — `PlaybookLookupService` dual-path GUID + alt-key
- `d75de048b` — `AnalysisEndpoints.ExecuteAnalysis` pre-loads `DocumentContext`
- `a4cf7560d` — populate `Metadata.GraphDriveId` + `GraphItemId`
- `3eb0aacbb` — expose `{{document.*}}` at Layer 1
- `0a8d200ba` — PATCH payload diagnostic log

To be reverted in Phase A (engine bandaids the Linear path replaces):

- `4facf26ef` — metadata accessor form
- `2021028da` — metadata $filter form
- `1909b4432` — heuristic pluralization
- `15511117b` — Layer 1 nested-JSON skip

Dataverse-side patches tonight (reversible when playbook rows retire in Phase E):

- Dropped `sprk_documenttype` from Update Record fieldMappings on Doc Profile playbook
- Added Start node + modelDeploymentId to Project prefill playbook

---

## Deferred / follow-up items

- **Playbook-to-code compilation** for the remaining engine consumers (Chat, Insight Engine, summarize Assistant when we get to it). Design captured in the architecture doc §"Future: Playbook-to-code compilation."
- **R5 Doc 06** already logs the `sprk_documenttype` Choice-field coercion pattern for reference; still relevant if the field mapping pattern comes up in Doc Profile or other Linear consumers.
- **Daily Briefing narrator formal refactor to shared Linear primitives** — deferred to a follow-on cleanup. Do NOT touch during this migration.

## Rollback

If Phase B / Doc Upload conversion causes unexpected regressions:

- Revert the Phase B commits (all under `LinearConsumers/` folder + endpoint modification)
- Return to `d75de048b` + the historical revert of `1909b4432` back to the last engine-working state
- Cost: operator UAT for Doc Upload remains blocked; other engine consumers unaffected

The Linear migration itself is architecturally safe — worst case is we discover a specific primitive shape is wrong and iterate.

## Reference

- Architecture doc: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- Work spec: [`notes/wave12-linear-consumer-migration.md`](notes/wave12-linear-consumer-migration.md)
- Task plan: [`notes/wave12-linear-consumer-tasks.md`](notes/wave12-linear-consumer-tasks.md)
- Historical doc-processing architecture (soon to be split): [`docs/architecture/sdap-document-processing-architecture.md`](../../docs/architecture/sdap-document-processing-architecture.md)
- Companion pattern (Playbook Engine + Daily Briefing narrator model): [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- Wizard integration: [`docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md`](../../docs/guides/DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md)

---

*End of current-task.md. Ready for `/compact` or session pause. To resume: read Quick Recovery, then the three companion docs, then execute Phase A of the task plan.*
