# TIER-C-B Diagnostic — `SYS-Recall_Session_File` Repeated Failures in UAT

> **Authored**: 2026-06-25 (continuation of TIER-C diagnostic; separate root-cause)
> **Source**: User UAT screenshot 2026-06-25 showed `SYS-Recall_Session_File` called 7+ times in a single chat turn, each returning "document not currently present in this session"
> **Verdict**: **Not a data deployment gap.** Most likely user-mode issue (session boundary, prior-session files, or tool-selection ambiguity). Requires UAT re-test in a fresh session to confirm, then targeted fix if reproducible.

---

## What the user saw

User asked: *"Can you provide summaries for each document?"*

The LLM invoked `SYS-Recall_Session_File` 7 times with filenames like:
- `US12271583B2_FileUpload.pdf`
- `US10300547.pdf`
- `1 MRT.P0043WO01_US2024029650-IASR.pdf`
- `SEC FORM 4_2.pdf`
- `2 MRT.P0043WO01_WO2024238766-ISR-20250308-2294.pdf`
- `51414_19183531_2025-07-31_NTC.PUB.pdf`
- `SEC FORM 4_2.pdf` (again)

Each call returned the literal message: *"It appears the document 'X' is not currently present in this session."*

---

## Code path — what the recall handler does

`src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/RecallSessionFileHandler.cs:435-471`:

1. Receives `args.FileId` from LLM
2. Calls `_sessionManager.GetSessionAsync(tenantId, sessionId)` → fetches `ChatSession` record (Redis hot → Cosmos → Dataverse fallback)
3. Looks for the file: `session.UploadedFiles.FirstOrDefault(f => f.FileId == args.FileId)`
4. If not found: returns `"File 'X' is not present in this session"` ← **EXACTLY the message the user saw**

So the handler is doing its job correctly. The question is **WHY** `session.UploadedFiles[]` doesn't contain these files.

---

## Possible root causes (ranked by likelihood)

### #1 — Session boundary: files were uploaded in a DIFFERENT session

**Most likely.** Two scenarios:
- User uploaded the files in a previous chat session that was closed/cleared
- User uploaded the files via a non-chat upload path (matter document upload, drag-drop into workspace), which doesn't write to `ChatSession.UploadedFiles[]`

Code reference: The upload path that DOES write to `UploadedFiles[]` is `ChatDocumentEndpoints.cs:520-548` — this is the `/api/ai/chat/sessions/{id}/documents` endpoint. Any other upload route (matter-level, workspace drag, etc.) won't populate the chat session's manifest.

**How to test**: User starts a fresh chat session AND uploads files via the chat-attach interface in THIS session, then asks for summaries. If recall succeeds → root cause confirmed.

### #2 — LLM hallucinated filenames or used IDs from elsewhere

The LLM may have seen filenames in:
- Knowledge base search results (`DOCUMENT-SEARCH` or `KNOWLEDGE-BASE-SEARCH` tools — both deployed in spaarke-dev)
- Previous-turn chat history mentioning files
- System prompt blocks (e.g., the matter-context block lists indexed documents)

The LLM then incorrectly chose `recall_session_file` (which is session-scoped) instead of `knowledge_base_search` or similar (which is matter/index-scoped). The `args.FileId` it passed wasn't a real session-file identifier.

**Code reference**: Tool descriptions in `infra/dataverse/sprk_analysistool-recall-session-file-row.json` `sprk_promptdescription` — should explicitly warn the LLM that this is for THIS chat session's uploaded files only.

**How to test**: Look at the LLM's tool-call arguments in the UAT — was the `fileId` a Cosmos GUID or a literal filename string? If literal filename, the LLM didn't have a real fileId.

### #3 — System prompt leaking filenames from non-session sources

The per-turn system prompt includes context blocks. If a block (e.g., matter-context, workspace state, knowledge sources) lists file names without making clear which are "in the current chat session" vs "available in the matter index", the LLM may pick filenames from the wrong source.

**Code reference**: 
- `SprkChatAgentFactory.cs:346-367` — workspace state block (R6 Pillar 6a)
- `PlaybookChatContextProvider.cs:152` — uploadedFiles normalization
- System prompt assembly may include `ChatSession.UploadedFiles` AND knowledge-source results without strong differentiation

