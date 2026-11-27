# Task 031 — spaarke_start_analysis and spaarke_get_analysis: decisions and notes

Date: 2026-11-26. Branch: agent worktree (wave 7, to be merged into `work/spaarkeai-mcp-bff-api-r2` by the main session). Tags: bff-api, api, ai, auth, integration-test. Rigor FULL. Model opus @ high. Step mode prescriptive. House pattern: tasks 007/008 (the shared authorization method) and 025 (the tool shape).

## What changed

| Step | Commit | Files |
|---|---|---|
| 2. Baseline, written and green on the unchanged code | `9ea528887c` | `tests/integration/contract/Api/Ai/AnalysisCreateContractBaselineTests.cs` (new) |
| 3. The extraction | `f2715fd8b5` | `Api/Filters/AnalysisAuthorizationFilter.cs`, `Api/Filters/FinanceAuthorizationFilter.cs`, `Api/Ai/AnalysisEndpoints.cs`, `Api/Ai/AnalysisCallerResults.cs` (new), `tests/Spaarke.ArchTests/RouteAuthorizedLookupsCallSiteTests.cs`, the baseline (line-ending fix only, see D8) |
| 4. The tools | (this commit) | `Api/Mcp/LegalOperations/Analysis/StartAnalysisTool.cs`, `GetAnalysisTool.cs` (new); `Api/Mcp/McpToolAuthorization.cs` (O-14 narrowed, D3); ledger rows `RouteAuthorizationGuardTests.Mcp.Analysis.cs`; both tool-set pins; `McpAuthTests` shapes; `tests/integration/contract/Api/Mcp/Analysis/AnalysisToolsContractTests.cs`, `tests/integration/auth/Mcp/Analysis/AnalysisToolsAuthTests.cs` (new) |

- **The create decision is one method.** `AnalysisAuthorizationFilter` keeps its registration. Its create checks moved into one private `DecideCreateAsync`, which returns a typed `AnalysisCreateAuthorization` (`Invalid` / `Refused` / `Allowed`, with the authorised playbook and document ids). The filter renders the decision exactly as before. `AuthorizeAnalysisCreateAsync(HttpContext?, CreateAnalysisRequest?, ct)` is the public method a non-route host calls; it adds only the caller check (`UserContextRequired`). The evaluation goes through `FinanceAuthorizationFilter.DecideDeclaredChecksAsync` (new, internal), a thin wrapper over the evaluator's private `DecideAsync`, in the 008 pattern.
- **The create is one method.** The handler's `RecordCreationService.CreateAsync` call moved into `AnalysisEndpoints.CreateAnalysisRecordAsync`. Exactly two callers feed it: the route handler and `StartAnalysisAsCallerAsync`. The ArchTest `RouteAuthorizedLookupsCallSiteTests.OnlyTheCreateDecisionFeedsTheSharedCreate` pins them, so `RouteAuthorizedLookups` is set only from the create decision's answer.
- **The caller-facing methods** (ADR-013 Path C, D-022/D-024): `AnalysisEndpoints.StartAnalysisAsCallerAsync`, `ReadAnalysisAsCallerAsync` and `AnalysisWorkspaceLink`, with result types in `AnalysisCallerResults.cs`. The tools name no `Services/Ai` type.

## Outcome to tool answer

`spaarke_start_analysis`:

