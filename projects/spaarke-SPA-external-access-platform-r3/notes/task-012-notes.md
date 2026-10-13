# Task 012 — Member-test branch for registered workforce-contact guests (FR-15, #1563)

Date: 2026-10-10. Rigor FULL (auth, tenant isolation). A parallel-wave implementer agent did the work; the main session runs the two adversarial review passes. Step 8 of the POML (TASK-INDEX / POML status) is left to the main session under the wave rules.

## Changes

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforceIdentityOptions.cs`
  - `WorkforceMembershipTest.Evaluate` gains two optional parameters, `stampTenantId` and `idp`. One branch is added, an exact conjunction: user token, `acct = 1`, `tid` = the stamp tenant, and the home tenant parsed from `idp` ∈ `CustomerTenantIds` → `Member`. If any part fails, the result is the same as before the branch, with the same deny code. The order is unchanged (kind → list → tid → acct):
    - The branch is consulted at the tid step. On a Model 1 stamp the guest's `tid` (Spaarke) is never on the list, so it would be `ForeignTenant` there.
    - It is also consulted at the acct switch's `"1"` arm. That arm is only reached when a deployment lists its own tenant (dev, NFR-07). Without the branch it stays `Guest`.
  - New `TryParseHomeTenant(idp)` (internal). It accepts only these shapes:
    - a bare `D`-format tenant GUID;
    - `https://sts.windows.net/{tid}/` (v1, the live shape);
    - `https://login.microsoftonline.com/{tid}/v2.0`.

    It refuses everything else and fails closed:
    - another host (`live.com`, `mail`, social or federated IdPs, look-alike hosts);
    - `http`, a port, user info, a query or fragment;
    - an extra or empty path segment, a v1 host with a v2 path, a v2 host without `/v2.0`;
    - a non-`D` GUID format, or the all-zero GUID.
  - `WorkforceCallerClaims` gains `Idp`. It reads `idp` and its mapped form (`http://schemas.microsoft.com/identity/claims/identityprovider`). It requires exactly one distinct value; two that disagree give null, which denies.
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs`
  - `ResolveWorkforceCallerAsync(user, oid, stampTenantId, ct)` passes the stamp tenant and `claims.Idp` into `Evaluate`. The resulting `Member` maps to `BindingEligibility.EmailBindAndCreate` exactly like a home-tenant member: an email bind, or a create keyed by the guest `oid`, through the unchanged `ContactBindingDecision`.
  - The old three-argument overload is kept. It forwards `stampTenantId: null`, so for it the guest branch never applies (fail closed). It exists because `tests/integration/auth/.../IdentityLinkReconciliationTests.cs:316` calls it, and that file is outside this task's edit set.
  - The audit log line says `Member (registered guest, idp=…)`, so a guest is distinguishable from a home-tenant member.
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs`
  - Branch (b) passes `_configuration["AzureAd:TenantId"]` (new constant `StampTenantConfigKey`) to the binder. The stamp tenant comes from deployment configuration, never from the token.
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/WorkforcePrincipalResolverTests.cs`
  - 43 new test cases, all end to end through the real resolver and the real `ContactIdentityBinder` over the in-memory identity store.
  - The default `CreateSut` is now stamp-shaped: `AzureAd:TenantId` = the Spaarke tenant, and `CustomerTenantIds` = {customer tenant} only.

## Decisions

1. **Plain `Member`, no new enum value.** `WorkforceMembershipTests.EveryNonMemberOutcome_HasItsOwnDenyCode` enumerates every non-`Member` value and requires a non-null deny code. A `RegisteredGuest` value would need either a null code (which breaks that test) or a mapping at every caller. `Member` keeps the binder's existing `membership == Member` gate as the only gate.
2. **Optional parameters on `Evaluate`, and an overload on the binder.** Existing callers outside the edit set (`tests/integration/auth/.../WorkforceMembershipTests.cs`, `IdentityLinkReconciliationTests.cs`) compile unchanged. Omitting the new arguments can only deny a guest, never admit one.
3. **Stamp tenant = `AzureAd:TenantId` only,** as the POML constraint names. The legacy `TENANT_ID` is not consulted. If it is missing or unparseable, the branch is off.
4. **Bare-GUID `idp` accepted.** The brief says `idp` "may be … a tenant GUID". Entra's documented guest shape is the v1 issuer URL. A GUID is accepted only in `D` format and is still checked against the list.
5. **Exact conjunction kept.** No extra "home tenant ≠ tid" condition was added. On a stamp, the stamp tenant is never listed, so an `idp` naming it is denied anyway (tested). Only on dev, where Spaarke's tenant is listed, would an internal guest of Spaarke qualify.

## Escalation trigger check (licensed U2 guests)

Not hit. The systemuser branch (a) runs before the binder (`WorkforcePrincipalResolver.ResolveAsync`). A licensed guest whose `oid` matches an enabled systemuser never reaches the member test. The test `ResolveAsync_SpaarkeStaffWithASystemUser_ResolveAsASystemUserAndNeverReachTheBinding` proves this: there are no store reads or writes.

## Acceptance criteria → evidence

| Criterion | Test(s) |
|---|---|
| Registered guest → Member; first sign-in creates or binds exactly one contact keyed by the guest oid | `ResolveAsync_RegisteredGuestFirstSignIn_IsAMemberAndCreatesExactlyOneContactKeyedByTheGuestOid` (×3 idp shapes); `ResolveAsync_RegisteredGuestFirstSignIn_EmailBindsTheOneUnboundContactAndCreatesNone`; `ResolveAsync_RegisteredGuestUnderTheMappedIdpClaimName_IsAMember` |
| Second sign-in binds the existing contact, creates none | `ResolveAsync_RegisteredGuestSecondSignIn_ResolvesTheSameContactAndCreatesNone` |
| Home tenant not listed → `workforce_tenant_not_customer` | `ResolveAsync_GuestWhoseHomeTenantIsNotListed_IsDeniedTenantNotCustomer` (unlisted, CIAM tenant, stamp tenant) |
| No idp / unparsable idp → denied | `ResolveAsync_GuestWithAMissingOrUnparsableIdp_IsDenied` (21 cases); `ResolveAsync_GuestWithTwoDifferentIdpClaims_IsDenied` |
| tid ≠ stamp tenant → denied as before | `ResolveAsync_GuestWhoseTidIsNotTheStampTenant_IsDeniedAsBefore` (another tenant, CIAM tenant) |
| Spaarke staff (acct = 0, tid = Spaarke) unchanged | `ResolveAsync_NonGuestInTheStampTenant_ResolvesExactlyAsBefore_EvenWithAListedIdp` (acct 0 / missing / 2); `ResolveAsync_SpaarkeStaffWithASystemUser_ResolveAsASystemUserAndNeverReachTheBinding`; the 6 pre-existing tests in the file are unchanged and pass |
| Seeding proof recorded | §Seeding proof below |
| Build green, all unit tests pass, publish size recorded | §Verification, §BFF hygiene |

### Test scope beyond the named criteria (one line each)

- `ResolveAsync_StampTenantNotConfigured_GuestIsDenied` (null / empty / non-GUID / zero): proves the branch fails closed when `AzureAd:TenantId` is absent, which the "tid = stamp tenant" criterion depends on.
- `ResolveAsync_ADeploymentListingItsOwnTenant_AdmitsOnlyAGuestWithAListedHomeTenant`: covers the acct switch's `"1"` arm (dev shape, NFR-07), which is otherwise unexercised. An unlisted guest keeps `workforce_guest` there.
- `ResolveAsync_GuestWithTwoDifferentIdpClaims_IsDenied` and the mapped-claim-name case: exact claim parsing, as the brief asks ("handle the real shapes, fail closed on anything else").

## Verification

- **SDK.** 10.0.101.
- **Build.** `dotnet build src/server/api/Sprk.Bff.Api/` gives **0 errors**.
- **Fail-before.** The three source files were restored from HEAD (`git checkout HEAD -- …`) and `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~WorkforcePrincipalResolverTests"` was run against them, with the new test file in place: **7 failed, 42 passed**.
  - The 7 failures are every positive case: the first sign-in create ×3 idp shapes, the email bind, the second sign-in, the mapped claim name, and the dev-shape admit.
  - The 42 that pass are the 6 pre-existing tests plus the 36 negative cases. Those negatives are regression guards that hold before and after; the seeding proof below shows each is load-bearing.
  - The files were then restored from the saved copies.
