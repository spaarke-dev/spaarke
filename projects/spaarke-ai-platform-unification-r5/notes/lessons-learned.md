# R5 Lessons Learned

> **Authored**: 2026-06-06 at R5 closeout
> **Status**: R5 closed with known limitations; R6 (architecture phase) initiated to address root causes
> **Audience**: future Spaarke contributors evaluating the R5 vertical slice + planning R6 work

R5 shipped the wire-layer plumbing for the "Summarize a Document" vertical slice end-to-end (BFF endpoint, structured-output streaming, session-files Azure Search index, PaneEventBus events, intent matcher, file holding) but the SC-18 SME walkthrough surfaced nine consecutive defect cycles whose root causes turned out to be **architectural**, not implementation. Rather than continue patching at the cycle-N layer, the project pivoted to a dedicated architecture phase (R6) that fixes the underlying contracts. R5 closes with the wire-layer working and the user-visible surface having one structural bug (renderer not schema-aware) and one structural duplication (chat-agent + workspace path both fire for `/summarize`). Both are explicitly carried into R6 Pillars 5 and 8 + 5 respectively.

This note captures what happened, what we learned, and what to carry forward.

---

## 1. The nine SC-18 cycles in one paragraph

Between 2026-06-04 and 2026-06-06 the walkthrough exposed defects in fast succession: tenant identity claim shape, upload-not-indexed wiring, Azure Search index field mismatches (twice), two-upload-paths divergence, FileList silent mutation, pdfjs v4 worker missing, misleading preview tab + missing workspace Summary tab, JSONPath-vs-schema-key mismatch. Each cycle was a single-PR fix; each fix surfaced the next gap. Cycles 1–5 merged via PRs #354/#359/#361/#362. Cycles 6–9 merged via PR #364. See [`current-task.md`](../current-task.md) SC-18 cycle log table for the per-cycle root-cause summary.

The defects were not random. They clustered into four architectural gap families.

---

## 2. The four architectural gap families R5 surfaced

### Gap A — Two upload paths, one mental model

R5 introduced server-side document promotion via `POST /api/ai/chat/sessions/{id}/documents`. R4 already had FR-07 inline attachments via `useChatFileAttachment` (paperclip → client-side extraction → `ChatAttachment.textContent`). Both paths shipped in R5 chat. They produced different session states. Users had no way to know which path their click took. The intent matcher in task 036 patched the symptom by always promoting via the server path when `/summarize` was the resolved intent — but the deeper issue is that **two paths exist by accident**, not by design.

R6 Pillar 5 (output-type / rendering destination) + Pillar 8 (command router) close this by making the path a property of the intent + node metadata, not an emergent property of which button the user pressed.

### Gap B — Chat agent persona + tool registry + playbook FK are bypassed in code

During the architecture chat we discovered that:
- The chat-agent system prompt is built by a 7-layer enrichment pipeline whose persona layer is **hardcoded** in `BuildDefaultSystemPrompt`. The `sprk_aipersona` Dataverse rows are not consulted.
- The `sprk_analysistools` Dataverse entity exists but is not read at runtime. Tool registration happens via C# class registration (`InvokeSummarizePlaybookTool` etc.).
- The orchestrator loads the action by code directly (`PlaybookExecutionEngine.GetActionByCode(...)`) and bypasses the playbook → node → action FK chain. Multi-node playbooks were the design target but the wire never followed the FK.

These were not introduced by R5 — they predate it. R5 exposed them because R5 was the first project to try to wire an end-to-end **data-driven** vertical slice (action + playbook + tool + persona all coming from data, the way R7+ feature work expects).

R6 Pillars 1, 2, 4 fix these three bypasses respectively.

### Gap C — Schema-aware rendering was implicit

The structured-output pipeline emits typed deltas with JSONPath keys (`$.tldr`, `$.summary`, `$.keywords`, `$.entities`). The widget's `SUMMARIZE_SCHEMA` declares bare top-level keys. The mismatch was caught at cycle 9 and patched in `sseToPaneEventBridge` by stripping the `$.` prefix. But the deeper issue is that the widget renderer is **string-only**: when a field's type is `string[]` or `object` (entities, keywords), the renderer concatenates the JSON fragments verbatim and the user sees `"data loss.","Organizations..."` or `organizations":["ACME"]`.

The widget needs to know each field's **type** to render appropriately (array → bulleted list, object → structured display, string → text). That's a schema-declaration responsibility that R5 never had a place to live.

R6 Pillar 5 (Playbook Output-Type / Rendering Destination) gives nodes a place to declare output schema + rendering destination, and lets renderers ask for it.

### Gap D — Workspace ↔ Assistant is one-way today

