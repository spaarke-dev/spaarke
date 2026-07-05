# spec.md — spaarke-ai-code-audit-r1

> **Status**: ACTIVE — Step 1 (inventory) in progress
> **Created**: 2026-07-05
> **Authority**: operator direction 2026-07-05 (recorded in
> `projects/spaarke-ai-platform-unification-r7/current-task.md` §Operator confirmations)

## 1. Objective

Produce a complete, honest inventory of all existing Spaarke AI code, classified
against the target architecture's five component categories, so that (a) §4-7 of
the canonical architecture doc can be designed against real constraints, and
(b) every existing component gets an explicit disposition (keep / refactor /
retire / new-required) instead of silent accumulation.

## 2. The five target categories (classification rubric)

Derived from `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`
v0.2.6 §3.10 (mechanisms M1-M7, dispatch Layers 0-4, two-catalog model, D1-D6):

| Category | Target-model meaning | Today's likely instances |
|---|---|---|
| **Session** | M1 session state graph — per-session store of documents, outputs (`uc_id@turn`), conversation, widget state, in-progress dispatch. Universal + automatic storage (D2). | `ChatSession` + Redis/Cosmos persistence, `ChatSessionFile.ExtractedText`, `AiSessionProvider`, session history endpoints |
| **Consumer** | Curated capability: fixed prompt, fixed output schema, disposition, chip transitions, match hints, slot schema (§3.10.7.5 12-field contract). | LinearConsumers, playbook-driven executors, consumer routing-table entries (`chat-summarize`, `matter-pre-fill`, ...), Daily Briefing narrative consumers |
| **Tool** | Typed primitive in the closed Tool catalog the LLM composes under the bounded L3 loop (§3.10.7.6 8-field contract). Reads cite; writes gate via M4. | SprkChatAgent tools (SYS-Recall_*, retrieval handlers), Dataverse MCP surface, document text sources, RAG search |
| **Dispatcher** | Layer 0 auto-composite + L1 chip / L2 Consumer NL classify / L3 tool loop / L4 refusal. Reads session context, not just utterance. | `TryDetectExplicitConsumerType` regex (flagged for retirement), CapabilityRouter, SoftSlashRouter, agent tool-loop selection, `linear_dispatch` SSE + `executeLinearDispatch.ts` (flagged), chip/suggestion wiring |
| **Manifest** | Maker-configurable declaration of Consumers + Tools + transitions + thresholds + dispositions + `on_event` bindings (§6, deferred). | Dataverse config tables (`sprk_analysisaction`, `sprk_playbook`, `sprk_playbooknode`, consumer routing config), JPS schemas, `.claude/catalogs/`, scope catalog |

Two auxiliary buckets for code that supports but isn't one of the five:

- **Widget/Output-routing** — M6 widget contract + M7 disposition routing:
  workspace widgets, SSE event plumbing, `sseToPaneEventBridge`, PaneEventBus.
- **Infra/Support** — OpenAI client, embeddings, telemetry, cost tracking,
  document extraction pipeline, background jobs.

## 3. Functional requirements

- **FR-1** Enumerate every AI-touching code surface on master (baseline) by
  subsystem: BFF server AI, client chat/widget libs, SpaarkeAi code page + wizards,
  Dataverse AI schema + JPS/playbook assets, peripheral surfaces (plugins, jobs,
  Office add-ins, daily-briefing).
- **FR-2** For every active worktree, enumerate AI-touching files in its
  merge-base diff vs master — what that branch adds/changes/deletes on the AI
  surface, and whether it is merged, in-flight, or abandoned.
- **FR-3** Classify every inventoried component against the rubric in §2
  (primary category + secondary if genuinely dual-role).
- **FR-4** Record per component: path(s), functional capability (UC-* mapping
  where evident), operational status (working / partial / dead / scaffolding),
  key dependencies, and origin project where determinable.
- **FR-5** Surface duplicate/overlapping implementations explicitly (the
  four-intent-mechanism drift is the known instance; find the others).
- **FR-6** Deliverable: `SPAARKE-AI-CODE-INVENTORY.md` in this folder —
  organized by category, with a per-worktree delta appendix.

## 4. Non-functional requirements

- **NFR-1** Read-only: no code, schema, or deployment changes.
- **NFR-2** Sub-agents return findings as text; only the main session writes
  files (CLAUDE.md §3 pattern).
- **NFR-3** Honest status labels — "working" only with evidence (recent verified
  flow, tests, or deployment); default to "partial" when unsure.
- **NFR-4** Inventory must be reconcilable: every claim carries a file path so
  Step 3 dispositions can be verified by grep.

## 4.5 Step 3 scope amendment (operator feedback, 2026-07-05)

The migration map (Step 3) carries TWO tracks, not one:
- **Track A — target alignment**: keep / extend / refactor-to-target / retire per component
  against canonical doc §4-7.
- **Track B — deadwood sweep**: explicit delete / keep-with-reason for EVERY dead-code
  register entry, regardless of relevance to the target design. Dead technical debt is
  in scope even when it is "not in the way". Stays require verification against an
  active project's written plan (e.g. Insights Engine r2/r3, Action Engine r1), not
  assumption.

Additionally, Step 2/3 must honor the objectives of the umbrella in-flight projects:
`ai-spaarke-action-engine-r1`, `ai-spaarke-insights-engine-r1/r2/r3`,
`ai-spaarke-insights-engine-widgets-r1` (see `notes/agent-findings-engine-projects.md`).

## 5. Out of scope

- §4-7 design content (Step 2, lives in the canonical doc).
- Disposition decisions (Step 3, `SPAARKE-AI-MIGRATION-MAP.md`).
- Any remediation, deletion, or refactoring.
