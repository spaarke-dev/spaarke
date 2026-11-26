# Task 008 — finance summary authorization as one shared method: decisions and notes

Date: 2026-11-25. Branch: agent worktree (wave 1, merged into `work/spaarkeai-mcp-bff-api-r2` by the main session).

## What changed

- `FinanceAuthorizationFilter.InvokeAsync`'s body after the 401 identity check moved into a private `DecideAsync` (step 0 when the route is flagged, the target declaration, each check through the existing `EvaluateAsync`). `InvokeAsync` maps its answer to the same `Deny(...)`, rejection or `next` it returned before, and still stamps `StepZeroVerdictKey`. The order, the catch blocks, the log lines and the cancellation token (`RequestAborted`) are unchanged, so every `/api/finance` route (and every other route that runs this filter) behaves as before.
- New `public static FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync(HttpContext?, Guid matterId, CancellationToken)` returns `MatterFinanceReadAuthorization` (`Outcome` + `ReasonCode`, no `IResult`). It builds a filter in the summary route's mode (`FinanceDenial.Forbidden`) and runs the same `DecideAsync` with step 0 on and the target `ForRecord(sprk_matters, matterId, "finance.read")`.
- `FinanceAuthorizationTargets.FromRoute` now delegates to a new `internal ForRecord(...)`, which the shared method also uses (empty id → no check → `no_target`, as before).
- `FinanceEndpoints.cs` is not edited: the summary route keeps its literal `AddFinanceAuthorizationFilter("finance.read", MatterEntitySet, routeKey: "matterId")`, which `ExternalDocumentNarrowingGuardTests` and `OperationAccessPolicyCompletenessTests` read. The parity tests below tie the route and the method together instead.

## Deviation from the POML (code wins, root CLAUDE.md §2)

The POML says an internal caller without Read on the matter gets "the uniform record-not-found result, matching REST". The route actually runs in `FinanceDenial.Forbidden` mode and answers **403 `sdap.access.deny.insufficient_rights`** (pinned by `FinanceEndpointsAuthorizationContractTests.Summary_NonReader_Gets403WithReasonCode_AndTheServiceIsNotInvoked`). The binding part of the criterion is "matching REST", so the method returns `Refused` with `sdap.access.deny.insufficient_rights`, and the tests compare against the live route rather than a hard-coded expectation of 404. The POML's "unreadable matter (uniform not-found)" byte case does not exist on this route; the REST bytes were not changed to create it.

## Decisions

- **UserContextRequired** uses `DataverseUserClientErrorCodes.UserContextRequired` (`DATAVERSE_USER_CONTEXT_REQUIRED`), the code the r1 decision tools already return through `DecisionResultMapper.ToToolError`. It covers a null context, a principal with no authenticated identity, and an authenticated principal with no Entra `oid` (REST: 401 without a reasonCode). No Dataverse call is made in any of the three.
- **A caller with no systemuser row** gets step 0's `sdap.access.deny.internal_callers_only`, as REST does (POML criterion), not `UserContextRequired`. ADR-028 A10 says such a token "gets the existing UserContextRequired refusal" on `/mcp/*`; FR-11's acceptance ("the same reasonCode") governs this tool. Task 033 should keep the method's code; flag for uac-r2 review.
- **Missing `AuthorizationService`** (configuration fault) is refused with `system_failure` instead of the REST path's `GetRequiredService` exception (500). Fail closed; unreachable in a correctly composed host.
- **Step-0 verdict item** (`StepZeroVerdictKey`) is not set by the shared method; it exists for the REST pipeline's banner (task 113).

## Placement Justification (`.claude/constraints/bff-extensions.md` §A)

