# R5 Summarize Vertical — Remediation Plan (P2 Closeout)

> **Status**: 🔴 BLOCKING Phase 2 closeout (task 030 → ❌ NOT-USABLE; task 031 cannot close).
> **Authored**: 2026-06-04 during SC-18 walkthrough on Spaarke Dev.
> **Source of finding**: [`task-030-summarize-walkthrough.md`](task-030-summarize-walkthrough.md) §6 Sev-1.
> **Triggers**: 3 new closeout tasks (032, 033, 034) + 1 follow-up PR + walkthrough re-run (035).
> **Operator decision (2026-06-04)**: fix systematically before closing Phase 2; do NOT advance to Phase 3 (tasks 040+) until end-to-end Summarize works in the SpaarkeAi shell.

---

## 1. Executive summary

R5 deployed the **complete backend** for the chat-driven Summarize vertical (orchestrator + endpoint + agent tool + telemetry + session-files index schema + indexing pipeline method) **AND** the deployed playbook (`summarize-document-for-chat@v1`, sprk_action `SUM-CHAT@v1`). The user-facing UX is **broken** because the wiring between three components was never authored:

1. **Upload endpoint → session-files index**: `ChatDocumentEndpoints.UploadDocumentAsync` writes to legacy Redis keys only; never calls R5's `RagIndexingPipeline.IndexSessionFileAsync` to populate the `spaarke-session-files` AI Search index.
2. **Upload endpoint → session manifest**: same handler never appends to `ChatSession.UploadedFiles[]`, so `SessionSummarizeOrchestrator` reads an empty list and the orchestrator declines.
3. **Chat agent → uploaded-files awareness**: `PlaybookChatContextProvider` doesn't surface `ChatSession.UploadedFiles[]` in `ChatContext`, so the agent's tool-call reasoning has no signal that files exist (verbatim agent response observed: *"I don't see the document uploaded yet"*).

All four R5 backend pieces (`IndexSessionFileAsync`, `SessionSummarizeOrchestrator`, `InvokeSummarizePlaybookTool`, `MapSummarizeSessionEndpoint`) are deployed and reachable via curl. The R5 playbook is correctly seeded in Dataverse. **Nothing actually invokes any of them in user flows** because the upload endpoint is the missing call site.

This is one root cause with three remediation steps + one UX polish step.

---

## 2. Reproduction (what the user saw on Spaarke Dev, 2026-06-04)

| Step | User action | Observed | Expected |
|---|---|---|---|
| 1 | Open `sprk_SpaarkeAi` Code Page on Dev (standalone — no entity context) | Three-pane shell loads; Workspace = Corporate Workspace; Context = Quick Start | ✅ correct |
| 2 | Type *"Let's work on summarizing a document"* | Agent: *"Great! Please upload the document…"* with `[action:upload]` link | ✅ correct (UX guides user to upload) |
| 3 | Click `[action:upload]` → pick file → POST `/api/ai/chat/sessions/{id}/documents` (multipart) | HTTP 401 — *"Tenant identity not found in token claims"* | Should be 200 + file accepted into session |
| 3a | (Pre-existing bug fixed mid-session by direct dev-patch of `ChatDocumentEndpoints.cs` + `Deploy-BffApi.ps1`) | After fix: HTTP 200; chat shows *"Document added to context"* | ✅ correct (after patch) |
| 4 | Wait for auto-summarize after upload completes | **Nothing happens.** No summary streams. Workspace pane unchanged. | Auto-summarize per operator UX expectation: file present + user already declared intent = run summary |
| 5 | Send `/summarize` (slash command via menu) | Frontend sends literal text to `POST /messages` (NOT to `/summarize` endpoint — verified Network tab). Agent: *"I don't see the document uploaded yet"* | Either path (slash or natural ask) should reach `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync`; agent should SEE the uploaded file and invoke the tool |

---

## 3. Root cause — file-by-file map

### 3.1 `ChatDocumentEndpoints.UploadDocumentAsync` ([file](../../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs))

**What it does today** (R3-era code; R5 left it untouched):
1. Validates tenant + session + file type/size (lines 142-225)
2. Reads multipart form; generates `documentId`
3. Extracts text via `ITextExtractor` → `TextExtractionResult`
4. Stores extracted text in Redis: `doc-upload:{sessionId}:{documentId}` (line 305)
5. Stores original binary in Redis: `doc-binary:{sessionId}:{documentId}` (line 341)
6. Stores metadata in Redis: `doc-upload-meta:{sessionId}:{documentId}` (line 366)
7. Returns 202 with `DocumentUploadResponse(documentId, filename, status="ready", pageCount, tokenEstimate, wasTruncated)`

