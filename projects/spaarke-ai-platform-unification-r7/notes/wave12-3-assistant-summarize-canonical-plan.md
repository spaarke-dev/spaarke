# Wave 12.3 — Assistant Summarize: Canonical-Architecture Plan

> **Created**: 2026-07-02 (end of Wave 12 core migration session)
> **Owner**: R7 Wave 12.3 continuation
> **Non-negotiable**: canonical Linear AI Consumer architecture — no band-aids on legacy playbook path

---

## Guiding architectural rule

All AI-invoked consumer surfaces converge on ONE pattern: **Linear AI Consumer library**.

- `ConsumerType` (compile-time constant in `ConsumerTypes.*`)
- → maps to a single `sprk_analysisaction` row via `LinearConsumersOptions.ActionIds`
- → Action row carries `sprk_systemprompt` (JPS or plain text), `sprk_outputschemajson` (strict schema), `sprk_temperature`
- → Consumer service composes `IActionResolver` + `IActionRunner` + optional `IDocumentTextSource` + optional Dataverse writes + optional downstream jobs
- → Emits `AnalysisStreamChunk` SSE events for progress + result
- → Clients use shared `useLinearRunProgress` hook + `<LinearRunProgressList>` presenter

**What the pattern REPLACES / RETIRES**:
- `sprk_playbookconsumer` routing rows for migrated consumers (Phase E deactivations)
- Vector-match / candidate selector / confidence-threshold flows for explicit-intent consumers
- Playbook Engine dispatch for the same
- Client-side numbered visual step bars in favor of honest scrolling text
- Per-consumer engine bandaids (like the `PlaybookSelector` threshold-tuning path we diagnosed tonight)

---

## The three failing surfaces from tonight's UAT

Recap from the operator's smoke:

1. `/summarize` (or NL "summarize this") returns **"I couldn't find a confident match"** — engine-path failure at the `PlaybookCandidateSelector` step (thresholds too strict on dev; but tuning them is a band-aid on the wrong pattern).
2. **Playbook library modal opens but items aren't selectable** — downstream of #1; the modal has no `execute` action wired when the LLM can't score a candidate.
3. **Manual "summarize this document" opens an empty Summary tab in Workspace pane** — indicates a client-side subscription/routing gap between chat SSE and Workspace pane render. Tab opens on intent detection; content never arrives because the execution never completes.

**All three symptoms have ONE architectural root**: chat-side summarize is still on the legacy playbook + candidate-selector + engine-execution path. Consolidating onto Linear collapses all three problems into one deterministic dispatch.

---

## Scope of Wave 12.3 continuation

### A. Chat-summarize → Linear Consumer #6 (server + client)

**Server**:
1. Identify or create an Action row for chat-summarize.
   - Current playbook `44285d15-…` (`summarize-document-for-chat@v1`) has nodes referencing an Action — inspect that Action's SystemPrompt; likely reusable.
   - Populate `sprk_outputschemajson` with strict-mode schema (nullable-but-required fields; no `additionalProperties: true`).
2. Add `ConsumerTypes.ChatSummarize` to the Linear consumers list (compile-time constant already exists — verify).
3. Add App Service settings: `LinearConsumers__ActionIds__chat_summarize=<action-guid>` on `spaarke-bff-dev`.
4. Build `ChatSummarizeService` in `Services/Ai/LinearConsumers/` — composes `IActionResolver` + `IDocumentTextSource` (from uploaded file) + `IActionRunner`.
5. Refactor `SessionSummarizeOrchestrator` (or the code path where chat currently dispatches summarize) to check `LinearConsumersOptions.TryGetActionId(ConsumerTypes.ChatSummarize)` first; dispatch to `ChatSummarizeService` when configured; fall through to engine only if the setting is absent.
6. Emit SSE identical to other Linear consumers: `metadata` → `progress` chunks → `chunk` (with summary text) → `result` (with structured JSON) → `done` → `[DONE]`.
7. **Retire what's now unreachable**: `PlaybookCandidateSelector` for chat-summarize (no longer called), `sprk_playbookconsumer` chat-summarize routing row (deactivate in Phase E).

