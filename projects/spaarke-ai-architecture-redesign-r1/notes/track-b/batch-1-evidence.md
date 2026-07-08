# Track-B Batch 1 Evidence — DirectOpenAiAgent cluster + server dependency-free deadwood

> Task: 070 (`tasks/070-track-b-batch-1-directopenaiagent.poml`)
> Executed: 2026-07-05 · Rigor: STANDARD (per POML `rigor-hint`; cleanup/deletion task)
> Cross-checked against `notes/audit-inputs/SPAARKE-AI-CODE-INVENTORY.md` §9 (Server) before every deletion.
> No git commit performed (main session owns commits). TASK-INDEX / current-task.md not touched (parallel-wave boundary).

---

## 1. Per-item verdict table

| # | Batch item | Verdict | Detail |
|---|---|---|---|
| 1a | `Services/Ai/Chat/DirectOpenAiAgent.cs` | **DELETED** | Registered but never consumed; only refs were cluster-internal + DI line + test |
| 1b | `Services/Ai/Chat/ISprkAgent.cs` | **DELETED** | Only implementor was DirectOpenAiAgent; only registration was AiChatModule:61 |
| 1c | `Services/Ai/Chat/AgentRequest.cs` (agent DTO) | **DELETED** | Only consumers: cluster + DirectOpenAiAgentTests |
| 1d | `Services/Ai/Chat/ConversationTurn.cs` (incl. `AgentRole` enum) | **DELETED** | Only consumers: cluster + DirectOpenAiAgentTests |
| 2 | `AiChatModule.cs:61` DI registration (`AddSingleton<ISprkAgent, DirectOpenAiAgent>()`) | **DELETED (line + AIPU2-008 comment block)** | Header remarks renumbered (DI count 7 → 6) with removal note; no other line in the file touched. `Program.cs:177` comment updated to drop `ISprkAgent` mention |
| 3 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/DirectOpenAiAgentTests.cs` | **DELETED** | ADR-038 SCAFFOLDING class — test maintaining dead code. **Recorded here for task-090 /test-diet reconciliation** |
| 4 | `Services/Ai/Chat/SseEvent.cs` | **KEPT — with reason** | LIVE compile-time consumer outside the cluster: `SseOutputGuard.ValidateAndFallback` constructs and returns `SseEvent` (`Services/Ai/Chat/SseOutputGuard.cs:56,62,75`). `SseOutputGuard` is production code, DI-registered at `Infrastructure/DI/AiSafetyModule.cs:117` (AIPU2-026) with its own test suite (`SseOutputGuardTests.cs`), and is NOT on the batch-1 list — deleting SseEvent.cs would either break the build or widen the batch. Doc-comment crefs to the deleted `ISprkAgent` inside SseEvent.cs were rewritten (dependent edit) so ISprkAgent greps zero. **Recommend**: audit `SseOutputGuard` + `SseEvent` as a pair in a later Track-B batch / task 050 P4 audit — SseOutputGuard has a DI registration but no observed production injection site |
| 5 | `Services/Ai/ScopeGapDetector.cs` | **DELETED** | Zero references outside its own file (no DI registration, no consumers, no tests) |
| 6 | `Services/Jobs/DocumentVectorBackfillService.cs` (incl. `DocumentVectorBackfillOptions`) | **DELETED** | One-time migration hosted service; `Enabled` defaults false and NO appsettings anywhere set `DocumentVectorBackfill:*` → registered no-op. Dependent edits: removed `Configure<DocumentVectorBackfillOptions>` + `AddHostedService<DocumentVectorBackfillService>` from `Infrastructure/DI/JobProcessingModule.cs` (former lines 68-70); removed stale mention from `Services/Ai/RagService.cs` comment (former line 499) |
| 7 | `SummarizeInvocationPath.AgentTool` enum value | **DELETED** | No production or test writer ever passed `AgentTool` (all call sites pass `DirectEndpoint`). Removed: enum member (`SessionSummarizeOrchestrator.cs`), `ToTelemetryValue` switch arm, stale comment refs in `SessionSummarizeOrchestrator.cs` (xml-doc), `SummarizeSessionEndpoint.cs` (task-015 comment), `IPlaybookExecutionEngine.cs` (xml-doc). **Intentional survivor**: the telemetry STRING `"agent_tool"` in `Telemetry/R5SummarizeTelemetry.cs` + `R5SummarizeTelemetryTests.cs` — that is the locked telemetry `path`-dimension vocabulary (different symbol, tests exercise the raw string), untouched by design |
| 8 | `PlaybookDispatcher.RunPhaseBManifestPresentAsync` (METHOD ONLY) | **KEPT — with reason** | LIVE compile-time reference: invoked at `PlaybookDispatcher.cs:493` from the live `RunPhaseBVectorMatchAsync` (Phase-B per-file fan-out), gated on `SessionFile.ClassifiedDocType != null`. The gate is never satisfied today (no production code writes a non-null `ClassifiedDocType` — `SessionPersistenceService` only round-trips it), so the branch is unreachable *in practice*, matching inventory's "unreachable scaffolding" — but surgical method removal requires restructuring the live if/else fan-out + `manifestPresentCount` telemetry in `RunPhaseBVectorMatchAsync`, violating the batch constraint "touch nothing else in those live files". The whole `PlaybookDispatcher.cs` file deletes at P2 (task 035); deferring the method to that deletion is strictly safer than restructuring live dispatch code in a P0 parallel wave |
| 9 | `LinearConsumersOptions.ActionIds` residue | **DELETED (residue)** | The property itself was already removed in R7 Wave 12.3 — remaining residue was 5 stale doc/comment references to the nonexistent member plus the orphaned config block. Removed/rewritten: `ActionResolver.cs:14` (cref → prose), `FileSummarizeService.cs:15` + `:58` (cref → routing-table wording), `PublicContracts/ConsumerTypes.cs:90` (stale "routes via ActionIds rather than the routing table" — factually wrong post-12.3; corrected), `SessionSummarizeOrchestrator.cs:434` (`LinearConsumers:ActionIds` → routing table), `appsettings.template.json` `"ActionIds"` block (former lines 332-334) + `_comment` updated. **Intentional survivors**: `PlaybookDto.ActionIds` / `PlaybookService` / `AnalysisOrchestrationService` / `PlaybookChatContextProvider` uses of `ActionIds` — that is the live playbook N:N actions property, an unrelated same-name symbol. Also `bin/Debug/**/appsettings.template.json` (untracked build artifact, regenerated on build; excluded from grep) |
| 10 | `CompoundIntentDetector` dead assignment (LINES ONLY, 97-98) | **DELETED (lines)** | Line 97 computed `toolName` via a pointless `CallId is not null` ternary then line 98 unconditionally overwrote it. Collapsed to the single live assignment `var toolName = toolCalls[0].Name ?? string.Empty;`. File itself NOT deleted (P2 task 035 owns it) |

## 2. Grep-zero verification (SHOWN output)

Command shape: `grep -rnE "<sym>" src/ tests/ --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules` (rg unavailable in shell; GNU grep -E used).

```
=== grep: DirectOpenAiAgent (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: ISprkAgent (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<        (after SseEvent.cs doc-cref cleanup; first pass showed 2 doc-comment hits there, fixed)
=== grep: \bAgentRequest\b (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: \bConversationTurn\b (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: \bAgentRole\b (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: ScopeGapDetector (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: DocumentVectorBackfill (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: \bAgentTool\b (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: LinearConsumersOptions\.ActionIds (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
=== grep: LinearConsumers:ActionIds (src/ + tests/, excl. bin/obj) ===
>>> ZERO HITS <<<
```

Not grepped-to-zero by design (kept items / unrelated same-name symbols):
- `SseEvent` — file kept (item 4); plus live unrelated `SseEventTypes/*`, `ISseEventValidator`, `ChatSseEvent*` families.
- `RunPhaseBManifestPresentAsync` — method kept (item 8); 2 hits in `PlaybookDispatcher.cs` (definition + live call site at :493).
- Bare `ActionIds` — live `PlaybookDto.ActionIds` (playbook N:N actions) is an unrelated symbol.
- `"agent_tool"` string — locked telemetry dimension vocabulary in `R5SummarizeTelemetry` (item 7 survivor note).

## 3. Build verification (SHOWN tail)

`dotnet build src/server/api/Sprk.Bff.Api/` — GREEN:

```
    22 Warning(s)
    0 Error(s)

Time Elapsed 00:00:05.69
```

All 22 warnings pre-existing (CS86xx/CS1998/CS0618 in ChatEndpoints, AgentEndpoints, DemoExpirationService, Null* peers — none in batch-1-touched code). Note: build ran against the shared parallel-wave worktree, i.e. it also proves batch-1 deletions coexist green with the other W-P0-A agents' in-flight edits present at build time (AnalysisServicesModule, FinanceModule, ChatSession/ChatSessionManager, SessionPersistenceService etc.).

## 4. Test verification — BLOCKED by pre-existing HEAD breakage (attribution shown)

`dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "...SessionSummarizeOrchestrator|SummarizeSession|R5SummarizeTelemetry|SseOutputGuard|PlaybookExecutionEngine|PlaybookDispatcher|DirectOpenAiAgent|CompoundIntent"` fails to COMPILE with exactly 3 errors, all CS7036 and none referencing any batch-1 symbol:

```
SessionSummarizeOrchestratorTests.cs(117,57): error CS7036: There is no argument given that corresponds to the required parameter 'fileSummarizeService' of 'SessionSummarizeOrchestrator.SessionSummarizeOrchestrator(ChatSessionManager, IPlaybookOrchestrationService, IHttpContextAccessor, IPlaybookLookupService, IConsumerRoutingService, IOptions<WorkspaceOptions>, ISessionFileTextSource, FileSummarizeService, ILogger<SessionSummarizeOrchestrator>)'
SessionSummarizeOrchestrator.PathA5.IntegrationTest.cs(277,23): error CS7036: (same)
SessionSummarizeOrchestratorTests.cs(502,23): error CS7036: (same)
```

**Attribution — pre-existing at HEAD, not batch 1:**
- `git show HEAD:src/.../SessionSummarizeOrchestrator.cs` → ctor at HEAD lines 115-123 already requires `ISessionFileTextSource` + `FileSummarizeService` (9 params; R7 Wave 12.3).
- `git show HEAD:tests/.../SessionSummarizeOrchestratorTests.cs` → `CreateSut()` at HEAD passes only 7 args (no `sessionFileTextSource`, no `fileSummarizeService`).
- `git diff` on `SessionSummarizeOrchestrator.cs` (shown in transcript) = doc comments + `AgentTool` enum removal only; no ctor change. `git diff --stat -- tests/` = empty except the deleted `DirectOpenAiAgentTests.cs`.
- Because the test PROJECT does not compile at HEAD, NO test subset can execute — this predates and is independent of batch 1. Fixing `SessionSummarizeOrchestratorTests` ctor call sites is tests/**-modifying work outside the batch-1 boundary (and likely intersects the parallel r7-absorb / Null-Object work in flight).

**Escalation**: main session should either route this to the task that owns the r7 absorb (013/025) or file a defer; once the 3 ctor call sites compile, re-run the subset above. The deleted `DirectOpenAiAgentTests.cs` is definitively out of the compile set (grep-zero above proves no residue).

## 5. ADR / NFR notes

- **NFR-08**: every retirement grep-zero-verified with shown output (§2); no compat shims retained.
- **ADR-038 / task-090 /test-diet**: `DirectOpenAiAgentTests.cs` deletion = SCAFFOLDING-class removal (test of dead code) — reconcile at project close.
- **ADR-029 / NFR-01 publish size**: batch is pure deletion (7 files, −~120 net lines in live files) — expected reduction. Per-task `dotnet publish` measurement deferred to the wave-level verification in the main session: 5 other agents share this worktree concurrently, so a publish now measures the combined wave, not this batch (not attributable). No anomaly expected; flag if wave-level measure shows growth.
- **Boundary compliance**: `Models/Ai/Chat` untouched (agent 001); `Narrators` untouched (agent 007); `FinanceModule` untouched (agent 006); `PlaybookDispatcher.cs` + `CompoundIntentDetector.cs` FILES retained (P2 task 035); `.claude/` untouched; no commit/push; TASK-INDEX/current-task.md untouched.

## 6. Files changed by this batch

Deleted (git rm):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DirectOpenAiAgent.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ISprkAgent.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AgentRequest.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ConversationTurn.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeGapDetector.cs`
- `src/server/api/Sprk.Bff.Api/Services/Jobs/DocumentVectorBackfillService.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/DirectOpenAiAgentTests.cs`

Edited (dependent edits only):
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiChatModule.cs` (registration + comment block + header renumber)
- `src/server/api/Sprk.Bff.Api/Program.cs` (one comment line)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/JobProcessingModule.cs` (backfill Configure/AddHostedService pair)
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` (one comment line)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` (AgentTool enum + switch arm + docs; ActionIds doc)
- `src/server/api/Sprk.Bff.Api/Api/Ai/SummarizeSessionEndpoint.cs` (comment)
- `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookExecutionEngine.cs` (doc)
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionResolver.cs` (doc)
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/FileSummarizeService.cs` (2 docs)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs` (doc)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs` (lines 97-98 → 1 line)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEvent.cs` (kept file; ISprkAgent doc-crefs rewritten)
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` (ActionIds block removed, _comment updated)
