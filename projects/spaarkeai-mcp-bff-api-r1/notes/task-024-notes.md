# Task 024 notes — `spaarke_complete_decision` (app-only write tool)

> Run 2026-11-13, Wave 6, Sonnet at high. Branch `worktree-agent-a7654fde8e3a0012a` cut from `work/spaarkeai-mcp-bff-api-r1` at `6ce612ff0e`. Rigor FULL (bff-api, new `.cs`, data-mutation tests). Conflict-check: new files only; 022/023/030 add sibling files; the one shared file touched is `RouteAuthorizationGuardTests.Mcp.cs` (below).

## What shipped

- `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/CompleteDecisionTool.cs`: one `[McpArea("legal-operations")] [Authorize]` `McpToolDefinition`. Name `spaarke_complete_decision`, title "Complete decision", a "Use for / Not for" description (under 1,024 chars), `ReadOnly=false`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`, `_meta.ui.visibility = ["app"]`. Input: `signalId` + `review` (a `DecisionReview`). Handler `CompleteAsync` calls `IDecisionCapability.CommitAsync(accessor.HttpContext, signalId, review, ct)` and nothing else. `ToToolError(result)` null → text "Decision recorded." (or the replay sentence) plus `structuredContent` = the `DecisionCommitResult` serialized with ASP.NET web defaults (the same options the REST route writes with), so `warnings[]` and each step's `warning` arrive unchanged. Otherwise `McpToolResults.FromError(error)` (isError; a failed commit carries `partial`).
- Input binding uses `SerializerOptions = new JsonSerializerOptions(Web) { TypeInfoResolver = DefaultJsonTypeInfoResolver }`, the REST binder's options (the SDK refuses options with no resolver).
- Tests (KEEP path, `tests/integration/data-mutation/Mcp/`): `CompleteDecisionToolTests.cs` (14 cases over the real `DecisionCapability` + `DecisionCommitService` rig: it is a further `partial` of `DecisionCommitServiceTests` so it reuses the private `Rig` and #2580's billing fixture shapes; this is why its namespace is `...DataMutation.Signals`), `CompleteDecisionToolWireTests.cs` (3 cases over the real MCP transport with a scripted capability at the module boundary).
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`: one `McpTools` entry (marked `--- task 024 ---`), and the reflection control now passes the ledger's classes as "text found" instead of an empty set.

## Decisions

1. **No-caller guard stays in the capability.** The ADR-028 constraint says a null HttpContext or unauthenticated caller returns `UserContextRequired` "before any capability call"; the ADR-002 constraint says the tool adds no validation of its own and the escalation trigger says logic beyond calling the capability stops the task. Reading both: the tool passes `IHttpContextAccessor.HttpContext` (null included) and the capability's own guard (`DecisionCapability.CommitAsync`, 014) answers `UserContextRequiredCommitResult` before the service is called, mapped by `ToToolError`. Same outcome (isError, `DATAVERSE_USER_CONTEXT_REQUIRED`, nothing read or written, nothing app-only; tested with a null context and an unauthenticated one over the real service), with one owner of the guard. If the owner wants the literal pre-check in the tool, it is a three-line change; say so.
2. **Success text is a fixed sentence** (ADR-015); everything the host or widget needs is in `structuredContent`.
3. **Tests share the `Rig`** instead of a copy: a partial of `DecisionCommitServiceTests`. The wire host is a small copy of `McpTestHost`'s startup (that class is 020/021's and its capability is fixed to a probe, so it is not edited).

## Acceptance criteria