In R5 the chat agent (Path A) and the workspace structured-output stream (Path B) both fire for `/summarize`. SprkChat's `onBeforeSendMessage` is **informational only** per its contract — it cannot suppress the chat send. So the workspace path fires (deterministically via intent matcher) AND the chat agent also gets the message and responds inline. The user sees the structured output in the workspace AND a streamed agent response in the chat — two takes on the same operation.

The workspace is also a black box from the agent's perspective: the agent can write *to* a tab (via tool dispatch) but cannot read tab state. That makes "Send this to the workspace" + "Add this back to the assistant context" affordances impossible to wire correctly.

R6 Pillar 6 (Workspace State Model + Bidirectional Events) is the largest pillar — 10–12 days — because it's the foundation any serious AI-assisted application requires.

---

## 3. The R5 → R6 decision

After cycle 9 the team faced a choice: keep patching at cycle-10+, or invest in architecture. The patching cost-per-cycle was climbing (each cycle revealed the next layer; the renderer bug in particular would have required a non-trivial rewrite). The architecture work was ~6 weeks but **structurally** fixed the root causes, after which R7+ feature work becomes "design a playbook in data" not "write a tool class + orchestrator + renderer + tab."

The decision was made 2026-06-06 in the architecture chat session and is captured in [`projects/spaarke-ai-platform-unification-r6/design.md`](../../spaarke-ai-platform-unification-r6/design.md). R5 closes with the wire-layer working and the renderer + duplicate-fire bugs explicitly deferred.

---

## 4. What R5 shipped successfully

The following are on master and on Spaarke Dev, working as designed:

- `spaarke-session-files` Azure AI Search index with `tenantId` + `sessionId` filtering
- `RagSearchOptions.SessionId` parameter + `RagIndexingPipeline.IndexSessionFileAsync` for per-session writes
- `ChatSession.UploadedFiles[]` manifest in Redis hot tier
- `AnalysisChunk.delta` variant + `FieldDelta` payload (additive SSE event types per ADR-030)
- Azure OpenAI Structured Outputs wiring via `IncrementalJsonParser`
- `SessionSummarizeOrchestrator` + `POST /api/ai/chat/sessions/{id}/summarize` endpoint
- `InvokeSummarizePlaybookTool` registered on `SprkChatAgent`
- `StructuredOutputStreamWidget` (renders deltas; schema-aware rendering deferred to R6 Pillar 5)
- `WorkspaceTabManager.prependTab` + WorkspacePane Summary-tab installer + auto-focus
- FR-07 chat attachments: pdfjs v4 worker config, FileList snapshot, cross-package File forwarding
- Intent matcher (`matchIntent`) with `summarize-session` intent + extensible registry
- `executeSummarizeIntent` orchestrator (promote + stream + bridge events)
- `sseToPaneEventBridge` SSE → PaneEventBus transformer (JSONPath strip included)
- `SessionFilesCleanupJob` background `IHostedService`
- Telemetry events on the BFF (R5 task 008 — discoverable in App Insights)
- `Spaarke.AI.Widgets` PaneEventBus event-type additions (5 new on existing 4 channels)

The wire layer is sound. R6 builds on it.

---

## 5. Patterns to carry forward into R6

These are non-obvious patterns that R5 either established or revealed; capture them so R6 doesn't re-discover them.

**a) Cross-package File-ref forwarding** — when a shared-lib hook (`@spaarke/ui-components`) needs to expose an underlying `File` to a host app (`@spaarke/legal-workspace` consumers, SpaarkeAi shell) that doesn't yet exist at hook-design time, the right pattern is an **optional** field on the type contract (`AttachmentChip.file?: File`, `ChatAttachment.file?: File`). Existing consumers ignore it; hosts that need binary upload pick it up. R6 Pillar 6 (Workspace state model) will face the same shape: state must be exposed without breaking older consumers.

**b) FileList is live** — any time a hook receives `input.files` and the input is then cleared (a common UX requirement for "let user re-pick the same file"), snapshot to `Array.from(list)` BEFORE the clear. The fix is one line; the absence cost us a full diagnostic cycle.

**c) Wire-stream contract assertions** — the `$.tldr` vs `tldr` mismatch was invisible until end-to-end testing. R6 should add unit tests at the **parser ↔ widget boundary** that assert key shape compatibility. The `sseToPaneEventBridge` is the right place for these assertions (already a pure transformer with no IO).

**d) Diagnostic logs as one-shot probes** — every cycle 6–9 diagnostic was a one-shot `console.info` that found the bug, then was deleted in the closeout PR. R6 will face similar bug-hunts; the pattern is fine but commit policy is: diagnostics go in via the bug PR, come out in the same or the closeout PR. They are not part of the long-term codebase.

**e) `SprkChat.onBeforeSendMessage` is informational** — it cannot cancel the send. Any feature that needs to **replace** the chat send (e.g., the deterministic Summarize promotion in task 036) must dispatch the workspace path in parallel and accept that the chat agent will also process the message. This is the duplicate-fire root cause; R6 Pillar 8 (command router) replaces this with a parser layer that resolves intent before SprkChat ever sees it.

