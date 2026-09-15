# Task 027 — e2e tier reconciliation (M6): decision

**Decision: RETIRE.** `tests/e2e/specs/secure-project/project-closure-cascade.spec.ts` and
`tests/e2e/specs/secure-project/revocation.spec.ts` are deleted. Reconciliation was evaluated and rejected —
not for lack of effort, but because the evidence below shows reconciliation cannot produce a suite that runs
unattended in CI, and the invariants both files intended to protect are already covered by real, running
tests elsewhere.

This decision is recorded before either spec file was edited, per this task's own constraint.

---

## 1. Verification method

Per the task's binding constraint ("verify every column and navigation property against live Dataverse
metadata … before writing any $select/$filter/@odata.bind"), every claim below is checked against one of:

- **Live Dataverse schema** — `mcp__dataverse__describe` on `tables/sprk_externalrecordaccess` and
  `tables/sprk_project` (run today, 2026-09-04).
- **Current route registrations** — `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/*.cs` (grep + read).
- **Current response contracts** — the `Dtos/*Response.cs` records the handlers actually return.
- **Current authorization behavior** — `DelegationRuleFilter.cs`, read line-by-line for the exact status
  code each branch returns.
- **CI wiring** — every file under `.github/workflows/*.yml` (22 files), grepped for `e2e`/`playwright`.
- **TypeScript baseline** — `npx tsc --noEmit` run directly against both spec files.

No claim below is carried over from the source finding (M6) without being independently re-checked.

---

## 2. Is there a workflow that runs `tests/e2e/**`? No — and this is tier-wide, not file-specific.