**What it should also do** (R5 integration MISSING):
- Call `IRagIndexingPipeline.IndexSessionFileAsync(parsedDocument, documentId, tenantId, sessionId, speFileId, fileName, ct)` → indexes chunks into `spaarke-session-files` AI Search index → returns search document IDs (the chunk IDs that the orchestrator uses for retrieval)
- Construct a `new ChatSessionFile(documentId, filename, contentType, sizeBytes, searchDocumentIdsCsv, DateTimeOffset.UtcNow)`
- Append to `session.UploadedFiles` (cap-check at `ChatSession.MaxUploadedFiles = 20` per NFR-02; `ChatSession` is an immutable record so use `session with { UploadedFiles = updatedList }`)
- Persist updated session via `ChatSessionManager.UpdateSessionCacheAsync(updatedSession, ct)` (Redis hot path + fire-and-forget Cosmos write-through per D-06)

### 3.2 `PlaybookChatContextProvider.GetContextAsync` ([file](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs))

**What it does today** (R3-era — confirmed via grep: ZERO references to `UploadedFiles`):
- Returns `ChatContext(SystemPrompt, DocumentSummary, AnalysisMetadata, PlaybookId)`
- `DocumentSummary` reflects the **workspace document** (R3 single-doc focus) — null when no document selected
- `UploadedFiles[]` from the session manifest is never read or surfaced

**What it should also do**:
- When `session.UploadedFiles is { Count: > 0 }`, augment the agent's context with a brief surface — e.g., extend `ChatContext` with `IReadOnlyList<ChatSessionFile>? UploadedFiles` (additive, nullable, backward-compat) and have `SprkChatAgentFactory` use it when building the system prompt or initial conversation context.
- The agent then has a signal like *"This session has 1 uploaded file: contract.pdf"* → its tool-call reasoning can correctly choose `InvokeSummarizePlaybookTool` instead of declining.

### 3.3 Auto-trigger UX (operator clarification, 2026-06-04)

The user's expectation: when the conversation establishes intent *and* the file becomes available, the agent should ACT — not wait for a third user message.

**Pattern options** (pick one in task 034):
- **A. Backend system-message injection**: when upload completes, append a synthetic chat message to the session (role: `system`, content: *"File 'X' is now available in the session. If the user has expressed intent to summarize, invoke `invoke_summarize_playbook` now."*). Next agent turn sees this and acts. Most durable; consistent with chat-LLM patterns.
- **B. Frontend auto-trigger**: when `[action:upload]` POST returns 202, the chat composer auto-sends `/summarize` (or invokes `POST /api/ai/chat/sessions/{id}/summarize` directly). Simpler; UX is tighter (no LLM round-trip needed); risk is that intent might not always be "summarize" (could be other tools downstream).
- **C. Hybrid**: backend injection of an "available file" notice (no instruction), frontend auto-trigger of `/summarize` ONLY when the last user-intent message matches a Summarize trigger phrase. Cleanest UX.

Recommended for R5 Phase 2 closeout: **B** (simplest, addresses observed scenario). Migrate to **A** in R6 once the chat agent's intent model is more sophisticated and supports multi-turn workflows.

### 3.4 `/summarize` slash command routing (NOT a bug per operator clarification)

The slash command sends literal text `/summarize` to `POST /messages`. **This is correct.** The slash command is a shorthand for the natural-language intent — the agent should reason and invoke the tool. The convergence promise of FR-01 is satisfied because both `/summarize` and *"summarize this"* reach the same `InvokeSummarizePlaybookTool` (once context is fixed). Direct endpoint `POST .../summarize` is preserved for non-chat consumers (programmatic / API).

---

## 4. Resolution — task decomposition

| Task | Description | Files | Tests | Effort |
|---|---|---|---|---|
| **032** | Wire `ChatDocumentEndpoints.UploadDocumentAsync` to call `IRagIndexingPipeline.IndexSessionFileAsync` + populate `ChatSession.UploadedFiles[]` + persist via `ChatSessionManager.UpdateSessionCacheAsync` | `ChatDocumentEndpoints.cs`, `ChatSessionManager.cs` (add `AppendUploadedFileAsync` helper) | Unit + integration | 1.5h |
| **033** | Update `PlaybookChatContextProvider` to surface `ChatSession.UploadedFiles[]` in `ChatContext` so the agent sees them | `ChatContext.cs` (add additive nullable field), `PlaybookChatContextProvider.cs`, `SprkChatAgentFactory.cs` (consume new field in system prompt) | Unit | 1h |
| **034** | Auto-trigger UX on upload completion (pattern B per §3.3): when `SprkChatUploadZone` upload succeeds, automatically invoke `/summarize` IF last user message expressed summarize intent | `SprkChatUploadZone.tsx` or `SprkChat.tsx` (post-upload hook), possibly new helper `useSummarizeIntent.ts` | Frontend jest | 1h |
| **035** | Re-run SC-18 walkthrough end-to-end on Spaarke Dev after 032+033+034 deployed; capture solo-SME signoff in `task-030-summarize-walkthrough.md` §7 | Notes file only | UI smoke | 30min operator |
| **PR follow-up** | Promote the live-patched `ChatDocumentEndpoints` tid-claim fix to master via small PR (currently only deployed via manual Deploy-BffApi.ps1 — next git pull on Dev will overwrite) | `ChatDocumentEndpoints.cs` | (already validated) | 15min |

