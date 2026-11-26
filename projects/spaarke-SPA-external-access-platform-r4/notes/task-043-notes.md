# Task 043 — binder faults answer 503 and the SPA retries; ProjectPage 404 folded into 403 (FR-28, #2269)

Rigor FULL (tags bff-api, auth). Model opus @ high. Steps directional. Owner go: "Yes, start 043" (2026-11-26).

## Gates checked before work
- ebilling-r1 task-130 (`017a965312`) is on master, so the ProjectPage fold went ahead (no escalation needed).
- origin/master merged into the branch first (`efefd0f542`); /conflict-check: no open PR touches 043's files.

## BFF
`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`:
- `CallerPrincipalResolution.IsBinderFault(code)` — true for `ContactBindingDecision.DenyContactLookupFailed`, `DenyContactCreateFailed`, `DenyContactBindFailed`.
- `CallerPrincipalResolution.BinderFault(code)` — 503 ProblemDetails, title "Service Unavailable", fixed `BinderFaultDetail` ("…could not be checked right now. Try again in a moment."), `reasonCode` = `errorCode` = the code. No tenant, oid, UPN, email or token (NFR-04).
- CIAM `ResolveAsync`: a binder code returns `BinderFault` after the existing warning log and before the removed → not-invited fold.
- Workforce switch: a new `PrincipalNotResolved when IsBinderFault(...)` case before the default (the default kept every other code at 403), with a `[WF-AUTH]` warning that logs only the code.
- No new endpoint, DI registration, package or background work. Placement: in the BFF (it is the resolver's own answer).

Unchanged (must-not-fire): not-invited and removed → 403 not-invited; `registration_required` → 403; owner-unconfigured → 500; `contact_create_unavailable` (a schema fault, not transient), `SystemUserDisabled`, `ContactOidAmbiguous` and the other deny codes → 403 with their own code.

Known limit (K): the CIAM plane never creates a contact (create-by-oid runs only on the creating planes), so `contact_create_failed` cannot arise there. The mapping is shared, so it would answer 503 if it ever did.

### Tests
- New `tests/integration/auth/ExternalAccess/BinderFaultStatusTests.cs` (17):
  - workforce theory over the three codes;
  - the real home resolver with a failed link/contact lookup → 503 `contact_lookup_failed`, no writes;
  - CIAM real binder: oid lookup failed → 503; an invited contact whose bind fails → 503 `contact_bind_failed`;
  - must-not-fire: not-invited, removed, registration_required, owner-unconfigured, contact_create_unavailable and the other 403s, CIAM not-invited and not-found;
  - host: `GET /api/v1/external/me` answers 503 `application/problem+json` on CIAM and workforce;
  - every 503 body is checked for status, codes, title, detail, and for no oid, email or tenant.
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/CallerPrincipalResolverTests.cs`: the old "a lookup failure stays 403" test and the `unreadable` 403 row (and its now-dead `case "unreadable":`) were removed. They pinned the old behaviour, and the new file replaces them with comments pointing there.
- Related suites: 430/430 pass.
- **Seeding proof (BFF):** `contact_bind_failed` mapped back to 403 → 8 of 17 BinderFaultStatusTests fail; restored (diff back to only the intended +44 lines).

## SPA
- `src/api/me-client.ts`:
  - `BINDER_FAULT_CODES`, `isBinderFault(err)` (an ApiError 503 whose ProblemDetails names a code in `reasonCode` or `errorCode`; parsed like the owner-unconfigured check, now one shared `problemCodeIn`);
  - `BinderFaultError`, `BINDER_FAULT_MESSAGE` ("We couldn't finish signing you in. Try again"), `BINDER_FAULT_RETRY_DELAYS_MS` [1000, 2000, 4000];
  - `withBinderFaultRetry(call, wait?)` retries only binder faults, at most 3 times, then rejects with `BinderFaultError`; anything else rejects at once with the original error.
  - `fetchMeEntitlements` runs through it and rethrows `BinderFaultError`, so these codes NEVER reach the plane-default fallback. A plain 5xx keeps the fallback.
- `components/auth/WorkforceHomeGate.tsx`: `probe` wraps `getExternalUserContext` in the retry (behind the "Checking your account..." spinner). On `BinderFaultError` a new `binderFault` state shows the message in a warning MessageBar with **Retry** (`probe(true)`, the whole sequence again).
- `pages/WorkspaceHomePage.tsx`: on `BinderFaultError` shows the message with **Retry**, which bumps `meAttempt` and re-runs the effect (request + retries).
- Fluent v9 components and tokens only.

### Caller audit (every reader of /me, /me/entitlements and `accessFailureOf`)
| Caller | Handling of a binder 503 | Decision |
|---|---|---|
| `fetchMeEntitlements` (me-client) → `WorkspaceHomePage` | retry ×3, then the message + Retry; never plane defaults | changed (the CIAM sign-in surface; also runs after the workforce gate) |
| `getExternalUserContext` → `WorkforceHomeGate.probe` | retry ×3, then the message + Retry | changed (the workforce sign-in surface, browser and Teams) |
| `getExternalUserContext` → `useExternalContext` → `useAccessLevel` → `ProjectPage` | the hook's error is unused by ProjectPage; the level stays ViewOnly (safe) | unchanged: a post-sign-in read; its own error text and safe default stand |
| `getExternalUserContext` → `useExternalContext` → `OutsideCounselDashboard` | not routed (legacy page) | unchanged |
| `getExternalUserContext` → `useCallerContext` (record view) | failure is silent; Delete and Upload stay hidden (safe default) | unchanged: post-sign-in, documented silent |
| `getExternalUserContext` → `web-api-client` `listInvoiceFirms` / `probeInvoiceSubmission` | the submit flow's own error / 'unknown' | unchanged: post-sign-in |
| `accessFailureOf` (BffAccessState) — delete-own, intake-client, EventsCalendar, RecordMessages, DocumentUploadPage, WorkforceHomeGate, WorkspaceHomePage | a 503 is neither 401 nor 403, so it gives `null` and the caller shows its own error | unchanged: no 503 becomes a denial |
| `AuthGuard` | makes no BFF call (MSAL only) | n/a |

### Tests
- New `src/client/external-spa/tests/me-client.binderFaultRetry.test.tsx` (21):
  - `isBinderFault` true for each code (and errorCode only); false for a plain 503, another code, a 403 or a 500 with a binder code, and a non-ApiError;
  - `withBinderFaultRetry`: success after one retry (waits [1000]); exhaustion after 4 calls (waits [1000, 2000, 4000]) → `BinderFaultError`; a 403, a plain 503 or a network fault rejects at once with no wait;
  - `fetchMeEntitlements` (fake timers): fault then success; exhaustion → `BinderFaultError` and no plane-default warning; plain 503 → plane defaults with one call; 403 → rejects with one call;
  - `WorkspaceHomePage`: exhaustion → the message, no tablist, 4 calls; Retry → the sequence again → the workspace; plain 503 → the default workspace; 403 → the denied state, one call.
- `tests/workforce-home-auth.test.ts` new describe "a /me binder fault (503)": fault, fault, OK → the workspace, 3 calls; exhaustion → the message with Retry (not the workspace, not the generic error, `onReady(true)` never), Retry → the workspace; a 403 is never retried. The existing "plain 5xx → generic error with a retry" test is the must-not-fire pair.
- **Seeding proof (SPA):** `isBinderFault` forced to `false` and the ProjectPage fold reverted → 10 of 24 in the two new files fail and both gate binder tests fail; restored.

## ProjectPage
The 404 branch ("Project Not Found") is removed. `403 || 404` shows the one access message. The new `tests/ProjectPage.noAccess.test.tsx` checks that 403 and 404 both show it and never the not-found text, and that a 500 still shows the generic error (must-not-fire). No existing ProjectPage test asserted the 404 text.

## Test scope note
All tests cover the stated contract and its must-not-fire and negative cases. `ProjectPage.noAccess.test.tsx` is the regression test for the fold (every bug fix gets one).

## Adversarial reviews (two passes, auth; opus)
**Pass 1 (server).** The mapping is correct: only `ContactIdentityBinder` produces the three codes, never for a definite refusal; there is no stray 503; no identifiers are added. Fixed in scope:
- **F1** `ExternalRegistrationEndpoints.cs`: the registration POST answered a home link/contact lookup fault with 403 "cannot be registered". It now answers 503 `sdap.access.registration_unavailable`, like its own binder faults and like `/me`. New contract test `FailedHomeLookup_BeforeRegistering_Is503Unavailable_NotA403_NothingWritten` (link, contact) in `tests/integration/contract/Api/ExternalAccess/ExternalRegistrationEndpointsTests.cs`.
- **Docs:** `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (first sign-in is now `503 contact_lookup_failed`) and the `ContactBindingDecision.cs` comment ("the same 403 on every plane").
- **Test:** the home-lookup case in BinderFaultStatusTests never reached the contact step; it is now a theory over the link step and the contact step.

Filed (outside scope, pre-existing): **D-43 #3264** (Dataverse exceptions in the workforce resolver answer 403; a privilege-refused bind now retries forever — known limit of this task until D-43); **D-44 #3265** (the workforce resolver logs caller identifiers).

**Pass 2 (client).** The `/me` and `/me/entitlements` paths are correct. Fixed in scope:
- **F1** `ProjectPage.tsx`: the project read is a partner's first BFF call when they open a project link (no gate on CIAM). A binder 503 there showed the raw ProblemDetails JSON and was never retried. Now the read runs through `withBinderFaultRetry`, then the message with Retry (`projectAttempt`). The generic error is a fixed sentence: the server body is never shown. Tests in `ProjectPage.noAccess.test.tsx`: binder exhaustion → the message, 4 reads, no JSON; Retry reads again; a 500 shows the fixed sentence with no body and one read.
- **K4/K5** `WorkspaceHomePage.tsx`: the message is now a warning MessageBar (a live region, matching the gate), and Retry clears the state in the click handler.
- **K6** gate tests: fake timers are installed before the gate renders (`shouldAdvanceTime`), so the first wait is always on the fake clock.

Known limits recorded (K):
- **K2:** a superseded run (unmount during the waits) can send up to 3 more GETs over about 7 s. No state is set after unmount, and production builds do not double-run effects.
- **K3:** the other `/me` callers stay as audited above.

**Seeding proofs for the fixes.** The registration branch disabled → both new contract cases fail. The ProjectPage retry removed → the binder test fails. Both restored.

## Gates
- `dotnet build` BFF: 0 errors. Related BFF tests: 193/193 after the fixes (462/462 before them on the wider filter). ArchTests: 2845/2845.
- SPA: `tsc --noEmit` exit 0; eslint clean on the changed files.
  - Full vitest before the review fixes: 2166/2167. The one failure, `ProjectPage.createAgreesWithList` "Upload is offered", was a 5.5 s load timeout and passes 12/12 alone.
  - After the fixes, the 7 affected files pass, except two 60 s cold-import timeouts under load (AuthGuard sign-in, Teams AC1). Both pass alone.
- **Publish size**, fresh master `51ce01a116` vs branch `675457fbb4`: short-path worktrees `C:\wt043m` / `C:\wt043b`, `dotnet publish -c Release`, `Compress-Archive`, 194 files each. Zips 43,661,147 → 43,661,371 bytes, **+224 bytes** (≈ 43.7 MB, under 55/60 MB). The review fixes add a few lines to one endpoint file: no package or DI change.
- CVE: `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package change.

## code-review (standard, after both adversarial passes)
No Critical and no F-class finding.
- **Security:** no secrets; fixed ProblemDetails detail; the SPA never shows a server body on these paths (ProjectPage improved from showing raw JSON).
- **Performance:** at most 3 extra requests per client over about 7 s during an outage.
  - Suggestion K4: the delays are fixed (no jitter), so clients retry in step after a Dataverse blip. Bounded by the owner-set 1/2/4 s and 3 retries.
- **Lint:** BFF build 0 errors (TreatWarningsAsErrors); tsc 0; eslint clean.
- **AI smells:** none (no new interface, no log-rethrow, comments explain why). Clean.
- **Metrics / direction:**
  - `CallerPrincipalResolver.cs` (large, pre-existing) is cohesive: caller-principal resolution, and the change adds one helper group and two branches.
  - `me-client.ts` (+75) is cohesive: the `/me` client and its failure rules.
  - `ProjectPage.tsx`: net neutral (the 404 branch removed, the binder branch added).
  - Overall neutral.
- **§16 enforcement:** the SPA code list duplicates the server's.
  - Added a source pin in `me-client.binderFaultRetry.test.tsx` that reads `IsBinderFault` in `CallerPrincipalResolver.cs` and the constants in `ContactBindingDecision.cs`, and asserts the SPA list equals them.
  - It has a must-fire control: a widened server set fails the pin.
  - The server rule itself is pinned by BinderFaultStatusTests (must-fire and must-not-fire).
- **§11 justification:**
  - `CallerPrincipalResolution.IsBinderFault` / `BinderFault` / `BinderFaultDetail`. Existing: `ProblemDetailsHelper.Forbidden`, the precedent shape. Extension: `CallerPrincipalResolution` extended in place, no new type. Cost of doing nothing: the three codes stay 403 and FR-28 fails.
  - SPA `withBinderFaultRetry` and its friends. Existing: the `OwnerUnconfiguredError` / `isOwnerUnconfigured` pattern in the same file, now sharing `problemCodeIn`. Cost of doing nothing: no retry, and a binder 503 degrades to the plane-default workspace.
  - No new file except tests, and no endpoint, DI registration, package or column.

## adr-check
- **ADRs considered:** ADR-001, ADR-007, ADR-008, ADR-010, ADR-012, ADR-019, ADR-021, ADR-028, ADR-038, ADR-052. **All compliant.**
  - ADR-001: no new endpoint; the changed route is still Minimal API.
  - ADR-007: no Graph.
  - ADR-008: filters unchanged.
  - ADR-010: no DI.
  - ADR-012: no shared-lib change.
  - ADR-019: ProblemDetails with stable `reasonCode`/`errorCode`.
  - ADR-021: Fluent v9 MessageBar, no hex.
  - ADR-028: indeterminate → retryable 503, never 403. The client still uses `bffApiCall`, with no raw token handling.
  - ADR-038: new tests at KEEP paths `integration/auth` and `integration/contract`; SPA tests under `tests/`; no banned mocks (the mocked resolver is the strategy's module boundary, and real resolver/binder cases sit alongside).
  - ADR-052: no background work.
- **ArchTests:** 2845/2845.

## UAC review
- Requested from uac-r2 on 2026-11-26 (diff summary of the resolver and registration changes, plus D-43/D-44). Answer: pending at this checkpoint.
