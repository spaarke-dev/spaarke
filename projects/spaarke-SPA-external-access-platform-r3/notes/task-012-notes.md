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
