# Task 007 — playbook run authorization as one shared method: decisions and notes

Date: 2026-11-25. Branch: agent worktree (wave 2, merged into `work/spaarkeai-mcp-bff-api-r2` by the main session). House pattern: task 008 (`FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync` over a private `DecideAsync`).

## What changed

- `PlaybookAuthorizationFilter`'s Run-mode body moved into a private instance method `DecideRunAsync(HttpContext, userId, playbookId, documentIds, parameters, ct)` that returns a typed `PlaybookRunAuthorization`. It is the ONE implementation of the run checks, in the order they ran before: document ids (400), the parameter policy's syntax half (400), the playbook-use decision (uniform 404), every document and record parameter (uniform 403), the run's user (uniform 403).
- The Run-mode filter (`AuthorizeRunAsync`) now only binds (the route id and the `ExecutePlaybookRequest` argument; an unbindable request stays the configuration-fault uniform 404 it was), calls `DecideRunAsync` with `RequestAborted`, and maps the outcome to the same `IResult` it returned before (`FromLegacyError` 400, `ParameterRejected` 400, `UniformPlaybookNotFound` 404, `UniformRecordAccessDenied` 403), or publishes the run's user on `HttpContext.Items` and calls `next`. An unexpected outcome is the mode switch's existing `system_failure` 403 (unreachable).
- New `public static PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync(HttpContext?, Guid playbookId, IReadOnlyCollection<Guid>? documentIds, IReadOnlyDictionary<string,string>? parameters, CancellationToken)` for non-HTTP hosts (task 030). It applies the filter's identity test, resolves `IPlaybookService` / `AuthorizationService` from `RequestServices`, builds a Run-mode filter and runs the same `DecideRunAsync`.
- `PlaybookRunAuthorization` (record) and `PlaybookRunAuthorizationOutcome` (enum) added in the same file. No `IResult`, no ProblemDetails, no AI-internal type.
- `ParameterRejected(HttpContext, Evaluation)` (public, used by `POST /api/insights/ask`) now delegates to a private `(HttpContext, key, reason)` overload that the Run mode also uses; the body is unchanged.
- `EvaluatePlaybookUseAsync` and `AreRunRecordsAllowedAsync` take the cancellation token as a parameter instead of reading `RequestAborted` themselves; every REST caller passes `RequestAborted`, so REST behaviour is unchanged.
- The oid identity test (`CallerResolution.ResolveObjectId` + GUID parse) moved into `ResolveCallerObjectId`, used by `InvokeAsync` (all modes) and the shared method, so the identity rule has one implementation.

Other modes (OwnerOnly, OwnerOrSharedOrPublic, UniformById, TablePrivilege, PublicFlagShare) are not changed.

## Outcome → REST → reasonCode

| Outcome | REST response (unchanged) | `ReasonCode` |
|---|---|---|
| `UserContextRequired` | 401 "User identity not found" (from `InvokeAsync`, before the mode runs) | `DATAVERSE_USER_CONTEXT_REQUIRED` (REST body has no code) |
| `DocumentIdsRequired` | 400 legacy error, `DocumentIdsRequiredMessage` | **new** `playbook.document-ids-required` (REST body has no code) |
| `ParameterRejected` | 400 `errorCode` `playbook.parameter-rejected` | `playbook.parameter-rejected`; plus `RejectedParameterKey` / `RejectedParameterReason` |
| `PlaybookUnavailable` | uniform 404 | `sdap.access.deny.record_unavailable` |
| `RecordAccessDenied` | uniform 403 | `sdap.access.deny.insufficient_rights` |
| `RunUserUnresolved` | the same uniform 403 | `sdap.access.deny.insufficient_rights` |
| `Allowed` | `next` (SSE) | `null`; `RunUserId` = caller's systemuserid |

## No-caller and no-row outcomes (ADR-028 A10; open point from 008)

