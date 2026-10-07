# Task 114 — `sprk_isexternal` and sharing: blank is internal, Restricted refuses external (owner round 67)

> **Task**: `tasks/114-iss024-external-licensed-user-is-internal.poml` — the BINDING scope is the 2026-10-06 amendments
> block (owner round 67, `notes/session27-owner-decisions-and-research.md` "Round 67"), which supersedes the POML's
> original goal ("regardless of the external flag") and the 2026-10-05 amendment n=2.
> **Register**: ISS-024 / GitHub #1003. **Branch**: `task/uac-r2-114` (from `origin/master` @ `4eeed68ee5`).
> **Why now**: on dev, 7 of 11 enabled person system users have a BLANK `sprk_isexternal`. The old rule
> (`isExternal is not false` → refused) answered `/share-user` 422 `user_not_internal` for them and made the
> Assigned-To materializer give their contacts a grant row instead of a share.

## 1. The rule (owner round 67)

| Record | `sprk_isexternal` false or blank | `sprk_isexternal` true |
|---|---|---|
| Not Restricted | share | share (2026-09-18 ruling) |
| Restricted (`sprk_accesspermission` = 100000002) | share | **refuse** |

No licence check is added: an enabled person (access mode 0–2, no application id) is the proxy; the platform's own
Share dialog shares only with licensed users. **Blank = not external, everywhere.**

## 2. What changed, per amendment

### Amendment 1 — one rule for both consumers
- `InternalShareEndpoints.ClassifyEligibility(isDisabled, accessMode, applicationId, isExternal, rootIsRestricted)` — the
  third branch is now `isExternal == true && rootIsRestricted is not false → ExternalOnRestricted` (enum renamed from
  `NotInternal`). `rootIsRestricted` is consulted only for an external-flagged user; `null` (not read) refuses such a
  user (fail closed by construction).
