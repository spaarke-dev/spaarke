# Task 023 notes: spaarke_get_decision_plan and spaarke_search_decision_lookup

Branch `worktree-agent-af7747abe5e2e2fdb` (from `work/spaarkeai-mcp-bff-api-r1` @ `6ce612ff0e`). Commits: `291bc0aa14` (tools, tests, ledger), `8c8ce6e5f3` (review follow-up: helper names, comment).

## What shipped

- `Api/Mcp/LegalOperations/Decisions/GetDecisionPlanTool.cs`, `SearchDecisionLookupTool.cs`, `PlanAndLookupToolSupport.cs` (shared guard, app-only meta, success builder, result shapes).
- `tests/integration/contract/Api/Mcp/PlanAndLookupToolTests.cs` (17 cases; real capability, access check, plan resolver, stamp and lookup search over the module-boundary fakes; REST routes in the same host).
- Two `McpTools` ledger entries (one contiguous block) in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`.

## Decisions (design choices the brief did not settle)

1. **`structuredContent` of the plan tool** is `{ plan, suppression, row, suppressionError?, rowError? }`. `plan` and `suppression` are byte-equal to the REST decision-plan and suppression bodies; `row` is the REST worklist row for that Signal (equality tested). The 032 shim wraps `structuredContent.plan` / `.suppression` in a `Response` unchanged.
2. **Partial failure.** A plan failure is the whole result (`isError`, the REST `reasonCode`; nothing else read, no stamp). Once the plan is returned, a failed suppression or row read does NOT fail the call: that member is null and `suppressionError` / `rowError` (`{reasonCode, message}`) say why. Reason: the wizard already opens without the dismissal count (`suppressionFailed` in `DecisionItemWizard.tsx`), and the stamp is already written, so failing the call would only make an item unopenable. An item that left the caller's lanes gives `row: null` with no error.
3. **The "full Work Item row" (013 left it to 023).** No route returns one row, and a new read path is out of scope, so the tool takes it from `IDecisionCapability.ListDecideAsync(http.User, ct)` (the caller's own worklist read, the Console lane's) and picks the Signal. Known limit: that is a whole-lane read per plan open, so plan latency now includes it (NFR-01 plan p95 ≤ 3 s is not yet measured; task 025 measures it). Follow-up if it misses: a single-Signal row read in `SignalReadService` (ontology owns that service).
4. **`readOnlyHint: true` although the plan read writes the stamp.** As the spec says; the stamp is server bookkeeping (best effort, 4 h TTL) and the host consent prompt must not fire per open.
5. **No-caller guard** runs in the tool before any capability call (`HttpContext` null or no authenticated identity: `DATAVERSE_USER_CONTEXT_REQUIRED`, via the mapper). The capability repeats it. `RunAsync` is `internal static` so a test can call it without a host (the A10 filter keeps that state unreachable over HTTP).
6. **Success `content`** is a count-only line ("Decision plan loaded: N action(s), M next step(s)." / "N candidate(s) found."): no record text (ADR-015).
7. **App-only meta** is `_meta.ui.visibility = ["app"]`, the shape the Phase 0 spike used and Copilot honoured (T3: the model never listed `spike_write_app`).
8. **Lookup inputs** `parameterCode` and `query` are optional in the schema, as on the REST route, so a missing `parameterCode` is the REST refusal (`ontology.decision.invalid_parameter`), not a schema error.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | tools/list: both app-only, readOnly, title, description ≤ 1,024 | pass | `ToolsList_ShowsBothToolsAppOnly...` |
| 2 | plan equals REST plan; stamp written | pass | `Plan_ForASignalTheCallerCanRead_EqualsTheRestPlanSuppressionAndRow_AndWritesTheStamp` (key, TTL 4 h, readable back) |
| 3 | out-of-access Signal: isError, REST reasonCode, no stamp | pass | `Plan_ForASignalOutsideTheCallersCoreRecordAccess...` (`sdap.access.deny.record_unavailable`, suppression and list never called) |
| 4 | lookup equals REST lookup | pass | `Lookup_ReturnsTheRestRoutesResults...` (3 rows) |
| 5 | unknown parameterCode isError invalid_parameter; other-matter budget and unreadable record never appear | pass | `Lookup_ForAParameterTheDecisionPlanDoesNotOffer...` (3 rows, nothing searched), `Lookup_NeverReturnsABudgetOfAnotherMatter...` |
| 6 | results < 64 KiB at 200 items; no ExternalCollaboration | pass | `EveryResult_IsUnder64KiB_At200SeededWorkItems...`, 25-candidate cap test; 021's ArchTest passes (full ArchTests 2,538 passed) |
| 7 | publish-size delta, no new HIGH CVE | pass | below |
| 8 | test scope | pass | below |

## Test scope beyond the stated contract (one line each)

- FR-10 same-instance test and no-caller test: constraints in the POML.
- Partial-failure and row-left-the-lane tests: they pin decision 2 above.
- 25-candidate cap with a size check: criterion 6 names size; the cap is what bounds the lookup result.
- Mutation checks (not kept): passing a constructed `HttpContext`, a `HasCaller` that returns true, and `["model","app"]` visibility each turned exactly the FR-10, no-caller and tools/list tests red.

## Step 9.5

- code-review (self-run over the 5 files; no agent): no F-class finding left. Fixed in review: generic test helper names (`ScriptedDecisionCapability`, `RecordingLookupSearch`, `CallerScript`) would collide with 022/024 helpers in the same namespace, renamed `PlanAndLookup*` (F2); stale "No production tool yet" comment above the ledger (F2).
- adr-check: ADR-001/052 (no background work), ADR-008 (bare `[Authorize]`, no ExternalCollaboration), ADR-010 (no DI registration, no interface), ADR-013 (no AI-internal type; guard green), ADR-015 (no logging added; count-only content), ADR-019 (every domain failure `isError` via `DecisionResultMapper` + `McpToolResults`), ADR-028 (A10 filter from 021; no app-only fallback), ADR-029 (below), ADR-038 (KEEP path `tests/integration/contract`; mocks only at module boundaries; no `Mock<HttpMessageHandler>` / `Mock<IServiceClient>`). No violation, no §6.5 path needed.
- Class search (rule 6): the repeatable pattern found is the MCP guard's false positive below; `grep` of tool bodies for the C# `is` keyword is the check, listed for the operator rather than fixed (the scanner is not this task's file).

## Found in passing (not fixed here; report to the operator)

1. **MCP guard false positive.** `McpToolViolations` reports "uses is, an AI-internal type" for any tool body containing the C# keyword `is` (e.g. `is not null`, `is { } x`), so the AI-internal-type name set evidently includes `is` (prose in `Services/Ai/**` comments read as a type declaration). 023 avoided it with `== null` / `!= null`. Failure scenario: every future tool author hits a red ArchTest for idiomatic C#. Not edited (scanner).
2. **Reflection control assumed an empty ledger** (`McpToolReflection_Controls`, ~line 795): it passed an empty text-found set for the real assembly, so the first production tool turned it red. 023 changed it to pass the tools the real source scan found. 024 made the same change; expect a textual merge conflict there and in the `McpTools` array.
3. **Malformed `signalId`** (a non-GUID string) is rejected by the SDK's argument binding before the handler runs, so it is the SDK's error, not a domain `isError` with a `reasonCode`. Not reachable from the widget (it sends GUIDs); noted for 025's conformance run.
4. **NFR-05 per-call telemetry** (tool name, latency, row count, outcome) is not added by this task (the POML constraint is only "no secrets in logs"); it needs a decision on where it lives (a shared MCP filter would serve all tools).

## Publish size and CVE (NFR-04, CLAUDE.md §10)

- Command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; RID framework-dependent linux-x64 (the csproj default); Release; `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; PDBs included.
- Both from fresh short-path worktrees (`C:\w23b` = base `6ce612ff0e`, `C:\w23h` = HEAD `8c8ce6e5f3`), removed afterwards with `Remove-WorktreeSafe.ps1`.
- Base 42,944,032 bytes (40.955 MiB); head 42,951,875 bytes (40.962 MiB); **delta +7,843 bytes (+0.007 MiB)**. Under +5 MB, 55 MB and 60 MB thresholds. `Sprk.Bff.Api.dll` 27,342,336 → 27,355,136.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages; no csproj or props change in the diff.
- Master-moved check: the measurement base is the branch parent, not current `origin/master`, because the delta of this task over its own base is what the rule isolates; the merge to master will add the others' deltas.

## Test results

- `PlanAndLookupToolTests`: 17 / 17 pass (0 before: the classes did not exist).
- `Sprk.Bff.Api.Tests` filter `Mcp|Signals|Decision`: 5,564 passed, 0 failed, 6 skipped. Re-run `Mcp|PlanAndLookup` after the rename: 89 passed.
- `Spaarke.ArchTests` full: 2,538 passed, 0 failed (`RouteAuthorizationGuardTests` alone: 101).
- Format: `bash scripts/quality/check-changed-cs-format.sh origin/master` clean (183 files). Build: 0 warnings, 0 errors.

## Owner questions

- Plan-open latency now includes a lane read (decision 3). Accept for v1 and measure in 025, or ask ontology for a single-Signal row read first?
- Should a failed suppression/row read hide the plan (all-or-nothing) instead of the partial result in decision 2? Chosen: partial, because the wizard tolerates it.

rework: none task 023
