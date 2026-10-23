# Task 045 — BFF WorkforceHome scheme (ADR-028 A9), census ArchTest, must-reject tests

Date: 2026-10-14. Rigor FULL (bff-api, auth, security, testing). Design: `notes/a9-bff-design.md` §2, §9.
Adversarial passes (two, auth): run by the main session, not in this implementer run.

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` | `AddWorkforceHomeScheme`: `AddMicrosoftIdentityWebApi(…, AuthSchemes.WorkforceHome)` on `https://login.microsoftonline.com/organizations/v2.0`, audiences = stamp BFF app id (bare + `api://`), identity `AuthenticationType` = `WorkforceHome`; `PostConfigure` chains the A9 allowlist into `OnTokenValidated` (`WorkforceHomeDenyReason`, reasons in `WorkforceHomeDeny`). `ExternalCollaboration` policy gains `AuthSchemes.WorkforceHome`. |
| `.../Infrastructure/Authentication/AuthSchemes.cs` | `WorkforceHome` constant. |
| `.../Infrastructure/Authentication/AuthPolicies.cs` | Doc comment only: `ExternalCollaboration` names WorkforceHome. |
| `.../Api/ExternalAccess/ExternalAccessEndpoints.cs` | Header comment only (the group's policy now has three schemes). |
| `tests/unit/.../Infrastructure/DI/AuthorizationModuleWorkforceHomeTests.cs` | New: 32 behavioural tests (real schemes, TestServer, locally signed tokens, offline Entra metadata). |
| `tests/Spaarke.ArchTests/AuthenticationSchemeCensusTests.cs` | New: scheme census, WorkforceHome-on-ExternalCollaboration-only, pinned route list (54 routes). |
| `tests/Spaarke.ArchTests/BootedBff.cs`, `RouteAuthorizationGuardTests.Runtime.cs` | `BootedBffCollection` shared collection fixture; two helpers `private` → `internal` for reuse by the census. |
| `tests/integration/auth/UnifiedAccessControl/ExternalCollaborationTestFixture.cs` | Comment only (scheme count). |

`WorkforceIdentityOptions.cs` is NOT modified: the scheme reads `CustomerTenantIds` through
`IOptionsMonitor<WorkforceIdentityOptions>.CurrentValue.ParsedCustomerTenantIds()` per request.

## Design decisions

- **Library layer untouched (design §9).** The issuer validator is Identity Web's `AadIssuerValidator` (not replaced; no
  `ValidIssuers`). Identity Web 4.14.2 `AddMicrosoftIdentityWebApiImplementation` calls
  `options.TokenValidationParameters.EnableAadSigningKeyIssuerValidation()` in the per-scheme `Configure` it registers for
  ANY scheme name (source read at tag 4.14.2). Escalation trigger 2 therefore does NOT fire; the behaviour is also proven
  by `SignedByKeyWhoseIssuerIsAnotherDirectory_Returns401` (asserts IDX40005) and its positive control.
- **Allowlist order:** tid (non-zero GUID, `D` format) → ≠ Spaarke tenant → ≠ CIAM tenant → list non-empty → listed →
  `iss` ordinal-equal `https://login.microsoftonline.com/{tid}/v2.0` → `idtyp` absent or `user` → `scp` contains
  `user_impersonation` (space-split, ordinal) → `acct` = `0`. All read from the validated `JsonWebToken`, so inbound claim
  mapping cannot rename a claim. Any failure → `context.Fail` (generic message) → 401.
- **Plane marker:** `TokenValidationParameters.AuthenticationType = "WorkforceHome"` — the identity's `AuthenticationType`
  is set by the handler and cannot be forged by token content (a claim could be). Task 046 branches on it.
- **Copilot audience:** WorkforceHome's `ValidAudiences` are derived from `AzureAd:ClientId` only; the Copilot merge is
  registered for the default scheme only. Tested (`WrongAudience_IncludingCopilot_Returns401`).
- **Logging:** Spaarke-tenant / CIAM tokens reach WorkforceHome on every ExternalCollaboration request (the policy tries
  each scheme); that refusal is Debug. Other refusals are Warning with `{Reason}` and `{Tid}`; the token is never logged.