**Client**:
1. Migrate `SprkChatMessageRenderer` (and the pipeline that emits "no confident match") to a straight execution path: user types summarize + has an attached file → invoke chat-summarize endpoint → subscribe with `useLinearRunProgress` → render progress + result.
2. Remove the playbook library modal step for THIS consumer (or gate it to only fire when there's a REAL ambiguity — future feature; not for explicit-intent summarize).
3. Wire the Summary tab in the Workspace pane to subscribe to `useLinearRunProgress`'s `result` event; render the structured JSON into TL;DR / SUMMARY / KEYWORDS / ENTITIES panes.
4. Retire `/summarize` slash command OR make it purely a shortcut to the NL "summarize this file" intent. Operator's earlier decision was to move away from `/`; confirm on start.

### B. Compose-summarize → Linear Consumer #7 (Compose R1 handoff)

Compose R1 team is removing their AI functionality. When their retirement lands:
1. Add `ConsumerTypes.ComposeSummarize` App Service setting on `spaarke-bff-dev`.
2. Either reuse the chat-summarize Action row (if semantic intent identical) or create a compose-specific one.
3. Wire the Compose surface's summarize trigger to `ChatSummarizeService` (or a `ComposeSummarizeService` variant if the surface needs different SSE flow).
4. Retire Compose R1's engine-path dispatch entirely.

Coordinate scope + timing with the Compose R1 team's retirement plan. This is a NEXT-NEXT session concern (not required for Wave 12.3 chat-summarize closure).

### C. Assistant ↔ Workspace pane data flow (AC13-AC15)

Once the server-side + client-side migration in (A) lands, the remaining Assistant↔Workspace UAT is:
- **AC13**: Assistant chat in workspace context knows current matter ID
- **AC14**: Assistant responses reference matter-specific data when present (not generic)
- **AC15**: Operator-verified end-to-end UAT

These require:
1. `PageContext` / `HostContext` plumbing to include current matter/project ID in every chat session (partially done via T151/T152)
2. Chat consumer prompts that use the matter context ("what documents are attached to THIS matter?") — depends on the Action row's SystemPrompt asking the LLM to use context
3. Retrieval over SharePoint Embedded — flagged in the Wave 12 plan as potentially deferred; audit T120 output should clarify whether this is Linear-consumer-compatible or needs a separate project

Scope after (A) lands: audit T120's Assistant↔Workspace disposition list + close each gap on the Linear-migrated foundation.

### D. Retire the Playbook Engine dispatch surface for migrated consumers (Phase E expanded)

Original Phase E: deactivate 4 `sprk_analysisplaybook` rows for the 5 wizard consumers. Expanded scope after Wave 12.3:
- `44285d15-…` (`summarize-document-for-chat@v1`) — chat-summarize's current playbook, deactivate post-migration
- `47686eb1-…` (`Document Summary` — Compose R1's playbook) — deactivate when Compose R1 rewires
- Plus the original 4 (`18cf3cc8-…`, `ddaa441e-…`, `2d660cad-…`, `fc343e9c-…`)

Total Phase E target: **6 `sprk_analysisplaybook` rows deactivated + their `sprk_playbooknode` children**.

Ordered execution: deactivate only AFTER the corresponding Linear consumer smoke passes. Fully reversible; done via MCP `update_record`.

---

## Sequencing (next session)

1. **Preflight** — read this doc + [`wave12-linear-migration-tech-debt.md`](wave12-linear-migration-tech-debt.md) + [`wave12-uat-checklist-2026-07-02.md`](wave12-uat-checklist-2026-07-02.md).
2. **A.1-A.3** (server-side action row + settings) — 30 min.
3. **A.4** (`ChatSummarizeService`) — 1 hr. Mirror `FileSummarizeService`'s shape.
4. **A.5** (dispatch shim in `SessionSummarizeOrchestrator` or wherever) — 30-60 min. Depends on how tangled the current chat-summarize path is.
5. **Build + local dev deploy + operator smoke server-only** (see if the SSE returns `result` correctly, even if client doesn't render yet) — 30 min.
6. **A.6** (client migration to `useLinearRunProgress` + Workspace pane subscription) — 2-3 hrs. Coordinate with subagent same pattern as tonight's Summarize wizard migration.
7. **Full smoke** — /summarize + NL "summarize this" both work end-to-end; Summary tab renders content.
8. **Phase E incremental** — deactivate chat-summarize playbook row after smoke.
9. **Phase G** — docs update: add chat-summarize to `BUILD-A-NEW-LINEAR-AI-CONSUMER.md` + Wave 12 changelog.
10. Session end / handoff / merge-to-master.

**Estimated total**: 6-10 hrs single-session or split across 2 sessions. Compose R1 handoff is sequential after.

---

## Decisions (LOCKED — 2026-07-02, end of Wave 12 core session)

1. **Slash commands REMOVED entirely.** `/summarize` is deleted. All chat intents are natural language. Client-side slash-command parser + `SprkChatMessageRenderer` slash-command branch retired.

2. **Two-step ambiguous-intent flow KEPT** — repurposed for Linear architecture. Real-estate-agreement vs NDA analysis is the canonical example: same "analyze this contract" input, multiple valid Linear consumer Actions. Design:
   - **Explicit intent** ("summarize this document") → skip picker → direct dispatch to the appropriate Linear consumer (chat-summarize in this case).
   - **Ambiguous intent** ("analyze this contract") → LLM picks top-N candidate Actions from the Action library → chat pane renders picker → user selects → Linear consumer runs the chosen Action.
   - The existing `PlaybookCandidateSelector` code retires; replaced with an `ActionCandidateSelector` that vector-matches against Action embeddings (not playbook embeddings). Confidence thresholds still apply, but they gate WHICH Actions surface in the picker — not whether execution happens.
   - **NOT in Wave 12.3 chat-summarize scope**. Ambiguous-intent flow is a separate future consumer set. Wave 12.3 delivers the explicit-intent chat-summarize path only.

3. **Reuse the existing Action row.** Playbook `44285d15-…` (`summarize-document-for-chat@v1`) has nodes referencing an Action — inspect that Action first. Populate `sprk_outputschemajson` with strict-mode schema if empty (Phase B.5 gate applies). Don't rebuild the JPS if it's already coherent.

4. **Workspace Summary tab loading is SCHEMA-DRIVEN, not hardcoded.**
   - Tab opens with generic loading state on request start (no section labels yet).
   - As soon as the Action's `sprk_outputschemajson` is resolved (server-side or hydrated to client), the tab renders the SKELETON of the sections defined in the schema (top-level properties become section headers — e.g., `tldr` → "TL;DR", `summary` → "SUMMARY", `keywords` → "KEYWORDS", `entities` → "ENTITIES").
   - Section content populates from the SSE `result` chunk's parsed JSON.
   - **No hardcoded section list in the client.** If the Action's schema changes (add a section, remove one), the tab renders accordingly with no client change. Maker-tunable.
   - Screenshot in this session shows the desired loading skeleton with 4 sections defined — that's an example of the schema-driven render for the current chat-summarize Action; NOT a hardcoded UI.

---

## Non-goals (explicit deferrals)

- Retrieval over SharePoint Embedded (R5 territory)
- Action Engine R1 tool-use
- SprkChat Context pane pickup of `useLinearRunProgress` (documented as a Wave 12 follow-on already)
- Compose R1 rewire (sequenced AFTER chat-summarize)
- `AiProgressStepper` component consumers other than the migrated Summarize wizard (Doc Upload, PlaybookBuilder, AnalysisWorkspace still use it; separate refactor)

---

## Success criteria (Wave 12.3 close)

- User uploads file + types "summarize this" → gets a summary end-to-end
- Slash command decision honored
- Summary tab in Workspace pane renders TL;DR / SUMMARY / KEYWORDS / ENTITIES with real content
- No dependency on `PlaybookCandidateSelector` / vector match / confidence threshold for this consumer
- Old chat-summarize playbook row (`44285d15-…`) deactivated
- AC13-AC14-AC15 addressed to operator satisfaction
- One less consumer on the Playbook Engine

---

## Reference

- Linear architecture: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- Wave 12 MVP plan: [`wave12-mvp-completion-plan.md`](wave12-mvp-completion-plan.md)
- Tonight's tech debt inventory: [`wave12-linear-migration-tech-debt.md`](wave12-linear-migration-tech-debt.md)
- Full R7 delivery + UAT: [`wave12-uat-checklist-2026-07-02.md`](wave12-uat-checklist-2026-07-02.md)
- Client shared substrate follow-up: [`wave12-client-shared-progress-follow-up.md`](wave12-client-shared-progress-follow-up.md)
- Path A diagnostic findings (tonight): thresholds ConfidenceThreshold=0.85, SecondaryThreshold=0.80, no dev overrides — cause of "no confident match"

---

*Session-start prompt for next session: "continue from wave12-3-assistant-summarize-canonical-plan.md — chat-summarize Linear migration"*