| # | Criterion | Evidence |
|---|---|---|
| 1 | listed app-only, readOnlyHint false, absent from model set | `CompleteTool_IsListedAppOnly_...` (catalog) + `ToolsList_ShowsTheCompleteTool_AppOnly_...` (tools/list on the wire: `_meta.ui.visibility == ["app"]`, annotations, input schema = signalId, review) |
| 2 | one completion → one Decision Record, Signal closed | `CompleteTool_OneCompletion_...` (one record, status Resolved; body byte-equal to the REST body) |
| 3 | same reviewId twice → first outcome, nothing new | `CompleteTool_TheSameReviewIdTwice_...` (`replayed` true, same record id, write count unchanged) |
| 4 | no plan read on a system-closed Signal → `signal_not_open`, no write | `CompleteTool_WithNoPriorPlanRead_...` |
| 5 | no Write on the subject → `ontology.decision.not_authorized`, no write | `CompleteTool_ACallerWithoutWriteOnTheSubject_...` |
| 6 | billing Signal → `decided_in_invoice_review`, no write, rights probe not asked | `CompleteTool_ABillingSignal_...` (both `sprk_invoice` and `sprk_billingevent`; the one MCP billing test) |
| 7 | no HttpContext / unauthenticated → UserContextRequired, no write | `CompleteTool_WithNoHttpContextOrNoAuthenticatedCaller_...` (both) + `..._StillCallsTheCapabilityWithNull_...` |
| 8 | because on the Decision Record | `CompleteTool_TheBecauseValue_...` (`sprk_factsnapshot.because`) |
| 9 | structuredContent unchanged incl. warnings | `CompleteTool_ABudgetInquiryWhoseArchivalFailed_...` (`inquiry_email_not_archived` in `warnings` and the step's `warning`; JSON equals the REST body for the same input) + the wire test |
| 10 | no billing / rights check under `Api/Mcp/` | Grep `billing\|IsBillingSubject\|GetCallerRights\|CallerMayDecide` under `Api/Mcp`: no hit (the words "invoice" appear only in prose/description) |
| 11 | publish size | below |
| 12 | test scope | contract + the criteria's named edges only; extras one line each: the wire tests exist because the SDK binding and `_meta` can only be proven there; the same-instance and null-context tests pin FR-10 |
| 13 | Failed commit → isError with `landed`, `possiblyLanded`, `resumable` matching REST | `CompleteTool_AFailedCommit_...` (REST `ProblemDetails` extensions compared value by value, reasonCode too) |

Before/after: the tests cannot compile without the tool (new type), so "fail before" is a build failure; mutation checks proved they bite: visibility `"model"` and a constructed `DefaultHttpContext` in place of the accessor's turned 14 of 17 red; reverted.

## Step 9.5

- **code-review** (standard; files: tool, 2 test files, ArchTests ledger). Metrics: tool 94 lines, 1 public type, 1 handler, cohesive; test files single-purpose. Direction: new files. AI smells: none (no single-impl interface, no log-rethrow, no null check on a non-nullable, comments say why). Security: `[Authorize]` class-level, policy default, no logging, no record text in results. BFF hygiene: placement = in the BFF per spec "Placement Justification"; component justification: POML `<justification>` is concrete. Findings:
  - **F2 (fixed): ArchTests reflection control assumed no production tool** (`new HashSet<string>()` as "text found"): it went red the moment any tool landed, and will for 022/023 too. Fixed (passes the ledger's classes).
  - **F2 (found in passing, reported, not fixed): the MCP source guard's AI-internal type set contains words, not just types.** `AiInternalTypeNames` takes every "type" `SourceUnit.Types` finds under `Services/Ai/**`; the guard reported "uses is, an AI-internal type" for a tool body using the `is` keyword (`x is { } y`). Probable cause (not traced in the scanner): prose in `Services/Ai/**` comments such as "this record is …" / "this class is …" is read as a declaration of a type named `is` (Grep shows dozens of such comment lines). I avoided the keyword (`!= null`). 022/023 will hit it with `is not null` / pattern matches. Guard that would fix it: restrict the extraction to declarations (skip comment text) in `SourceUnit.Types`. Out of this task's files (021's scanner): file it and tell 022/023 now.
  - **K4**: the stale comment above `McpTools` ("No production tool yet: tasks 022-024 …") is half-true until 022/023 merge; left untouched on purpose to keep the three branches mergeable.
  - **K4**: `CompleteDecisionToolWireTests.DecisionCapabilityRecorded` is oddly named (a scripted Recorded result). Left.
- **adr-check** (inline, files above): ADR-001/052 no background work: clean. ADR-002: one invariant owner, the tool adds no check, validation or write; escalation trigger not fired. ADR-008/010: no new DI registration, no new endpoint/route; discovered by attribute. ADR-013: injects only `IDecisionCapability` and `IHttpContextAccessor`; no `Services/Ai` type (ArchTest green). ADR-015: no logging; fixed text; the partial payload carries codes/ids only. ADR-019: every domain failure an isError result through `DecisionResultMapper.ToToolError` + `McpToolResults`. ADR-028: A10 filter + class `[Authorize]`; request context only; no `ICallerContext`; no app-only fallback. ADR-029: below. ADR-038: tests on `data-mutation`, no `Mock<HttpMessageHandler>`/`Mock<IServiceClient>`, boundary doubles only. No violation, no exception needed.
- **Lint/format:** `dotnet build` 0 warnings; `check-changed-cs-format.sh 6ce612ff0e`: all 4 files formatted.

## Test counts

- Task tests: 17 passed (14 `CompleteTool_*` + 3 wire), 0 failed.
- Related suites (filter `DecisionCommitServiceTests | Mcp | DecisionCapability | DecisionResultMapper | CompleteDecisionToolWireTests`, includes the `Task174_*` and invoice-review cases, the 011 baselines and the 020/021 host and A10 tests): 479 passed, 0 failed.
- `Spaarke.ArchTests`: 101/101 `RouteAuthorizationGuardTests`; the first full run had exactly 3 failures (all this task's: the `is` scan, the reflection control, and the Rule A route credit that follows from the first), 2535 others passed; after the fix those 3 pass.

## Publish size (BFF hygiene §10 / ADR-029)

Command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (csproj RID linux-x64, framework-dependent), each side from a fresh `git archive` export at a short path (`C:\wt024m`, `C:\wt024p`, `C:\wt024b`); zip `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included.

| Side | Commit | Files | Zip MB |
|---|---|---|---|
| fresh master | `942b2889bf` | 191 | 40.191 |
| task parent (project branch) | `6ce612ff0e` | 194 | 40.951 |
| this task | `f738bd0a07` | 194 | 40.953 |

- **This task's own delta: +0.002 MB** (same file count; `Sprk.Bff.Api.dll` +3.5 KB).
- Delta vs fresh master: +0.762 MB, of which ~0.76 MB and +3 files are 020's MCP SDK (unchanged by this task). Total 40.95 MB: under 55 (no review trigger) and 60 (hard stop).
- `dotnet list package --vulnerable --include-transitive`: none. No package added.

## Open questions for the owner

- Decision 1 above: keep the guard in the capability (done) or add the literal pre-check in the tool.
- The guard-scanner defect (`is`): file it; ideally fixed before 022/023 merge.

rework: fix-round-1 task 024