- `/share-user` (`ShareAsync`) reads the root's flags ONLY for a user flagged external who is otherwise eligible (a
  disabled / non-person account keeps its own 422; an ordinary share costs no extra read). Unreadable flags → 500
  `read_failed` ("could not be read", never "Restricted" — task 138's rule). New injected parameter:
  `ExternalParticipationService participations`.
- Reason code **kept**: `sdap.access.user_share.user_not_internal` (422), now ONLY "Restricted + flagged external". Its
  summary and the refusal message were rewritten.
- `AssignedAccessMaterializer.ResolveTargetAsync(subject, flags, ct)` passes `flags.IsRestricted`. `ExternalOnRestricted`
  → `Skip(restricted, user)` — no share and no contact grant (Restricted admits no contact access either). The old
  "refused only because external → contact grant row" mapping is gone: a blank- or true-flagged linked user is SHARED
  with on a non-Restricted root, and an existing contact grant for such a user converts to a share through the existing
  task-141 conversion path.

### Amendment 2 — messaging resolver
- `SystemUserIdentityResolver.IsExternalAsync`: only a stored `true` is external; a blank flag is internal. A missing
  row and an empty id stay external (fail closed); a read that throws propagates. Doc comments updated there and in
  `CommunicationFanOutTargetingService`; `docs/architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md` and
  `docs/guides/NOTIFICATIONS-AND-SUGGESTIONS-USER-GUIDE.md` (which told operators to "backfill" internal users) updated.
- `FanOutTargetingSecuritySeamTests` gains case (h): the fan-out through the REAL resolver (only Dataverse + cache
  doubled) — blank and false receive an internal-only message, true and an unknown systemuser do not. The existing
  external-licensed-user exclusion test (g) is unchanged and green.

### Amendment 3 — becoming Restricted
- **The "existing Restricted transition path"**: contact-sourced access on a Restricted record is removed at READ time
  (`RootRecordFlags.RemovesContactSourcedAccess`); there is no write-side transition for it. The place a record BECOMES
  Restricted is its save — the MDA form's post-save sync (`POST /assigned-access/sync`, `sprk_assignedaccess_postsave.js`),
  also "Update Access" — and the 5-minute `AssignedAccessReconciliationJob`, which task 142 documented as covering
  "Secure/Restricted transitions". The new rule runs in exactly those two places.
- New `Infrastructure/ExternalAccess/RestrictedExternalShareRemover` (Component Justification in its header): on a
  Restricted root, removes every DIRECT system-user share whose holder's `sprk_isexternal` is a stored `true`. Strict
  read first, revoke through the one share seam, strict read-back, root-set cache cleared in `finally`, task 149's child
  sync after a removal. Never a team share, never an internal user's, never on a non-Restricted record. **S5**: on a
  Restricted SECURE record, when no enabled internal user with a readable share would remain, nothing is removed and the
  external sharers are reported `keptAsLastReader`. Fails closed on unreadable flags / shares / users.
- Sync route: runs between the No Access enforcement and the materializer; response gains `restrictedExternal`
  (additive); a removal that cannot be confirmed → 500 `sdap.access.assigned.restricted_external_incomplete`.
- Materializer: an auto share the remover took away is recorded `Skipped(restricted)` — a KNOWN cause, never
  `Declined(removed-out-of-band)` — so the share comes back when the record stops being Restricted.

### Amendment 4 — OOB Share on Restricted records
- **(a) ribbon hide rule** — the existing Access ribbon pattern (`infrastructure/dataverse/ribbon/AccessRibbons/`): the
  template gains `sprk.Access.{{entity}}.ShareAllowed.EnableRule` → `Spaarke.Access.Ribbon.isShareAllowed`
  (`access_ribbon.js` 1.6.0; the form field when present, else one saved-value read; a failed read hides Share).
  `Merge-AccessRibbon.ps1 -ShareCommandXml` copies the PLATFORM's form Share command and appends the rule (idempotent;
  verified locally: a second merge is byte-identical). `Set-AccessRibbon.ps1 -Apply` reads that command from the live
  ribbon — the `Command` of the `Mscrm.Form.<entity>.Share` button, never an assumed id — and `-Verify` checks it.
  The dry run uses `fixtures/share-command.dry-run-sample.xml` (a stand-in, never imported). Dry run PASSED for all
  three entities. `assignedaccess_postsave.js` 1.1.0 refreshes the command bar when Access Permission changes. **Cost:
  small** (one rule, one function, merge/apply/verify additions). **Follow-up:** the GRID and SUBGRID Share get
  `ShareAllowedSelection.EnableRule` → `isShareAllowedForSelection(SelectedControlSelectedItemIds, SelectedEntityTypeName)`
  (hidden when ANY selected row is Restricted), copied from the live ribbon (`-GridShareCommandXml`), with `-Verify`.
