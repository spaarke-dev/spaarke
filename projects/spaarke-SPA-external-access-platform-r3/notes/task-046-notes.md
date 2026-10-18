# Task 046 — BFF home principal resolution (ADR-028 A9)

Date: 2026-10-15. Rigor FULL (bff-api, auth, security, tenant-isolation, testing). Design: `notes/a9-bff-design.md` §3, §4, §5.
Branch `task/046-home-resolution` from `origin/work/spaarke-SPA-external-access-platform-r3` at d84a05cfc.
Adversarial passes (two: auth, tenant-isolation) are run by the main session, not in this implementer run.

## Step 0 — escalation check (UAC task 185 vs design §3/§4): no conflict

UAC 185 is already on R3 (merged via f488597b4, PR #1827), so no rebase was needed. Every change 185 made to the binder and decision contract is on the
**CIAM plane**:
- E11's invite-code marker leg: `ContactBindingDecision.cs:577` ("CIAM plane, E11 (task 184 …; task 185 …)") and `:807`.
- `BindingDecision.ConsumeInviteCode`: `:457` ("set on a CIAM-plane BindByEmail").
- "No grant is read; a grant is no bind evidence": `:344`.
- `ContactIdentityBinder.cs:224`: the CIAM bind consumes the code.

Design §4 keeps exactly those planes as they are: "The CIAM and Spaarke-tenant planes do not change" (`a9-bff-design.md:66`). §3 adds a separate home branch with its own order (`:51-55`). The home branch implemented here never calls `ContactBindingDecision.Decide` or any binder write. It uses two new pure decisions, `DecideHomeLink` and `DecideHomeContact`, over two new store reads. So 185's contract and §3/§4 do not overlap, and there was nothing to escalate.

## Placement

BFF (`Sprk.Bff.Api/Infrastructure/ExternalAccess`). This extends the existing resolver, with no new surface beyond the store read methods: no endpoint, DI registration, package or column. New members are the two `IContactIdentityStore` reads (below); the internal helpers `WorkforcePrincipalResolver.WorkforceHomeIdentity` / `IsWorkforceHomePrincipal`; the internal constant `DenyRegistrationRequired`; and the pure decisions `DecideHomeLink` / `DecideHomeContact` with their records (`HomeLinkRow`, `HomeLinkLookup`, `HomeLinkOutcome`, `HomeLinkDecision`, `DenyHomeLinkAmbiguous`).

§11, per new store method:
- `FindHomeLinkedSystemUsersAsync(tid, oid)`
  1. *Existing:* `FindSystemUserAsync` / the reference reads query systemuser by id or by contact link, and `ResolveSystemUserAsync` by `azureactivedirectoryobjectid`. None reads the secured home pair.
  2. *Extension:* it is an added method on the existing store interface. Widening an existing read would change its filter for every Spaarke-tenant caller, and the home lookup must never touch `azureactivedirectoryobjectid`.
  3. *Cost of doing nothing:* design §3 step 2 has no data source. A licensed user's home sign-in would get `registration_required` and become a second, contact principal with split access.
- `FindHomeContactsAsync(tid, oid)`
  1. *Existing:* `FindContactsByOidAsync` filters on oid alone, across all planes.
  2. *Extension:* that read is shared by the CIAM and Spaarke-tenant decisions. Adding the plane and tid predicates to it would change their behaviour, and leaving them out lets a home oid match an External or Workforce contact.
  3. *Cost of doing nothing:* without it, a registered WorkforceHome contact either never resolves (it would be sent to the form again) or matches on oid alone, across planes and directories (the cross-tenant match the AC forbids).

## Files changed (d84a05cfc..HEAD)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs` | 045 guard and `DenyWorkforceHomeResolutionPending` removed. `WorkforceHomeIdentity` / `IsWorkforceHomePrincipal` added (by `AuthenticationType` = `WorkforceHome`). `ResolveHomeAsync` + `ResolveLinkedHomeSystemUserAsync` (read-only; no `TryLinkSystemUserAsync`, no `membership-currentuser` cache). `DenyRegistrationRequired`. Header (H). |
| `.../ExternalAccess/CallerPrincipalResolver.cs` | `DeterminePlane` returns Workforce for a home principal before any iss/tid test. `WorkforcePrincipalStrategy` maps `registration_required` to 403 with `reasonCode`, `givenName`, `familyName`, `email` (from `WorkforceCallerClaims` of the home identity). |
| `.../ExternalAccess/ModuleEntitlementResolver.cs` | A home principal gets `WorkforceDefaultModules`; its roles claim and the map are never read (finding (e)). |
| `.../ExternalAccess/ContactBindingDecision.cs` | `HomeLinkRow` / `HomeLinkLookup` / `HomeLinkOutcome` / `HomeLinkDecision`, `DecideHomeLink`, `DecideHomeContact`, `DenyHomeLinkAmbiguous`, the column/option constants, and `ContactBindingRow.RawExternalTenantId`. |
| `.../ExternalAccess/ContactIdentityStore.cs` | The two reads on the interface and the Dataverse store. Path builders `BuildHomeLinkLookupPath` (secured pair only) and `BuildHomeContactLookupPath` (plane + tid + oid). `ParseHomeLinkRow`; `ParseContactRow` reads `sprk_externaltenantid`. |
| `tests/integration/auth/UnifiedAccessControl/IdentityBinding/InMemoryContactIdentityStore.cs` | Home fields on Contact/SystemUser and the two reads. They deliberately return an oid-only superset (mirrors included) so the production decisions do the filtering. |
| `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs` | `HeaderDrivenIdentityStore` delegates the two reads. |
| `tests/unit/.../WorkforcePrincipalResolverTests.cs` | 045 guard test removed; 22 home cases. |
| `tests/unit/.../CallerPrincipalResolverTests.cs` | 7 cases (CIAM plane, prefill). |
| `tests/unit/.../ModuleEntitlementResolverTests.cs` | 4 cases (finding (e)). |
| `tests/unit/.../DataverseContactIdentityStoreTests.cs` | 3 path/parse pins (ADR-038 B1: no HTTP double). |

## Tests mapped to criteria

| Criterion | Test(s) |
|---|---|
| Missing oid/tid → 401 `missing_identity_claims` | `Home_MissingOidOrTid_Is401MissingIdentityClaims_BeforeAnyLookup` (no-oid, no-tid, tid-not-a-guid) |
| Stored link → enabled systemuser | `Home_StoredLinkToAnEnabledSystemUser_ResolvesThatSystemUser_WithItsDerivedContact` |
| Stored link → disabled → `systemuser_disabled` | `Home_StoredLinkToADisabledSystemUser_IsDeniedSystemUserDisabled_AndNeverReachesTheContactLookup` |
| WorkforceHome contact (plane + tid + oid) → contact | `Home_WorkforceHomeContactWithTheSameTidAndOid_ResolvesThatContact` |
| Else 403 `registration_required`; never External / Workforce plane / no-tid / other-tid contact | `Home_NoMatchingContact_IsRegistrationRequired_AndNothingIsCreated` (other-tenant, no-tenant, plane-external, plane-workforce, no-plane, nothing); `Home_LinkedInAnotherDirectory_IsNotThisCallersLink_IsRegistrationRequired` |
| Prefill `givenName` / `familyName` / `email` from the token | `WorkforceStrategy_RegistrationRequired_Is403_WithTheReasonCodeAndTheTokenPrefill` |
| Never look up by the mirrors / alternate key | `Home_AKeyMirrorEdit_NeverRedirectsASignIn`; `TheHomeLinkLookup_FiltersOnTheSecuredPairOnly_NeverOnTheMirrorsOrTheAadObjectId`; `TheHomeContactLookup_FiltersOnPlaneTenantAndOid_AnyState`; `ParseHomeLinkRow_ReadsTheSecuredPairAndIsDisabled` |
| (d) no home oid vs `azureactivedirectoryobjectid`; `ResolveSystemUserAsync` / `ResolveSystemUserIdAsync` never reached | `Home_OidEqualToASystemUsersAadObjectId_WithNoStoredLink_IsRegistrationRequired_NeverThatSystemUser`, plus `AssertNothingWritten` in every home test (`dataverse.Invocations` empty) |
| (e) roles ignored → defaults | `ResolveAsync_WorkforceHomePrincipal_IgnoresTheRolesClaim_GetsExactlyTheDefaults` (4) |
| Nothing created/updated/bound | `AssertNothingWritten` in every home test (store `Writes` empty, no `InvalidateAsync`, no binder reads) |
| Never resolves on the CIAM plane | `DeterminePlane_WorkforceHomePrincipal_IsAlwaysWorkforce_WhateverItsIssOrTid` (4), `ResolveAsync_AWorkforceHomePrincipal_NeverReachesTheCiamStrategy` |
| 045 guard removed | `ResolveAsync_WorkforceHomePrincipal_IsDeniedBeforeAnyLookup` deleted |
| Spaarke-tenant and CIAM unchanged | Every existing test in the filtered and full runs passes unchanged |

Beyond the closed set, one line each:
- `Home_AReadThatFails_Denies_NeverRegistrationRequired` (4): a failed or column-missing read must deny, not send a registered user to the form. It is the fail-closed arm of both new reads.
- `Home_TwoEnabledSystemUsersWithTheSameStoredLink_AreDeniedAmbiguous`: the only new deny code (`home_link_ambiguous`). Without it, which user won would be arbitrary.
- `Home_TheMatchingContactIsInactive_IsDeniedContactInactive_NotRegistrationRequired`: D3 (deactivation removes a person) must not turn into "register again".
- `Home_AnAppOnlyToken_IsDeniedNotUserToken_BeforeAnyLookup`: keeps the resolver's own rule (0) on the new branch (defence in depth behind the scheme's user-token check).
- `WorkforceStrategy_AnyOtherDeny_CarriesNoPrefill`: the must-not-fire control for the prefill, which stays only on `registration_required`.

