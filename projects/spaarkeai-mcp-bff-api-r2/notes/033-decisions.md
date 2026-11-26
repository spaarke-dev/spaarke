# Task 033 — spaarke_get_matter_finance: decisions and notes

Date: 2026-11-26. Branch: agent worktree (wave 2), merged from `origin/work/spaarkeai-mcp-bff-api-r2` (tip `cfc92e4b0e`) then `origin/master`.

## What changed

- New `Api/Mcp/LegalOperations/Finance/GetMatterFinanceTool.cs`: `spaarke_get_matter_finance(matterId)`, read-only, idempotent, not destructive, model-visible, no widget, bare `[Authorize]`, description ≤ 1,024 characters ("Use for / Not for").
- Ledger row `GetMatterFinanceTool` in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.Finance.cs` (seam `IFinanceSummaryService`).
- The two pinned lists gain the tool: `McpHostContractTests` (exact `tools/list`) and `McpCallerPathTests` (static-schema argument pin).
- Tests (all KEEP paths): `tests/integration/auth/Mcp/Finance/GetMatterFinanceToolAuthTests.cs`, `tests/integration/contract/Api/Mcp/Finance/GetMatterFinanceToolContractTests.cs`.
- CAPABILITY-INDEX proposal in `notes/capability-index-pending.md` (the main session applies it; sub-agents cannot write `.claude/`).

## Decisions

- **One authorization, no copy.** The tool's only check is `FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync(http, matterId, ct)` (CAPABILITY-INDEX row 52). `HasCaller` runs first (the signed-in test every tool applies) so an unsigned request is `UserContextRequired` before an argument error; the method repeats it and also refuses a principal with no `oid`.
- **Refusal mapping (`RefusalOf`).** Each reason code maps to fixed text and the route's status through `McpToolResults.FromError`: `internal_callers_only`, `insufficient_rights`, `no_target`, `system_failure` (all 403, as the route), `UserContextRequired` (401, the r1 tools' code). An unknown code is refused as `system_failure`, never allowed. No id, oid or exception text is in any message.
- **ADR-028 A10 Path A (owner, 2026-11-26).** A caller with no systemuser row gets `sdap.access.deny.internal_callers_only`, not `UserContextRequired` (pinned by a seeding proof: mapping that code to `UserContextRequired` fails 4 tests). A caller without Read on the matter gets 403 `insufficient_rights`, REST parity, not 404.
- **K1 (ebilling-r1) — the A10 gate.** Nothing new is needed in the tool: it is a catalog tool on `/mcp/legal-operations`, so `RequireMcpConnectorToken` (delegated `user_impersonation`, BFF audience, a user) runs before it, and the bare `[Authorize]` is the SDK's per-tool check. Proven on the real host: a token with the wrong scope, the Copilot audience, or an app-only token gets 401 and no step-0 lookup, no rights question and no summary read happen (`AnA10RefusedToken_IsRefused401_BeforeTheTool_...`).
- **K2 (ebilling-r1) — no REST handler internals.** The tool calls `IFinanceSummaryService.GetSummaryAsync` directly after the method allows. It does not touch `StepZeroVerdictKey` or the REST handler. (The summary handler in this tree does not read that item today; the rule holds for when task 113 adds the banner.)
- **K3 (ebilling-r1) — replay.** 008's route-vs-method replay stays as it is. Two replays are added for the tool: in the auth test every refusal case (9) is compared with a live route on `FinanceAuthHost` under the same caller and rights, and in the contract test six refusal cases are compared with the live route on the real BFF host, one token, tool and route on the same host. Every refusal's `reasonCode` equals the route's, and `IFinanceSummaryService` is never called.
- **Result is slimmed (ADR-015).** `structuredContent` is `{ matterId, currentSpend, budget, budgetVariance, velocityPct?, activeSignals[{ signalType, severity, generatedAt }], generatedAt }` (camelCase, web options, nulls omitted). Dropped from the REST body: each signal's `message` and the `recentInvoices` rows (invoice number, vendor, amount, status). The figures are the REST body's (compared raw, in the contract test). Signals are capped at 20 (`MaxSignals`); the REST body is unbounded. `content` is 3–4 lines of totals. A 300-signal seed gives a result far under 64 KiB.
- **"Accruals" is not a field.** The POML prompt says "budget, spend and accruals"; the REST summary has no accrual figure, so the description lists what exists (spend, budget, variance, velocity, active spend signals) and does not promise accruals.
- **Two tool-owned reason codes where REST has none.** No financial data (REST: 404 ProblemDetails, no `reasonCode`) is `finance.summary.not_found` (404). A service fault (REST: 500, no `reasonCode`) is `finance.summary.unavailable` (500) with fixed text; the log line carries the exception type only. A call the caller abandoned (cancellation) propagates.
- **Arguments.** Exactly one string `matterId`; the closed check is `DocumentToolSupport.TryReadStringArguments` (reused, as `GetTaskTool` does), reasons `mcp.invalid_argument` / `invalid_id` (400). The empty GUID is not rejected by the tool: it goes to the shared method and is refused `no_target` (403) as the route does, so parity holds.
- **Rate limit (owner Q15).** The `mcp` limiter is accepted as is; no per-tool limiter.
- **EB-D-194.** The tool offers no invoice review or approval and no invoice search; `TheFinanceFolder_OffersNoInvoiceWorkflow` scans every source file of the folder for the review, confirm, reject, approval and search identifiers (with a must-fire control that the scan reads the tool).
- **Telemetry (NFR-05).** `DecisionToolCall.LogAroundAsync`: tool name, outcome (`ok` or the reason code), rows (1 or 0), milliseconds, correlation id. Proven over the SDK handler on the real host; the lines carry no amount and no oid.

## Placement Justification (`.claude/constraints/bff-extensions.md` §A)

- Placement: stays in the BFF, `Api/Mcp/LegalOperations/Finance/`, on the existing `/mcp/legal-operations` host. Per-request read of a BFF service; no background work (ADR-052 does not apply). ADRs: 008 (the route keeps its filter; the method reuses it), 013 (no `Services/Ai` type), 015 (totals only), 019 (typed `isError` result, the route's `reasonCode`), 028 A10 (as the caller, fails closed, no app-only path), 010 (no DI registration: the catalog finds the class by `[McpArea]`).
- Component justification (root §11): Existing — `FinanceSummaryService.GetSummaryAsync`, row 52, `McpToolResults`, `DecisionToolCall`, `DocumentToolSupport` (argument check, not-found shape). Extension — this is the extension; nothing new but the adapter and the slim projection. Cost of doing nothing — Copilot users cannot ask for a matter's finance position (FR-11 fails).
- New surface this task adds under `src/server/api/Sprk.Bff.Api/`: one file, `Api/Mcp/LegalOperations/Finance/GetMatterFinanceTool.cs`. No package, no DI registration, no endpoint.

## Tests and evidence

- **New tests: 41, all pass.** Auth file (direct calls + route replay): 9 refusal cases with the route's reasonCode, fixed-text refusals, allowed (internal blank/false flag) with figures equal to the route, null summary, no-caller cases, bad arguments, service fault, cancellation, two must-not-leak cases, EB-D-194 source scan. Contract file (real host): `tools/list` entry, figures equal the route's and 300 signals bounded, 6 refusal replays against the live route, 3 A10-refused tokens (K1), telemetry.
- **Fail-before evidence.** The tool is new, so before the change the test files do not compile (no `GetMatterFinanceTool`). Three seeding proofs, each restored: (1) authorization bypassed (`Allowed` instead of the shared method): 19 of 41 fail (all 9 refusal cases, the allowed-caller cases, the contract refusal replays, telemetry); (2) the tool returns the full REST summary: 3 fail (both must-not-leak cases and the contract totals-only case); (3) the no-row refusal mapped to `UserContextRequired` (the rejected Path C): 4 fail. The mutated file was restored byte for byte from a copy (run 2026-11-26).
- **Runs (2026-11-26).** `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors. `Sprk.Bff.Api.Tests` filtered `Mcp|Finance|FinanceAuthorization`: 10,320 passed / 0 failed / 7 skipped (none new). `Spaarke.ArchTests` filtered `Mcp|RouteAuthorization|FinanceStepZero|OperationAccessPolicyCompleteness`: 129 / 129. `bash scripts/quality/check-changed-cs-format.sh origin/master`: all 174 changed files formatted. The mcp-conformance job (`tests/integration/Mcp.Conformance.Host`) was not run locally (it is a CI job and needs the Node suite); CI runs it.