**f) Two-wrapper widget architecture** — R5 task 038 added a Summary tab via `WorkspaceTabManager.prependTab`. The pattern is: side-effect from a PaneEventBus event (`streaming_started`) installs the tab; the widget then renders independently. R6 Pillar 6's workspace state model formalizes this — tabs become typed, persisted, agent-readable artifacts and the install side-effect becomes a proper API. The R5 prependTab pattern is the proof-of-concept.

**g) Path A vs Path B parallelism is a smell** — when two execution paths produce equivalent state via different mechanisms, the architecture is leaking. R5 had Path A (chat agent) and Path B (deterministic workspace) for `/summarize`. R6's invariant: **one intent → one path**, chosen by the command router based on the intent's declared output-type + rendering destination.

---

## 6. What R5 deliberately deferred to R6

Per the closeout TASK-INDEX update, these R5 tasks are marked ⏭️ deferred-to-R6:

| Task | Title | R6 pillar that addresses it |
|---|---|---|
| 018 | FilePreviewContextWidget | R6 Pillar 6 (workspace state model + context-pane widgets) |
| 021 | "Summarize this only" per-file affordance | R6 Pillar 8 (command router — hash/at/slash vocabulary) |
| 022 | DocumentViewerWidget upgrade | R6 Pillar 9 (widget visibility contract — `getAgentVisibleState()`) |
| 026 | Insights two-path renderer | R6 Pillar 5 (output-type / rendering destination) |
| 027 | Insights clickable citations | R6 Pillar 5 (rendering-destination metadata) |
| 028 | Insights confidence floor badge | R6 Pillar 5 |
| 030 | Insights smoke tests + SC-18 SME walkthrough | R6 vertical-slice validation target |
| 031 | Phase 2 verification | R6 vertical-slice validation target |
| 034 | Frontend auto-trigger | Superseded by task 036 intent matcher (effectively done) |
| 035 | SC-18 re-run + signoff | R6 vertical-slice validation target |
| 037 | Context-pane execution-trace widget | R6 Pillar 6 (workspace state model) + Pillar 9 (widget visibility) |
| 040 | `/analyze` proof point | R6 Phase 4 (vertical-slice validation) |
| 041 | Get Started welcome card | Future polish; not blocking |
| 042 | Telemetry dashboards | Future polish; not blocking |
| 043 | Operator-led E2E testing | R6 Phase 4 (vertical-slice validation) |
| 044 | Lessons-learned + R6 backlog | Superseded by this file + R6 design.md |

The pattern: every deferred task either (a) needs the R6 architecture to be implementable correctly or (b) is polish whose timing is decoupled from R6.

---

## 7. What did NOT work in R5 process-wise

**a) Diagnostic logs left in shipped code** — early cycle 8/9 logs would have shipped with PR #362 if not caught. Lesson: any `console.info` with a `[Component]` prefix is a probe; treat it as a debt item until the bug it found is closed.

**b) Two-upload-path ambiguity was not surfaced at design** — R5 design.md assumed FR-07 inline attachments + R5 server-side promotion would coexist cleanly. The ambiguity (which path does a click go down?) was only visible at SC-18. Lesson: when a project adds a parallel path, the design doc must explicitly say which path each user action takes.

**c) Renderer fidelity was not test-covered** — the structured-output widget's tests covered "events arrive" but not "rendering matches the user expectation for `string[]` / `object` field types." Lesson: schema-conformance + rendering-fidelity tests belong in the widget, not the bridge.

**d) Time-to-architecture-decision was too fast in good ways and too slow in others** — once cycle 9 surfaced, the decision to invest in R6 came within hours. That was healthy. But the underlying architectural debt (hardcoded persona, ignored tool registry, bypassed FK chain) had been accumulating since R2/R3. Lesson: when a project has 9 cycles of fixes in a single SME walkthrough, the next planning cycle should include a deliberate **architecture-debt review**, not just feature scoping.

---

## 8. References

- [`projects/spaarke-ai-platform-unification-r6/design.md`](../../spaarke-ai-platform-unification-r6/design.md) — full R6 design (9 pillars, sequencing, 8 open questions, 13 appendix discussion notes from the 2026-06-06 architecture chat)
- [`projects/spaarke-ai-platform-unification-r6/README.md`](../../spaarke-ai-platform-unification-r6/README.md) — R6 landing pad
- [`current-task.md`](../current-task.md) — R5 closeout checkpoint with SC-18 cycle log table
- PRs that shipped R5: #345 (main, tasks 001–031), #354 (cycles 1–2), #359 (cycle 3), #361 (cycle 4), #362 (cycle 5 / task 036), #364 (cycles 6–9 / task 038)

---

*R5 is closed. R6 takes the wire-layer foundation and gives it an architecture worth standing on.*