## Tests first

Against the pre-change code (guard present, only the constant added so the tests compile), the run was 31 failed / 91 passed out of 122. Every new case failed except the two controls, which passed as designed: `DeterminePlane…(customer)` and `WorkforceStrategy_AnyOtherDeny_CarriesNoPrefill`. The three store path tests landed with the store reads in 047618cc7. Before that they could not compile.

## Seeding proofs (both restored; tree clean afterwards)

1. **Home branch falls through to `ResolveSystemUserAsync`** (the home `if` made never-true): 20 of the 22 home tests fail, including the (d) test with "Expected result.IsResolved to be false because a home oid is never compared with systemuser.azureactivedirectoryobjectid, but found True." The two that still pass are no-oid (the Spaarke-tenant branch also 401s) and app-only (it also denies non-user tokens).
2. **tid predicate dropped from the contact lookup** (`DecideHomeContact` and `BuildHomeContactLookupPath`): 3 fail: `Home_NoMatchingContact…(other-tenant)` and `(no-tenant)` ("Expected result.IsResolved to be false, but found True.") and `TheHomeContactLookup_FiltersOnPlaneTenantAndOid_AnyState`.

## Build and test counts

- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.
- Affected classes (`WorkforcePrincipalResolverTests`, `CallerPrincipalResolverTests`, `ModuleEntitlementResolverTests`, `DataverseContactIdentityStoreTests`): 267 passed, 0 failed.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests` with the filter ExternalAccess|Workforce|Principal|Entitlement|Membership: 3671 passed, 0 failed.
- Full `dotnet test tests/unit/Sprk.Bff.Api.Tests`: run by the main session on R3 after merge (dfee0a438, R3 + master f63b57f8d): 23,467 passed, 17 failed, all TaskCanceledException under machine contention; those 17 (26 cases) rerun alone: 26 passed, 0 failed. ArchTests 1,049/1,049. Build -warnaserror clean.
- `dotnet test tests/Spaarke.ArchTests`: 1010 passed, 0 failed.

## Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, Compress-Archive)

| Build | Files | Zip bytes |
|---|---|---|
| origin/master f292ad119 (fresh) | 192 | 38,904,607 |
| branch 45ed0ae4d | 192 | 38,456,693 |
| R3 base d84a05cfc | 192 | 38,449,840 |

The file counts are equal. The branch is −447,914 bytes (−0.43 MB) against master. That gap comes from master's own BFF work not yet on R3 (ontology #1111 and the uac-r2 witness walker), not from 046. The 046-attributable delta against the R3 base is **+6,853 bytes**, and `Sprk.Bff.Api.dll` grew by 19,968 bytes. No package was added. Both worktrees were removed.

## Known limits

- K: the home link read is uncached (one Dataverse read per home request, plus one contact read when unlinked). This is deliberate: the existing `membership-currentuser` cache is keyed by the Spaarke-tenant oid and must not hold a home oid. If load shows it, add a tenant-scoped home cache in a later task.
- K: if field security masks the secured columns from the BFF's app user, both reads return no match and home principals get `registration_required`. That fails safe (no wrong principal), but registered users would see the form. 044 grants the BFF writers' profile, and 074 is the live check.
- K: a principal with BOTH a WorkforceHome identity and another authenticated identity resolves as home. This cannot happen under the one-authority-per-token policy, and it is untested (the same gap as 045).
- K: the `registration_required` ProblemDetails keeps the default `type`, and the code travels as `reasonCode`, which is what the SPA reads.
- K: `IdentityPlaneMarker` has no WorkforceHome member, so the plane is matched as the raw value 100000002 in `DecideHomeContact` only. 047 (the writer) decides whether to add the member.
- `GrantAccessNotifier.SendAsync` compared a home oid with `azureactivedirectoryobjectid`: fixed in round 1 (F1-c below).
- `/conflict-check` was not run in this implementer run.

rework: none task 046

## Fix round 1

Findings and fixes (one commit, `fix(external-access-r3): 046 fix round 1 ...`):

- **F1-a, home link read had no ordering.** With systemusers [disabled S1, enabled S2, enabled S3] sharing the secured pair, the `$top=2` any-state read could return {S1, S2}, and `DecideHomeLink` would resolve S2 instead of denying `home_link_ambiguous`. `BuildHomeLinkLookupPath` now appends `&$orderby=isdisabled asc` (the precedent is `WorkforcePrincipalResolver.ResolveSystemUserAsync`'s `AddOrder("isdisabled", Ascending)`), so the two-row sample is enabled-first and two enabled rows are seen. The in-memory double `InMemoryContactIdentityStore.FindHomeLinkedSystemUsersAsync` orders `IsDisabled` ascending before `Take(2)`.
- **F1-b, two error logs on a missing A9 column.** `FindHomeContactsAsync` went through `ReadContactsAsync` (logs `binding_column_missing` naming `Set-ContactIdentityBindingSchema.ps1`) and then `LogHomeSchemaMissing` (naming `Set-WorkforceHomeIdentitySchema.ps1`). It now does `GetAsync` plus `ParseContactRow` directly, as the home link read does, so a missing A9 column logs exactly one error, naming `Set-WorkforceHomeIdentitySchema.ps1`. No test: the log is only reachable through `GetAsync`, an HTTP response, and the store's unit-test seam has no way to answer one without an HTTP double (ADR-038 B1 forbids adding one). The change is structural: the home read no longer calls the method that logs the binding-schema script.
- **F1-c, GrantAccessNotifier compared a home oid with `azureactivedirectoryobjectid`.** `SendAsync` skips `ResolveGrantedBySystemUserIdAsync` when `WorkforcePrincipalResolver.IsWorkforceHomePrincipal(caller)` (granterId = null, nobody excluded), with a comment citing ADR-028 A9 / 046 guard (d).

Tests (fail before, pass after; the three source files were restored to HEAD to prove it, then put back):

- `DataverseContactIdentityStoreTests.TheHomeLinkLookup_FiltersOnTheSecuredPairOnly_NeverOnTheMirrorsOrTheAadObjectId`: now also pins `EndWith("&$orderby=isdisabled asc")`. Failed before.
- `WorkforcePrincipalResolverTests.Home_ADisabledAndTwoEnabledSystemUsersWithTheSameStoredLink_AreDeniedAmbiguous_NotResolvedToAnEnabledOne`: [disabled, enabled, enabled] with the same pair, denied `home_link_ambiguous`. Failed before (double reverted).
- `GrantAccessNotifierExternalTests.AHomeCaller_IsNeverLookedUpByOid_AndNobodyIsExcludedAsTheGranter`: no `systemusers` query is made and the recipient is not excluded. Failed before.
- `GrantAccessNotifierExternalTests.ANonHomeCaller_IsStillLookedUpByOid_AndExcludedAsTheGranter` (must-not-fire control): the lookup is made and the granter gets `NotApplicable`. Passed before and after. `FakeDataverse` gained a recording `QueryAsync` override (the existing `DataverseWebApiClient` seam, no HTTP double).

Filtered runs (`dotnet test tests/unit/Sprk.Bff.Api.Tests --filter`):

- WorkforcePrincipalResolverTests | DataverseContactIdentityStoreTests | GrantAccessNotifierExternalTests: before 3 failed / 202 passed (205); after 0 failed / 205 passed.
- Plus ContactBinding | IdentityBinding | GrantExternalContactEmail | ContactGrant (suites that use the in-memory double): 0 failed / 591 passed.
- `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.