- **Pass-after.** The same filter gives **49 passed, 0 failed**.
- **Related suites.** `--filter "FullyQualifiedName~WorkforcePrincipalResolverTests|FullyQualifiedName~WorkforceMembershipTests|FullyQualifiedName~IdentityBinding|FullyQualifiedName~ContactIdentityBinder"` gives **202 passed, 0 failed**.
- **Full suite.** `dotnet test tests/unit/Sprk.Bff.Api.Tests/` gives **19,447 passed, 0 failed, 54 skipped (19,501 total)**.

## Seeding proof (each check of the new branch disabled in turn, then restored)

Each mutation was applied to `WorkforceIdentityOptions.cs`. `WorkforcePrincipalResolverTests` (49 cases) was then run, and the file was restored from the saved copy (byte-compared with `cmp`).

| Mutation | Failing tests |
|---|---|
| M1 drop `idp` home tenant ∈ list (`&& true`) | 4: `GuestWhoseHomeTenantIsNotListed` ×3, `ADeploymentListingItsOwnTenant(unlisted)` |
| M2 drop `tid == stamp` | 2: `GuestWhoseTidIsNotTheStampTenant` ×2 |
| M2b treat an unconfigured stamp tenant as a match | 4: `StampTenantNotConfigured_GuestIsDenied` ×4 |
| M3 drop `acct == "1"` | 3: `NonGuestInTheStampTenant_ResolvesExactlyAsBefore` (acct 0 / null / 2) |
| M4 drop the issuer host/path shape check | 6: `GuestWithAMissingOrUnparsableIdp` (another host, look-alike host, extra segment, v2 host without `/v2.0`, v2 host `/v1.0`, v1 host + v2 path) |
| M5 drop the https-scheme check | 1: `GuestWithAMissingOrUnparsableIdp(http://…)` |
| M6 ambiguous `idp` picks the first value | 1: `GuestWithTwoDifferentIdpClaims_IsDenied` |