- **(b) reconciliation removal** — `AssignedAccessReconciliationJob` scans Restricted roots
  (`AssignedAccessStore.ScanRestrictedRootsAsync`, `sprk_accesspermission eq 100000002`) and runs the remover on each,
  before the materializer. WRITES ON (like task 143's No Access job — it only removes); NOT behind the revoke-on-change
  switch (that switch governs ended assignments). A root only in the Restricted set is not materialized. A kept S5 share
  makes the run partial (`KEPT-LAST-READER`); an unconfirmed removal fails the run. **Cost: small** (one scan, one call).
- **(c) Manage Access label** — `/user-shares` reads `sprk_isexternal` with the names and, when one is listed, the
  root's flags; `RecordUserShare.ExternalNoAccess` (additive) is true for an external-flagged user on a Restricted
  record. `AccessGrantModal` shows **"External user — no access"** (`EXTERNAL_USER_NO_ACCESS_LABEL`) for such a row,
  still revocable. TrackingFieldTrio PCF bumped 1.0.35 → 1.0.36 to ship the modal. **Cost: small**.

### Follow-up — every other path that WRITES a share on a project, matter or work assignment

| Path | Rule |
|---|---|
| `/share-user` (`InternalShareEndpoints.ShareAsync`) | ✅ `ClassifyEligibility` (amendment 1) |
| Assigned-To materializer (`WriteShareAsync`) | ✅ `ClassifyEligibility` via the target (amendment 1); its `ModifyAccess` on an ended assignment only narrows |
| Secure-root inheritance (`SecureChildShareSynchronizer.SyncInheritedRootAsync`, `:879/:885`) | ✅ follow-up item 1: `RestrictedExternalPrincipalsOrThrowAsync` → `ClassifyEligibility(…, rootIsRestricted: true)`; a barred sharee is `InheritedShareAction.Restricted` (never copied); `SecureRootInheritance` records a removed one `Skipped(restricted)`, restored when lifted; an unreadable answer gives nobody anything |
| `SecureRootInheritance` `:1664` (`ModifyAccess` back to the prior mask) | n/a — narrows only |
| Provisioning colleagues (`ProvisionProjectEndpoint.ShareToColleaguesAsync`) | ✅ follow-up item 1: `WithoutExternalOnRestrictedAsync` — an external-flagged colleague (or, on Make Secure, the record's creator) on a Restricted record is skipped `sdap.provision.principal_external_on_restricted`; an unreadable flag skips as `principal_no_access_unverifiable` |
| Provisioning creator / Make Secure caller (`EnsureCreatorShareAsync`, the unconfirmed-move fallback grant) | ⚠️ deliberately exempt: it is the share that keeps a secure record openable (S5's floor). An external-flagged creator on a Restricted record is then the remover's S5 "kept as last reader" case — owner item 3 decides |
| Provisioning undo (`RestoreCreatorShareAsync`) | n/a — puts back the pre-call state |
| Secure-child mirror (`SecureChildShareSynchronizer` `:1753/:1759`) | n/a — writes CHILD tables (documents, events, …), never a root; mirrors the root's remaining shares |
| `PlaybookSharingService`, `DirectThreadAccessService` | n/a — playbooks and communication threads, not roots |

### Amendment 5 — data step
- `scripts/Set-ExternalFlagForB2BGuests.ps1` — dry run (default) / `-Apply` / `-Verify`; lists every systemuser whose
  `domainname` contains `#EXT#` (URL-encoded) and whose flag is blank or No; `-Apply` PATCHes only `sprk_isexternal = true`
  (`If-Match: *`); JSON report. Added to `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (§7.7 and §12.2 Phase 7b) and
  `scripts/README.md`. **Written, not run.**

## 3. Decisions recorded

1. **`null` (blank) is eligible** — owner round 67 item 3; pinned by `Share_OnARestrictedRecord_WithABlankOrFalseFlag_IsShared`
   and the `ClassifyEligibility` table test. The retirement of the old fail-closed `null` guard is deliberate.
2. **Reason-code constant retained** (`UserNotInternalReasonCode`, same string): round 67 says "keep a reason code"; it
   is reachable only on Restricted + flagged external.
3. **S5 wins over the Restricted removal** on a secure record whose only readers are external-flagged — the precedent of
   `/unshare-user` and task 143's enforcer. Reported, not silent. ⚠️ Owner may prefer the opposite; see the report.
4. **Serialized with task 143's enforcer** (follow-up item 4): the remover takes task 143's per-record lease
   (`NoAccessShareEnforcer.RecordLockId` on `IScheduledJobLease`, renewed before every revoke) around its share read, S5
   decision and revokes; `/unshare-user` takes the same lease around its S5 check and revoke (409
   `sdap.access.user_share.record_busy` while another removal holds it). A held or unreachable lease removes nothing.
5. **An external-flagged OWNER** (follow-up item 2): ownership confers access no share revoke removes (and Dataverse
   refuses an app-only revoke of the owner's own share, 0x80040223). The remover reads `_owninguser_value`, leaves that
   user's share alone, reports `ownerIsExternal` (kind `owner-is-external`) with a warning naming the record, and the job
   counts it (`ownerIsExternal`) without making the run partial. Ownership is NEVER changed by the BFF.
6. **`sprk_isexternal` must not be field-secured**: a masked value reads as blank, which is now INTERNAL (owner ruling).

## 4. Tests

| Where | What |
|---|---|
| `tests/integration/auth/UnifiedAccessControl/InternalUserShareTests.cs` | the retired `[Theory]` inverted (true/null/false shared on a non-Restricted record, level + mask read back, `created`); Restricted + true → 422 `user_not_internal`, nothing written; Restricted + blank/false → shared; flags unreadable (throw / absent) → 500 `read_failed`; disabled / support / delegated-admin / application user flagged external on Restricted keep their own 422; the rule table |
| `tests/integration/contract/Api/ExternalAccess/InternalUserShareContractTests.cs` | the retired 422 inverted to 200 created; Restricted + external → 422 `user_not_internal`; `externalNoAccess` on `/user-shares` |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMaterializerTests.cs` | linked external/blank → shared on Standard; Restricted: external → Skipped(restricted), blank → shared; the transition (shared → Restricted → removed → Skipped(restricted) → Standard → shared again) |
| `tests/integration/auth/UnifiedAccessControl/RestrictedExternalShareRemoverTests.cs` (new) | removal + read-back + cache key; only a stored true (blank, internal, team kept); not Restricted → no read; flags / shares / users unreadable → nothing written; unconfirmed revoke; S5 kept / removed; idempotent |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessSyncEndpointTests.cs` | through the real filter pipeline: Restricted + external share removed and named; unconfirmed → 500 `restricted_external_incomplete` |
| `tests/unit/Sprk.Bff.Api.Tests/Services/ExternalAccess/AssignedAccessReconciliationJobTests.cs` | a Restricted-only root's external share removed (counts); S5 kept → not ok; unconfirmed → failed run |
| `tests/integration/seam/Communication/FanOutTargetingSecuritySeamTests.cs` | (h) through the real resolver |
| `src/client/shared/Spaarke.UI.Components/src/__tests__/accessRibbon.restrictedShare.test.ts` (new) | `isShareAllowed` (field / saved value / failed read / unsaved), the on-change ribbon refresh, the summary lines |
| `…/AccessGrantModal/__tests__/AccessGrantModal.userShare.test.tsx` | the "External user — no access" label, only on the marked row |

Unshare of a disabled external account (`Unshare_ADisabledExternalAccountsShare_IsStillRemoved`) stays green.

## 5. Deploy order (manual gates; none run by this task)

1. **BFF** (fresh short-path master worktree, `Deploy-BffApi.ps1`). From this moment blank-flag users are internal for
   sharing and messaging, and the 5-minute job converts their contacts' Assigned-To grants into shares.
2. **Data step** per environment: `scripts/Set-ExternalFlagForB2BGuests.ps1` dry run → owner review → `-Apply` →
   `-Verify`. ⚠️ Within 5 minutes the job removes each newly flagged guest's share on every Restricted record.
3. **Web resources**: `sprk_/scripts/access_ribbon.js` 1.6.0, `sprk_/scripts/assignedaccess_postsave.js` 1.1.0.
4. **Ribbon**: `Set-AccessRibbon.ps1` dry run → `-Apply` → `-Verify` (the Share command rule rides the existing Access
   group import; same `-SecureTransitionDeployed` rule as before).
5. **PCF**: TrackingFieldTrio 1.0.36 (the "External user — no access" label) — `npm run build:prod`, solution import.
6. **Administrator action, after the job's first runs**: a Restricted record OWNED by a user flagged external keeps that
   user's access by ownership — no share revoke can remove it, and the BFF never reassigns. Look for `owner-is-external`
   warnings (`[RESTRICTED-EXTERNAL]`) or `ownerIsExternal > 0` in the Assigned-To job's result, and reassign each named
   record to an internal owner (or team).