| Case | REST `POST /api/ai/analysis/create` (frozen) | Tool |
|---|---|---|
| No signed-in caller | 401 (`RequireAuthorization`) | `isError` `DATAVERSE_USER_CONTEXT_REQUIRED`, 401. Nothing is read. |
| Bad arguments (closed set, GUIDs, key length ≤ 200) | 400 | `isError` `mcp.invalid_argument` or `invalid_id`, 400 |
| Route 400 (the decision's `Invalid`) | 400 with the detail | `isError` `mcp.invalid_argument`, 400, the route's fixed detail |
| No Create privilege | 403 `sdap.access.deny.insufficient_privilege` | `isError`, the same code, 403. Nothing written. |
| Document without Read + AppendTo (`analysis.attach`) / unusable playbook | 403 `sdap.access.deny.insufficient_rights` | the same code, 403. Nothing written. |
| Fault while deciding | 403 `sdap.access.error.system_failure` | the same code, 403. No exception text. |
| Owner unresolved (task 146) | 409 (or 403/503) with the owner reasonCode | the same code and status, fixed text (the route's detail is not copied) |
| Create fault / no decision | 500, no code | `isError` `analysis.create_failed`, 500 (new code, D4) |
| Created | 201 + `{analysisId}` | `{ analysisId, documentId, name, link, replayed:false, warningCount }` |
| Same key and same inputs again | — | the first analysis, `replayed: true`. No second create. |
| Same key, different inputs | — | `isError` `mcp.idempotency_key_reused`, 409 |
| Same key while the first start is in flight | — | `isError` `mcp.idempotency_in_progress`, 409 ("try again shortly") |

`spaarke_get_analysis`: the route's read rule (`IsAnalysisReadableAsync`), then the handler's read. Unreadable, hidden, absent and deleted-between-check-and-read all give one byte-identical `isError` `sdap.access.deny.record_unavailable`, 404 (`DocumentToolSupport.NotFound`). A fault gives `analysis.get_failed`, 500, with fixed text.

## Decisions and deviations

- **D1: no 404 in the create baseline (deviation from the POML's "201/400/403/404").** No branch of the filter or the handler answers 404 on `POST /api/ai/analysis/create`. The baseline freezes the statuses the route does answer: 201 (Location, body and the written row), two 400s, four 403s (privilege, attach, private playbook, declaration fault with no exception text) and the 409 owner refusal.
- **D2: the escalation trigger did not fire.** The create check was exposed without changing REST bytes: the baseline passes unchanged after the extraction, and so do `AnalysisEndpointsAuthorizationContractTests` and `SecureChildOwnershipEndpointTests`.
- **D3: O-14 narrowed in `McpToolAuthorization.ValidateTool` (cross-task: 030 and 036 need the same).** Before: every non-read-only tool had to be app-only, so a model-callable write refused startup. That rule is r1's O-14, superseded for reversible writes by spec NFR-06 and the owner's 2026-11-25 decision (spec Owner Clarifications; project CLAUDE.md). After: a non-read-only tool passes when it is app-only, OR when it declares a reversible write: `Destructive = false` and `Idempotent = true` set explicitly, and a required `idempotencyKey` property of type `string` in its input schema (`McpToolAuthorization.IdempotencyKeyArgument`). Anything else still refuses startup. `McpAuthTests` adds two must-not-fire shapes (model-visible, and model+app) and six must-fire shapes (no key, key optional, key not a string, destructive, not idempotent, hints unset). The four existing O-14 refusals still fire. The rule checks the declaration; each such tool's own tests prove it honours the key. **Coordination:** tasks 030 (`spaarke_run_playbook`) and 036 (task writes) rely on the same rule. If either branch also edits this method, the main session merges one rule, not two.
- **D4: new reasonCodes.** `analysis.create_failed` and `analysis.get_failed` are for the cases where the route answers a bare 500 with no code (the 025 pattern: `events.get_failed`). `mcp.idempotency_key_reused` and `mcp.idempotency_in_progress` are this host's own, like `mcp.invalid_argument`.
- **D5: retry key store = `ITenantCache` (no new store; the second escalation trigger did not fire).** The key is per tenant (`tid`) and per caller (oid + SHA-256 of the key; the key itself is never stored or logged). The entry holds the analysis id, the document id and a SHA-256 fingerprint of document, playbook and name, for 24 h. Only a created analysis is stored, so a refused start can be retried with the same key once the caller may start it (tested). The in-flight lock is `IdempotencyFilter`'s get-then-set with a 2-minute TTL. A cache fault fails open, as in `IdempotencyFilter`. §11 answers: **Existing**: `ITenantCache`, `IdempotencyFilter`. **Extension**: the tool cannot use `IdempotencyFilter`, which is an endpoint filter bound to HTTP POST and the `X-Idempotency-Key` header; it reuses the same store and the same lock pattern. **Cost of doing nothing**: a model retry creates a second analysis, which fails NFR-06 and the acceptance criterion.
- **D6: a replay returns the stored id without asking the decision again.** The entry is the caller's own (keyed by their oid) and holds only the id they were already given. Reading or running the analysis still goes through the read rule.
- **D7: the deep link** is `HandoffUrlBuilder.BuildAnalysisWorkspaceUrl(analysisId, documentId)` on `DecisionsTextSummary.ConsoleBaseUrl(Dataverse:EnvironmentUrl)`, reached through `AnalysisEndpoints.AnalysisWorkspaceLink` (Path C). With no https environment URL there is no link.
- **D8: the baseline's row literal compares with LF line ends.** After the first commit, git checked the test file out with CRLF (`core.autocrlf`), and `RowOf` joins with `\n`. The expected text is the same; only the comparison normalises line ends. No REST behaviour changed.
- **D9: get output (ADR-015).** The result holds id, status, document id and name, action name, start and completion times (UTC), a summary of at most 500 characters (the final output, else the working document, on one line) and the link. It carries no chat history and no token usage (tested). `AnalysisDetailResult` has no analysis name, so the get result has none.
- **D10: `ArchTest` allow-list.** `RouteAuthorizedLookupsCallSiteTests.SetterAllowList` now names `CreateAnalysisRecordAsync` instead of `CreateAnalysis`, because the setter moved with the create. The new fact pins its callers.

## Known limits (K-class; not defects on a real path unless noted)

- **K1: the in-flight lock is not atomic** (get-then-set; `ITenantCache` has no set-if-absent). Two calls with the same key arriving within milliseconds can both pass the lock and create two analyses. `IdempotencyFilter` has the same window. Filed as D-031-1 in `defer-issues.md`.
- **K2: `IsAnalysisReadableAsync` uses `HttpContext.RequestAborted`, not the tool's cancellation token.** On the MCP host these are the same request, so no behaviour differs.
- **K3: a cache fault fails open** (D5), so during a Redis outage a retry can create a second analysis. This is the existing `IdempotencyFilter` behaviour, chosen for availability.

## Verification

- **Baseline:** 8 of 8 pass before the extraction (commit `9ea528887c`) and after it.
- **New tests:** 46 pass. That count covers the 8 contract and 10 auth tests for the tools, 25 `ValidateTool` shapes, and both tool-set pins.
- **Wider filter:** `Mcp | Api.Ai.Analysis | AnalysisCreateContractBaseline | AnalysisAuthorization | SecureChildOwnership` gives 1,591 passed, 1 skipped, 0 failed.
- **ArchTests:** 2,859 of 2,859 pass.
- **Fails without the change:**
  - The pre-031 O-14 rule, restored with only the constant added: 4 tests fail. Those are the two reversible-write accept shapes and both real-host pins, which fail because the host refuses to start.
  - The retry read skipped: 2 tests fail (the retry and key-reuse tests).
  - Both files were restored, and the suites were re-run green.
- **Conformance:** `bash tests/integration/Mcp.Conformance.Host/run-conformance.sh` with the widget built exits 0: 92 checks passed, 38 failed, all in the baseline ("Baseline check passed: all failures are expected").
- **Build:** the BFF builds with `-warnaserror`: 0 warnings, 0 errors.
- **Format:** `check-changed-cs-format.sh origin/master` reports all 147 files formatted.
- **CVE:** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api` finds no vulnerable packages. This task adds no package.
- **Publish size** (ADR-029): `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish`, zipped with `Compress-Archive -CompressionLevel Optimal`. Each tree came from `git archive` into a fresh short path, all measured the same day.

  | Build | Compressed | Folder | Files |
  |---|---|---|---|
  | master `152397ab11` | 43,635,704 bytes (41.61 MB) | 144,609,263 | 194 |
  | base `35e0f1529b` | 43,228,409 bytes (41.23 MB) | 143,580,939 | 186 |
  | this task `44ad7a9bd4` | 43,244,775 bytes (41.24 MB) | 143,640,339 | 186 |

  - The task's delta against its base is **+16,366 bytes compressed**, all of it in `Sprk.Bff.Api.dll` (+51,200 bytes uncompressed).
  - Against master the total is 390,929 bytes smaller. That gap is master's newer content that the project branch has not merged yet; it is not this task.
  - The total is far under the 55 MB review line.

## Verifier fixes (two review passes)

Where this section and an earlier one disagree, this section wins: D5, D6 and D9 describe the first build.

| # | Finding | Fix | Test (fails before, passes after) |
|---|---|---|---|
| 1 (F1) | `spaarke_get_analysis` said "Completed" for a row that was never run. The route's loader (`AnalysisDocumentLoader.GetOrReloadFromDataverseAsync`, uncached read) sets `Status = "Completed"` for every row, `DocumentName` = the analysis name and `CompletedOn` = `ModifiedOn`, and caches that for 2 h. | **Choice: the fields are dropped, not mapped.** The tool cannot read a real status through an API-layer seam: nothing in the server writes a status to `sprk_analysis` after the create (`statuscode` stays 1, Active; `CreateAnalysisRecordAsync` and `CreateAnalysisAsync` both write 1), and the repo defines no Draft / In progress / Completed / Failed value for it. So `status`, `completedOn` and `documentName` are gone from `structuredContent` and from the text, the description says the tool does not report whether the analysis has run, and the text says "Its status is not reported here: open it to see whether it has run." `startedOn` stays: it is the row's creation time, which the loader reads for real. REST GET and `Services/Ai` are unchanged. Filed as D-031-2. | `AnalysisToolsContractTests.Get_NeverRunAnalysis_OverTheRealLoader_DoesNotSayCompleted` runs the REAL `AnalysisOrchestrationService.GetAnalysisAsync` over the REAL loader (doubles: Dataverse and the cache) on a seeded, never-run row and asserts no "Completed", no status / completedOn / documentName and no analysis name in the answer. `Precondition_TheRestLoaderFabricatesCompleted_ForANeverRunRow` pins that the loader still does it (delete with the D-031-2 fix). `Get_Readable_IsTheSlimAnalysis` now sends Completed, a document name and a completion time through the route double and asserts none reaches the tool. |
| 2 (F1) | A fault while deciding (the 403 with `sdap.access.error.system_failure`) read as "the user may not start an analysis". | Its own text: "Spaarke could not check access right now. Try again shortly." (`StartAnalysisTool.SystemFailureMessage`; same code and 403). | `AnalysisToolsAuthTests.Start_DecisionFault_IsSystemFailure_NoExceptionText` asserts the message in `structuredContent` and `content`; `Start_NoAttachRights_IsTheRouteRefusal` asserts a real refusal keeps its text (control). |
| 3 (F1) | The retry entry was stored with the caller's token, so a caller who went away after the create left no key and the model's retry created a second analysis. | `McpIdempotency.RememberAsync` uses `CancellationToken.None`, as `UnlockAsync` does. | `Start_CallerCancelsAfterTheCreate_RetryReplays_OneRow`: a cache that honours the token throws as the key is stored and the token is cancelled; the first call returns its id, the same-key retry is `replayed: true`, one row. (The cancel is placed at the key's write, because a cancel before the create returns its id leaves nothing to store: see K-limit below.) |
| 3 (K1) | A call that read the entry, then took the lock after the first call finished, could create a second row. | After the lock is taken the entry is read again; if present it replays (or refuses a different fingerprint). | `Start_FirstFinishedBetweenReadAndLock_Replays`: the entry appears as the lock is written; no create. |
| 4 (F1) | A replay said "It has not been run yet" and `warningCount: 0`, neither of which the replay knows. | A replay says "Open it to see its status." and carries no `warningCount` (null, omitted). The first call is unchanged. | `Start_Replay_SaysNothingAboutRunState_AndNoWarningCount`. |
| 5 (F4, D-209) | "playbook" was unqualified in the description, the schema text and the refusal text. | "JPS playbook (AI analysis recipe)" in all three (and the `playbookId` GUID error); the description adds "Not for: legal or contract-review playbooks (Markdown or Microsoft contract-review playbooks)". Description length is asserted at most 1,024 (this one: under the limit; the get tool too). | `Start_Description_QualifiesPlaybook`, `Start_SchemaAndRefusalText_QualifyPlaybook`. |
| 6 (F2) | The retry logic was inline in the tool, to be copied by tasks 030 and 036. | One internal helper, `Api/Mcp/McpIdempotency.cs`: `RunOnceAsync` (read, lock, re-read, run, remember, unlock), `FingerprintOf`, `RetryIdOf`, `IMcpRetryEntry`, `McpIdempotencyScope`. The cache key is unchanged: `tenant:{tid}:{resource}:{oid}:{SHA-256(key)}:v1`, and the fingerprint bytes are unchanged, so an entry written before this change still replays. D-031-1 (#3231) is in its doc comment. The pending CAPABILITY-INDEX row says "call `McpIdempotency`". | All the existing retry tests pass over the helper; `RetryId_IsOidAndKeyHash_NeverTheKey`. |
| 7 (F2) | A model-visible write passed O-14 for having the right hints. | `McpToolAuthorization.ModelCallableWrites` is a closed, named list (today `spaarke_start_analysis`); `ValidateTool` refuses a reversible-write tool that is not app-only and not in it. 030 and 036 add their names with their tools. r1's `CompleteDecisionTool` (app-only) is unchanged. | `McpAuthTests`: must-fire `ReversibleWriteUnlisted` (a perfect shape under an unlisted name) and `ValidateTool_ReversibleWrite_MustBeNamedInTheClosedList`; must-not-fire `ReversibleWrite` (listed name) and the app-only write. The six broken-shape cases now carry the LISTED name, so each refuses for its own fault, not for being unlisted. |
| 8 (F2) | No test that the retry key is per caller. | Test added; no code change was needed (the cache id already holds the caller's oid). Not "fails before": it guards the existing behaviour. | `Start_SameKeyDifferentCaller_IsAnotherStart`: caller A then caller B (other `oid`, same `tid`), key "k": two creates, different ids, B `replayed: false`. |

Also added (item 9): `AnalysisCreateContractBaselineTests.Create_Fault_Is500_Frozen` freezes the REST route's generic `CreateAnalysisFailure` 500 (it was not frozen), and `Start_CreateFault_IsCreateFailed_AndNotRemembered` pins the tool's answer to the same fault.

### Known limits and owner questions found by the reviews (not fixed here)

- **No skill, knowledge or tool ids.** `spaarke_start_analysis` takes `documentId`, `name` and an optional `playbookId` only; the REST create also takes `skillIds`, `knowledgeIds` and `toolIds` and associates them. A tool-created analysis has no scope rows. That is the task's stated scope (no scopes named by the tool); a later task adds them if Copilot needs them.
- **The name is trimmed and has no maximum length.** The tool trims `name`; it caps nothing (the REST route caps nothing either, so the column's own limit decides). A very long name fails in Dataverse as the route's 500 (`analysis.create_failed`). Add a cap with the REST validator if a column length is confirmed.
- **Permanent refusals read as "try again".** `analysis.create_failed` (500) says "Try again shortly", and so does the new system-failure text. A refusal kind that is permanent (a Dataverse validation or a business rule on the row) reaches the same generic 500 on REST and so the same text here. Separating them needs the route to expose the refusal kind, which is a REST contract change.
- **"Analysis Workspace" or "Spaarke Console".** The text and the link name say "Spaarke Analysis Workspace" (the Analysis Workspace is the SpaarkeAi three-pane surface). CLAUDE.md §1.1 says the user app is the "Spaarke Console". Owner call: keep the surface name, or say "Spaarke Console". One constant per tool to change.
- **Write tests are not in `data-mutation/Mcp/`.** The start tool's tests are in `tests/integration/contract/Api/Mcp/Analysis/` and `tests/integration/auth/Mcp/Analysis/`, with the REST baseline in `contract/Api/Ai/`, beside the host they reuse (`AnalysisAuthHost`). The row-level write assertions (`AppOnlyCreates`) are contract-style, not data-mutation. Move them if the project wants all write tests under `data-mutation/`.
- **A cancel before the create returns its id leaves no key.** If the caller goes away while the create is in flight, the row may exist and the id is never known to the tool, so no entry can be stored and a retry creates a second row. `RememberAsync` ignoring the token fixes the window after the id is known. The remaining window is the same one `IdempotencyFilter` has; it belongs with D-031-1 (#3231).
- **`startedOn` is the creation time.** The loader maps `CreatedOn` to `StartedOn`; for an analysis that has not run that is when the row was made. The tool keeps the field name for the REST contract's sake. Rename to `createdOn` when D-031-2 is fixed and the run state has its own fields.

### Verification (fix round)

- Build: `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- Unit (filter `Mcp | Analysis`): 1,204 passed, 2 skipped, 0 failed. New tests: 14 (10 in `AnalysisToolsContractTests`, the 500 baseline, two closed-list facts, one new `McpAuthTests` theory case); changed: the slim-get test and two auth tests.
- Fail before: with the old `StartAnalysisTool.cs` and `GetAnalysisTool.cs`, 8 of the `031` tests fail (the cancel, lock-race, replay text, D-209 x2, system-failure message, slim get and never-run get). With the closed-list check disabled, 2 fail (`ReversibleWriteUnlisted` and the must-fire fact). The per-caller, key-shape, create-fault and 500-baseline tests guard behaviour that already held, so they pass before and after.
- ArchTests (`RouteAuthoriz | Mcp`): 124 of 124 pass.

## Main-session close (2026-11-26)
- **Merged-tree gates at 1895213b48:**
  - full ArchTests: 2,859 passed;
  - unit tests filtered to Mcp, Analysis and Briefing: 1,367 passed, 2 skipped;
  - BFF build clean.
- **Publish size,** measured with both widgets built on each side: HEAD 44,038,798 bytes compressed vs master 152397ab11 at 44,094,475. `Sprk.Bff.Api.dll` grew by 1.33 MB, mostly the embedded briefing page (about 1.19 MB), well under the +5 MB threshold; total about 42 MB, under the 55 MB review line.
- **CVE check:** no NuGet packages changed.
- **Verifier fixes:** re-checked by the main session.

Task 031 done.