## Main-session review and close (2026-10-15)

- Adversarial pass 1 (Opus, auth): no F. K: root-set cache eviction misses customer-tenant keys (carried to 048), field-security-masked columns lead to registration_required (carried to 047), and the link read had no $orderby (fixed in round 1). It judged the GrantAccessNotifier residual a false positive.
- Adversarial pass 2 (Opus, tenant isolation): two F1. The $orderby gap allowed a [disabled, enabled, enabled] ambiguity to resolve, and a missing column logged twice. Both are fixed in round 1 (bc0113e9d). It showed the GrantAccessNotifier path IS reachable (ContactGrantEndpoints -> NotifyInviteNeededAsync -> SendAsync); the main session verified this, and it is fixed in round 1. K2 (the last-seen marker is per tid) is accepted and noted in 048. K3 (plane 100000002 is unreadable outside the home read) is carried to 047.
- Round-1 re-check (main session, fix diff and callers): the new home-contact read matches ReadContactsAsync except for its single log; the $orderby copies the ResolveSystemUserAsync precedent; the notifier skip is narrow. No F remains.
- Carried constraints were added to the 047 and 048 POMLs (validator PASS).
- Not deployed: dev runs 3115b90f2 (no 046). The next R3 dev deploy turns on home-principal resolution on dev, and it needs owner approval plus the shared-BFF coordination check.

## UAC review (2026-10-15)

- **F3 fixed by a test.** `ContactBindingDecision.DecideHomeContact` returned `DenyContactOidAmbiguous` for several WorkforceHome matches, but no test covered it. Added `WorkforcePrincipalResolverTests.Home_TwoWorkforceHomeContactsWithTheSameTidAndOid_AreDeniedAmbiguous`: two active plane-WorkforceHome contacts with the caller's tid and oid are not resolved, the deny code is `DenyContactOidAmbiguous`, and nothing is written.
- **Seeding proof.** With the `matches.Count > 1` guard removed and `== 1` changed to `>= 1`, the new test fails (`Expected result.IsResolved to be false, but found True.`); the production file was restored (`git diff -- src/` empty).
- **Filtered counts** (`WorkforcePrincipalResolverTests | ContactBindingDecision`): 0 failed / 227 passed (227).
- **K1 (carried to 047).** The home-link and home-contact filters match lowercase `:D` GUID text only; 047 must write `:D`-formatted GUIDs.
- **K2 (noted).** The `GrantAccessNotifier` skip for home callers is defensive: its callers are on `/api/v1/external-access`, which uses the default scheme, so a home principal does not normally reach it.
