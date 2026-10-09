# Task 175: a filed work assignment / project follows its parent — a floor, never looser; stricter by hand allowed

Owner round 84 (2026-10-08, binding): "Child access should always follow parent; if parent changes, then child changes."
Follow-ups the same day: un-securing cascades ("Child follows"); it applies to work assignments and projects.

**Owner round 87 (2026-10-09, binding) refines it: the parent sets a FLOOR; a child may be stricter.**
- A filed record inherits Secure and Access Permission, the most restrictive across its parents and the chain, and is never
  looser.
- A user may make it stricter by hand, with the same permissions as today. Removing that extra strictness (never below the
  floor) needs F3 for Secure.
- When the parent loosens, only INHERITED values follow it; a value set ON the child stays.
- A re-file never loosens; it can only raise the record to the new floor.
- The lock covers Secure and Access Permission only. `/grant`, `/share-user` and Update Access stay as they are.

Absorbs #1478 (the five readers that decided Restricted from the record's own stored column). Verifier pass 1 on PR #1503
found F1, a re-file that un-secured a record without F3; round 87 closes it. The verifier's second pass found F1-1 (the
access record was user-writable, so a forged one bypassed F3) and items (a)-(e); the fix round closes them (below).

Branch `task/uac-r2-175`, rebased onto `origin/master` (`c8a87d818`: #1481 and #1490 merged).

## Superseded (for `notes/decisions.md`, on the work branch)

> **2026-10-09 — rounds 84 / 87, task 175, supersede round 6 item 4 and task 158's "no unsecure cascade" constraint.**
> - A work assignment or project filed under a matter or project is never looser than the floor its parents set.
> - Its INHERITED Secure follows the parent out of isolation, through `/unsecure-project`'s own steps (task 158's order and
>   stops), when the parent's own unsecure, or the job, sees the parents' floor drop.
> - A value set on the record by hand stays.
> - A re-file never loosens.
> - Removing a Secure that comes from a secure parent is refused (409 `sdap.access.access_follows_parent`). Removing one set
>   on the record follows F3, as for a parentless record.
> - Nothing else about task 158 changed.

(`notes/decisions.md` exists only on `work/unified-access-control-r2`; this PR is against master, so the main session adds
the entry there, as for task 173.)

## Inventory: every path that sets `sprk_issecure` / `sprk_accesspermission` on a work assignment or project

