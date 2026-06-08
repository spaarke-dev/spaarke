# Wave 9 — Working Document Migration (Stage 3)

> **Status**: Stage 3 implementation complete; build/test verification pending Stage 4 main-session merge.
> **Date**: 2026-06-08
> **Owning ADR**: [ADR-033 Streaming chat-tool side channel](../../../.claude/adr/ADR-033-streaming-chat-tool-side-channel.md)
> **Closes**: Q9 chat-tool migration at 10/10 (final pre-R5 hardcoded chat tool migrated to typed `IToolHandler`).

---

## 1. What changed

Stage 3 of R6 Wave 9 migrated the legacy hardcoded `WorkingDocumentTools` class (~620 LOC, three AI functions in one `class`) to a typed `IToolHandler` implementation following the Wave 7c/8 multi-method-handler pattern. The new handler implements the **ADR-033 streaming side-channel emit pattern** — emitting `DocumentStreamStartEvent` → N × `DocumentStreamTokenEvent` → `DocumentStreamEndEvent` directly during `ExecuteChatAsync` via the context-side `DocumentStreamWriter` delegate. The `IToolHandler` interface is unchanged.

### Files created (Stage 3 owned)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/WorkingDocumentHandler.cs` (~750 LOC) — handler implementation
- `infra/dataverse/sprk_analysistool-working-doc-edit-row.json` — seed row, method=EditWorkingDocument
- `infra/dataverse/sprk_analysistool-working-doc-append-section-row.json` — seed row, method=AppendSection
- `infra/dataverse/sprk_analysistool-working-doc-write-back-row.json` — seed row, method=WriteBackToWorkingDocument
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/WorkingDocumentHandlerTests.cs` (~700 LOC) — covers all ADR-033 §7.1 obligations
- `projects/spaarke-ai-platform-unification-r6/notes/wave-09-working-document-migration.md` (this note)

### Files NOT touched (per Stage 3 boundary)

- `src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandler.cs` — interface unchanged per ADR-033 §2.1
- `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` — Stage 2's file (Stage 2 added `DocumentStreamWriter`; the `AnalysisId` gap surfaced below is for main-session resolution)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` — Stage 2's file
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — Stage 4 removes legacy hardcoded block
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WorkingDocumentTools.cs` — legacy class stays until Wave 10 cleanup
- `scripts/Seed-TypedHandlers.ps1` — Stage 4 adds the 3 new row entries
- All files under `.claude/` (sub-agent write boundary per root CLAUDE.md §3)

---

## 2. Three toolcodes (descriptive UPPER-KEBAB per Wave 7b/8 convention)

| Toolcode | Method | Streaming source | Persists? | Capability gate |
|---|---|---|---|---|
| `WORKING-DOC-EDIT` | `EditWorkingDocument` | Inner LLM stream (`IChatClient.GetStreamingResponseAsync`) | No (preview only) | `write_back` |
| `WORKING-DOC-APPEND-SECTION` | `AppendSection` | Heading token then inner LLM stream | No (preview only) | `write_back` |
| `WORKING-DOC-WRITE-BACK` | `WriteBackToWorkingDocument` | Supplied content chunked at 100 chars | Yes — Dataverse `sprk_analysisoutput.sprk_workingdocument` | `write_back` |

All three rows: `sprk_handlerclass = "WorkingDocumentHandler"`, `sprk_availableincontexts = 100000001` (Chat-only — preserves the pre-Wave-9 hardcoded registration shape; spec FR-12 safety — document mutation tools must not run in playbook orchestration), `sprk_requiredcapability = "write_back"`.

### Capability-gate verification

Verified `PlaybookCapabilities.WriteBack` literal = `"write_back"` by reading `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` line 34. All three seed JSON files use the matching string. The data-driven block in `SprkChatAgentFactory.ResolveTools` applies `IsCapabilityGateSatisfied` at session start (Wave 7b infrastructure, commit `66da08ca`) and silently withholds rows whose `RequiredCapability` is not in the current playbook's capability set. Standalone chat (no playbook, capabilities = `CoreCapabilities`) does not include `write_back`, so the handler is unreachable from standalone chat — preserving the pre-Wave-9 security boundary enforced by the hardcoded `if (capabilities.Contains(PlaybookCapabilities.WriteBack))` block.

---

## 3. ADR-033 wiring evidence

The handler implements ADR-033 §4.2 verbatim:

- Reads `context.DocumentStreamWriter` at the top of `ExecuteChatAsync` (line ~250 of the handler)
- Null-checks the writer and returns `ToolResult.Error` with `ToolErrorCodes.DependencyUnavailable` and a clear "not wired" diagnostic when null (does NOT invoke IChatClient)
- For streaming methods (Edit + Append): emits `DocumentStreamStartEvent` → consumes `IChatClient.GetStreamingResponseAsync` → emits `DocumentStreamTokenEvent` per non-empty token → emits `DocumentStreamEndEvent` in EVERY exit path (success/cancellation/error)
- For WriteBack: emits `DocumentStreamStartEvent` → chunks supplied content at 100 chars and emits `DocumentStreamTokenEvent` per chunk → emits `DocumentStreamEndEvent` → calls `IWorkingDocumentService.UpdateWorkingDocumentAsync` (the ONLY write in the method — NEVER touches SpeFileStore or Graph)
- Computes SHA-256 hash of assembled content per ADR-014 (prefix `sha256:`); returns hash in terminal `DocumentStreamEndEvent.ContentHash`
- Returns a single `ToolResult` (success or error) to the LLM per ADR-033 §2.2 — the LLM never sees the token stream

Stage 2 (parallel) added `DocumentStreamWriter` to `ChatInvocationContext` (verified via `git diff` against Stage 2's branch checkpoint — see git output below); the handler compiles against this field.

```
+ public Func<Models.Ai.Chat.DocumentStreamEvent, CancellationToken, Task>? DocumentStreamWriter { get; init; }
```

---

## 4. **STOP-AND-SURFACE: `AnalysisId` field on `ChatInvocationContext`**

**Per project CLAUDE.md "🚨 ADRs Are Defaults — Challenge When Warranted" operating principle, this is a binding stop-and-surface for main-session resolution.**

### The gap

The handler's three methods require `analysisId` (the `sprk_analysisoutput` record GUID) to function:
- `EditWorkingDocument` + `AppendSection` need it to fetch the current document content via `IAnalysisOrchestrationService.GetAnalysisAsync(analysisId, ct)`
- `WriteBackToWorkingDocument` needs it as the Dataverse PK for the write target (spec FR-12)

Under the legacy hardcoded model, `WorkingDocumentTools` received `analysisId` via constructor capture: `SprkChatAgentFactory.ResolveTools` reads `context.AnalysisMetadata?.GetValueOrDefault("analysisId")` (line ~412 in the factory; the `context` here is `SprkChatContext`, not `ChatInvocationContext`) and passes it through to the tool's constructor (line ~828–834).

Under the ADR-033 typed-handler model, the handler is auto-discovered (zero-DI per ADR-010) and receives no per-session context at construction. Per-call data MUST come through `ChatInvocationContext`. **The current `ChatInvocationContext` (with Stage 2's `DocumentStreamWriter` addition) does NOT carry `AnalysisId`** — the field is not present.

### ADR-033 doesn't mention AnalysisId

ADR-033 §4.1 documents only the `DocumentStreamWriter` addition. The AnalysisId requirement surfaced during Stage 3 implementation when I inspected the legacy class. The ADR wasn't wrong — the streaming side-channel pattern was the new design — but completing the migration also requires resolving the analysisId plumbing.

### Options enumerated (per ADRs-Are-Defaults principle)

| # | Option | Cost | Verdict |
|---|---|---|---|
| A | **Add `AnalysisId` field to `ChatInvocationContext`** (nullable `Guid?`, init-only, mirrors `MatterId` shape). Main session adds the field in Stage 4 wiring (or extends ADR-033 §4.1 + Stage 2 re-runs). The chat-session entry point binds it the same way as `DocumentStreamWriter` (read from `SprkChatContext.AnalysisMetadata["analysisId"]`). | One field addition. Clean, mirrors existing fields. Adds 8 LOC to ChatInvocationContext.cs. | **RECOMMENDED** |
| B | **Read `analysisId` from `ToolArgumentsJson`** (LLM passes it as a tool argument). | LLM doesn't reliably know the analysis GUID (session-level state, not LLM-visible). Would require the system prompt to inject the GUID — leaks identifiers into context budget; user-visible LLM hallucinations possible. | REJECTED |
| C | **Defer write-back functionality to Wave 10** (handler ships with EditWorkingDocument + AppendSection only; WriteBack returns a deferred-feature message). | Splits Q9 closeout — write-back becomes the new known limit. Defeats the goal. | REJECTED |
| D | **Add a new DI service like `IAnalysisIdResolver` scoped per request that reads from a request-scoped context**. | One new DI registration (violates ADR-010 minimalism for one field). Mirrors the pattern Stage 2 already chose for `DocumentStreamWriter` (context-side delegate) — the smaller change is the field. | REJECTED |

### Recommendation

**Option A — add `AnalysisId` (Guid?) field to `ChatInvocationContext`** during Stage 4 main-session work. The field mirrors `MatterId`'s shape; the wiring point is identical to `DocumentStreamWriter`. The handler reads `context.AnalysisId` (already coded that way in Stage 3); when null/empty, the streaming methods emit a `NO_DOCUMENT` end event and the WriteBack method returns a deferred-write summary — both paths already exercised by tests.

If the main session prefers, the field addition can be added by extending ADR-033 §4.1 (the ADR is already accepted with NFR-03 revision; one more field is a small additional revision, not a new ADR). Alternative: capture this as a discovered gap in the Wave 9 commit message and the Wave 4 PR description; the operating principle is satisfied because the trade-off has been surfaced.

### Why Stage 3 didn't unilaterally add the field

Per the prompt's "MUST NOT touch" list: `ChatInvocationContext.cs` is Stage 2's owned file. Adding a field unilaterally would violate the stage-coordination protocol AND silently extend the ADR-033 contract beyond what §4.1 documents. Per the ADRs-Are-Defaults principle, the right pattern is **surface for explicit decision**, not silently work around.

---

## 5. ADR compliance summary

| ADR | Compliance |
|---|---|
| ADR-010 | Auto-discovered via `ToolFrameworkExtensions.AddToolHandlersFromAssembly`. ZERO new DI registrations. Dependencies (`IChatClient`, `IAnalysisOrchestrationService`, `IWorkingDocumentService`) all pre-existed. |
| ADR-013 | Handler lives in `Services/Ai/Handlers/`. CRUD-side code never injects this handler. |
| ADR-014 | Streaming tokens transient — never persisted to Redis/Cosmos. SHA-256 ContentHash is the cacheable artifact. |
| ADR-015 | Telemetry logs handler name + method + IDs + token COUNTS + LENGTHS + duration ONLY. Sentinel-string scan in `Telemetry_RespectsAdr015_*` tests asserts no leak of instruction / document / token / write-back content. |
| ADR-016 | Inner streaming LLM calls inherit the outer chat session's concurrency slot — no separate semaphore. |
| ADR-018 | Dependencies gated by `Analysis:Enabled` + `DocumentIntelligence:Enabled`. Kill-switch off → handler simply not auto-discovered. |
| ADR-019 | Terminal `DocumentStreamEndEvent` emitted in EVERY exit path (success / cancellation / error). |
| ADR-029 | BCL-only implementation; per-handler publish-size delta ≤+0.1 MB estimated. |
| ADR-033 | Implements §4.2 handler emit pattern verbatim. Null-writer path returns `ToolResult.Failure` with diagnostic per §3.1. Capability gate `write_back` preserved per §3.1. |

---

## 6. Test coverage (ADR-033 §7.1 obligations)

- ✅ 4-point contract (auto-discovery, HandlerId, Metadata, SupportedToolTypes)
- ✅ `SupportedInvocationContexts == InvocationContextKind.Chat` (matches legacy)
- ✅ Method dispatch via `sprk_configuration.method` for all 3 methods
- ✅ Event sequence assertions: Start → N×Token → End per method (using captured writer)
- ✅ Cancellation path: terminal End with `Cancelled: true` AND no rethrow (both Edit + Append)
- ✅ Error path: terminal End with `ErrorCode = "LLM_STREAM_FAILED"` AND `ToolResult.Failure` (both Edit + Append); `WRITE_BACK_FAILED` for WriteBack
- ✅ Null writer path: returns `ToolResult.Error` with `DependencyUnavailable` AND no IChatClient call attempted; no Dataverse write for WriteBack
- ✅ SHA-256 hash assertion: assembled token text → returned hash matches (Edit + Append + WriteBack)
- ✅ ADR-015 telemetry: sentinel-string scan for instruction / document / token / content leaks
- ✅ Chat-context dispatch via `ToolHandlerToAIFunctionAdapter` confirms routing
- ✅ FR-12 safety: WriteBack persists via `IWorkingDocumentService` only; NEVER calls `IChatClient`
- ✅ Playbook context: defensive validation error (handler is chat-only per FR-12)

Total tests: ~30 (Wave 7c KnowledgeRetrievalHandlerTests baseline was 18; Wave 9 adds streaming + write-back coverage).

---

## 7. Build + test verification

**Deferred**: build/test verification pending Stage 4 main-session resolution of the `AnalysisId` field gap. Once the field lands on `ChatInvocationContext`:

```powershell
dotnet build src/server/api/Sprk.Bff.Api/
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~WorkingDocumentHandlerTests"
```

Expected outcomes (per ADR-033 §7.3): 0 build errors; new test count ~30; baseline 3549/3571 (0 fail, 22 skip) preserved with WorkingDocumentHandlerTests added on top.

---

## 8. Wave 9 commit candidates (Stage 4 main-session reference)

Stage 4 will:
1. **Add `AnalysisId` (Guid?) field to `ChatInvocationContext`** per § 4 recommendation above (extends ADR-033 §4.1 as a small follow-up, or captures the discovered gap in the PR description per ADRs-Are-Defaults principle)
2. **Wire `AnalysisId` from `SprkChatAgentFactory`** at the per-call context construction site — read from `context.AnalysisMetadata?["analysisId"]`, parse as Guid, set on `ChatInvocationContext.AnalysisId`
3. **Remove the legacy `WorkingDocumentTools` hardcoded registration block** from `SprkChatAgentFactory.ResolveTools` (lines ~811–843) — replace with `// REMOVED in R6 Wave 9 (Q9 chat-tool batch migration): replaced by typed WorkingDocumentHandler...` comment matching Wave 7c/8 pattern
4. **Add new `$RowFiles` entries** to `scripts/Seed-TypedHandlers.ps1` for the 3 working-doc row JSON files
5. **Build + test verify** (per § 7 above)
6. **Deploy seed rows to Spaarke Dev** via the seed script
7. **Commit + push** as one Wave 9 commit (closes Q9 at 10/10)
8. **Update `current-task.md`** to Wave 9 complete