## BFF hygiene (§10)

**Publish size.** Measured from fresh exports at short paths (`git archive` of `Directory.*.props`, `global.json`, `src/server`, `config`). The temp trees were removed afterwards.

**Convention:**
- command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`;
- RID / mode: framework-dependent linux-x64 (the csproj sets `RuntimeIdentifier` linux-x64 and `SelfContained` false; no `libcoreclr.so` in the output);
- configuration: Release;
- compression: `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`;
- PDBs: **included**.

| Tree | Commit | Zip bytes | MB |
|---|---|---|---|
| master (fresh) | `origin/master` 61c9e80a1 | 38,210,401 | 36.44 |
| branch base | `work/…-r3` c9fa6db96 | 38,212,198 | 36.44 |
| branch + task 012 | c9fa6db96 + this change | 38,213,560 | 36.44 |

**Deltas and limits:**
- Delta vs fresh master: **+3,159 bytes**. Delta attributable to this task (vs the branch base): **+1,362 bytes**.
- Well under +5 MB, and the total is ≤ 60 MB.
- The recorded 44.96 MB baseline is not comparable: it is an older tree, and all three numbers above are this run's own same-convention measurements.

**CVE check.** `dotnet list package --vulnerable --include-transitive` on the branch and on master both report: "no vulnerable packages given the current sources". No package was added, so there is no new HIGH CVE.

BFF Hygiene §10 verified: publish size = 36.44 MB (incl. PDBs), delta vs master = +3,159 bytes, no new HIGH CVEs.

- **Placement** (`.claude/constraints/bff-extensions.md`): this modifies existing identity-resolution code in `Infrastructure/ExternalAccess`. It adds no endpoint, route, DI registration, package, background work or AI dependency. No route is added or modified, so the `/api/v1/external` group constraint is satisfied vacuously.
- **No OBO; no new Dataverse or SPE call shape.** The binder's existing app-only store calls are reused. **The plane decision is unchanged** (`CallerPrincipalResolver.DeterminePlane` was not touched).
- **No new secret, and no invite permission anywhere.**

## §11 component justification (new surface)

- **`WorkforceMembershipTest.TryParseHomeTenant` (internal):**
  - Existing: `CallerIdentity`/`TenantResolution` parse `tid`, but nothing in the repo parses an `idp` issuer (grep for `idp` in `src/server`: none).
  - Extension: it lives inside the member test it serves.
  - Cost of doing nothing: FR-15 cannot read the home tenant, and every registered guest stays `ForeignTenant`.
- **`WorkforceCallerClaims.Idp` + `IdpClaim`/`IdpClaimUri`:**
  - It extends the existing claims record instead of adding a reader.
  - Without it, `Evaluate` has no `idp` to test.
- **`ContactIdentityBinder.ResolveWorkforceCallerAsync(…, stampTenantId, ct)` overload:**
  - It extends the existing method.
  - The binder has no `IConfiguration`, and the stamp tenant must come from configuration.
  - Without it, the binder cannot apply the `tid = stamp` part of the conjunction.
- **`WorkforcePrincipalResolver.StampTenantConfigKey`:**
  - A named constant for an existing config key.
  - Without it, the key is an inline literal on a security path.

## Observations for the reviewers (not changed — outside scope)

1. **The email trust basis differs for a guest.**
   - The binder email-binds a registered guest exactly like a member (POML step 4).
   - For a home-tenant member, the email claim is controlled by the customer's tenant admin (`WorkforceCallerClaims.TokenEmail` remarks).
   - For a B2B guest, it comes from the guest object in **Spaarke's** tenant. That object is set at invitation by the FR-14 registration service, or by anyone allowed to invite guests into Spaarke's tenant.
   - So the email bind is only as safe as Spaarke-tenant guest-invite governance and FR-14 deriving the invite address from the verified home token. FR-14 / task 060 should state this.
2. **Disabled licensed users.** `MembershipEndpoints.ResolveSystemUserIdAsync` (`Api/Membership/MembershipEndpoints.cs:450-453`) excludes disabled systemusers. A *disabled* licensed guest whose home tenant is listed now reaches the binder and can get a workforce contact. This matches how a disabled `acct = 0` member of a listed tenant behaves today. It is not the escalation trigger (an enabled licensed guest never reaches the branch), but the reviewers should confirm that it is intended.
3. **Sovereign-cloud issuers** (`login.microsoftonline.us`, `sts.chinacloudapi.cn`, …) are refused by `TryParseHomeTenant`. That is correct for the current commercial-only Model 1, and fails closed if that ever changes.

## Review results and fixes (2026-10-10)

Two adversarial passes (auth task: two). Fixes below ship in the commit "fix(external-access-r3): task 012 review fixes + app-only token hardening". "Fail-before" = the named tests were run against the same tree with the fix neutralised (condition forced false / helper stubbed) and failed; the fix was then restored.

### Pass 1

**F2 (fix-now) — a disabled licensed guest regained access.** `WorkforcePrincipalResolver` branch (a) looks the systemuser up with `isdisabled = false` (`MembershipEndpoints.ResolveSystemUserIdAsync`: a disabled systemuser "is no longer a valid principal"). On null the oid fell through to the binder, where FR-15 admits the same guest token as a Member and hands it a contact.
- Fix: on the null path only, one extra query for a systemuser row with that oid regardless of `isdisabled` (same query shape, no `isdisabled` condition, uncached). A row means DENY: `Denied(PrincipalNotResolved, "sdap.access.deny.systemuser_disabled")`, no binder call, no store writes. If the existence check itself throws, the caller is denied (`principal_not_resolved`), not 500.
- This also closes the **pre-existing Model-2 analogue**: a disabled systemuser carrying an `acct = 0` member token of a listed tenant fell through the same way.
- Tests: `ResolveAsync_DisabledSystemUser_IsDeniedAndNeverReachesTheBinder(acct 1/guest, acct 0/member)`, `ResolveAsync_SystemUserExistenceCheckThrows_DeniesFailClosed`. Fail-before: both theory rows failed (the guest and the member token were resolved through the binder). `..._ExistenceCheckThrows_...` is a fail-closed control (it passes with the deny branch disabled only because the throwing check still runs).

**Pass 1 K-list**
- K1 (task 011) app-only token whose oid is a Dataverse application user's systemuser could resolve as a person before `Evaluate`'s NotUserToken check — fixed (see "App-only hardening").
- K3 diagnostics — fixed (see "Diagnostics").
- K4 `#EXT#` UPN used as the guest's email, and an app-only `acct=1` theory row — fixed (see below).