## Publish size and CVEs (`.claude/rules/bff-hygiene.md` item 4; NFR-01)

Convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` (framework-dependent linux-x64 from the csproj), PDBs included as published, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, fresh worktrees at `C:\wt033m` (this branch), `C:\wt033b` (fresh `origin/master`, `51ce01a116`) and `C:\wt033p` (this branch's parent commit, to isolate this task). All removed with `Remove-WorktreeSafe.ps1`.

| Build | Compressed | Uncompressed | Files (recursive) |
|---|---|---|---|
| fresh master `51ce01a116` | 41.639 MB | 137.97 MB | 194 |
| this branch's parent (r2 work + master) | 41.297 MB | 137.15 MB | 186 |
| this branch (adds the tool) | 41.303 MB | 137.16 MB | 186 |

- This task's delta: **+0.006 MB compressed** (`Sprk.Bff.Api.dll` +13,312 bytes), same 186 files. Under +5 MB.
- Versus fresh master: **-0.336 MB**. The file counts differ (194 vs 186) for a reason that is not this task: the r2 branch already removed the `Microsoft.Agents.*` and `AdaptiveCards` packages (legacy agent code), so master carries 8 more files. The like-for-like (parent vs this branch) comparison above is the one that isolates this task. Total 41.3 MB: under 55 MB and 60 MB.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package changed.

## Review

- Code review (inline, FULL): no F-class finding. Checked: the shared method is the only check and is reached after the signed-in test and the closed argument read; every refusal and fault returns before `GetSummaryAsync`; the summary is never read for a refused caller (proven by a strict mock in 12 cases); no message names an id, oid or exception text; the service fault log carries the exception type only; a cancelled caller propagates; the result is bounded (20 signals).
- ADR check (inline): ADR-008 (the route keeps its filter; the tool reuses the method), ADR-010 (no DI registration), ADR-013 (no `Services/Ai` type; ArchTest green), ADR-015 (totals only, no amounts in logs), ADR-019 (typed `isError` result, the route's reasonCode), ADR-028 A10 (as the caller, fails closed, no app-only path; Path A recorded), ADR-029 (delta measured, no CVE), ADR-038 (both test files on KEEP paths `auth/` and `contract/`; doubles only at the module boundaries: the systemuser lookup, the rights source, the privilege probe, `IFinanceSummaryService`; no `Mock<HttpMessageHandler>` or `Mock<IServiceClient>`). No violation.
- Adversarial verifier passes (two, `auth` tag): not run in this sub-agent; the main session runs them. External reviews: ebilling-r1 and uac-r2 (the three ebilling-r1 constraints K1-K3 are applied above).

## Known limits (K-class) and reported items

- K4: the mcp-conformance CI job was not run locally (above).
- K4: the plugin skill under `src/solutions/CopilotPlugins/spaarke/skills/` does not list this tool, as it lists none of the other r2 tools yet; the HOW-TO says to update it when the model should call a tool. Left for the task that owns the plugin skill.
- K4: `RefusalOf` maps an unknown reason code to `system_failure`; no unknown code exists today.
- No defect found in other code. The only pre-existing oddity seen: the REST summary route's 404 ("no financial data") and 500 carry no `reasonCode`, so the tool has its own two codes (`finance.summary.not_found`, `finance.summary.unavailable`); not a defect, a difference to know.

rework: none task 033
