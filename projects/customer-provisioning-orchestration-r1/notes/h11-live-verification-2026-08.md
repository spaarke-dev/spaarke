# H11 Live Verification Report — task 144 (2026-08-20)

> **Scope**: DS-4 §2 classified `H11UserProvisioningHandler`'s three REST/Graph collaborators as
> "✅ REAL — Graph REST provisioner + B2B invitation + consent verifier. ~0 code. C5.8 grants
> (User.ReadWrite.All, User.Invite.All etc.)." with task 111's C5.8 grants as the only documented
> blocker. This task performs the live verification pass DS-4 called for, following task 143's
> H10-verify template — not a re-implementation.

## 1. The 3 REST/Graph seams — enumeration + verification status

| # | Seam (production class) | Operation | Verification status |
|---|---|---|---|
| 1 | `GraphRestUserProvisioner` | NativeAccount: `CreateUserAsync` (UPN idempotency-check GET + `POST /users`), `AssignLicenseAsync` (`POST /users/{id}/assignLicense`) | **REST shape live-verified** (idempotency-check GET `/users?$filter=userPrincipalName eq '...'` confirmed live — real, correct empty-result shape). `POST /users` + `POST /assignLicense` **request/response shapes ground-truthed against Microsoft Learn** (exact field-name match — §4). Write paths **deferred to live-ceremony** (§3) — creating a real Entra user is not safely automatable in this sandbox. |
| 2 | `GraphRestB2BInvitationClient` | B2BGuest: `InviteAsync` (`POST /invitations`) | **REST shape ground-truthed against Microsoft Learn** (§4) — exact field-name match, including least-privileged permission (`User.Invite.All`). Write path **deferred to live-ceremony** (§3) — sending a real invitation email to an arbitrary address is not safely automatable. |
| 3 | `GraphRestB2BConsentVerifier` | B2B consent gate: `VerifyAsync` (`GET /users/{id}?$select=externalUserState` per invited guest) | **Fully live-verified end-to-end, both branches**, via new durable xUnit smoke tests (`H11SeamsSmokeTests.cs`) AND via direct REST calls made during authoring: `Pending` (unknown/never-invited guest id → Graph 404 → correctly folded to Pending, never Verified) and `Verified` (a real existing accepted guest, `ad268fcd-ac34-4e40-b63f-dacdc849fcbb`, `externalUserState=Accepted`, confirmed live). Fully read-only — no write-path caveat. **This is the escalation-trigger-relevant seam** (POML trigger #2: false-Pass-on-pending would be a HIGH-severity defect) and it is the one fully exercised live. |

**Live-tested vs fake-tested vs deferred, summarized**: seam 3 (the consent gate, fully read-only)
is **fully live-verified end-to-end** — both outcome branches, via genuine live Graph calls in
THIS sandbox (unlike H10's smoke tests, which soft-skipped on `DefaultAzureCredential` — see §5).
Seams 1 and 2's read-shape (idempotency GET) and full request/response shapes are verified via a
combination of live REST calls + Microsoft Learn citation (§4); their WRITE components (`POST
/users`, `POST /assignLicense`, `POST /invitations`) are **deferred to live-ceremony** (§3).

## 2. Bonus catch — missing `User.Invite.All` grant (found via cross-reference, FIXED, MAJOR)

**Finding**: H11's B2BGuest branch (`GraphRestB2BInvitationClient.InviteAsync`) issues
`POST /invitations`, whose least-privileged app-only permission is `User.Invite.All`
(Microsoft Learn, `learn.microsoft.com/graph/api/invitation-post` — Permissions table, confirmed
live via WebFetch during this task). The pre-existing 15-minus-1 = **14**-role catalog
(`GraphAppRoles.cs`, populated by r1 task 005 on 2026-08-17, mirrored to L2 by
`L2GraphAppRolesRegistry.cs`) did **NOT include `User.Invite.All`** — it was never in the 14-role
list at all (verified by full enumeration of every constant in `GraphAppRoles.cs`).

**Impact if left unfixed**: once task 111's C5.8 grants are live-executed, the L2 UAMI would hold
`User.ReadWrite.All` (sufficient for H11's NativeAccount branch's `POST /users` +
`POST /assignLicense`, per Microsoft Learn's "higher privileged permissions" column, and for the
consent-verifier's `GET /users/{id}`) but **NOT** `User.Invite.All` — every B2BGuest-preset H11
run would receive a **permanent 403 Forbidden** from Graph on the invitation POST, with no
retry-recoverable path (the grant catalog itself is the blocker, not a transient failure). This is
precisely the failure class task 144's own escalation trigger #1 names: *"a signal that either
task 111's grant scope is wrong or the classification needs correction."*

**Root cause**: the DS-4/DS-5 design intent (`design-study-ds5-cat456-remediation.md` line 78:
*"Roles needed derive from the in-process handler set (H10/H11: ... user-management roles; exact
list from `GraphAppRoles.cs`...)"*) anticipated H11 as a consumer of this catalog, but the catalog
itself was built out under task 005 against the (then-current) BFF's own Self-Service Registration
subsystem needs — which does NOT include B2B invitations (the BFF's own registration flow moved to
a CIAM broker-only model per ADR-028 Amendment A1 / `CiamUserProvisioningService.cs`, which
explicitly does NOT issue B2B guest invitations). H11's B2BGuest preset is a genuinely new
consumer of the shared catalog that the 2026-08-17 GUID-completion pass did not anticipate.

**Fixed** (same commit as this task, same discipline as task 143's `GroupMember.ReadWrite.All`
GUID fix):
- `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs` — added `UserInviteAll`
  constant + `IdUserInviteAll` + a new `GraphAppRole` entry (category "Customer Provisioning",
  `ModuleConditional=true` since it is specific to the B2BGuest preset). Catalog is now
  **15 roles**.
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/DataverseAppUserGraphParity/L2GraphAppRolesRegistry.cs`
  — mirrored addition.
- **GUID ground-truthed live** (same methodology as task 005/143): `GET
  /v1.0/servicePrincipals?$filter=appId eq '00000003-0000-0000-c000-000000000000'&$select=appRoles`
  against the real Microsoft Graph resource SP in tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`
  returned `User.Invite.All` = `09850681-111b-4a89-9bed-3f2cae46d706` — used verbatim.
- Re-verified: `L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` (task 067's unconditional
  mirror-parity test) **PASSES** post-addition — the two catalogs remain byte-identical (confirmed
  by live `dotnet test` run this task, §7).
- Updated every stale "14"/"14-role" reference this addition touched: `GraphAppRoleParityTest.cs`
  (nightly), `H10DataverseAppUserGraphParityHandlerTests.cs` (`AC16`, renamed off the now-inaccurate
  `...EnumeratesAll14PopulatedGuids` method name), `H10DataverseAppUserGraphParityHandler.cs` (2
  doc/diagnostic-string mentions), `IGraphAppRolesRegistry.cs` (3 doc mentions), `Program.cs` (2 doc
  mentions), `spec.md` FR-33 (T3 acceptance text), `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`
  (T3 operator command comment). Grep-collision self-check: none of these edits introduced a
  literal `pac ` / `ProcessStartInfo` / other acceptance-criterion trap string.

**Consolidation note (CLAUDE.md §11 default-to-reuse)**: the new role was added to the SAME shared
`GraphAppRoles.cs` catalog H10 already grants wholesale onto every UAMI (per that file's own
"Grant target" note — the operator/H10 tooling replays the FULL catalog regardless of BFF-side
`ModuleConditional` filtering) rather than inventing a second, H11-specific grant list — extending
the existing single source of truth, not creating a new one.

## 3. Why write-path E2E (creating a real Entra user / sending a real invitation email) was NOT attempted live

1. **`POST /users` creates a REAL Entra ID user** with a generated UPN + temporary password in the
   target tenant. There is no clean, safe, unattended undo for an automated test in a shared dev
   tenant (same class of reasoning H10's report §3.1 gives for Dataverse App Users).
2. **`POST /invitations` sends a REAL invitation EMAIL** to whatever address is passed as
   `invitedUserEmailAddress`. An automated test cannot safely choose a target address without
   either spamming a real external inbox or depending on a disposable mailbox this sandbox does
   not have access to provision.
3. **Task 111's `Grant-ControlPlaneIdentity.ps1` (C5.8) has NOT been live-executed** (its own
   `<notes-completion>`: *"Live-exec verification: DEFERRED"*). Even setting aside (1) and (2), the
   L2 UAMI does not yet hold the Graph app-role grants (including the `User.Invite.All` role this
   task just added) a production H11 run depends on — a live write attempt today would exercise a
   configuration state that does not match what a production run will actually see.

This follows the SAME "live-ceremony vs authoring separation" pattern task 143 (H10) and this
project's `current-task.md` (task 089/108/110/113 precedent) already established: authoring +
read-path live verification complete now; write-path E2E is grouped into the live-ceremony operator
run.

## 4. B2B invitation + user-creation REST shapes — ground-truthed against Microsoft Learn

Fetched live during this task (not guessed, per Wave G discipline):

- **`POST /invitations`** (`learn.microsoft.com/graph/api/invitation-post`): request body fields
  `invitedUserEmailAddress` (required), `inviteRedirectUrl` (required), `invitedUserDisplayName`
  (optional), `sendInvitationMessage` (optional) — **exact match** to
  `GraphRestB2BInvitationClient.InviteAsync`'s payload. Response shape `{ id, invitedUser: { id,
  userPrincipalName }, inviteRedeemUrl, status }` — **exact match** to the production parsing
  (`id` + `invitedUser.id`). Least-privileged app-only permission: **`User.Invite.All`** — the
  permission the §2 bonus catch adds to the grant catalog.
- **`POST /users`** (`learn.microsoft.com/graph/api/user-post-users`): request body fields
  `accountEnabled`, `displayName`, `mailNickname`, `userPrincipalName`, `passwordProfile
  { forceChangePasswordNextSignIn, password }` — **exact match** to
  `GraphRestUserProvisioner.CreateUserAsync`'s payload (the additional `givenName`/`surname`/
  `companyName`/`usageLocation` fields the production code sends are legitimate optional writable
  `user` resource properties, not fabricated).
- **`POST /users/{id}/assignLicense`** (`learn.microsoft.com/graph/api/user-assignlicense`):
  request body `addLicenses[].skuId` + `removeLicenses[]` — **exact match** to
  `AssignLicenseAsync`'s payload. Least-privileged permission `LicenseAssignment.ReadWrite.All`,
  but `User.ReadWrite.All` is explicitly listed as a covering "higher privileged" alternative — the
  L2 UAMI's existing `User.ReadWrite.All` grant is sufficient; no separate license-assignment
  permission gap exists.
- **`GET /users/{id}?$select=externalUserState`** (the consent verifier): live-verified directly
  (§1 seam 3) rather than via docs — the strongest possible ground-truthing.

## 5. Sandbox `DefaultAzureCredential` behavior — BETTER than H10's precedent for this seam

H10's smoke tests (task 143) soft-skipped in this sandbox because their collaborators construct
`DefaultAzureCredential` in a way that hits `ManagedIdentityCredential`'s IMDS-unreachable failure
before any fallback. **`GraphRestB2BConsentVerifier`'s own live smoke tests in THIS task actually
completed successfully** (not soft-skipped) — `DefaultAzureCredential` resolved a real, working
token in ~23s in this sandbox and both `H11SeamsSmokeTests` assertions ran against genuinely live
Graph responses (confirmed: the tests reached their `Should().BeOfType<...>()` assertions, which
only happens after `IsCredentialAcquisitionFailure` did NOT trigger). This is a genuinely stronger
live-verification outcome than H10 achieved for its own equivalent seams, though the underlying
`DefaultAzureCredential` construction is identical in shape (§4D I5 explicit-tenant pattern) —
credential-chain resolution appears to be non-deterministic/session-dependent in this sandbox
across the two tasks (the NightlyTests project's `GraphServiceClient`-based test, run separately in
§7, DID hit the IMDS failure — same sandbox, same session, different code path through the Graph
SDK vs raw `HttpClient` + `Azure.Identity` directly). Both outcomes are consistent with task 143's
own finding: this is a sandbox-only credential-chain characteristic, not a production concern (a
real Azure-hosted Worker's managed identity resolves immediately).

## 6. Consent verifier reuse status — H3's `GraphAdminConsentVerifier` vs H11's `GraphRestB2BConsentVerifier`

**Confirmed distinct, NOT a missed-reuse opportunity** (CLAUDE.md §11 three-question check
performed):

- **Existing**: H3's `GraphAdminConsentVerifier` (`IAdminConsentVerifier`) queries a service
  principal's `oauth2PermissionGrants` to confirm **tenant-admin consent for the BFF app
  registration's delegated scopes** — an app-registration-level consent gate.
- **Extension**: cannot extend — H11's `GraphRestB2BConsentVerifier` queries an individual invited
  **user's** `externalUserState` to confirm **B2B guest invitation redemption** — a completely
  different Graph resource (`/users/{id}`, not `/servicePrincipals/{id}/oauth2PermissionGrants`)
  answering a different question (has this specific guest accepted their invite, not has the
  tenant admin consented to the app). The two verifiers share only the "Verified vs Pending" result
  shape (documented explicitly in `IB2BConsentVerifier.cs`'s own header: "Shape mirrors H3's
  IAdminConsentVerifier"), which is the correct level of reuse (parity of pattern, not merged
  implementation across genuinely different Graph resources).
- **Cost-of-doing-nothing** (i.e., of NOT merging them): N/A — they answer different questions
  against different resources; forcing them into one verifier would require a discriminated
  request shape with no shared logic, adding complexity without reducing surface.

**Conclusion**: H11 correctly has its own consent verifier. No consolidation recommended.

## 7. Test suite deltas

| Project | Before | After | Delta |
|---|---|---|---|
| `Sprk.Provisioning.ControlPlane.Tests` (L2, CI-gated) | 1003/1003 | **1005/1005** | +2 (`H11SeamsSmokeTests.cs`) |
| `Sprk.Provisioning.ControlPlane.NightlyTests` (nightly-only, not CI-gated) | 3 tests | 3 tests | 0 (no new nightly test added this task; existing `L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` re-confirmed passing post-15-role-addition; `GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions` hits the pre-existing sandbox-only IMDS limitation task 143 already documented — not a regression) |

Both new `H11SeamsSmokeTests` facts were run **genuinely live** during authoring (env vars set,
real Graph calls against tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`) AND confirmed to
soft-skip cleanly with env vars unset (default CI posture) — 4ms total, zero live calls attempted.