- **Constant location (deviation from POML file list):** the scheme-name constant is in `AuthSchemes.cs`, where every
  scheme name lives; the POML listed `AuthPolicies.cs` "(scheme name constant only)". `AuthPolicies.cs` got a doc comment.
- **Instance:** the authority and the exact-`iss` host are the constant `https://login.microsoftonline.com/` (A9's text),
  not `AzureAd:Instance`.

## Escalation triggers

1. Routes carrying ExternalCollaboration: 54, all under `/api/v1/external/`. None is a management route a contact must not
   reach other than the three owner-allowed contact-grants routes (2026-10-13, option A). Not fired.
2. Signing-key issuer validation: enabled by Identity Web for the named scheme (above). Not fired.

## Pinned route list (54, `AuthenticationSchemeCensusTests.PinnedExternalCollaborationRoutes`)

Data/read: `GET /me`, `/me/entitlements`, `/projects`, `/projects/{id}`, `/projects/{id}/contacts|organizations|events|todos|documents`,
`/matters/{id}/events|todos|documents`, `/workassignments/{id}/events|todos|documents`, `/invoices/{id}/documents`,
`/servicerequests/{id}/documents`, each `…/documents/{documentId}/content|versions` (projects, matters, workassignments,
invoices, servicerequests), `/{root}/{id}/messages`, `/notifications`; module-data seam `GET /api/dataverse/metadata|record|savedqueries|savedquery`,
`POST /api/dataverse/fetch`.
Writes: `POST /projects|matters|workassignments/{id}/todos`, `POST /projects/{id}/events`, `POST …/documents` (projects, matters,
workassignments, invoices, servicerequests), `PATCH /todos/{id}`, `DELETE /documents|events|todos|servicerequests/{id}` (creator-only),
`POST /{root}/{id}/messages`, `POST /notifications/seen`, `POST /servicerequests`.
Owner-allowed management (option A): `GET /contact-grants`, `POST /contact-grants`, `POST /contact-grants/revoke`.
(All prefixed `/api/v1/external`; exact strings in the test.)

## Tests

- Unit (`AuthorizationModuleWorkforceHomeTests`): **before** (old module + only the `WorkforceHomeDeny` constants so the
  file compiles): 30 failed / 2 passed (the two passing are the regression guards: Spaarke token on a workforce route, CIAM
  token on the external route). **After:** 32/32 pass.
- Hermeticity check: with the fake discovery endpoint answering 404 for everything, 25 tests fail — so Identity Web's
  `AadIssuerValidator` really reads the offline documents and nothing reaches Entra.
- `AuthorizationModuleCiamTests` unchanged: 33/33 pass.
- Filtered unit run `FullyQualifiedName~Auth|~CallerPrincipal|~Workforce|~Infrastructure.DI|~Ciam`: **2649 passed, 0 failed**.
- ArchTests (full project): **972 passed, 0 failed**. The first full run failed the 3 new census tests: two `BootedBff`
  fixtures booted Production in parallel and `BootedApp` clears the process-static
  `CacheModule.NetworkStepsForBootedHostTests` hook in `finally`, so one boot tried a real MI Redis connect. Fixed by one
  shared `BootedBffCollection` (sequential, one boot).

### Seeding proofs (failing, then restored)

| Seed | Expected failure | Result |
|---|---|---|
| 1. `AuthorizationModule.WorkforceHomeDenyReason`: the `!customerTenantIds.Contains(tenant)` check disabled | `UnlistedTenant_Returns401` | FAILED: "Expected status … Unauthorized {401}, but found OK {200}" (1 failed / 31 passed). Restored → 32/32. |
| 2. `p.AuthenticationSchemes.Add(AuthSchemes.WorkforceHome)` added to the `SystemAdmin` policy | census | FAILED: `WorkforceHome_IsNamedByExternalCollaborationOnly` ("Named by: [policy 'ExternalCollaboration', policy 'SystemAdmin']") and `RoutesCarryingExternalCollaboration_AreThePinnedList` ("routes outside /api/v1/external/ … admit WorkforceHome: DELETE /api/ai/rag/{documentId}, GET /api/admin/jobs/ …"). Restored → 3/3. |

### Test scope justification (beyond the named cases)

- Stamp audience in both forms and `acct` as a JSON string: accept-side controls so the must-reject tests cannot pass vacuously.
- `IdtypApp_EvenWithUserImpersonationScope`, `user_impersonation` among other scopes, case/near-miss scopes: the `scp`/`idtyp` rule's edges.
- `SpaarkeTenantToken_*` / `CiamToken_*` on the external route: prove exactly one scheme authenticates (no merged double identity).
- `SignedByKeyScopedToTheTokensOwnDirectory_Returns200`: positive control for the signing-key issuer test.

## BFF hygiene (root CLAUDE.md §10)

- **Placement Justification:** in the BFF, inside `AuthorizationModule` (ADR-010: no new module). Inbound token acceptance for
  the BFF's own routes can only live in the BFF that serves them; it is not AI, not background work (ADR-052 n/a).
- **§11:** Existing — default scheme (single Spaarke tenant) and Ciam scheme. Extension — no: widening the default scheme to
  `organizations` would expose every workforce/OBO route to customer directories. Cost of doing nothing — every home token
  fails IDX40001; no workforce contact can sign in under A9.
- **Packages:** none added or changed (Microsoft.Identity.Web 4.14.2 and JwtBearer already referenced).
- **CVE:** `dotnet list … package --vulnerable --include-transitive` on the BFF: one HIGH, PRE-EXISTING and unrelated —
  transitive `System.Security.Cryptography.Pkcs 10.0.11` (GHSA-846f-f764-76mx, also a Moderate GHSA-2fmv-rv2c-59x7). No new
  package here, so not introduced by this task. Not found tracked by GHSA id in the repo: to be filed by the main session.
- **Publish size:** see below.

| Build | Uncompressed | Compressed |
|---|---|---|
| Baseline `15f2dc686` (this task's parent) | 122.480 MB | 36.606 MB (38,384,111 B) |
| Task 045 `1392bf660` | 122.486 MB | 36.609 MB (38,386,833 B) |
| **Delta** | **+0.006 MB** | **+0.003 MB (+2,722 B)** |

Convention: `dotnet publish src/server/api/Sprk.Bff.Api -c Release -r linux-x64 --self-contained false -o deploy/api-publish/`
on a clean `git archive` export of each commit (`src/server`, `config`, `Directory.*`, `global.json`, `NuGet.config`);
Release; framework-dependent linux-x64; zipped with `Compress-Archive -CompressionLevel Optimal`; PDBs included (4).
Baseline is the parent commit, not a fresh `origin/master` build: the worktree branch sits on the project branch, and the
parent isolates this task's delta exactly. Well under the +5 MB task threshold and the 55 MB total review line.

## Known limits (K)

- K: `AadIssuerValidator` caches one validator per authority per process; the unit tests use tenant ids unique to the class.
  If another unit test ever builds the `organizations` validator without the fake discovery client first, these tests fail
  loudly (they never pass silently).
- K: a CIAM token on an ExternalCollaboration route makes WorkforceHome look up an unknown `kid`; JwtBearer may request a
  metadata refresh, rate-limited by the ConfigurationManager (same behaviour the default scheme already has for CIAM tokens).

## Observed, not fixed (outside scope)

- `BootedApp`'s `finally` clears `CacheModule.NetworkStepsForBootedHostTests` for Development boots too, so
  `Adr032/GateCombinationConstructibilityTests` (Development boots, not in the collection) can still clear the hook while a
  Production `BootedBff` boot is in progress in a parallel collection. Pre-existing latent flake; not seen in the runs here.
- Pre-existing HIGH transitive CVE above.
- Ship with 046 only (POML note): until 046, a WorkforceHome principal reaches `WorkforcePrincipalResolver`'s A8 create path.

## Fix round 1

Review passes A and B (auth task, two passes). Findings and disposition:

| Pass | Finding | Disposition |
|---|---|---|
| A | K2: 045-without-046 window (a WorkforceHome principal reaches the A8 create path) | Superseded by the F2 guard below |
| A | K3: "tid is not the Spaarke tenant" rule skipped when `AzureAd:TenantId` is not a GUID | Fixed: fail closed (`WorkforceHomeDeny.SpaarkeTenantUnknown`, logged as `Reason`); no startup refusal |
| A | K4s | Accepted (known limits above) |
| B | F2: no mechanical guard for the 045-without-046 window | Fixed: `WorkforcePrincipalResolver.ResolveAsync` denies any identity with `AuthenticationType == WorkforceHome` first, before any Dataverse/binder call (`sdap.access.deny.workforce_home_resolution_pending`, reason `PrincipalNotResolved`) |
| B | K2: census did not pin the default authenticate/challenge schemes | Fixed: `DefaultSchemes_AreTheWorkforceBearerScheme` (both boots) |
| B | K3: `BootedApp` cleared `NetworkStepsForBootedHostTests` for Development boots too | Fixed: reset in `finally` only when `_redis is not null` (the condition under which this boot set it) |

**F2 guard lifecycle.** Task 046 replaces it with the home branch; 046's tests must remove the guard and
`ResolveAsync_WorkforceHomePrincipal_IsDeniedBeforeAnyLookup`. The "Ship with 046 only" line above is now enforced by the
guard rather than by convention.

**Strategy reach.** `WorkforcePrincipalStrategy` (CallerPrincipalResolver.cs) calls only `IWorkforcePrincipalResolver.ResolveAsync`
and composes the accessible set after a resolved principal; it has no other route to `ContactIdentityBinder` (the other
binder callers are the invite endpoints, not principal resolution). It needed no guard of its own.

**Tests.** `ResolveAsync_WorkforceHomePrincipal_IsDeniedBeforeAnyLookup` (Dataverse and identity doubles record zero
invocations, binder store zero reads/writes); `ResolveAsync_BearerWorkforcePrincipal_IsUnaffectedByTheWorkforceHomeGuard`
(regression control); `SpaarkeTenantNotAGuid_RejectsEveryHomeToken_FailClosed` (null, empty, placeholder, non-GUID, zero
GUID; with a real tenant the same token is not refused for that reason); `DefaultSchemes_AreTheWorkforceBearerScheme`.

**Seeding proof.** Guard disabled (`if (false && ...)`) and the K3 branch disabled together: the deny test and all five
K3 cases failed (6 failed, 1 passed - the Bearer control, as it should); both restored byte-for-byte, then 165 / 91 pass.

## Re-check of fix round 1 (2026-10-14, scoped, sonnet adversarial-reviewer)

No F-class finding. The reviewer confirmed:
- There is no bypass of `ResolveAsync`.
- A mixed principal (WorkforceHome plus Bearer identities) is still denied (`Identities.Any`).
- No legitimate Bearer caller is denied.
- The fail-closed tenant rule has a single caller, the WorkforceHome `OnTokenValidated`.

Tests: 110 filtered unit tests and 4 census ArchTests pass. Merged into R3 as `e6f6c6cd7`.

Known limits:
- K4: the guard sits only in the Workforce-plane resolver. `CiamContactPrincipalStrategy` calls `ResolveCiamCallerAsync` without it. Today a WorkforceHome principal cannot reach the CIAM plane: WorkforceHome requires an exact `login.microsoftonline.com/{tid}/v2.0` issuer and rejects the CIAM tenant, and `DeterminePlane` reads that same `tid` first. **046 carries this:** when 046 replaces the guard, it adds a test that a WorkforceHome principal never resolves on the CIAM plane, or it denies WorkforceHome at the top of `CallerPrincipalResolver.ResolveAsync` for the CIAM strategy.
- K4: if `AzureAd:TenantId` is misconfigured, `SpaarkeTenantUnknown` logs a Warning for every external request. This only happens in an already-broken config, and the noise helps surface it.
- Test gap: no resolver test covers a mixed principal. `Any` covers it by construction.
