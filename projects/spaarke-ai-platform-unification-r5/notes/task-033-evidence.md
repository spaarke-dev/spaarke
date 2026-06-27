# Task 033 — Evidence Note (P2-CLOSEOUT-02)

> **Task**: Surface `ChatSession.UploadedFiles[]` in `PlaybookChatContextProvider` so the chat agent sees uploaded files.
> **Status**: ✅ Complete (committed; awaiting main-session push coordination with task 032).
> **Executed**: 2026-06-04 in fresh isolated worktree `c:\code_files\spaarke-wt-spaarke-ai-platform-unification-r5` on branch `work/spaarke-ai-platform-unification-r5`.
> **Parent branch commit**: `1ba5160b` (remediation plan + task POMLs + live-patched tid-claim fix in ChatDocumentEndpoints.cs — NOT touched per task scope).
> **Rigor level**: FULL (per CLAUDE.md §8 — BFF C# code change + multiple files + test additions).

---

## 1. Acceptance criteria (from task 033 POML)

| Criterion | Status | Evidence |
|---|---|---|
| `ChatContext` has additive nullable `UploadedFiles` field; existing callers unchanged | ✅ | `ChatContext.cs` new param at position 6 with `= null` default; full-suite (6228 tests) passes with zero regression |
| `PlaybookChatContextProvider` surfaces `session.UploadedFiles` on returned `ChatContext` when session has them | ✅ | Both return paths in `GetContextAsync` (no-playbook generic + playbook-resolved) forward `normalizedUploadedFiles`; verified by new unit test `GetContextAsync_SurfacesUploadedFiles_WhenSessionHasFiles` |
| `SprkChatAgentFactory` adds compact system-prompt suffix listing fileId + fileName when `context.UploadedFiles` non-empty | ✅ | `BuildSessionFilesManifestSuffix` helper + try/catch enrichment block at line ~226 in `CreateAgentAsync`; verified by `CreateAgentAsync_AppendsSessionFilesNoteToSystemPrompt_WhenUploadedFilesPresent` |
| System-prompt suffix does NOT include extracted text content (manifest only); prompt token count reasonable | ✅ | Suffix emits only fileId + fileName + count + tool-name binding; verified by `CreateAgentAsync_SystemPromptSuffixDoesNotIncludeExtractedTextContent` (asserts content-type, byte size, chunk CSV all absent from prompt) |
| All 5 new unit tests pass; existing tests unaffected | ✅ | Targeted run: 49/49 passed in 153 ms; full suite: 6228/6228 passed (111 skipped — pre-existing infrastructure skips) |
| Full unit suite passes; 0 regressions | ✅ | `dotnet test Sprk.Bff.Api.Tests -c Release` → `Failed: 0, Passed: 6228, Skipped: 111, Total: 6339, Duration: 1 m 14 s` |
| BFF publish-size delta ≤ +50 KB (logic-only change) | ✅ | 45.49 MB compressed (delta -0.16 MB vs ~45.65 MB baseline — within noise) |

All seven acceptance criteria met.

---

## 2. Files modified

### Production code (6 files)

1. **`src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatContext.cs`** — added optional nullable 6th param `IReadOnlyList<ChatSessionFile>? UploadedFiles = null`. Record-with semantics preserve full backward compatibility.
2. **`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/IChatContextProvider.cs`** — added optional nullable 6th param `IReadOnlyList<ChatSessionFile>? uploadedFiles = null` to `GetContextAsync` (before `CancellationToken`). XML doc references R5 task 033 + ADR-015.
3. **`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs`** — implementation matches interface; normalizes empty manifest to null at top of method; forwards `normalizedUploadedFiles` on BOTH return paths (no-playbook path at line ~141, playbook-resolved path at end).
4. **`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs`** — three changes:
   - Added optional nullable 11th param `IReadOnlyList<ChatSessionFile>? uploadedFiles = null` to `CreateAgentAsync` (before `CancellationToken`).
   - Forwarded to `contextProvider.GetContextAsync(...)`.
   - Inserted manifest-suffix enrichment block right after `AppendActiveCapabilities` (line ~226): when `context.UploadedFiles is { Count: > 0 } files`, append `BuildSessionFilesManifestSuffix(files)` to the system prompt via `context with`. Wrapped in try/catch (soft failure → warning log + continue).
   - Added `BuildSessionFilesManifestSuffix` private static helper (after `// === Private helpers ===` marker) implementing the manifest formatting + tool-name binding.
5. **`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/NullSprkChatAgentFactory.cs`** — matched the new `uploadedFiles` parameter on the override so the Null subclass continues to compile and throw `FeatureDisabledException` when the AI kill switch is OFF.
6. **`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs`** — updated TWO `CreateAgentAsync` call sites (SendMessage at line ~479; ApprovePlan at line ~1183) to pass `uploadedFiles: session.UploadedFiles`.
7. **`src/server/api/Sprk.Bff.Api/Api/Agent/AgentEndpoints.cs`** — updated ONE `CreateAgentAsync` call site (line ~186) to pass `uploadedFiles: session.UploadedFiles`.

### Test code (2 files)

8. **`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderTests.cs`** — added 2 tests (`GetContextAsync_SurfacesUploadedFiles_WhenSessionHasFiles`, `GetContextAsync_LeavesUploadedFilesNull_WhenSessionHasNone`). Empty manifest is treated identically to null (verified within test #2).
9. **`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs`** — TWO categories of edits:
   - Updated 7 existing Moq Setup calls to add `It.IsAny<IReadOnlyList<ChatSessionFile>?>()` for the new interface parameter (preserves existing tests unchanged behaviorally).
   - Added 3 new tests (`CreateAgentAsync_AppendsSessionFilesNoteToSystemPrompt_WhenUploadedFilesPresent`, `CreateAgentAsync_DoesNotAppendSessionFilesNote_WhenUploadedFilesEmpty`, `CreateAgentAsync_SystemPromptSuffixDoesNotIncludeExtractedTextContent`).

**Total: 9 files modified.**

Files explicitly NOT touched (per task scope): `ChatDocumentEndpoints.cs` (task 032), `ChatSessionManager.cs` (task 032), `RagIndexingPipeline.cs` (task 032), any frontend code (task 034), any `.claude/` paths (sub-agent permission boundary), the remediation plan, the task POMLs themselves.

---

## 3. Implementation notes + decisions

### Decision 1: Insertion point for manifest suffix in factory

Placed the suffix-append block **after** `AppendActiveCapabilities` rather than before it. Rationale: LLM recency bias favors instructions at the end of the system prompt; the manifest + tool-name binding is the most action-relevant context for the immediate tool-call decision, so it should be the last thing the model reads in the system prompt before user/assistant history.

### Decision 2: Empty manifest treated as null

`PlaybookChatContextProvider.GetContextAsync` normalizes an empty `uploadedFiles` collection to `null` at the top of the method (`normalizedUploadedFiles`). All downstream consumers use the single `is { Count: > 0 }` check. This keeps the factory's enrichment block simple and means callers don't need to disambiguate.

### Decision 3: Soft-failure enrichment

The manifest-suffix block in `SprkChatAgentFactory.CreateAgentAsync` is wrapped in try/catch (warning log on exception, continue without suffix). Matches the existing `AppendActiveCapabilities` pattern (line ~215-219). The chat must remain functional even if the suffix builder throws — the LLM may then decline to invoke the summarize tool until the user re-prompts, but the agent itself works.

### Decision 4: Tool-name binding literal in suffix

The suffix names the tool literally: `` invoke the `invoke_summarize_playbook` tool ``. This matches `InvokeSummarizePlaybookTool.ToolName` (R5 task 015 constant). If that constant ever changes, this suffix must change in lock-step — flagged in the helper's XML comment as a binding reference.

### Decision 5: 3 return paths or 2?

Task POML §3.1 mentioned three branches ("no-playbook generic ~line 77, standalone host-context ~line 118, main playbook-resolved path at end"). Inspection showed the "standalone host-context" path is a SUB-branch INSIDE the no-playbook block that builds a different `defaultKnowledgeScope` but falls through to the SAME `return new ChatContext(...)` at line ~141. So there are only TWO actual return statements in `GetContextAsync`, both updated. The standalone branch's `defaultKnowledgeScope` correctly propagates because it sets the variable before the shared return.

---

## 4. Quality gates

### Build verification

| Target | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` | ✅ Build succeeded — 0 Errors, 16 Warnings (all pre-existing CS0618 / CS1998 / CS8601 / CS8604 in unmodified files) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -c Release` | ✅ Build succeeded — 0 Errors, 0 Warnings |

### Test verification

| Run | Result |
|---|---|
| Targeted (`FullyQualifiedName~PlaybookChatContextProvider\|FullyQualifiedName~SprkChatAgentFactory`) | ✅ 49 passed, 0 failed, 0 skipped — 153 ms |
| Full suite (`tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`) | ✅ 6228 passed, 0 failed, 111 skipped, 6339 total — 1 m 14 s |

Pre-task brief baseline expectation: "6223+ passing, 0 failing" — we hit **6228 passing** (5 new tests added; 0 existing tests broken).

### ADR compliance check

| ADR | Status | Reason |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | Zero new DI registrations. Extended existing interface + record additively. No new top-level `services.AddXxx()`. |
| ADR-013 (BFF zone boundary) | ✅ | No injection of AI internals into CRUD code. `ChatContext` is internal to chat surface. |
| ADR-015 (AI data governance / no-leakage) | ✅ | Manifest carries `FileId` + `FileName` + count only. Test #3 (`SystemPromptSuffixDoesNotIncludeExtractedTextContent`) explicitly verifies content-type, byte size, and AI Search chunk IDs are NOT present in the prompt. Logging emits count only. |
| ADR-018 (flag scope discipline) | ✅ | No new feature flag introduced. R5 services remain unconditionally registered (per R5 CLAUDE.md §3.2). |
| ADR-019 (ProblemDetails error handling) | ✅ | No new endpoint or error code. Manifest enrichment is a soft enrichment with warning-log fallback — no user-facing error surface. |
| ADR-028 (Spaarke Auth v2) | ✅ | No new token snapshots; no auth-bearing code paths touched. The factory + provider both rely on the existing scoped-per-request resolution. |
| ADR-030 (PaneEventBus channels) | ✅ | Not applicable — no event-bus changes. |
| R5 CLAUDE.md §3.1 (reuse mandate) | ✅ | Extended existing `ChatContext` + `IChatContextProvider` + `SprkChatAgentFactory`. No new parallel components introduced. |
| R5 CLAUDE.md §3.4 (no 5th PaneEventBus channel) | ✅ | Not applicable — no event-bus changes. |

---

## 5. Publish-size verification (per CLAUDE.md §10 + R5 CLAUDE.md §3.6)

| Metric | Value |
|---|---|
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish` |
| Compression | `Compress-Archive -CompressionLevel Optimal` (PowerShell) |
| Output zip | `deploy/api-publish-task033.zip` (47,702,388 bytes) |
| Compressed size | **45.49 MB** |
| Baseline | ~45.65 MB (Phase 5 Outcome A baseline per CLAUDE.md §10) |
| Delta | **-0.16 MB** (slight decrease — within measurement noise for a logic-only change) |
| Ceiling | ≤ 60 MB compressed (binding NFR-01) |
| R5 budget | ≤ +1 MB (target +0.5 MB per R5 CLAUDE.md §3.6) |
| Escalation threshold | ≥ +5 MB single-task delta |
| **Verdict** | ✅ Well under ceiling. No escalation required. |

---

## 6. End-to-end impact (what changes on Dev after task 032 + 033 + 034 all deploy)

This task by itself does not change observable behavior unless task 032 also deploys (because `session.UploadedFiles` will remain empty until 032 wires the upload endpoint to populate it). The complete sequence:

1. User uploads file via `[action:upload]` → task 032 makes `ChatDocumentEndpoints.UploadDocumentAsync` call `IRagIndexingPipeline.IndexSessionFileAsync` + populate `ChatSession.UploadedFiles[]`.
2. User sends next message → `ChatEndpoints.SendMessage` calls `agentFactory.CreateAgentAsync(..., uploadedFiles: session.UploadedFiles, ...)` — this task wires that argument.
3. Factory passes `uploadedFiles` to `PlaybookChatContextProvider.GetContextAsync` → provider returns `ChatContext` with `UploadedFiles` surface — this task implements that.
4. Factory builds manifest suffix → appends to `context.SystemPrompt` — this task implements that.
5. LLM sees the system prompt with "Session Files: This chat session has 1 uploaded file(s) available for tool calls: contract.pdf. When the user asks to summarize, invoke the `invoke_summarize_playbook` tool with these file IDs: file-001."
6. User says "summarize" → LLM correctly invokes `invoke_summarize_playbook(fileIds=["file-001"])` → `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` runs → streaming summary renders in workspace pane (FR-01 convergence path satisfied).

Without task 032, this task is dormant (no UploadedFiles ever populated). Without this task, even with task 032 in place, the LLM has no signal that files exist and declines the tool call.

---

## 7. Out-of-scope items deferred (per task POML constraints)

- Frontend auto-trigger UX (task 034 — pattern B per remediation plan §3.3).
- The follow-up PR to promote the tid-claim fix in `ChatDocumentEndpoints.cs` (kept untouched in this task per scope; covered by remediation plan §4 "PR follow-up").
- SC-18 walkthrough re-run (task 035 — happens after 032+033+034 all deploy to Dev).

---

## 8. Commit details

| Item | Value |
|---|---|
| Branch | `work/spaarke-ai-platform-unification-r5` (worktree-local) |
| Commit hash | (filled by `git commit` step) |
| Commit message subject | `feat(r5): surface ChatSession.UploadedFiles on ChatContext + agent system prompt (task 033)` |
| Files in commit | 9 modified + 1 evidence note + 1 TASK-INDEX update = 11 files |

Push deferred per task brief: main session coordinates the push after task 032 also completes.

---

*Authored 2026-06-04 by task-execute (FULL rigor). Sources: task 033 POML; remediation plan §3.2 + §5; CLAUDE.md §10; R5 CLAUDE.md §3.1, §3.4, §3.6, §3.7.*