`H11UserProvisioningHandlerTests.cs`'s pre-existing test suite (per DS-4, already passing) was
**not modified** and continues to pass unmodified as part of the 1005/1005 full run.

## 8. Deferred, documented (not fixed) — `H11UserProvisioningOptions` deployment-wiring gap

**Finding**: `H11UserProvisioningOptions` (`AccountDomain`, 3 license SKU ids,
`InvitationRedirectUrl`, `GraphRequestTimeout`) has **zero Bicep wiring** anywhere in
`infrastructure/bicep/` (confirmed via grep — zero matches), and is bound via the OLD bare
`Configure<T>()` pattern in `Program.cs` rather than the NFR-05
`AddOptions().Bind().Validate().ValidateOnStart()` fail-fast pattern tasks 122/123/132/142/141
established for their own options classes in the same wave. `AccountDomain` defaults to
`"spaarke.onmicrosoft.com"` — the **Spaarke tenant's own domain**, not a customer's.

**Assessed impact**: NOT a silent-fail risk in practice — Microsoft Graph enforces that a UPN's
domain suffix must be a **verified domain in the tenant the POST authenticates against**. Since
H11's credential is scoped to the CUSTOMER's tenant (`tenantId` parameter, §4D I5), a UPN ending in
`spaarke.onmicrosoft.com` would almost certainly be **rejected by Graph with a loud 400** in any
real customer tenant — a fail-loud config-completeness gap, not a fail-silent one. Still, it is a
genuine gap: every NativeAccount-preset H11 run against a real customer will fail until this is
wired.

