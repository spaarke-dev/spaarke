# Task 139 — the grant model: share rights and the grantor cap (C4, #1062)

Executed 2026-10-02 on `task/uac-r2-139` (from `task/uac-r2-138-r1` @ `d32cc6f35`). Owner rules: session27 round 1 (C4),
round 2 item 3 + Q1, round 3 A1 / S5, round 3b (A1 settled: the cap stays for MANUAL Grant Access), round 4.

## 1. What changed

| Rule (owner) | Where |
|---|---|
| Collaborate = Read, Write, Append, AppendTo, **Share** (262167); Full Access = Collaborate + Delete (327703); no Assign | `Services/Access/RecordShareLevels.cs` (masks, CSV constants, the Share row in the two-vocabulary pairing table) |
| Legacy masks 23 / 65559 still READ as Collaborate / Full Access; nothing writes them | `RecordShareLevels.LevelForMask` |
| Colleagues named at secure provisioning get exactly the creator's rights | `ProvisionProjectEndpoint.CreatorAccessRights` = `CollaboratorAccessRights` = `RecordShareLevels.CollaborateRights` (creator value unchanged: 262167) |
| ONE grantor ceiling: Full iff R+W+D, Collaborate iff R+W, View iff R, else none; Create/Append/AppendTo/Share not consulted | `ExternalAccessLevels.GrantCeilingFor` (beside `ToDisplayLevel`, which is not used) |
| Ceiling, never-lower, No Access refusals INSIDE the one grant-writing core, REQUIRED ceiling parameter (WP-1) | `GrantExternalAccessEndpoint.CreateGrantAsync(…, GrantCeiling ceiling, …)` → `CheckGrantAsync`; `GrantCeiling` (private ctor, named factory `FromGrantorRights`) in `ExternalGrantLifecycle.cs` |
| `/grant`, `/invite-and-grant` re-probe the caller (not trusted from the filter); throw → 500 `sdap.access.grant.caller_rights_unreadable`; None → 403 `sdap.access.grant.caller_cannot_grant` | `GrantExternalAccessEndpoint.ProbeGrantorCeilingAsync` |
| NARROW, never refuse; response says so | additive `GrantedAccessLevel` + `Narrowed` on `GrantAccessResponse` / `InviteAndGrantResponse` |
| Never silently lower → 409 `sdap.access.grant.would_lower_existing` | core (`CheckGrantAsync` step 3) and `/share-user` (`RecordShareLevels.WouldRemoveRights`) |
| No Access list at write time → 422 `sdap.access.grant.grantee_denied`, from the read path's own veto code | `IAccessibleRecordSetService.IsGranteeDeniedOnRecordAsync` → `ResolveDenyVetoAsync` (guard widened so an organization subject with no contact is checked) |
| `/invite-and-grant`: never-lower + deny BEFORE onboarding, existing contact found by the same email match | ~~`InviteExternalUserEndpoint.FindContactByEmailAsync`~~ — **since the merge with task 141 (§9)**: `ContactIdentityBinder.ResolveInviteContactAsync`, resolved ONCE and handed to onboarding (`ProvisionAsync(…, resolution, …)`) + `CheckGrantAsync` |
| S5 (amendment R3): `/unshare-user` refuses to remove the last enabled user who can read a SECURE record → 409 `sdap.access.user_share.last_reader_on_secure_record` | `InternalShareEndpoints.LastReaderRefusalAsync` |
| Modal renders narrowed + the server's detail for 403/409/422 and the S5 refusal | `AccessGrantModal.tsx`; TrackingFieldTrio **v1.0.33** (5 locations + bundle) |
| Backfill (dry-run default) | `scripts/Upgrade-LegacyRecordShareMasks.ps1` |