- **No caller** — null `HttpContext`, no authenticated identity, no `RequestServices`, or an `oid` that is missing or not a GUID → `UserContextRequired`, before any Dataverse call. Proven by tests (strict mocks + recording access source see no call).
- **A caller with no `systemuser` row** — the method cannot tell this from "no rights" in the first two Dataverse checks, because those are answered by Dataverse as the caller (OBO): a private playbook ends in `PlaybookUnavailable`, a public one in `RecordAccessDenied` (a failed OBO/rights query is a deny). If every rights check passes and only WhoAmI fails, the answer is `RunUserUnresolved`. All three carry the REST codes (FR-05 parity). `RunUserUnresolved` only separates a WhoAmI failure after every check passed; it is NOT the no-row outcome (verifier pass 1, F2 — corrected). To meet ADR-028 A10 on `/mcp` (if the owner picks path C on 008), task 030 must resolve the caller's systemuser (e.g. `ResolveCallerSystemUserIdAsync`) BEFORE calling `AuthorizePlaybookRunAsync` and refuse a no-row caller with `UserContextRequired`; the method cannot do this itself without changing REST's 404/403. No new ADR conflict is raised by 007: the REST bytes are unchanged and the method returns REST's codes; the A10 mapping is task 030's, and follows the owner's decision on 008's conflict.
- **No bearer token on an authenticated principal** — not `UserContextRequired`: as on REST, the checks run and refuse (public playbook → `RecordAccessDenied`; private → `PlaybookUnavailable`), with no app-only query. Kept for parity (changing it would change the REST 403 to a 401).

## Deviations from the POML (code wins, root CLAUDE.md §2)

- **"Subject checks" are not part of Run mode.** `BuildSubjectRunChecksAsync` serves `POST /api/insights/ask` (task 163) through `FinanceAuthorizationFilter`; the execute route has no subject. It is not touched, and the shared method does not include it.
- **Two refusals whose REST body has no code** get codes on the typed result: `UserContextRequired` uses the r1 code `DATAVERSE_USER_CONTEXT_REQUIRED` (as 008); `DocumentIdsRequired` uses the new `playbook.document-ids-required` (ADR-019 constraint: "new codes recorded in the task notes").
- **Missing `IPlaybookService`** (configuration fault) is `PlaybookUnavailable` in the shared method — the answer the route gives when the playbook-use decision cannot run (`AuthorizationService` missing → uniform 404). On REST a missing `IPlaybookService` is a `GetRequiredService` exception (500) before the filter runs; unchanged.
- **Empty playbook id** (only a non-route host can pass it) → `PlaybookUnavailable`, no Dataverse call. The route's `:guid` binding never passes it.
- **Escalation trigger not fired.** The filter's own work in Run mode is binding and mapping. The 401 identity test lives in `InvokeAsync` before every mode (pre-existing, not a Run-mode check); it and the shared method use the same `ResolveCallerObjectId`.

## Placement (ADR-013 / ADR-029, `.claude/constraints/bff-extensions.md` §A)

- Placement: the BFF, in the existing `Api/Filters/PlaybookAuthorizationFilter.cs` (not `Services/Ai/PublicContracts/`). The filter is the grandfathered owner of the `IPlaybookService` lookup (`ADR013_AiBoundaryTests` prefix `Sprk.Bff.Api.Api.Filters.PlaybookAuthorizationFilter`); the public signature carries only `HttpContext`, `Guid`, BCL collections and the new result type, so an MCP tool calls it without depending on `IPlaybookService` (the boundary test stays green). Same placement as task 008's finance method. Per-request authorization of a BFF route and a BFF MCP tool on the same host; no background or separable work (ADR-052 n/a); no DI registration (ADR-010); no package.
- Component justification (root §11): **Existing** — `PlaybookAuthorizationFilter`'s Run mode (reuse-scan row "Playbook run start and status"); CAPABILITY-INDEX has no playbook-auth row. **Extension** — extended in place; the new types are its result shape. **Cost of doing nothing** — task 030's run tool would copy the playbook, document and parameter checks (spec FR-05 MUST NOT), giving the rule two owners.

## Tests