**How to test**: In the UAT failure case, log the system prompt that was sent to the LLM. Check if file names appear from sources OTHER than `UploadedFiles[]`.

### #4 — Race / cache staleness (unlikely but possible)

`UpdateSessionCacheAsync` writes to Redis hot tier with fire-and-forget Cosmos write-through. If recall fires within the Redis TTL window of a different session ID, it could get a stale view. Unlikely because session ID is request-scoped, but worth ruling out.

**How to test**: Check Redis directly during a failing UAT — query `chat-session:{tenantId}:{sessionId}` and verify `UploadedFiles[]` length matches expectations.

---

## NOT root causes (ruled out)

| Candidate | Why ruled out |
|---|---|
| `RecallSessionFileHandler` row missing from Dataverse | ✅ Deployed and Active (verified via MCP query 2026-06-25) |
| `UpdateUploadedFilesAsync` not being called | Called from `ChatDocumentEndpoints.cs:544` — code path verified |
| Session manager broken | `ChatSessionManager.GetSessionAsync` is the same path used by 50+ passing tests |
| TIER-C primary fix overlap | TIER-C primary was about workspace handlers (different tools entirely); recall handler is independent |

---

## Why this is investigation-not-fix today

The root cause is one of three user-mode/UX scenarios, all of which need UAT data to distinguish:

| If root cause is... | Fix surface | Effort |
|---|---|---|
| Session boundary (#1) | UX clarity: show user which files are "in this session" + warn if upload was in previous session | ~half day |
| LLM tool selection (#2) | Tune `sprk_promptdescription` on the recall-session-file row + DOCUMENT-SEARCH/KNOWLEDGE-BASE-SEARCH rows to better differentiate | ~2h |
| System prompt leak (#3) | Audit per-turn prompt assembly; mark non-session file sources clearly | ~half day |

None of these are blocking R6 surface completion. Document the diagnostic; defer fix until UAT-2 (after primary TIER-C fix and other surface items) confirms which scenario is reproducible.

---

## Recommended UAT test sequence (when user resumes)

1. **Fresh chat session** (hit `/new-session` or open new SpaarkeAi)
2. **Note current session ID** (browser devtools → Network → check chat session response)
3. **Upload 1-2 test files via the chat attach interface** (paperclip icon in chat — uses `/api/ai/chat/sessions/{id}/documents`)
4. **Confirm files appear in chat UI** (file chips below message input)
5. **Ask**: *"Can you summarize each file I just uploaded?"*
6. **Observe**:
   - If LLM calls `recall_session_file` with a fileId matching the uploaded files' Cosmos GUIDs → recall should SUCCEED → recall handler is working
   - If LLM calls `recall_session_file` with the filename as fileId → tool-selection issue (#2)
   - If LLM doesn't call recall at all but uses knowledge_base_search → LLM is choosing the right alternate tool
   - If recall is called and fails → log handler diagnostics, check session.UploadedFiles in Redis

The outcome of this test determines which fix surface to pursue.

---

## Open follow-up — coordinate with chat-routing-redesign-r1?

`RecallSessionFileHandler` was added by chat-routing-redesign-r1 Phase 4 task 085 (load-bearing MVP retrieval tool). The successor's `ChatSessionContinuityTests` (FR-56) verify `UploadedFiles[]` persistence at the test level. If TIER-C-B turns out to be a contract gap (e.g., upload not writing to UploadedFiles in some path), it's a successor concern, not R6.

If the diagnostic identifies the issue as `RecallSessionFileHandler` behavior (tool description, scope guard), that's also successor-owned per their wave 4 ownership. **R6's responsibility ends at deploying the rows we already deployed.**

---

## Action

- **Defer fix** until UAT re-test confirms reproducibility and identifies which scenario (#1/#2/#3)
- **Document** as TIER-C-B in R6 closeout notes; carry to successor or R7 backlog if not closed in R6 window
- **Continue surface completion sprint** with 097b / 098 / 095 / 096 — these are independent

---

*End of diagnostic. Ready to proceed with 097b.*