### Check order inside the core (`CheckGrantAsync`)
1. Record access policy (task 138) — first, unchanged.
2. Ceiling: none → 403; otherwise `CapAt(requested, ceiling)`, `narrowed = granted != requested`.
3. Never-lower: if narrowed and any ACTIVE row on the key has a higher level → 409.
4. No Access list (contact + its active orgs' wall set + the firm on the request / the org of an org grant) → 422; a throw → refuse.

### Deviations from the POML text (all deliberate)
- **WP-1 over the background text.** The background said the ceiling lives "in the /grant and /invite-and-grant HANDLERS, not
  inside the core" (because task 142 has no human grantor). The later main-session-review constraint WP-1 binds the opposite:
  the core enforces it with a REQUIRED ceiling. Implemented WP-1; task 142 supplies its own named factory (uncapped
  Collaborate). The handlers compute the ceiling (re-probe) and pass it; `/invite-and-grant` also calls `CheckGrantAsync`
  before onboarding.
- **Task 142 note (never-lower).** The core's never-lower fires only when the ceiling NARROWED the request. An Assigned-To
  auto-grant asking for exactly Collaborate is not narrowed, so task 142 must check the existing level itself (or extend
  `CheckGrantAsync`) — documented on `GrantCeiling`.
- **Refusals reuse `GrantPolicyDecision`** (task 138's typed refusal) — new factories `CallerCannotGrant` (403),
  `WouldLowerExisting` (409), `GranteeDenied` (422); `PolicyRefusalProblem` titles by status.
- **Deny detail — SUPERSEDED by task 142 r4 (owner round 13 item 4, 2026-10-03); corrected here in task 142 round 18.**
  As built by this task, `ResolveDenyVetoAsync` folded every fault into "denied" and the 422 `grantee_denied` detail said
  "…it is on the record's No Access list, or that list could not be checked". That is no longer the wire contract. The
  write-time check (`IAccessibleRecordSetService.CheckGranteeNoAccessAsync`) answers Allowed / Denied / Unverifiable, and
  the grant core maps them apart:
  - an entry → **422 `sdap.access.grant.grantee_denied`**, detail "This contact or organization cannot be given access to
    this record: it is on the record's No Access list. Nothing was granted.";
  - a check that could not be completed (a read fault) → **503 `sdap.access.grant.no_access_unverifiable`**, detail
    "Whether this contact or organization is on the record's No Access list could not be checked, so nothing was
    granted. Try again in a moment."
  Neither detail names an entry or its reason (task 143). Record: task 142's note §14.1.
- **Deny subjects for a contact grant include the request's firm (`OrganizationId`)** besides the contact's own active
  memberships — over-matching is the specified direction for a veto (B-10).
- **S5 counts only enabled system users with a Read-bearing direct share**; team shares are not counted (membership not
  read), so the rule can over-refuse (safe direction). Fail closed on an unreadable secure flag (treated as secure) and on
  an unreadable sharer read (500, nothing removed).
- **`/share-user` never-lower uses the shared `sdap.access.grant.would_lower_existing`** code (criterion 7 names it for "all
  three routes").

### 🔴 Discovered and fixed: the POA share read could not read a live share row (pre-existing, all branches incl. master)
Found while running the backfill dry run READ-ONLY against spaarkedev1 (2026-10-02). The raw Web API response is:
```
{"principalid":"1d02f31c-…","objectid":"65a3fab2-…","changedon":"2026-10-02T03:51:06Z","accessrightsmask":262167,"principaltypecode":"systemuser"}
```
`principaltypecode` is an EntityName column and comes back as the logical-name STRING. `DataverseWebApiService.ReadPrincipalAccessRows`
read it with `TryReadInt` (JSON number only), so on every record that HAS a share the strict read
(`GetPrincipalAccessOrThrowAsync`) threw "a share row has no readable principal" → `/share-user`, `/unshare-user`, `/user-shares`
answered 500 `read_failed`, and the soft read silently answered "no shares". The wire tests fed the numeric form (8/9), so it
went unseen; the 2026-09-22 fix verified only that the QUERY answered 200. Same code on `origin/master`, `live/uac-r2-batch2-gates`,
133, 146, 152. **Fixed here** (discovered scope, it blocks this task's routes and live gate): `TryReadPrincipalKind` accepts the
string (new `DataversePrincipalRefExtensions.FromPrincipalTypeName`) and the number; neither → unreadable (strict refuses).
Pinned by `DataverseRecordShareWireTests.GetPrincipalAccess_ReadsTheLiveStringPrincipalTypeCode_InBothReads` (live body) + a
neither-form twin. The backfill script had the same assumption in its first draft and was corrected the same way (accepts
"systemuser"/"team" and 8/9). **Main session:** the other consumers of this read in `src/server` — `UnsecureProjectEndpoint`,
`DirectThreadAccessService`, `PlaybookSharingService` (and task 133's creator-share confirmation on its branch) — were reading
nothing (soft) or refusing (strict) on live data until this lands; their live behaviour should be re-checked after deploy.

## 2. Placement + justification (CLAUDE.md §10 / §11)

**Placement: BFF.** Everything is a change to existing grant/share routes and their one core; no new endpoint, service,
DI registration, option, job or package. Publish size: measured by the main session (skipped here per instructions).

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ExternalAccessLevels.GrantCeilingFor` | `ToDisplayLevel` (display-only, containment includes Create — forbidden for authz); `RecordShareLevels.Intersect` (POA masks only) | Added to the existing static class, not a new type | A Collaborate holder mints Full Access (with Delete they lack) or org-wide grants through `/grant` |
| `GrantCeiling` (sealed, private ctor) | none — the core took no grantor input | A value type inside `ExternalGrantLifecycle.cs`, no DI | WP-1: a writer could persist a grant without stating a ceiling; the guard has nothing to pin |
| `GrantGrantee` record | `ExternalGrantKey` (has no "contact not created yet" shape, and no deny subjects) | Wraps the key | `/invite-and-grant` could not run the checks before onboarding without creating the contact (forbidden) |
| `IAccessibleRecordSetService.IsGranteeDeniedOnRecordAsync` | `ResolveDenyVetoAsync` (private, composition-only) | One method on the existing interface/impl that calls the existing veto — no new registration | Write-time deny would have to re-implement the key shapes (forbidden) or be skipped |
| ~~`InviteExternalUserEndpoint.FindContactByEmailAsync`~~ (removed at the 141 merge, §9) | the inline query in `ResolveOrCreateContactAsync` | Extracted; the onboarding path now calls it | The pre-check would use a second, divergent email match |
| `InternalShareEndpoints.LastReaderRefusalAsync` + 1 reason code | none | Private helper in the existing handler | Owner S5 unenforced: a secure record could be left with nobody able to open it |
| DTO fields `GrantedAccessLevel`, `Narrowed` | `/share-user` has `narrowed` | Additive optional record params | A narrowed grant reports success at a level the caller did not get |
| `scripts/Upgrade-LegacyRecordShareMasks.ps1` | no script enumerates POA rows (`Deploy-AccessEventEntity.ps1` is schema-only) | Follows its shape: own `az` identity, read-only by default, report-first | Colleagues shared before 2026-09-30 keep no ShareAccess → MDA Share unavailable to exactly the people the owner said may share |
| `DataversePrincipalRefExtensions.FromPrincipalTypeName` + private `TryReadPrincipalKind` | `FromPrincipalTypeCode` (number only) | Added beside it in the existing extension class | Every live POA row is unreadable: the share routes 500 and the soft read reports "no shares" |
| ArchTest `GrantCeilingGuardTests` | `PoaShareClientSingletonGuardTests` pattern | Source-scan guard via `SourceScan` | WP-1 has no enforcement; a second writer or a ceiling-less call would compile and ship |

No `.WithClientSecret(...)`; `GrantMembershipAsync` untouched (still zero callers). No background work. No plugins.

## 3. ADR / design tensions
- **ADR-008** (authorization in endpoint filters) — path C, precedent: the allow/deny stays in `DelegationRuleFilter` (still
  Write; `RequiredRight` unchanged); the cap is a NARROWING of the write, done in the handler + core exactly like task 063's
  `/share-user`. Cite in the PR.
- **"No re-share at any level" (2026-09-15, not an ADR)** — superseded by the owner; recorded as design-register **B-14a**,
  SUPERSEDED banners on `notes/task-063-…md` §4 and `tasks/063-…poml`; history kept.

## 4. Escalation triggers
| # | Status |
|---|---|
| 1 RetrievePrincipalAccess under-reports Write/Delete | **Pending live gate** (not testable offline). |
| 2 Collaborate sharee can hand out a right they lack via MDA Share | **Pending live gate**. |
| 3 Backfill finds team shares at 23/65559 or non-level masks | **Did not fire** on the 2026-10-02 dev dry-run (below): 2 shares, both at current level masks; 0 to upgrade; 0 team; 0 non-level. |
| 4 Task 143 landed and expects `/share-user` to refuse denied systemusers | Not landed on this branch. The internal-user No Access list on `/share-user` is task 143 (owner Q4); NOT implemented here. |
| 5 Task 133/144 changed the provisioning share step in a conflicting way | **Did not fire.** 144 (merged) did not touch the share constants. 133 (`task/uac-r2-133-r1-r2`, not merged here) keeps both constant NAMES and adds `CreatorAccessMask = MaskForRightsCsv(CreatorAccessRights)` — with this task that is 262167 = the Collaborate mask, consistent. Expect a textual merge near `RecordShareLevels.cs`'s end (133 appends `MaskForRightsCsv`/`RightsCsvForMask`) and `ProvisionProjectEndpoint.cs` :135-160 (this task's remark rewrite). Semantics agree **only if 139's `CreatorAccessRights` line wins** — see §8 (r1 merge hazard). |

## 5. Backfill dry-run (dev, 2026-10-02, operator identity ralph.schroeder@spaarke.com, READ-ONLY)
```
Upgrade-LegacyRecordShareMasks — DRY RUN (read-only) — https://spaarkedev1.crm.dynamics.com
Before: current 2 · upgrade 0 · legacy-other-principal 0 · non-level 0
System-user shares to upgrade: 0
After:  current 2 · upgrade 0 · legacy-other-principal 0 · non-level 0
EXIT=0
```
So in dev there is nothing to upgrade today; the `-Apply` run (gate 13e) is expected to be a no-op unless "+ User"
shares are made at the old masks before the BFF deploy.

## 6. Tests
New / rewritten (all KEEP paths):
- `tests/integration/auth/UnifiedAccessControl/GrantorCeilingTests.cs` (NEW, 37): ceiling table + ignored rights (AC4);
  `/grant` + `/invite-and-grant` narrow / not-narrow twins (AC5); org-wide narrow twins (AC6); never-lower 409 on
  contact, org and invite-before-onboarding + explicit-downgrade and raise twins (AC7); probe throw → 500, None → 403 on both
  routes, nothing onboarded (AC8); No Access list for contact / contact's org / org grantee / invite existing + new person's
  firm, reader fault and membership fault, each with a non-denied twin, using the REAL `AccessibleRecordSetService` +
  REAL `NoAccessListReader` behind `QueryChunkAsync` (AC9); the core called directly (WP-1).
- `InternalUserShareTests`: level literals 1 / 262167 / 327703, Share at Collaborate+ and never Assign, legacy masks read
  as levels, legacy 23 → "updated" by a Share-holding caller vs "unchanged" twin without Share (AC1), Share pairing (AC2),
  provisioning literal (AC3), `/share-user` never-lower 409 + explicit downgrade twin, throwing probe 500 (AC8), S5: last
  reader 409 + 5 twins/variants, list shows legacy level.
- `SecureProjectShareTests`: colleague share == creator share == the Collaborate literal (AC3).
- `DelegationRuleCharacterizationTests`: every existing Write pin unchanged; NEW positive twin for the exact new
  Collaborate rights on `/grant`, `/invite-and-grant`, `/share-user`, `/can-manage-access`; `/can-manage-access` 200 with
  Write / 403 without on Restricted, Limited and Secure records (AC10).
- `GrantLifecycleCharacterizationTests`: passes a Full-Access ceiling + a not-denying deny check; one assertion now
  compares row ids instead of whole outcome records (the outcome carries the granted level).
- `InternalUserShareContractTests` / `ExternalAccessContractTests`: mask literals updated; entitled probe gains Share.
- `RecordAccessGateTests`: untouched, green.
- ArchTest `GrantCeilingGuardTests` (NEW, 4).
- jest `AccessGrantModal.grantOutcome.test.tsx` (NEW, 8): narrowed notice from `/invite-and-grant` and `/grant`, plain
  success twin, 409/422/403 server detail, `/grant` 409, S5 refusal detail with the row kept (AC11).

### Perturbation record (each seeded, run, restored, file touched)
| Seeded violation | Went red |
|---|---|
| Ceiling removed (`granted = requested`) | 8 `GrantorCeilingTests` (narrow ×3, never-lower ×3, raise, core-direct) |
| Share row removed from `LevelRights` | 10 `InternalUserShareTests` (incl. both pairing tests, legacy-upgrade, every Collaborate write) + 1 contract test |
| Never-lower removed (core + `/share-user`) | 4 `GrantorCeilingTests` + `Share_WhenNarrowedBelowWhatTheUserAlreadyHolds_Is409AndWritesNothing` |
| Deny check removed (core) | 8 `GrantorCeilingTests` (every deny case) |
| S5 check removed | 6 `InternalUserShareTests` (last-reader + variants) |
| `ResolveDenyVetoAsync` guard reverted to "no contact → nothing" | 3 (org grantee, new person's firm, core-direct) |
| ArchTest seeds: `GrantCeiling?` param; `internal` ctor; a second `["sprk_accesslevel"] =` writer + a ceiling-less `CreateGrantAsync(` | all 4 `GrantCeilingGuardTests` |
| Modal: `narrowed` not read; would_lower code dropped from the detail set; S5 code ignored | 5 of 8 jest cases |

### AC12 grep (src/ + tests/, build output excluded) — 0 hits
Pattern: `No re-share|re-sharing stays with the creator|no level carries Share|Share and Assign are at no level|WithoutShareAccess` (case-insensitive) → **No matches** in `src/` and `tests/` (2026-10-02).

## 7. Manual live gate (criterion 13) — PENDING (not approved for this task in round 4; main session runs)
Prereqs: BFF deployed from this branch (`scripts/Deploy-BffApi.ps1`), TrackingFieldTrio v1.0.33 imported
(`src/client/pcf/TrackingFieldTrio/Solution/pack.ps1` then `pac solution import --path …/bin/TrackingFieldTrioSolution_v1.0.33.zip`),
two non-admin test users in the correct BU (owner round 4: ask the owner to create users at the needed access levels rather
than reuse the root-BU accounts).
- (a) Owner shares a secure project to user A at Collaborate via "+ User". As A: open in MDA, use **Share** (try to include
  Delete — Dataverse must refuse; else escalation 2), open Manage Access (gate 200).
- (b) Share to user B at View Only. As B: Manage Access disabled (gate 403), no MDA Share command.
- (c) As A: Manage Access → grant a contact **Full Access** → row written at Collaborate, modal shows the narrowed notice.
  Also capture `RetrievePrincipalAccess` for A (escalation 1 if Write/Delete under-reported).
- (d) Provision a new secure project with a colleague; read `principalobjectaccessset?$filter=objectid eq <id> and objecttypecode eq 'sprk_project'` — colleague mask == creator mask == 262167.
- (e) `.\scripts\Upgrade-LegacyRecordShareMasks.ps1` (dry run) then `-Apply`; record counts; a previously shared colleague can then use MDA Share.
- (f) Run U-3 as rewritten in `notes/phase4-uat-acceptance.md`.
- (g, S5) On a secure project with a single user share, `/unshare-user` that user → 409 `last_reader_on_secure_record`.

## 8. Verifier round 1 (r1, branch `task/uac-r2-139-r1`, 2026-10-02)

**Fail-closed catches now proven (findings 3, 4, 17).** Both catches were real paths that no test reached:
- `GrantExternalAccessEndpoint.CheckGrantAsync` step (4). `ResolveDenyVetoAsync` (and `NoAccessListReader`) rethrow EVERY
  `OperationCanceledException`, so an HttpClient timeout (`TaskCanceledException`, caller not cancelled) in the
  referenced-organization read or the deny-list read reaches the handler's catch. The existing `reader-fault` /
  `memberships-unreadable` cases are absorbed inside `AccessibleRecordSetService` and never got there. New:
  `GrantorCeilingTests.Grant_WhenTheNoAccessCheckThrowsATimeout_Is422GranteeDenied_AndWritesNothing` (Theory: referenced-org
  timeout, deny-list timeout) and `InviteAndGrant_WhenTheNoAccessCheckThrowsATimeout_Is422BeforeOnboarding`. Positive twin:
  `Grant_ToAContactNotOnTheList_IsWritten`. Seams: `FlagStubParticipationService.ReferencedOrganizationsThrow` and
  `SeamNoAccessListReader.Throws` (test doubles only; the REAL `AccessibleRecordSetService` and `NoAccessListReader` run).
- `InternalShareEndpoints.LastReaderRefusalAsync` flag read. New:
  `InternalUserShareTests.Unshare_WhenTheSecureFlagReadThrows_AppliesTheLastPersonRule` (`ThrowOnRead = true` → 409
  `last_reader_on_secure_record`, the read was attempted, nothing removed) — the twin of the existing `Unreadable` case.

Perturbation (seeded, run, restored from HEAD, files touched): `denied = true` → `denied = false` in the step-(4) catch AND
`isSecure = true` → `isSecure = false` in the S5 catch → exactly the 4 new tests went red (3 + 1); the other 126 in the two
classes stayed green. Restored → 130/130.

**Merge hazard with task 133 (finding 5) — main session, at the 133 merge.** Every 133 branch (`task/uac-r2-133`, `-r1`,
`-r1-r2`) still has
`internal const string CreatorAccessRights = RecordShareLevels.CollaborateRights + ",ShareAccess";`.
Since this task, `CollaborateRights` already ends in `ShareAccess`, so keeping 133's line produces
`...,AppendToAccess,ShareAccess,ShareAccess`. **Resolve by taking 139's line:
`internal const string CreatorAccessRights = RecordShareLevels.CollaborateRights;`** (133's
`CreatorAccessMask = MaskForRightsCsv(CreatorAccessRights)` is then 262167, as intended). A wrong resolution is caught by the
literal assertions in `InternalUserShareTests` (`CreatorAccessRights.Should().Be(collaborateLiteral)`) and
`SecureProjectShareTests`; run both after the merge. Nothing on the 133 branches was edited here.

**Accepted, no change (findings 6, 7, 8, 9).**
- 6: never-lower counts every ACTIVE row, including one past its expiry date — a narrowed re-grant over an expired higher row
  gets 409 although the grantee holds nothing effective; an explicit lower-level request still works (over-refusal is the safe
  direction).
- 7: a row with a null level never blocks (`AccessLevel ?? 0`) — that direction can only raise a grant, never lower one.
- 8: `PolymorphicGrantWriteTests.cs` was listed as "modify" but needed no change: it compiles against the REQUIRED
  `GrantCeiling` and passes; the task-139 coverage lives in `GrantorCeilingTests`.
- 9: the "onboarding not called" check stays indirect (null `CiamUserProvisioningService` / `RegistrationEmailService` → a
  call would surface as 500, plus `Creates`/`Updates` asserted empty). Both are concrete classes; an explicit call count would
  need new subclass doubles over them, which the verifier judged unnecessary.

**AC14 (finding 16), partly closed.** `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable
--include-transitive` (2026-10-02, nuget.org): "has no vulnerable packages" — no HIGH CVE (task 139 adds no package). The
publish-size delta remains with the main session (fresh master worktree, short path, Compress-Archive, equal file counts).

**AC13 (finding 15) — still pending**: the §7 manual live gate, the BFF + PCF v1.0.33 deploy and the backfill `-Apply` (live
writes; main session).

## 9. Merge with task 141 (`integ/uac-r2-batch3`, 2026-10-02)

Task 141 (identity binding) replaced the invite's email lookup with `ContactIdentityBinder.ResolveInviteContactAsync`
(ACTIVE contacts, two rows, the systemuser-reference check; ambiguity → 409 `email_ambiguous`, a contact another
sign-in owns → 409 + flag, an unreadable lookup → 503 `contact_lookup_failed`) and deleted the `top: 1`, any-state
query `FindContactByEmailAsync` was built on. Resolution:

- `/invite-and-grant` resolves the invitee ONCE through the binder, before the grant checks, and passes that SAME
  resolution into `InviteExternalUserEndpoint.ProvisionAsync` — the contact the never-lower / No Access checks judge
  IS the contact onboarding provisions (one email→contact answer; onboarding does not look the email up again).
- A refusal or a lookup failure from that resolution is answered before the grant checks, through the one mapping
  `InviteExternalUserEndpoint.NotProvisionable` that `ProvisionAsync` also uses (409 + reasonCode, or 503 +
  `sdap.access.invite.contact_lookup_failed`). Neither is ever read as "no contact yet" (which would judge a
  prospective person and then onboard a new contact). An unexpected decision state fails closed (500).
- Order on `/invite-and-grant`: validation → record policy (138: 422/503) → caller-rights probe (139: 500) → invitee
  resolution (141: 409/503) → grant checks (139: 403/409/422) → onboarding → grant core (re-checks). `/invite`:
  policy (138) → `ProvisionAsync` (141).
- Tests: `GrantorCeilingTests` now seeds the invitee in `InMemoryContactIdentityStore` behind the real binder (its
  Dataverse double THROWS on a `contacts` query); +3 cases (two active contacts → 409 email_ambiguous and a lookup
  failure → 503, each with the firm on the No Access list so a "no contact" misreading would answer 422; one email
  lookup per successful request). `GrantPolicyContractTests`' "the Contact lookup is never reached" assertions read the
  identity store (they had become vacuous). Seeds: treating a refusal/failure as "no contact" → 6 red; onboarding
  re-resolving → 1 red; resolving before the policy check → 4 red.