**Total effort**: ~4 hours backend + 1 hour frontend + 30 min walkthrough.

---

## 5. Acceptance criteria (validates against the `summarize-document-for-chat@v1` playbook)

Phase 2 closes ✅ when ALL of the following pass on Spaarke Dev:

1. User opens SpaarkeAi shell, says *"summarize a document"*, clicks `[action:upload]`, picks a small PDF.
2. Upload completes → HTTP 200 from `/api/ai/chat/sessions/{id}/documents`.
3. **Within 2 seconds** of upload completing, the Workspace pane shows the streaming summary indicator (no additional user message required — auto-trigger fires per task 034).
4. **TL;DR section appears first** (verifies FR-02 declaration-order streaming via `StructuredOutputStreamWidget`).
5. **Summary section appears second**; keywords + entities follow in declaration order.
6. Final state shows the complete `DocumentAnalysisResult` — no spinner, structured envelope rendered.
7. **Network tab** shows `POST /api/ai/chat/sessions/{id}/summarize` SSE stream (direct endpoint convergence path) OR `POST /messages` → tool call → orchestrator path (agent-tool convergence path) — EITHER is acceptable per FR-01 convergence promise.
8. Backend logs show:
   - `[SESSION-SUMMARIZE] Start tenant={...} session={...} fileIds={1}` ← session manifest populated
   - `[RagIndexingPipeline] Indexed session file to spaarke-session-files: documentId={...} chunkCount={N}` ← AI Search index populated
   - `[R5SummarizeTelemetry] r5.summarize.invocation path=AgentTool` (or `DirectEndpoint`) ← telemetry firing
9. User says *"summarize again with focus on dates"* → agent invokes Summarize tool a second time with the StyleHint; new stream renders in Workspace pane.
10. (Manual test) Send 21st file upload → HTTP 400 ProblemDetails with `errorCode: "summarize.too-many-files"` (NFR-02 cap defense in depth at orchestrator); session manifest does NOT grow beyond 20.

---

## 6. Out of scope for this closeout

These remain valid R6 backlog candidates (not blocking Phase 2 closure once §5 passes):
- Composer paperclip silent no-op (Sev-2 finding §6 #1 in walkthrough) — alternate UX affordance, `[action:upload]` works
- Auto-trigger pattern A or C (backend system-message injection) — pattern B is sufficient for Phase 1.5 acceptance
- Capability manifest narrow routing (current-task.md §6.5) — Layer 3 fallback works; manifest update is a polish item
- Frontend `RichFilePreview` / Context-pane uploaded-files UX surface — task 020/021 follow-on work for Phase 3

---

## 7. Dependencies + sequencing

```
032 (backend: upload wires session manifest + indexes to AI Search)
  ↓
033 (backend: agent context surfaces UploadedFiles) — independent; can run in parallel with 032
  ↓
deploy + smoke-test backend changes (Deploy-BffApi.ps1)
  ↓
034 (frontend: auto-trigger on upload completion) — needs 032+033 deployed to test
  ↓
deploy frontend (Code Page deployment)
  ↓
035 (SC-18 walkthrough re-run) — needs all of 032+033+034 deployed
  ↓
task 030 → ✅ (signoff captured)
task 031 → ✅ (Phase 2 verification closes)
Phase 2 → ✅ → unblocks Phase 3 (tasks 040+)
```

**Parallelization**: tasks 032 + 033 can be executed in parallel (different files, no shared state). Recommended: spawn 2 sub-agents per `task-execute` parallel-execution pattern.

---

## 8. References

- Walkthrough finding origin: [`task-030-summarize-walkthrough.md`](task-030-summarize-walkthrough.md) §6 (Sev-1 row)
- Deployed playbook config: [`summarize-document-for-chat.playbook.json`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json)
- R5 model fields: [`ChatSession.cs:82`](../../../src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs#L82) (UploadedFiles), [`ChatSession.cs:134`](../../../src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs#L134) (ChatSessionFile record)
- R5 orchestrator (consumer of UploadedFiles): [`SessionSummarizeOrchestrator.cs:217`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs#L217)
- R5 indexing pipeline (call needed from upload endpoint): [`RagIndexingPipeline.cs:274`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs#L274) (`IndexSessionFileAsync`)
- R5 chat agent tool registration: [`SprkChatAgentFactory.cs:856-890`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L856-L890) (gated on `PlaybookCapabilities.Summarize`)
- Upload endpoint to modify: [`ChatDocumentEndpoints.cs:124-407`](../../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs#L124-L407)
- Spec gate: [`spec.md`](../spec.md) FR-01 (convergence), FR-02 (TL;DR-first streaming), FR-08 (chat-session scope), NFR-02 (20-file cap), SC-08 (byte-identical convergence), SC-18 (SME walkthrough)

---

*Authored 2026-06-04 immediately after SC-18 walkthrough surfaced the Sev-1 integration gap. Owner: R5 project lead. Next action: execute tasks 032 + 033 + 034 via `task-execute` skill in a follow-up session.*