**Not fixed here** — Bicep wiring is a new-component-sized change (new Bicep params, new app
settings threaded through `customer.bicep` → `controlplane-worker-app-service.bicep`, its own
Placement Justification) out of proportion to a verification-scoped task, and a NFR-05 `Validate()`
addition would not meaningfully help here (all 5 fields have non-null defaults, so a
null/empty-check `Validate()` would never fire — the defect class is "wrong value in play", which
options-validation cannot detect without per-customer context). **Recommend as a follow-up task**
(thread `H11UserProvisioningOptions:AccountDomain`/SKU ids/`InvitationRedirectUrl` as Bicep app
settings, parity with task 142's `EnvVarValues__ClientSecret` wiring) — flagged, not applied here,
consistent with task 143's own precedent for its 2 out-of-scope wrong-GUID doc occurrences.

## 9. Cross-tenant MI-token limitation — confirms existing project-level understanding extends to H11

`design.md` (line 1234, 2026-08-19) already documents: *"Cross-tenant: MI tokens are home-tenant-only
— an enforcement of registry-writes-are-admin-env-only, not a limitation. The sanctioned future
cross-tenant path (customer-owned-tenant Model 2 handler writes) is MI-as-FIC on a multitenant
app-reg (Path Z, GA) — noted for r2+, not built in r1."* H11's `DefaultAzureCredential(TenantId=
customerTenantId)` construction (identical shape to H10's) is subject to the SAME limitation: for
**Model 1** customers (shared Spaarke tenant), `tenantId` equals the L2 UAMI's own home tenant, so
MI token acquisition works correctly; for **Model 2 customer-owned-tenant** deployments, MI tokens
cannot cross tenant boundaries — this is a pre-existing, already-accepted r1/r2 scope boundary
(Path Z deferred), not a new defect this task introduces or needs to fix. Documented here only to
confirm H11's own cross-tenant behavior is governed by the SAME already-understood constraint, not
a fresh one.

## 10. Escalation-trigger check

- **Trigger 1** ("EITHER identity-preset branch fails live verification"): no branch **failed** —
  every REST shape that could be safely exercised (consent-verifier, both branches; idempotency-
  check GET) succeeded live; the genuine issue found (§2, missing `User.Invite.All` grant) is a
  **catalog completeness** defect, fixed in this same commit, not a live orchestration-logic
  failure in H11 itself. Per task 143's own precedent for the analogous H10 finding, a fixed
  catalog defect discovered via live cross-reference is handled by fixing it in-commit, not by
  treating the fix itself as an escalation-worthy live failure.
- **Trigger 2** ("consent verifier reports Pass for a genuinely-pending consent — false positive"):
  **directly tested and NOT observed.** The Pending-branch smoke test (§1 seam 3) proves an
  unknown/unaccepted invited-guest id is correctly folded to `Pending`, never `Verified`, both by
  live execution and by code inspection of `GraphRestB2BConsentVerifier.VerifyAsync`'s
  `!response.IsSuccessStatusCode → pendingIds.Add(userId)` fail-closed branch (a 404/error response
  is never silently treated as accepted). No false-positive risk found.

Both escalation triggers are cleared. The one genuine defect found (§2) was fixed, not escalated,
following the exact precedent task 143 set for the analogous H10 finding.