| Path | Now |
|---|---|
| `POST /provision-project` (Make Secure; the wizards' create-then-secure) | allowed on a child (tightening); after success on a work assignment / project the access record marks the Secure as set on the record |
| `ProvisionProjectEndpoint.ProvisionInheritedAsync` (task 158: parent secured, create into isolation, re-file, job) | unchanged (the securing direction stays task 158's) |
| `POST /unsecure-project` on a work assignment / project | refused 409 `access_follows_parent` when the floor is secure; otherwise F3, as before |
| `POST /unsecure-project` on a parent | its filed records follow it: an INHERITED Secure is un-secured, an own one stays (step 6) |
| `UnsecureProjectEndpoint.UnsecureInheritedAsync` (new) | the cascade's un-secure: the endpoint's own steps, no F3, a team owner |
| `OwnedChildWrite` isolated create (AI tool) | unchanged |
| BFF generic updates through `SecureRootFilingGate.CheckAsync` | `sprk_accesspermission` below the floor, or `sprk_issecure` false under a secure floor: refused `access_follows_parent`; at or above the floor: allowed; an unreadable filing: refused `record_owner_parent_undetermined` |
| Model-driven forms (`sprk_accesspermission_inherited.js` 1.2.0) | work assignment / project: a looser pick is put back, a stricter one kept, `sprk_issecure` disabled while filed |
| Grid edit, import, flow, direct Web API | a value below the floor is put back by the job (≤ 5 min); a stricter one is recorded as the record's own |
| A parent's change outside the BFF | the job (≤ 5 min); enforcement already follows the parent (task 174) |

## The design (round 87)

### Targets
- Stored value = max(own, floor).
- The floor: Secure if any ancestor is secure. Access Permission: the most restrictive EFFECTIVE value through the DIRECT
  parents, each folded with what is above it (task 174's walk; there is no second filing reader).
- Own: what was set on the record by hand, or held while it was parentless.

### The marker: one column, `sprk_accessinheritance`
- Multiple lines of text on `sprk_workassignment` and `sprk_project`.
- Written only by the BFF; never on a form. Field-secured: only task 133's "Spaarke BFF-Managed Field Writers" profile (the
  BFF application users) can read or write it; every BFF writer refuses a caller-supplied write that names it (fix round).
- Versioned JSON: `parents`, `floorSecure`, `floorPermission`, `ownSecure`, `ownPermission`.
- Data model: `docs/data-model/access-inheritance.md`. Schema: `scripts/Set-AccessInheritanceSchema.ps1`.

How it is read (`SecureRootInheritance.Decide`, pure):
- A stored value that differs from max(own, last floor) is a USER's edit:
  - an Access Permission above the last floor becomes own; at the floor it becomes "none set"; below the floor it is put back;
  - a flag turned secure while the floor does not explain it becomes own (Make Secure);
  - a flag cleared becomes "not own" (an F3 unsecure).
- A parent set that differs from the record's is a RE-FILE. What the stored values hold beyond the NEW floor becomes own, so
  a re-file never loosens.
- The same parents with a lower floor means the PARENT loosened: only the inherited part follows.

### Component justification (CLAUDE.md §11) — the new column
1. **Existing.** Nothing records whether a value is set on the record or inherited.
   - `sprk_issecure` / `sprk_accesspermission` hold only the effective value.
   - Task 173's reconciler treats a child's value as a pure copy.
   - The relocation ledger `sprk_relocationpending` is the precedent for a BFF-owned JSON column.
   - Searched with grep: `docs/data-model` and `src/server` have no own / origin / inherited column on these tables.
2. **Extension.** A per-value choice marker ("own" or "inherited", or the own value) was the first candidate, but on its own it
   cannot decide two cases, and both decide security:
   - a stored Restricted above a Standard floor is either "the parent just loosened" (follow down) or "a user just tightened"
     (keep);
   - a lower floor is either a parent loosening (follow) or a re-file (never loosen, verifier F1).

   Deciding them needs the last applied floor and parents too: five facts. One JSON column holds them, instead of five.
3. **Cost of doing nothing.**
   - Round 87 items 2 and 5 cannot be met: the owner's example fails, where a child made secure by hand, then its parent
     secured and un-secured, must stay secure.
   - A re-file un-secures without F3 (F1).
   - A form edit cannot be told from a parent change.

### Backfill rule (existing rows)
- There is no script backfill. The first time the job (every 5 minutes, every work assignment and project) or an inline
  follow sees a record without a marker, it applies the rule against the current floor and writes the marker:
  - a value EQUAL to the floor is inherited;
  - a value STRICTER than the floor is set on the record;
  - a parentless record's values are all its own.
- No exception (fix round): an EMPTY record never loosens anything. The earlier `parentWasSecure` exception (a flag below a
  parent being un-secured counted as inherited before its record existed) is removed: an empty record cannot be told from
  one field security hides from the BFF, and reading either as "inherited" would loosen. Instead a record secured by
  inheritance gets its record at once (fix a), and the job backfills the rest within one run.
- Consequence: a child whose parent was un-secured BEFORE the job first saw it (under round 6's rule it stayed secure; or in
  the minutes between the BFF deploy and the first job run) reads as stricter than its floor, so as set on the record. It
  stays secure (fail closed); its F3 holder can remove it, since its parent is ordinary.

### What was built, per goal item
1. **Cascade.** `SecureRootInheritance.FollowParentsAsync` (`SecureRootInheritance.Cascade.cs`, a partial of the ONE owner).
   - Order: (a) a stricter Access Permission; (b) the un-secure of an INHERITED Secure, through `UnsecureInheritedAsync`;
     (c) a looser Access Permission (the inherited part only); (d) the marker, last.
   - The un-secure uses the endpoint's own steps: owner = the parents' business-unit team via `RecordOwnershipResolver`
     (never the Secure Record Owners team), read back, related records out of isolation, shares revoked, flag cleared last.
   - The securing direction stays task 158's.
2. **Re-file.** `SecureAfterWriteAsync` follows the new parents (a changed project cascades below it). The marker tells a
   re-file apart, so it never loosens. A record left with no parent keeps its values; its marker records them as its own.
3. **Job.** `SecureRootInheritanceJob.FollowParentsPassAsync`.
   - Every work assignment and project, parentless ones included so their own values are recorded before any filing, is
     decided over one batched walk.
   - Only records with something to write are followed (a fresh read each).
   - Changed projects send their children round again, so a chain settles in one run.
   - 25 un-secures (cursor) and 500 other writes per run. Undecidable records are reported, not failing the run.
4. **Floor lock.**
   - Tightening is never refused.
   - `/unsecure-project` answers 409 when the floor is secure.
   - The gate refuses a write below the floor.
   - `can-manage-access` adds `floorSecure` and `floorAccessPermission`.
   - Ribbon 1.8.0: Make Secure follows its normal rules on a child; Remove Secure is hidden when `floorSecure`; both are
     hidden when `parentUnverifiable`.
   - Form library 1.2.0: a floor lock on the work assignment / project main forms, with inherited vs set-on-record text.
   - Manage Access: both banners; Access Permission options never below the floor; read-only when `parentUnverifiable` (K2);
     grants stay.
5. **Safety.**
   - A failed un-secure leaves the record flagged secure and Restricted. The marker still says "secure applied", so the next
     run retries and does not read the flag as the record's own (AC 6).
   - S5 holds: the record lands on its business-unit team.
   - Round 76 is untouched.
   - Documents are not moved (O-1, owner-accepted); container routing is not touched.

### #1478 — the five stored-column readers
These now also consult `EffectiveRootFlags.RestrictedThroughFilingAsync` (the 174 walk), each failing closed per its own
contract:
- `ProvisionProjectEndpoint` creator rule and colleague filter;
- `SecureChildShareSynchronizer.RestrictedExternalPrincipalsOrThrowAsync`;
- `SpeContainerMembershipSync.IsRestrictedAsync`;
- `OfficeEditAccessService`;
- the Assigned-To Restricted sweep, which also visits records filed below a Restricted matter or project.

### Other component justification (CLAUDE.md §11)
- `UnsecureInheritedAsync` + `UnsecureActor`: the endpoint's core gains a caller/cascade actor and a team owner; there is no
  second sequence.
- `AccessFollowsParent`: one 409 code shared by the unsecure route and the gate.
- `RecordAccessGateResponse.FollowsParents` / `ParentUnverifiable` / `FloorSecure` / `FloorAccessPermission`,
  `RecordUserShare.InheritedFrom`: additive fields; no new endpoint.
- `EffectiveRootFlags.RestrictedThroughFilingAsync`: a thin read over the 174 fold.
- Optional `IRecordOwnershipResolver` ctor parameter on `SecureRootInheritance`: resolves the existing singleton; no new
  registration.

### Placement (bff-extensions.md)
BFF. The transition runs inside the parent's unsecure request, the re-file writers and Make Secure. The safety net is the
existing in-process job (ADR-036/052). No package, endpoint, job or timer. One Dataverse column.

## Fix round (verifier pass 2: F1-1 and a-e)

| Item | What was wrong | Fix | Test |
|---|---|---|---|
| F1-1.1 FLS | `sprk_accessinheritance` was user-writable: forging "inherited" on a hand-secured child let the job un-secure it without F3 | `Set-AccessInheritanceSchema.ps1` creates the column SECURED and grants only "Spaarke BFF-Managed Field Writers" (read/create/update; members = the BFF app users, (p3)); no reader profile; any other profile that can read/write it FAILs. `-Verify` asserts IsSecured, the writer permission and members, and that each BFF user can READ it (System Administrator, or the platform's `RetrievePrincipalAttributePrivileges`; a read probe as the BFF user once a record exists). A grant failure leaves the column secured (fail closed), never reverted. (p7): a pre-existing unsecured column holding values blocks -Apply. | script `-SelfTest` 26 checks; `AccessInheritanceSchemaScriptAgreementTests` (6) |
| F1-1.1 empty vs unreadable | an FLS read loss reads empty, like "no record yet" | an empty record never loosens (`parentWasSecure` removed). The two ARE told apart on a write: each record write is read back; empty = hidden (`sdap.inherit.access_record_hidden`), the run fails, and the pass writes no further record (no churn) | `AnEmptyAccessRecord_NeverLoosens_…`, `AnAccessRecordTheBffCannotRead_LoosensNothing_FailsTheRun_…` |
| F1-1.2 writers | the BFF writers accepted the column in a caller's payload | `SecureRootFilingGate.CheckAsync` / `PlanCreateAsync` (every BFF writer: generic update handler, AI update/create handlers, `OwnedChildWrite`, Office record creation, field-mapping push) and `CheckRefileAsync` / `PlanCreateAsync` refuse any write that names it (any spelling, alone or with other columns, update or create): `sdap.access.access_record_server_only`. Only the cascade writes it. | `AForgedAccessRecord_IsRefusedByEveryBffWriter_AndNeverLeadsToAnUnsecure` |
| F1-1.3 config | a field-mapping rule / AI topic row / email update field could target it | the script's (p6), the same three channels as task 150 (pinned equal to `SecureFlagFieldSecurityAssertion.ConfiguredWriterChannels`) | agreement test |
| (a) | a record secured by inheritance had no record until the job ran, so a parent un-secured in between left it secure for good | `ProvisionAsync` writes the inherited record at once (`RecordInheritedSecureAsync`, best effort; the job writes it otherwise) | `ARecordSecuredByInheritance_GetsItsAccessRecordAtOnce_…` |
| (b) | the unreadable-record guard was not pinned | a corrupt record is reported undetermined (`access_record_unreadable`), loosens nothing, and is NOT overwritten | `ACorruptAccessRecord_IsReported_LoosensNothing_AndIsKept` |
| (c) | a user's edit between the read and the write was overwritten | `WritePermissionAsync` / `WriteMarkerAsync` re-read the column just before writing and skip on a change (`sdap.inherit.changed_concurrently`, counted `changedConcurrently`, not a failure); the next run decides again. `IGenericEntityService` has no row-version update, so it is re-read-and-compare (K below). | `AUsersEditBetweenTheReadAndTheWrite_IsNeverOverwritten_…` |
| (d) | round-2 un-secures did not count against `MaxUnsecuresPerRun` | checked before and counted after each round-2 follow | `UnsecuresInTheLaterRounds_CountAgainstTheRunsBound` |
| (e) | a failed record write was counted as in step | reported (`access_record_not_written`), `notCompleted`, the run fails | `AnAccessRecordThatCannotBeWritten_IsReportedAsAProblem` |

## Fix round 2 (final verifier pass 2 on `37113c322`: F1 + F4, K1-K4)

| Item | What was wrong | Fix | Test |
|---|---|---|---|
| **F1** | A record the BFF can WRITE but not READ read EMPTY; the concurrency check saw empty = empty and the backfill rule's guess overwrote the real record. Once read came back, a hand-secured child could be un-secured without F3. | Nothing is decided on, and nothing written over, an EMPTY read until the BFF's read of the column is PROVEN in this scope (one job pass, one request), cached: a non-empty record read in the scope; or the BFF user holds System Administrator; or a field security profile it is a member of grants Read on the column on both tables (`EqualUserId` -> `systemuserroles_association` / `systemuserprofiles_association` -> `fieldpermissions`). Unproven: `access_record_hidden`, Undetermined, nothing written. This applies to every path because it sits in `FollowParentsAsync`: the job's round 1 (checked up front over the listing) and round 2, the parent's records-below step and cascade, Make Secure, re-files, and task 158's secure path. A read-back that comes back empty flips the cache to unproven and stops both rounds. | `AnAccessRecordTheBffCannotRead_IsNeverOverwritten_NothingIsWritten_AndTheRunFails`; seeding check: the guard forced to "readable" turns it red |
| **F4** | The FLS test did not cover an own-secure record, round 2 or an inline path | The test now has an inherited record under an ordinary matter AND an own-secure record under a secure matter. It asserts both stored records are unchanged and nothing is written (no record, Access Permission or owner write), through the job and through the secure matter's own `/unsecure-project`. A second test covers round 2 (`OnceARecordReadsBackEmpty_NeitherRoundWritesAnotherRecord`). | those two |
| **K1** | Children secured before the deploy stayed secure for good when their parent was un-secured (round 87 item 1 gap) | `/unsecure-project` Step 1.6 (`RecordBelowBeforeUnsecureAsync`): before the parent's flag is cleared, every record below it with no access record gets one by the backfill rule while the parent is still secure, so it is recorded as inherited and follows in the cascade (best effort, at most 50; one not reached stays secure). The job writes inherited-secure backfills before any other record-only write. | `AChildSecuredBeforeTheDeploy_IsRecordedAsInheritedBeforeItsParentIsUnsecured_AndFollowsIt` (seeding check: step removed, red), `TheJob_RecordsAnInheritedSecureFirst` |
| **K2** | The cascade trusted records without knowing the column was secured | Before trusting any record, the column's metadata must say `IsSecured` on both tables (cached per scope; a fault = not secured). Otherwise every record is Undetermined (`sdap.inherit.access_record_not_secured`), nothing is written and the job run fails (`followParents.untrusted`). | `WhenTheColumnIsNotFieldSecured_NoAccessRecordIsTrusted_NothingIsFollowed_AndTheRunFails` |
| **K3** | `CheckRefileAsync` let `sprk_issecure` (true or false) through the app-only generic writers, skipping F3 and the provisioning / un-secure steps | The gate (`SecureRootFilingGate.CheckAsync` / `PlanCreateAsync`, and `CheckRefileAsync` / `PlanCreateAsync`) refuses any caller-supplied `sprk_issecure` write on a work assignment or project: `sdap.access.secure_flag_transition_only`. Only `/provision-project`, `/unsecure-project` and the cascade set it, and they write it directly, not through the gate. | `UpdateHandler_ASecureFlagWrite_IsRefused_AndNothingIsWritten` (true and false), `ActionCore_ASecureFlagWrite_IsRefused_AndNothingIsWritten`, `FieldMappingPush_OntoTheSecureFlag_FailsThatRecord_AndWritesNothing`; gate update and create in the forged-record test |
| **K4** | A parent re-secured between the child's Step 1.5 re-check and its flag clear | Left as a documented K (below). | - |

## Decisions and interpretations
- **Access Permission vs F3 (O-5, answered: "same as today").** Round 87 item 4 says removing extra strictness needs F3. On a
  parentless record today only removing Secure is F3-gated; the Access Permission is edited on the form with Write
  (`Set-SecureFlagFieldSecurity.ps1:47`: `sprk_accesspermission` stays editable by Write-holders). The owner chose "same as
  today": Secure removal goes through F3 (`/unsecure-project`); lowering a hand-set Access Permission needs Write only and is
  never below the floor. Kept as built.
- **A parent's loosening.** A Secure floor can only drop through the parent's F3 unsecure, or an administrator outside the
  BFF. The inline cascade and the job both make inherited Secure follow. The job does it only when the parents are unchanged,
  so it is never a re-file.
- **Make Secure on a child** records the Secure as set on the record at once. Without that, a parent secured within the same
  5-minute window would make it read as inherited.
- **A matter's own values:** a matter files under nothing, has no marker and is unchanged.
- **Display:** clients compare the value with the floor. Above the floor: "set on this record". At a floor above Standard:
  "inherited from {first direct parent}".

## POML premise corrections
- "Documents back to the business-unit container": `/unsecure-project` does not move documents. The cascade matches it, and
  the owner accepted this (O-1). Escalation trigger 1 cannot fire.

## Tests

New and changed tests, by area:
- **`ChildAccessCascadeTests`** (14). Real routes, inheritance and job over the provisioning fixture:
  - AC 1: another secure parent keeps the child secure; matter → project → work assignment in one call.
  - AC 2: Access Permission both ways; in-step rows not written; a parentless record never written.
  - Round 87:
    - the owner's example: Make Secure by hand under an ordinary matter, the matter secured and then un-secured, the child
      stays secure;
    - an own Limited is kept under a Restricted floor and comes back when the parent goes back to Standard;
    - an Access Permission below the floor is put back;
    - a re-file never loosens (F1), and a secure parentless work assignment filed under an ordinary matter stays secure;
    - the BFF floor gate: below refused naming the parent, equal and stricter allowed, a parentless record anything;
    - K1: the gate refuses when what the record is filed under cannot be read;
    - Remove Secure on an own-secure child is allowed (F3).
  - AC 6: a fault mid-cascade leaves the child secure and Restricted, never turned into "own"; the next run completes it.
  - The Secure-team-owner refusal; an unreadable parent; the #1478 helper and the mirror.
- **`SecureRootInheritanceTests` "the way back"** (4):
  - the parent's unsecure un-secures the inherited child;
  - `alsoUnsecure` is subsumed;
  - under a secure parent: 409; once the parent is ordinary: no longer refused;
  - an unreadable parent: 500.
- **Round 31 (2) and Round 39 (4 unsecure cases)** are updated to follow the matter. Round 39's five route-provisioning cases
  on a filed record are RESTORED from master unchanged, since Make Secure on a child is allowed again.
- **`RecordAccessGateTests`**: `followsParents`, `floorSecure`, `floorAccessPermission`, `parentUnverifiable`.
- **`ParentLineageTests`**: the root-map parity test.
- **Harness only**: `OfficeEditAccessServiceTests` and `AssignedAccessReconciliationJobTests`, plus one #1478 sweep test.
- **Jest (22 suites, 586 passed)**:
  - modal `AccessGrantModal.followsParent` (29): grants stay, both bars, inline 409, floor option filter, read-only when
    `parentUnverifiable`, origin display;
  - `TrackingFieldTrio.accessPermissionNote` (3);
  - ribbon `accessRibbon.followsParent` (28);
  - form library `accessPermissionInherited.roots` (30);
  - existing suites with updated version pins.
- **Fix round** (`ChildAccessCascadeTests`, +8; 22 in all): a forged record refused by every BFF writer and no un-secure;
  an empty record never loosens through the route; a record secured by inheritance gets its record at once and follows; an
  FLS read loss loosens nothing, fails the run and stops writing records; a corrupt record is reported and kept; a user's
  concurrent edit is never overwritten; round-2 un-secures count against the bound; a record write failure is a problem.
  `AccessInheritanceSchemaScriptAgreementTests` (6): the script's column, tables, writer profile, created-secured and (p6)
  channels agree with the BFF. The "way back" tests seed the inherited record task 158 now writes (`InheritedAccessRecord`).
- **Script self-tests**: `Set-AccessInheritanceSchema.ps1 -SelfTest` 26 checks; `Set-InheritedAccessPermissionFormLock.ps1
  -SelfTest` 47 checks.

### Seeding proofs (mutation checks, reverted)

| Mutation | Fails |
|---|---|
| Loosen the Access Permission before the un-secure | `AFaultMidCascade_LeavesTheChildSecureAndRestricted_…` |
| Re-file freeze removed | `ReFiling_NeverLoosensAChild` |
| Target ignores the own Access Permission | `AnAccessPermissionSetOnTheChild_…` and `ReFiling_NeverLoosensAChild` |
| No read-back after a record write (fix round) | `AnAccessRecordTheBffCannotRead_…` |
| Round-2 un-secures not counted (fix d) | `UnsecuresInTheLaterRounds_CountAgainstTheRunsBound` |

### Suite results, fix round 2 (2026-10-09)
- `tests/unit/Sprk.Bff.Api.Tests`: 19,110 passed, 3 failed, 54 skipped on the contended machine. The 3 were AI spend-limit,
  workspace AI summary and document email contract tests; they pass when re-run on their own (6 passed). The task-175
  classes: `ChildAccessCascadeTests` 26, writer tests incl. K3, all pass.
- `tests/Spaarke.ArchTests`: 841 passed. `Sprk.Bff.Api.IntegrationTests`: 87 passed, 5 skipped.
- `Set-AccessInheritanceSchema.ps1 -SelfTest`: 26 checks PASS.
- Seeding checks (reverted): the readability guard forced to "readable" turns the FLS test red; removing Step 1.6 turns the
  K1 test red.
- Publish: 38,156,084 B zipped (192 files) vs master `e78c47149` 38,110,531 B: **+45,553 B (+0.05 MB)**.

### Suite results (2026-10-09, fix round 1, rebased on `c8a87d818`)
- `tests/unit/Sprk.Bff.Api.Tests`: 19,098 passed, 7 failed, 54 skipped in the full run on a contended machine (other
  sessions' test hosts at 6-7 GB each); the 7 (Compose, RAG, health-header and agreement seam tests, none touching this
  task's code) all pass when re-run on their own (34 passed). Every task-175 class passes.
- `tests/Spaarke.ArchTests`: 841 passed.
- `Sprk.Bff.Api.IntegrationTests`: 87 passed, 5 skipped.
- `Spe.Integration.Tests`: 350 passed, 25 skipped.
- Jest (access ribbon, form library, AccessGrantModal, TrackingFieldTrio; no client change this round): 19 suites, 470
  passed.
- `Set-AccessInheritanceSchema.ps1 -SelfTest`: 26 checks PASS.

### Publish size, CVEs (CLAUDE.md §10, NFR-06)
- Fresh master `e78c47149`: 38,110,531 B zipped (Compress-Archive, as `Deploy-BffApi.ps1`; 192 files). No server change on
  master since then (`c8a87d818` is client only).
- Branch (fix round): 38,148,587 B (192 files). **Delta +38,056 B (+0.04 MB).**
- `dotnet list package --vulnerable --include-transitive`: none (no package added).

## Known limits
- **K2 (K4 of fix round 2):** a parent RE-SECURED between a cascaded child's Step 1.5 re-check (its parents are no longer
  secure) and the child's flag clear: the child is un-secured although its parent is secure again. The next job run secures
  it again (task 158's securing loop), and every BFF surface already treats it as secure through the parent meanwhile
  (task 174's effective rule).
- **K2 (fix round 2, F1):** the readability proof's data branch accepts any non-empty record read in the scope. Field
  security can also be granted per record (field sharing); a BFF that reads one record through such a share but not the
  column in general would pass that branch. The deploy grants the column through the writer profile only (`-Verify`).
- **K2 (fix c):** the concurrency check is re-read-and-compare just before the write, not a row-version (`If-Match`) write:
  the app-only `IGenericEntityService` the cascade writes through has no conditional update. A user edit landing in the
  milliseconds between that re-read and the write can still be overwritten; the stored value is then never below the floor
  and the next run records the edit's effect from what is stored.
- **K2 (F1-1):** System Administrators keep full access to the field-secured column (platform rule, owner decision F4, as
  for `sprk_issecure`).
- **K2 (backfill):** a record secured through its parent before the BFF deploy, whose parent is un-secured before the job
  first writes its record (one 5-minute run after the deploy), stays secure as its own; its F3 holder can remove it.
- **K2:** a parent's change outside the BFF reaches the stored values within one job run. Enforcement already follows the
  parent.
- **K2:** the inline cascade after a parent's unsecure is bounded at 50 records and `MaxFilingDepth`; the job does the rest.
- **K2:** Make Secure and a parent's securing done within the same job interval, without the explicit record (a BFF before
  this deploy): the backfill reads the child's Secure as inherited.
- **K2:** a grid / import edit of `sprk_accesspermission` stricter than the floor becomes the record's own on the next job
  run. A looser one is put back.
- **K2:** the form library reads the DIRECT parents' stored values for its floor (the server folds the chain). A parent not
  yet raised to its own floor gives the form a lower floor; the server puts back anything below the real one.
- **K2:** work assignment / project Quick Create forms are not registered for the form lock; the server is the backstop.
- **K2:** the external SPA's project "secure" label reads the stored flag and catches up within a run.

## Deploy order (main session)
1. **Schema + field security, BEFORE the BFF** (the BFF's writers refuse the column and its cascade needs to read it):
   `pwsh -File scripts/Set-AccessInheritanceSchema.ps1 -SelfTest`, then
   `pwsh -File scripts/Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
   (dry run), then the same with `-Apply`, then with `-Verify` (exit 0 required). Prerequisite: task 133's writer profile
   and its members (`Set-RecordCreatorPersonSchema.ps1`), already live on dev. If `RetrievePrincipalAttributePrivileges`
   cannot be called, `-Verify` FAILs and names the derivation; confirm it and re-run with `-AcceptDerivedReadCheck`.
2. Merge; deploy the BFF from a fresh short-path worktree of `origin/master`:
   `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`.
3. Web resources (existing names, so no `AddSolutionComponent`; read back and compare the hash):
   `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_/scripts/access_ribbon.js -FilePath src/client/webresources/js/sprk_access_ribbon.js -WebResourceType 3`
   `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_accesspermission_inherited -FilePath src/client/webresources/js/sprk_accesspermission_inherited.js -WebResourceType 3`
4. Form lock: `pwsh scripts/Set-InheritedAccessPermissionFormLock.ps1 -SelfTest`, then
   `-EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run), then `-Apply`, then `-Verify`.
5. Ribbon: no XML change. `Set-AccessRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -SecureTransitionDeployed -Verify -BeforeList <WorkDir>/before.json`
   (`-Apply` only if that fails).
6. PCF TrackingFieldTrio 1.0.45 through the `pcf-deploy` skill.
7. Watch two `secure-root-inheritance` runs, field `ResultJson.followParents`:
   - first run: `accessRecordsWritten` is about the number of work assignments and projects (the backfill), possibly deferred
     across runs at 500 per run;
   - then about 0;
   - `notCompleted` 0 and no `sdap.inherit.access_record_hidden` problem.
8. Re-run step 1's `-Verify`: now also the read probe (a record read as each BFF application user).

## Live gate (AC 8, main session)
1. Secure a throwaway matter with a filed work assignment, then unsecure the matter.
2. Confirm the work assignment follows: flag cleared, owner = the business-unit team.
3. Confirm Remove Secure is hidden on it while the matter is secure and Make Secure is shown after.
4. Confirm Access Permission cannot be set below the matter's.
5. Owner's example: Make Secure on the work assignment under the ordinary matter, secure then unsecure the matter; the work
   assignment stays secure.
6. Clean up.

## Open questions
- O-5 (answered by the owner, "same as today"): see Decisions.
- O-2 / O-3 (resolved by the coordinator): Update Access, `/grant` and `/share-user` stay available on a child.