- New: `tests/integration/auth/Ai/PlaybookRunAuthorizationSharedMethodTests.cs` (19 cases). Each refusal and the allowed case call the method directly (as task 030 will) over the REAL `AuthorizationService`, `OperationAccessRule` and document narrowing, substituted only at the boundaries `PlaybookRouteAuthorizationContractTests` uses (it reuses that file's `PlaybookAuthHost`, recording access source and WhoAmI probe); the same input is then sent to the REAL route over the same substitutes. The test asserts the route's status, that its `reasonCode` / `errorCode` equals the method's, and that both asked the access-data boundary the same questions in the same order.
- Scope beyond the named criteria (one line each): `RunUserUnresolved` (the outcome kept apart for the A10 point); `DocumentIdsRequired` (the new code); the empty playbook id and the missing `IPlaybookService` (inputs only a non-route host can produce, both fail closed); the non-GUID `oid` row (the route's 401 condition).
- Seeding proof: mapping `RunUserUnresolved` to the uniform 404 in the filter failed 2 tests (the new parity test and `Execute_CallerWhoseSystemUserIdCannotBeResolved_Is403_AndNothingRuns`); restored.
- `PlaybookRouteAuthorizationContractTests` unchanged and green.
- Runs (2026-11-25): baseline before the change — `Sprk.Bff.Api.Tests` filtered Playbook / AnalysisAuthorization / Insight: 1907/1907. After — contract + new: 104/104; Playbook / AnalysisAuthorization / Insight / Filters: 2200 passed, 0 failed, 1 pre-existing skip; a wider run (adding `~Auth`) 5937 passed, 4 failed, 3 skipped — the 4 failures (`ComposeAuthoredWarningSuppressionTests`, `PlaybookRunEndpointsTests.ExecutePlaybook_WithoutAuth_RequiresAuthentication`, `ExternalModuleDataContractTests`, `DocumentIdentityByIdContractTests` 401 cases) each ran ~3 minutes in a 20-minute run under load and all 4 pass when re-run (4/4); not caused by this change. `Spaarke.ArchTests` 2830/2830 (incl. `ADR013_AiBoundaryTests`). `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors. `dotnet format whitespace --verify-no-changes` on both changed files: clean.

## Publish size and CVEs

- Release publish (`dotnet publish -c Release`, `Compress-Archive` Optimal) with only `PlaybookAuthorizationFilter.cs` reverted to the pre-change commit vs this change: 41.156 MB → 41.158 MB compressed (+0.002 MB), 136.71 → 136.72 MB uncompressed. Under +5 MB, 55 MB and 60 MB. The baseline isolates this task's delta; the main session re-measures against a fresh master build at PR time.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package changed.

## Review

- Code review (inline): no F-class finding left. One F2 fixed during review: the oid identity test was written twice (`InvokeAsync` and the shared method); it is now `ResolveCallerObjectId`, used by both. Checked: Run-mode order (binding → document ids → parameter policy → playbook use → documents and record parameters → run user) unchanged; each outcome maps to the exact `IResult` call used before; the parameter 400's body is built by the same code for Insights and execute; every REST caller of `EvaluatePlaybookUseAsync` / `AreRunRecordsAllowedAsync` passes `RequestAborted` as before; the shared method reads nothing before the identity test and never answers Allowed without a run user; a fault inside a check is that check's refusal; only cancellation propagates.
- ADR check (inline): ADR-008 (the route keeps its filter; Run mode binds and maps, the decision is the filter's own method), ADR-019 (typed result, no ProblemDetails/IResult), ADR-028 A10 (as the caller, fail closed, no app-only branch; no-row mapping above), ADR-013 (Api/Filters, no AI-internal type in the public signature; `ADR013_AiBoundaryTests` green), ADR-029 (no package; delta measured; no CVE), ADR-010 (no DI registration), ADR-038 (KEEP path `tests/integration/auth/Ai/`; doubles only at the access-data, WhoAmI and service-interface boundaries; no `Mock<HttpMessageHandler>` or `Mock<IServiceClient>`). No violation; no ADR conflict raised.
- Reviews pending: **unified-access-control-r2** (caller path), **spaarke-ontology-platform-r1** (capability extraction).
- Adversarial verifier passes: run by the main session (two, `auth`).

## Known limits (K-class)

- K4: the shared method passes its own token to the checks, but `InternalPlaneDocumentNarrowing` and the access services may also observe `HttpContext.RequestAborted`; for an MCP host whose tool token and request token differ, a cancellation of only one of them ends the check as a refusal or an `OperationCanceledException`. No security effect (both fail closed).
- K4: the identity test (`Identities.Any(IsAuthenticated)`) repeats `DecisionCapability.HasSignedInCaller` rather than referencing it, as in task 008, to avoid a Filters → Signals dependency.

rework: none task 007

## Verifier pass 1 (2026-11-25, opus; correctness + REST parity)
- No F1/F3/F4. Order, Dataverse questions and 404/403 uniformity unchanged on every branch; no fail-open path; `RunUserId` comes only from WhoAmI on the caller's token. 85/85 focused tests.
- F2 fixed (main session): the `RunUserUnresolved` doc comment and §A10 note claimed it covers "no systemuser"; it does not (a no-row caller is answered "no rights" first). Both now say task 030 must resolve the caller's systemuser before calling the method to meet A10.
- K2: A10 no-row mapping deferred to task 030 + owner decision on 008 (fails closed today).
- K4: parity helper compares only `IAccessDataSource` calls (both paths run the same `DecideRunAsync`, so it proves the mapping; order parity rests on unchanged contract tests + side-by-side reading).
- K4: no cap on `documentIds` size in the shared method (REST had none); task 030's tool sets its own.
- PR-time items: publish delta vs fresh master; uac-r2 + ontology-r1 reviews.