Grepped all 22 files in `.github/workflows/*.yml` for `e2e`/`playwright` (case-insensitive): the only hit is
a comment in `sdap-ci.yml:182` referencing an unrelated "e2e audit finding" from another project — not an
invocation. `client-tests.yml` runs Jest only, and its header explicitly scopes out Playwright/e2e (line 15:
"one package has been red since 2026-06-11" refers to Jest packages; the workflow's own docstring says
nothing about `tests/e2e/**`). `ci-tier2-advisory.yml` (the job the task's notes said to prefer) has exactly
7 jobs — format, lint, full-unit-tests (.NET), adr-compliance, markdown-link-validator, last-reviewed-stamp,
plugin-size — none of which touch Playwright.

`package.json` at the repo root defines `test:e2e` / `test:e2e:ui` / `test:e2e:headed` / `test:e2e:debug`
scripts (lines 6–11) that invoke `playwright test --config=tests/e2e/config/playwright.config.ts`. No
workflow calls any of them. `tests/e2e/README.md`'s "CI/CD Integration" section (lines 336–364) is a
*hypothetical example* block, not a real workflow file — there is no corresponding file in
`.github/workflows/`.

This is **not** specific to the two files this task targets: **none** of the 29 spec files under
`tests/e2e/specs/**` (8 unrelated domains: outlook-addins, smart-todo, spe-file-viewer, universal-dataset-grid,
word-addins, quickcreate-flow, secure-project, secure-project-creation) run anywhere. That is a tier-wide gap,
not something this task can or should fix by itself (see §7).

---

## 3. `project-closure-cascade.spec.ts` — every Dataverse/BFF-touching contract is fictional

### 3.1 Routes — all 4 `BFF_ENDPOINTS` entries are missing `/v1`, and one doesn't exist at all

```typescript
// project-closure-cascade.spec.ts:78-83
const BFF_ENDPOINTS = {
  closeProject: '/api/external-access/close-project',
  grantAccess: '/api/external-access/grant',
  revokeAccess: '/api/external-access/revoke',
  userContext: '/api/external-access/user-context',
} as const;
```

Real routes, confirmed at `ExternalAccessEndpoints.cs:54,60,123,128,131,142`:

| Spec targets | Real route | Confirmed at |
|---|---|---|
| `/api/external-access/close-project` | `/api/v1/external-access/close-project` | `ExternalAccessEndpoints.cs:142` |
| `/api/external-access/grant` | `/api/v1/external-access/grant` | `ExternalAccessEndpoints.cs:128-129` |
| `/api/external-access/revoke` | `/api/v1/external-access/revoke` | `ExternalAccessEndpoints.cs:131-132` |
| `/api/external-access/user-context` | **no such route.** Real endpoint is `GET /api/v1/external/me` — a different route group (`externalGroup`, dual-scheme CIAM/workforce), not `external-access` (`adminGroup`, workforce-only) | `ExternalAccessEndpoints.cs:54,60` |

### 3.2 Response shape — 4 fields, 0 of which exist

```typescript
// project-closure-cascade.spec.ts:104-109
interface ProjectClosureSummary {
  projectId: string;
  revokedCount: number;
  contactsAffected: number;
  containerPreserved: boolean;
}
```

Real response, `Dtos/CloseProjectResponse.cs:17-20`:

```csharp
public record CloseProjectResponse(
    int AccessRecordsRevoked,
    int SpeContainerMembersRemoved,
    IReadOnlyList<Guid> AffectedContactIds);
```

Zero field-name overlap. This confirms M6's "asserts a response shape whose four fields have never
existed" exactly — it is 4 for 4.

### 3.3 A container-status endpoint that was never built

`project-closure-cascade.spec.ts:495-496` calls
`GET {bffBaseUrl}/api/external-access/projects/${testProjectId}/container` and asserts a `containerId` /
`exists` / `status` shape. Exhaustive grep of every `MapGet`/`MapPost`/`MapDelete` in
`Api/ExternalAccess/**` (both the `externalGroup` and `adminGroup` route tables) shows no `/container`
sub-route anywhere in this domain. This is not a stale path — the endpoint has no live counterpart to be
stale against.

### 3.4 Two more stale-column instances (of the review's tracked five) — confirmed against live metadata

`mcp__dataverse__describe tables/sprk_externalrecordaccess` (today):

```
sprk_contact LOOKUP (GUID) ( Related table : contact),
sprk_project LOOKUP (GUID) ( Related table : sprk_project),
```

There is no `sprk_projectid` or `sprk_contactid` attribute on `sprk_externalrecordaccess` at all — those
strings only exist as the *primary key* of the unrelated `sprk_project` table. The spec's FetchXML
(lines 409, 597) filters on exactly the wrong names:

```xml
<condition attribute="sprk_projectid" operator="eq" value="${testProjectId}" />
<condition attribute="sprk_contactid" operator="eq" value="${participant.contactId}" />
```

### 3.5 The project-creation payload uses two columns that don't exist

`mcp__dataverse__describe tables/sprk_project` (today) has no `sprk_name` and no `sprk_issecureproject`
attribute. The real fields are `sprk_projectname` (NVARCHAR(1000)) / `sprk_projectnumber` (NVARCHAR(850),
**required**) and `sprk_issecure` (BIT). The spec's `beforeAll` (lines 305-309):

```typescript
testProjectId = await dataverseApi.createRecord(ENTITIES.project, {
  sprk_name: `E2E Closure Test Project ${Date.now()}`,
  sprk_issecureproject: true,
  sprk_status: 'Active',
});
```

`sprk_status` is also not a real attribute (there's `statuscode`, an option set — not a free-text field named
`sprk_status`). A create against real Dataverse with these property names is rejected outright (unknown
property), so **every single test in the file that depends on `beforeAll` cannot progress past setup** — not
"might flake," cannot run, structurally, against the schema that exists today.

### 3.6 A full test block for a subsystem Spaarke does not manage

Lines 573–636 assert Power Pages web-role removal (`mspp_webrole`, `mspp_webrole_contacts`,
`WEB_ROLE_NAME = 'Secure Project Participant'`). `revocation.spec.ts`'s own header (lines 14-18, written by
task 017 / register H-8b) documents why this is dead: `WebRoleRemoved` was hard-coded `false` at every call
site because Spaarke does not manage Power Pages web roles, and the field was deleted from the
`RevokeAccessResponse` contract. `revocation.spec.ts` already deleted its two equivalent tests for this
reason; `project-closure-cascade.spec.ts` still carries the full block, targeting a mechanism that isn't
wired to anything live.