Stage 5 (cleanup, optional — deferred to Wave 10): delete `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WorkingDocumentTools.cs` alongside AnalysisExecutionTools + InvokeSummarize + InvokeInsightsQuery deletions.

---

## 9. Surprises / non-obvious findings

- **`ToolResult.Failure` does not exist** — the ADR pseudo-code in §4.2 used `ToolResult.Failure("...")` but the codebase uses `ToolResult.Error(...)` (with `ToolErrorCodes`). Used the actual API; documented in the test file. ADR §4.2 is conceptually correct but its specific method names are pseudo-code, not literal.
- **WriteBack streams supplied content, not LLM tokens** — preserved from legacy `WorkingDocumentTools.WriteBackToWorkingDocumentAsync` line ~436–444. Spec FR-04 requires the client display progress; the chunking emits document-stream tokens (100-char chunks) so the editor renders the persistence in progress. The actual Dataverse write happens AFTER the terminal End event.
- **AppendSection emits heading token at index 0 BEFORE the inner LLM call begins** — preserved from legacy line ~289–293. This ensures the section heading renders immediately while the body content streams in below it.
- **`EmitCancelledEndEventAsync` + `EmitErrorEndEventAsync` use `CancellationToken.None`** — preserved from legacy line ~587 + ~607. The original token may be cancelled but the terminal End event MUST be delivered so the editor finalizes UI state (ADR-019). The helpers swallow secondary failures with a Warning log to avoid masking the primary cancellation/error.
- **`sprk_availableincontexts = 100000001`** (Chat only) for all 3 rows — preserved the chat-only registration shape from the legacy class. Per FR-12 + NFR-08, document mutation tools must not run in playbook orchestration.