- Placement: stays in the BFF, in the existing `Api/Filters/FinanceAuthorizationFilter.cs`. It is per-request authorization of a BFF route and of a BFF MCP tool on the same host; nothing here is background or separable work (ADR-052 does not apply). ADRs: 008 (endpoint filter keeps the route's decision; the method reuses it), 019 (typed result, no ProblemDetails), 028 A10 (as the caller, fail closed, no app-only path), 010 (no new DI registration).
- Component justification (root §11): Existing — `FinanceAuthorizationFilter.EvaluateStepZeroAsync`, `EvaluateAsync`, `FromRoute` (reuse-scan row "Matter finance"). Extension — extended in place; no new class, service, policy or registration. Cost of doing nothing — task 033 would copy step 0 and the `finance.read` check (spec MUST NOT), and FR-11's "refused with the same reasonCode" could drift.

## Tests

- New: `tests/integration/auth/Finance/MatterFinanceReadSharedMethodTests.cs` (14 cases). Each case calls the method directly (as task 033 will) over the real step-0 resolvers, `AuthorizationService` and `OperationAccessRule`, and every refusal is replayed against the real summary route with the same caller and rights; the two `reasonCode`s must be equal.
- Scope beyond the named criteria (one line each): the `no_target` (empty id), `system_failure` (rights query throws) and faulted-None rows are the remaining reason codes the route exposes for this check, so FR-11's "same reasonCode" is covered for every code the route can return.
- Seeding proof: switching the shared method to `stepZero: false` fails the 4 step-0 cases (run 2026-11-25, then restored).
- Runs (2026-11-25): baseline before the change — finance-filtered `Sprk.Bff.Api.Tests` 3205/3205, `FinanceStepZeroCoverageGuardTests` 12/12. After — finance, auth, `UnifiedAccessControl` and `Filters` suites 3846 passed / 0 failed / 1 pre-existing skip (`EntityAccessFilterCharacterizationTests`); the whole `Spaarke.ArchTests` 2831/2831; `FinanceEndpointsAuthorizationContractTests` unchanged and green. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.

## Publish size and CVEs

- Release publish (`dotnet publish -c Release`, PDBs included as published, `Compress-Archive` Optimal) of the pre-change commit `6cd3e4bbc9` vs this change: 41.607 MB → 41.613 MB compressed (+0.006 MB), 137.91 → 137.90 MB uncompressed. Under +5 MB, 55 MB and 60 MB. The baseline is the branch commit this task started from, which isolates this task's delta; the main session re-measures against a fresh master build at PR time.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package changed.

## Review

- Code review (inline): no F-class finding. Checked: `InvokeAsync` keeps the 401, step-0, declaration-throw, rejection, no-target and per-check order, catches, logs and `RequestAborted`; `FromRoute` still yields no check for an unreadable or empty id; the shared method reads nothing before the caller test; it never throws (every fault is a refusal). One doc fix applied (the 401 wording).
- ADR check (inline): ADR-008 (the route keeps its filter; the method reuses the filter's own decision), ADR-019 (typed result, no ProblemDetails/IResult), ADR-028 A10 (as the caller, fail closed, no app-only branch), ADR-029 (no package; delta measured), ADR-010 (no DI registration), ADR-013 (Filters gains a reference to `Infrastructure.Dataverse` only, no `Services.Ai` type), ADR-038 (KEEP path `tests/integration/auth/Finance/`; doubles only at the Dataverse seams through the existing `StatedCaller` and `RecordingAccessDataSource`; no `Mock<HttpMessageHandler>`). No violation.
- Adversarial verifier passes: run by the main session (two, `auth`).
- External reviews pending: ebilling-r1 (owner of the finance rules), unified-access-control-r2 (caller path), ontology-platform-r1 (capability extraction).

## Known limits (K-class)

- K4: the signed-in test (`Identities.Any(i => i.IsAuthenticated)`) repeats `DecisionCapability.HasSignedInCaller` (Services/Signals, internal) rather than referencing it, to avoid a Filters → Signals dependency. One line; no behaviour difference.
- K4: the deny log lines carry the caller's `oid`, as the REST filter's always have; ADR-028 A10 allows `oid` in `/mcp/*` audit enrichment. Task 033 adds its own NFR-05 log line.

rework: none task 008

## 🔔 ADR Conflict — Resolution Required (verifier pass 1, 2026-11-25)

- **ADR:** ADR-028 A10.
- **Rule (quoted from verifier):** a `/mcp` caller who resolves to no `systemuser` "gets the existing `UserContextRequired` refusal".
- **Conflict:** `AuthorizeMatterFinanceReadAsync` returns `Refused(sdap.access.deny.internal_callers_only)` for that caller, because finance step 0 (`IsExternal || SystemUserId is null` → `InternalCallersOnly`) does not distinguish an external caller from a caller with no row. FR-11 requires the tool to return the same `reasonCode` as REST. The r1 decision tools answer the same identity `DATAVERSE_USER_CONTEXT_REQUIRED`, so Copilot would see two refusals for one identity problem.
- **Proposed path:** A (project exception in spec ADR Tensions: FR-11 parity wins; code unchanged). Both answers refuse; nothing is exposed either way.
- **Alternative:** C — expose a distinct no-row verdict from `EvaluateStepZeroAsync` (REST code unchanged) and map it to `UserContextRequired` in the method; gives up FR-11 parity for that one case.
- **Impact:** blocks 008 completion and task 033's error mapping; nothing in Phase 0 waits on it.
- **Status:** RESOLVED 2026-11-26 — owner chose **Path A** ("path A"); recorded in spec.md ADR Tensions and notes/decisions.md D-owner-answers-2026-11-26.

Other pass-1 findings: K2 method/route linked by tests not shared code (a future extra filter on the summary route would not reach the tool; optional pin test); K4 publish delta to be re-measured vs fresh master before the PR. No other F-class findings; REST behaviour unchanged for every route using the filter; method fail-closed for every input; 403 (not 404) is correct and leaks nothing.
