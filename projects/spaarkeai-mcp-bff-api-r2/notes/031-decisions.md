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
