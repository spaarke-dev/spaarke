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

## Open at this checkpoint
- Reviews: code-review, adr-check, two adversarial passes (auth).
- Publish-size delta vs a fresh master build; CVE check; conflict check.
- UAC review via uac-r2 (send the diff summary); record it here and in the PR.