### Pass 2 (no F-class finding)

K-list: K3 stale comments — fixed; K4 diagnostics (startup warning when the stamp tenant is not a GUID) — fixed. Also hardened from pass 2: the 3-arg `ResolveWorkforceCallerAsync(user, oid, ct)` overload (silently skips FR-15) is removed, and `Evaluate`'s `stampTenantId` / `idp` parameters are now required.

### Fixes, with tests

| # | Change | Tests | Fail-before |
|---|---|---|---|
| App-only hardening | At the top of `ResolveAsync` (after the missing-oid check, before branch (a)): `CallerIdentity.FromPrincipal(user).Kind != UserDelegated` -> `Denied(PrincipalNotResolved, workforce_app_only_token)`. Before any lookup. No legitimate app-only caller of `/api/v1/external` or the workforce resolver exists (grep: the only entry is `WorkforcePrincipalStrategy` behind the `/api/v1/external` group; every client is a user-delegated SPA / Teams host; test hosts use `HeaderWorkforcePrincipalResolver`; resolver tests all carry `scp`). | `ResolveAsync_AppOnlyTokenWhoseOidIsAnEnabledSystemUser_IsDeniedNotUserTokenWithNoLookups`; theory `ResolveAsync_AppOnlyTokenWithTheMemberOrGuestShape_IsDeniedNotUserTokenWithNoWrites` (acct=1/tid=stamp/listed idp; acct=0/listed tenant) | all 3 failed. The system-user row resolved as a systemuser. The two theory rows already denied with the right code and no writes (the member test's NotUserToken), but failed the "no lookup side effects" assertion: the binder read the store. |
| Overload removal | `ContactIdentityBinder.ResolveWorkforceCallerAsync(user, oid, ct)` deleted; `IdentityLinkReconciliationTests` passes `stampTenantId: null`. `WorkforceMembershipTest.Evaluate(..., stampTenantId, idp)` parameters are required; the 9 `WorkforceMembershipTests` calls pass `null, null`. Compiles cleanly across `src/` and `tests/`. | compile | n/a |
| Diagnostics | Every `acct = 1` deny in the resolver (not-a-user-token, disabled systemuser, existence-check/binder failure, binder deny) logs ONE warning: deny code, `stampTenantParsed`, `idpHost`, `idpHomeTenant` (host and GUID only when `idp` parsed; `unparsed` otherwise; never the raw idp). The `[WF-AUTH] Resolved ... contact-only principal` line carries `homeTenant` (parsed GUID for a guest, `n/a` otherwise). `WorkforceIdentityOptionsValidator` (now takes `ILogger`) logs one startup warning "FR-15 registered-guest admission disabled" when `AzureAd:TenantId` is not a non-zero GUID; startup is NOT failed. | `ResolveAsync_GuestDenied_LogsOneWarning...`, `..._LogsTheTwoFactsAndNoRawIdp`, `ResolveAsync_NonGuestDeny_DoesNotWriteTheGuestDiagnosticLine`, `ResolveAsync_RegisteredGuestResolved_LogsTheParsedHomeTenant`, `Validator_StampTenantNotANonZeroGuid_...`, `Validator_StampTenantIsAGuid_LogsNothing` | three log tests (`GuestDenied_LogsOneWarning`, `..._LogsTheTwoFacts...`, `RegisteredGuestResolved_LogsTheParsedHomeTenant`) and the validator warning test failed with the logging stubbed; the non-guest and valid-GUID tests are negative controls |
| `#EXT#` email | `WorkforceCallerClaims.TokenEmail`: for `acct = 1`, a candidate containing `#EXT#` is skipped. One change covers the binder's bind/create email and the principal's display email. A guest with no real email claim creates the contact without an email. `acct = 0` unchanged. | `ResolveAsync_GuestWithOnlyAnExtUpn_CreatesTheContactWithoutAnEmail`, `..._NeverEmailBindsAnUnboundContactNamedLikeTheUpn`, control `..._GuestWithARealEmailAndAnExtUpn_UsesTheRealEmail`, control `TokenEmail_ForAMember_IsUnchanged...` | the two UPN tests failed (the UPN became the contact email / bind key); both controls pass before and after |
| Stale comments | `WorkforceIdentityOptions.cs` header, `WorkforceMembershipTests.cs`, `WorkforceEmailNoHijackTests.cs`: "a registered guest (FR-15) additionally requires tid = AzureAd:TenantId". | n/a | n/a |

### Known limits NOT fixed

1. **Guest email-bind trust basis.** A registered guest is email-bound exactly like a member, but a guest's `email` claim comes from the guest object in Spaarke's tenant, not the customer's. The bind is only as safe as task 060 (FR-14 registration) deriving the invite address from the verified home-tenant token AND being the only guest-creation path. This aligns with ADR-028 A8 :493 by delegation; task 060 must state it.
2. **Principal `TenantId` is the stamp tenant for guests** (the token's `tid`), not the home tenant. Audit consumers that want the home tenant must read the `homeTenant` / `idpHomeTenant` log fields.
3. **HTTP-level test hosts use a non-GUID `AzureAd:TenantId`**, so FR-15 is only resolver-tested (unit level) until a contract fixture sets a GUID.
4. **Sovereign-cloud issuers** (`login.microsoftonline.us`, `sts.chinacloudapi.cn`, ...) are refused by `TryParseHomeTenant`.
5. The systemuser id cache in `ResolveSystemUserIdAsync` (existing) can keep an enabled->disabled user resolving until its TTL; the new check only covers the not-cached path. Pre-existing, not changed here.


## Re-check follow-up: single systemuser read (K2) and deny-code naming (K3)

- **K2 fixed.** `WorkforcePrincipalResolver` no longer calls `MembershipEndpoints.ResolveSystemUserIdAsync` followed by `SystemUserRowExistsAsync`. A resolver-local `ResolveSystemUserAsync` reads the shared `membership-currentuser` cache entry (positive only, same key/version/10 min TTL) and, on a miss, issues ONE `QueryExpression` by `azureactivedirectoryobjectid` (Guid, `NoLock`, `TopCount = 2`, ordered `isdisabled` ascending, columns `systemuserid` + `isdisabled`, no `isdisabled` filter). Any enabled row wins (resolve as that systemuser, positive result cached); only disabled rows -> `sdap.access.deny.systemuser_disabled`, binder not called; no rows -> binder as before. A throw -> `principal_not_resolved` deny (cancellation propagates). A row whose `isdisabled` is absent is treated as disabled (fail closed). The shared helper keeps its behaviour for its other callers; only `CurrentUserCacheVersion` / `CurrentUserCacheTtl` in `MembershipEndpoints.cs` went from `private` to `internal` so the two lookups cannot drift apart.
- **K3 left as is, comment made accurate.** `sdap.access.deny.workforce_app_only_token` (`WorkforceMembershipTest.DenyNotUserToken`) is a documented, shared member-test code (task-141 notes: "Application / Indeterminate, incl. a token with no `idtyp`"), referenced by unit and integration tests and the `WorkforceMembership.NotUserToken` mapping in `WorkforceIdentityOptions.cs`. Renaming it means editing that file (outside this follow-up's allowed set) and the task-141 documented contract, so it was not renamed; the resolver now carries a comment that the code covers Indeterminate user-shaped tokens too, and the warning log line already prints the actual kind. No HTTP status or body changed. A rename to e.g. `workforce_not_user_delegated_token` is a candidate for a later change that owns `WorkforceIdentityOptions.cs`.
