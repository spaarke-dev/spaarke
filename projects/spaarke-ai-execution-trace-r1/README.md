# spaarke-ai-execution-trace-r1 — AI Execution Trace / Observability (Seed)

> **Status**: Project seed (pre-spec) — created 2026-07-14
> **Origin**: compose-r2 UAT — owner directive: the Context pane should DEFAULT to the Execution Trace tool, Quick Start is being removed, and the trace must **log anything AI-related and the resources it uses, following industry best practices for user visibility into how the AI is running**.
> **Relationship**: shares the **Context pane** with the planned Assistant/Workspace **pane-UI** project — this project owns the trace *data + behavior*; the pane-UI project owns the pane's *visual redesign*. Coordinate; do not double-own the Context pane.

---

## Goal

Turn the Context pane's Execution Trace from a tool-call log into a **first-class AI observability surface**: a live, auditable record of everything the AI does on the user's behalf and the resources it consumes — meeting industry-standard expectations for AI transparency.

---

## Current state (grounded 2026-07-14)

**The trace surface exists and is partly live.**
- Context pane → Tools → **Execution Trace** → `ExecutionTraceWidget`; **auto-opens on Compose tab activation** (`ContextPaneController.tsx:596-603, 803`).
- **Streams live today (6 typed `context.*` events → SSE → `useContextEventBridge` → context bus, with a replay buffer + `GET /api/ai/chat/sessions/{id}/trace` backfill):** `tool_call_started`, `tool_call_completed` (duration, outcome), `knowledge_retrieved` (relevance, count), `playbook_node_executing/completed`, `decision_made`. Source: `ContextEventEmitter.cs` (`TryEmitToSse`).
- **Captured but NOT surfaced (logs/OTel counters only — `TryEmitToSse` NOT called):** the whole upload/classify pipeline — `UploadStarted` (contentType, size), `UploadClassified` (documentType, confidence, duration), `UploadManifestExtracted` (sections/tables/pages), `UploadIndexed` (chunkCount), `UploadSummarized`, `UploadPersisted`, `UploadCompleted` (`ContextEventEmitter.cs:349-467`).
- **Not captured as trace events at all (NEW work):** LLM **model name, token counts (in/out), estimated cost, per-call latency**. These exist in App Insights traces but are never emitted as `context.*` events.
- **Quick Start** = `GetStartedCardsWidget`; **"Working on:"** = the editor-selection label — both unrelated to the trace.

---

## Requirements (owner, 2026-07-14)

1. **Default the Context pane to the Execution Trace tool** (not just on Compose tabs — the standing default).
2. **Remove Quick Start** from the Context pane.
3. **Log anything AI-related** to the trace — process transparency (steps) AND resource usage.
4. **Follow industry best practices** for user visibility into how the AI runs and what it uses.

### What "industry best practices for AI visibility + resources" means here (proposed scope — confirm)
- **Process transparency (the steps):** request received → routing/decision → knowledge retrieval (sources + counts + relevance) → tool calls (name, arg-summary, outcome, duration) → LLM calls (model, purpose) → file ops (load, classify w/ type+confidence, index w/ chunk count) → gates/approvals → **errors/failures** (never silent).
- **Resource metering:** model per call, **token counts (input/output)**, **estimated cost**, latency per step + turn total. (Governance: per **ADR-015**, the trace carries identifiers/counts/metrics — NOT verbatim content; token/cost is the user's own session footprint.)
- **Provenance:** which documents / knowledge sources informed each answer (citations).
- **Status + auditability:** running/succeeded/failed per step; the trace **persists** (replay buffer + `GET /trace` already exist) so a user can review after the fact.
- **Live, not post-hoc:** stream steps as they happen (this is also the ADR-040-friendly way to give the "AI is working" feel WITHOUT token-streaming final results — see compose-r2 §ADR-037/040 discussion).

---

## Work split

**Client (`src/solutions/SpaarkeAi` + `Spaarke.AI.Widgets`):**
- Context pane defaults to Execution Trace; remove Quick Start (`ContextPaneController.tsx` / `ContextPaneMenu`).
- New trace entry types + `useContextEventBridge` discriminants for upload/classify/index + resource (model/token/cost) events.
- `ExecutionTraceWidget` rendering for the new entry types (step timeline, per-step status, resource footprint, provenance).

**BFF (`Sprk.Bff.Api` — §10 hot-path; existing-service edits, new context event types):**
- Wire the `ContextEventEmitter.Upload*` methods to `TryEmitToSse` (data already gathered).
- **New:** emit LLM `model` + `tokensIn/Out` + `cost` + `latency` as `context.*` events from the LLM call sites (`OpenAiClient` / `ActionRunner` / dispatch) — likely a per-turn resource aggregator. New `ContextSseEventDto` fields + a discriminant.
- Preserve ADR-040 (trace = identifiers/metrics, Tier-2/3 governance; no verbatim content duplicated).

---

## Coordination / sequencing
- **Shares the Context pane** with the pane-UI project → the two must not concurrently restyle/rewire the same pane. Recommend: this trace project defines the **data + entry-type contract**; the pane-UI project consumes it for visual redesign. Sequence: land trace data/contract, then (or alongside, with a frozen contract) pane-UI polish.
- Touches BFF (`ContextEventEmitter`, LLM call sites) → hot-path conflict-check vs compose-r2 (which also touches BFF). Prefer to start BFF work after compose-r2 merges, OR coordinate on `ContextEventEmitter`.

## Next steps
1. Owner confirms placement (dedicated project vs first workstream of pane-UI) + the best-practices scope above.
2. `/design-to-spec` → `/project-pipeline`.
3. Quick wins (default-to-trace, remove Quick Start) can be early tasks; the resource-metering (model/token/cost emission) is the substantive new work.

---

## Feedback log
| Date | Source | Note |
|---|---|---|
| 2026-07-14 | Owner (compose-r2 UAT) | Context pane defaults to Execution Trace; remove Quick Start; log anything AI-related; follow industry best practices for AI visibility + resource usage. |
| 2026-07-14 | compose-r2 gap-check | Trace surface exists + 6 events live; upload/classify captured-but-not-SSE'd; model/token/cost NOT captured as events (new emitter work). |