**Verdict for this file: not "drifted," authored against a design that was never implemented as described.**
Reconciling it is not fixing four contracts — it is re-authoring the file end-to-end, including SPA
`data-testid` selectors I have not verified exist (`workspace-home`, `access-denied`, `document-library`,
`search-result`, etc. — out of scope to verify given the file doesn't clear the more basic bars above).

---

## 4. `revocation.spec.ts` — closer to correct, but not clean, and blocked on the same live-environment problem

This file is materially better: routes carry `/v1` (`revocation.spec.ts:139,160` —
`/api/v1/external-access/revoke`, `/api/v1/external-access/grant`, both confirmed correct against
`ExternalAccessEndpoints.cs`), and its FetchXML helper already uses the correct lookup names:

```typescript
// revocation.spec.ts:186-198 (queryAccessRecords) — CORRECT
<condition attribute="sprk_contact" operator="eq" value="${contactId}" />
<condition attribute="sprk_project" operator="eq" value="${projectId}" />
```

But it has two real, confirmed defects:

### 4.1 Same fictional project-creation columns (§3.5) — `makeTestProject()` at lines 81-89

```typescript
function makeTestProject() {
  return {
    sprk_name: `E2E Revocation Test Project ${ts}`,
    sprk_issecureproject: true,
    statecode: 0,
    statuscode: 1,
  };
}
```

Called by `setupGrantedAccess()`, which every non-mocked test in the file depends on. Same failure mode as
§3.5: a create against these property names does not succeed against live Dataverse.

### 4.2 Two of three error-case assertions are stale — confirmed by reading `DelegationRuleFilter.cs` directly

`revocation.spec.ts:518-535`:

```typescript
test('returns 400 when accessRecordId is empty GUID', ...) => expect(result.status).toBe(400);
test('returns 404 when access record does not exist', ...) => expect(result.status).toBe(404);
```

`DelegationRuleFilter.FromAccessRecordAsync` (`DelegationRuleFilter.cs:271-304`) runs **before** the
handler, on every route in the `adminGroup`:

```csharp
if (revoke.AccessRecordId == Guid.Empty)
{
    return null;   // → target unresolved
}
var row = await ExternalGrantLifecycle.RetrieveRowAsync(dataverseClient, revoke.AccessRecordId, ct);
if (row is null)
{
    logger.LogWarning("... Denying (403, not 404 — a 404 here would let an unauthorized caller " +
                       "enumerate access-record ids).", ...);
    return null;   // → target unresolved
}
```

`ResolveTargetAsync` returning `null` makes `InvokeAsync` (`DelegationRuleFilter.cs:141-150`) return
**403** (`Deny(httpContext, DenyTargetUnresolved, ...)`) — the handler's own `400`/`404` logic
(`RevokeExternalAccessEndpoint.cs:80-81,116-122`) never executes for either case, because the filter denies
first. The file's own header comment explains why this specific class of mistake happened twice: "that file
was edited twice during task 017 and the error-case block was missed both times" — confirmed by two
independent .NET test suites that assert the correct behavior directly:
`tests/integration/auth/UnifiedAccessControl/DelegationRuleCharacterizationTests.cs:244,263` and
`tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs:250,344,446,604`,
all asserting `reasonCode: sdap.access.deny.delegation_target_unresolved` with **403**.

The third error case (`returns 401 when no authorization token is provided`, line 537) is still correct —
`RequireAuthorization()` runs in ASP.NET Core's authorization middleware, ahead of endpoint filters, so an
unauthenticated request never reaches `DelegationRuleFilter` at all.

### 4.3 What reconciling `revocation.spec.ts` alone would still not solve

Even with §4.1 and §4.2 fixed, the file cannot run unattended in CI. Its non-mocked tests authenticate via
`DataverseAPI.authenticate(TENANT_ID, CLIENT_ID, CLIENT_SECRET, ...)` — an app-only `client_credentials`
token (`revocation.spec.ts:218-227`) — and hand that token straight to `/api/v1/external-access/grant` and
`/revoke` as the caller's bearer token. `DelegationRuleFilter` evaluates the caller's rights via
`CallerRecordAccessProbe.GetCallerRightsAsync`, which is an **OBO** exchange
(`DelegationRuleFilter.cs` class remarks, line 76: "an app-only Write probe would answer 'can the
application write', which is finding A-2"). An app-only `client_credentials` token has no delegated user to
run OBO against, and no Dataverse record-level rights of its own to be found by an impersonated-caller
check. There is no evidence in this codebase that this ever worked — nothing in the file, in
`tests/e2e/config/.env.example`, or in any workflow shows a mechanism for minting a real, short-lived
*user*-delegated token unattended. This is precisely the condition the task's own escalation trigger names:
*"If reconciling requires a live environment the CI runner cannot reach, that argues for retirement."* The
blocker here is not network reachability of `BFF_API_URL` — it is that the auth model these tests assume
(a bearer token good enough to mint an app-only token) was superseded by task 008's delegation rule, and no
part of this suite (or the repo's CI secrets/workflows) was ever updated to produce the credential the new
model requires.

The `@mock` describe block (lines 561-787) is the one part of the file that needs no live environment at
all — but it replaces the BFF entirely with `page.route()` interception, so it never executes any Spaarke
server code; it only proves that `fetch()` round-trips a hand-authored JSON blob through `window.__*`
globals. That is the review's own "seam-shadowing" pattern (§1 of the finding doc): "the test substitutes
the method whose internals are the thing under test." Keeping it, wired into CI on its own, would be
manufacturing the appearance of coverage this task exists to stop — it would go green forever and catch
nothing in `Sprk.Bff.Api`.

---

## 5. Coverage lost, and who owns it now

Both `.NET` characterization/integration suites the review already pointed to as replacements
(`revocation.spec.ts:458-461`'s own in-file comment) exist, are current, and run in CI today
(`tests/integration/auth/**` is an ADR-038 KEEP path, exercised by `ci-tier1-blocking.yml` and
`ci-tier2-advisory.yml`'s `full-unit-tests` job):

| e2e intent | .NET test that already covers it | Location |
|---|---|---|
| Closure deactivates every grant, contact + org combinations | `CloseProject_WithContactAndOrganizationGrants_Returns200AndDeactivatesEveryGrant`, `CloseProject_WithOnlyAnOrganizationGrant_DeactivatesIt`, `CloseProject_WithPersonAndOrganizationGrantsOnTheSameFirm_DeactivatesBoth` | `tests/integration/auth/UnifiedAccessControl/ProjectClosureCascadeTests.cs:285,306,325` |
| Closure is idempotent | `CloseProject_CalledTwice_IsIdempotent` | same file, line 642 |
| Closure doesn't sweep another project | `CloseProject_DoesNotDeactivateGrantsOnAnotherProject` | same file, line 620 |
| Container-clear reporting (incomplete vs. complete) | `CloseProject_WhenTheContainerCannotBeCleared_ReportsIncompleteWithTheRevokedCount`, `CloseProject_WhenSomeContainerMembersRemain_ReportsIncomplete`, `CloseProject_WhenTheContainerIsFullyCleared_Returns200WithTheRemovedCount` | same file, lines 504,534,556 |
| Cache invalidated per affected contact | `CloseProject_InvalidatesTheParticipationCacheForEachAffectedContact` | same file, line 707 |
| **Live-schema guard for the exact defect class M6 is about** | `ActiveGrantSelect_NamesOnlyColumnsThatExistOnTheTable` | same file, line 351 |
| Revoke doesn't sweep another contact's grant on the same root | `Revoke_DoesNotDeactivateAnotherContactsGrantOnSameRoot` | `tests/integration/auth/UnifiedAccessControl/GrantLifecycleCharacterizationTests.cs:402` |
| Revoke of nonexistent grant | `Revoke_OfNonexistentGrant_ReturnsNotFound` | same file, line 475 |
| Duplicate-row sweep on revoke | `Revoke_WithPreExistingDuplicateRows_DeactivatesEveryOne` | same file, line 342 |
| The exact 403/`delegation_target_unresolved` behavior §4.2 shows the e2e file got wrong | `DelegationRuleCharacterizationTests.cs:244,263`; `ExternalAccessIntegrationTests.cs:250,344,446,604` | `tests/integration/auth/UnifiedAccessControl/` and `tests/integration/Spe.Integration.Tests/ExternalAccess/` |

**New owner: `tests/integration/auth/UnifiedAccessControl/**` and
`tests/integration/Spe.Integration.Tests/ExternalAccess/**`.** These are KEEP paths under ADR-038 §2
(`tests/integration/auth/**` = security-auth category), so deletion of a file there would itself require a
same-PR replacement — the protection these two e2e files were meant to provide already lives in a place the
ADR actively defends, and that place runs in CI. Deleting the two never-run e2e duplicates does not create a
coverage gap; it removes a second, broken, silent copy of coverage that already exists correctly elsewhere.

**Genuinely not covered by the .NET suites**: the browser/SPA-level assertions in
`project-closure-cascade.spec.ts` (does the Power Pages SPA actually hide the closed project card, does
semantic search actually stop returning its documents in the rendered UI). That is real, currently-absent
UI-level coverage — but it was never real coverage in this file either, since the file cannot reach that
code today (its `beforeAll` cannot create a test project). If browser-level UI verification of closure is
wanted, it needs a fresh write against the current SPA and current contracts, wired into a job that runs —
which is a new task, not a fix to this file.

---

## 6. Acceptance criterion check — "no stale column name remains in `tests/e2e/**`"

Grepped `sprk_projectid|sprk_contactid|sprk_issecureproject|sprk_name` across the whole `tests/e2e/**` tree.
Besides the two files retired here, two other files matched, and both are false positives on inspection:

- `tests/e2e/specs/secure-project-creation/secure-project-creation.spec.ts` — matches are `sprk_projectid`
  used **correctly** as `sprk_project`'s own primary key (`$filter=sprk_projectid eq ${projectId}` against
  the `sprk_projects` collection itself, not as a lookup attribute on a different table). The file's own
  comment (line 311) states "Live columns only (verified 2026-08-25)," and it independently uses
  `sprk_projectname` / `sprk_issecure` — the correct names. No defect.
- `tests/e2e/specs/secure-project/access-level-enforcement.spec.ts` — the one match is `sprk_name` on a
  **mocked document fixture** (`sprk_documentid: MOCK_DOCUMENT_ID, sprk_name: 'Mock Contract.pdf'`), an
  unrelated entity (`sprk_document`, not `sprk_project`) inside `page.route()` test data, not a live
  Dataverse call. Its BFF route reference (`/api/v1/external-access/invite`) already carries the correct
  prefix. No defect of the kind M6 describes.

No further files in `tests/e2e/**` need changes to satisfy this criterion.

---

## 7. What was NOT touched, and why

- **`tests/e2e/utils/dataverse-api.ts`, `tests/e2e/config/playwright.config.ts`** — shared across the whole
  e2e tier (used by `secure-project-creation.spec.ts` and others). Not scaffolding specific to the two
  retired files.
- **`tests/e2e/config/.env.example`** — checked; it has zero entries for either retired file (no
  `TEST_CORE_USER_TOKEN`, `TEST_EXTERNAL_CONTACT_ID_*`, `TEST_EXTERNAL_PORTAL_TOKEN_*`,
  `TEST_SPE_CONTAINER_ID`). Its `secure-project/` sections are explicitly labeled for
  `invitation-onboarding.spec.ts` and `access-level-enforcement.spec.ts` (both untouched, both out of this
  task's scope). This is itself evidence the two retired files never had a documented local-run path
  either — not even manual execution was set up for them.
- **`tests/e2e/reporting/smoke-test-checklist.md`, `README.md`, `scenarios/*.md`** — unrelated domains
  (Reporting/Power BI, PCF grid refresh). No reference to either retired file.
- **`.github/workflows/**`** — under a freeze owned by another project (`projects/INDEX.md` "SHADOW WINDOW
  OPEN"). Not edited. See §8.
- **The other two `secure-project/` specs** (`access-level-enforcement.spec.ts`,
  `invitation-onboarding.spec.ts`) and **`secure-project-creation.spec.ts`** — not in this task's
  `<relevant-files>` or `<outputs>`, and §6 confirms they don't share this task's specific defect. They
  remain un-wired-to-CI along with the rest of the tier (see §8) — a pre-existing condition, not something
  this task introduced or is scoped to fix.
- **Historical notes referencing the retired files by name** (`notes/task-017-spe-revoke-matcher.md`,
  `notes/task-020-org-grant-spe-cleanup.md`, `notes/investigation/04-grant-lifecycle.md`,
  `notes/review-2026-08-24-findings.md`) — accurate records of what was true when written. Not rewritten.

---

## 8. Flagged, not fixed: the e2e tier is tree-wide un-wired

All 29 spec files under `tests/e2e/specs/**` — not just these two — run in no workflow (§2). That is a
pre-existing condition this task did not create and is not scoped to repair (the POML names exactly these
two files). It is named here so it isn't lost: if a future task wires *any* part of `tests/e2e/**` into CI
(the Tier 2 advisory job, per the 2026-08-24 CI decision this task's notes point to), it will need
`npx playwright install` browser provisioning, a genuinely reachable target environment, and — per §4.3 — a
credential-minting story for the delegation-rule auth model that none of the currently-authored specs have.
That is new infrastructure work, not a reconciliation of existing specs, and is out of this task's scope.

---

## 9. TypeScript baseline (acceptance criterion 4)

`npx tsc --noEmit` against both files (no project tsconfig exists for `tests/e2e/**`; ran directly against
the two `.spec.ts` files) reproduces **exactly 9** `TS2339` errors of the form
`Property '__revokeStatus'/'__revokeBody'/'__firstStatus'/'__secondStatus' does not exist on type 'Window &
typeof globalThis'` — matching the task's stated "9 pre-existing window.__* TS2339 errors are known" count
exactly. All 9 are in `revocation.spec.ts` (lines 593, 594, 633, 634, 668, 669, 708, 763, 779); zero are in
`project-closure-cascade.spec.ts`. Deleting both files removes all 9 along with the files that produced
them — the acceptance criterion ("introduces no new errors beyond the 9 known") is satisfied by
construction: 0 ≤ 9, and no new error class is introduced anywhere else.

---

## 10. Summary for the reviewer

| # | Contract pinned by the e2e tier | Live status | Evidence |
|---|---|---|---|
| 1 | 4 BFF routes (`BFF_ENDPOINTS`) | 3 of 4 missing `/v1`; 1 (`user-context`) doesn't exist at all | §3.1 |
| 2 | `ProjectClosureSummary` response shape (4 fields) | 0 of 4 fields exist on the real `CloseProjectResponse` | §3.2 |
| 3 | `GET .../projects/{id}/container` | Route was never built | §3.3 |
| 4 | `sprk_projectid` / `sprk_contactid` FetchXML filters | Neither attribute exists; real names are `sprk_project` / `sprk_contact` | §3.4 |
| 5 | `sprk_name` / `sprk_issecureproject` project-creation columns (both files) | Neither exists; real names are `sprk_projectname`/`sprk_projectnumber` / `sprk_issecure` | §3.5, §4.1 |
| 6 | 400/404 error-case assertions in `revocation.spec.ts` | `DelegationRuleFilter` returns 403 for both cases today | §4.2 |
| — | Any workflow executing `tests/e2e/**` | None — tier-wide, all 29 spec files | §2 |
| — | A credential model that lets these specs run unattended | None — the delegation rule requires an OBO-capable user token; only an app-only token is ever produced | §4.3 |

Six independently-verified broken contracts, zero CI wiring anywhere in the tier, and a structural
auth-model mismatch that blocks even a perfectly reconciled file from running unattended. The behavioral
invariants both files intended to protect are already covered, more precisely, by real KEEP-path `.NET`
tests that run today. Retirement is the correct call under this task's own escalation trigger, and matches
the "do not split the difference" instruction: fixing the six contracts without a way to run the result
would reproduce the exact defect this task exists to close.
