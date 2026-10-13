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
  sync after a removal. Never a team share, never an internal user's, never on a non-Restricted record. **Restricted wins
  over the last-reader rule** (owner round 67 item 3, decided 2026-10-06 — superseding the S5 hold first built here): on a
  Restricted SECURE record the external sharers are removed even when they are its last readers, and the pass reports
  `noInternalReader` (`no-internal-reader — an administrator must share it with an internal user`) with a warning naming
  the record — not a failure; administrators still see it. Fails closed on unreadable flags / shares / users.
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
  ribbon — the `Command` of the platform's Share buttons, never an assumed command — and `-Verify` checks it.
  **Corrected 2026-10-07 (live dev `-Verify` failed on all three entities):** there is no `Mscrm.Form.<entity>.Share` /
  `Mscrm.HomepageGrid.<entity>.Share` button. The live ribbons (RetrieveEntityRibbon, read-only) expose the UCI form
  Share as `Mscrm.Form.<entity>.Permissions.Sharing` → `Mscrm.SharePrimaryRecordRefresh` (plus the legacy flyout's
  `Permissions.SharingNonRefresh` → `Mscrm.SharePrimaryRecord`) and the grid / subgrid Share as
  `Mscrm.{HomepageGrid,SubGrid}.<entity>.Sharing` → `Mscrm.ShareSelectedRecord`; the script now looks there
  (fix/uac-r2-114-share-ribbon), and the dry run with `-EnvironmentUrl` reads them live as `-Apply` does.
  The dry run uses `fixtures/share-command.dry-run-sample.xml` (a stand-in, never imported). Dry run PASSED for all
  three entities. `assignedaccess_postsave.js` 1.1.0 refreshes the command bar when Access Permission changes. **Cost:
  small** (one rule, one function, merge/apply/verify additions). **Follow-up:** the GRID and SUBGRID Share get
  `ShareAllowedSelection.EnableRule` → `isShareAllowedForSelection(SelectedControlSelectedItemIds, SelectedEntityTypeName)`
  (hidden when ANY selected row is Restricted), copied from the live ribbon (`-GridShareCommandXml`), with `-Verify`.
- **(b) reconciliation removal** — `AssignedAccessReconciliationJob` scans Restricted roots
  (`AssignedAccessStore.ScanRestrictedRootsAsync`, `sprk_accesspermission eq 100000002`) and runs the remover on each,
  before the materializer. WRITES ON (like task 143's No Access job — it only removes); NOT behind the revoke-on-change
  switch (that switch governs ended assignments). A root only in the Restricted set is not materialized. A record left with
  no internal reader is counted (`noInternalReader`) without making the run partial; an unconfirmed removal fails the run. **Cost: small** (one scan, one call).
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
| Provisioning creator / Make Secure caller (`EnsureCreatorShareAsync`, every path: forward, resume, Make Secure resume, inherited) | ✅ owner item 3 (Restricted wins): `RestrictedCreatorRule` — on a Restricted record a person flagged external is NOT shared to (the step counts as proven: nothing is owed, so the unconfirmed-move fallback grant never fires); named in `skippedPrincipals` as `principal_external_on_restricted`; provisioning still succeeds and the response carries `noInternalReader` + `noInternalReaderMessage` when nobody internal can open the record |
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
3. **Restricted wins over the last-reader rule** (owner round 67 item 3, decided 2026-10-06). The remover removes an
   external-flagged share even from a secure record's last reader and reports `no-internal-reader`; provisioning does not
   share a Restricted record to an external-flagged creator / Make Secure caller and says so in the response. An
   administrator (who still sees the record) shares it with an internal user.
4. **Serialized with task 143's enforcer** (follow-up item 4): the remover takes task 143's per-record lease
   (`NoAccessShareEnforcer.RecordLockId` on `IScheduledJobLease`, renewed before every revoke) around its share read, S5
   decision and revokes; `/unshare-user` takes the same lease around its S5 check and revoke (409
   `sdap.access.user_share.record_busy` while another removal holds it). A held or unreachable lease removes nothing.
5. **An external-flagged OWNER** (follow-up item 2): ownership confers access no share revoke removes (and Dataverse
   refuses an app-only revoke of the owner's own share, 0x80040223). The remover reads `_owninguser_value`, leaves that
   user's share alone, reports `ownerIsExternal` (kind `owner-is-external`) with a warning naming the record, and the job
   counts it (`ownerIsExternal`) without making the run partial. Ownership is NEVER changed by the BFF.
6. **`sprk_isexternal` must not be field-secured**: a masked value reads as blank, which is now INTERNAL (owner ruling).
7. **ONE "barred on Restricted" predicate** (verifier V4): `InternalShareEndpoints.IsBarredOnRestricted(isExternal,
   rootIsRestricted)` = a stored `true` on a Restricted (or unread, fail-closed) root — disabled or not, person or not.
   `ClassifyEligibility` asks it after disabled / not-a-person for a NEW share (so `/share-user` keeps those refusals);
   every component that removes, keeps away or records the absence of a share asks it directly: the remover (sharers AND
   owner), the materializer's target (before `ClassifyEligibility`, so a disabled external user's removal is the known
   cause `Skipped(restricted)`, never `Declined`), the inheritance's barred set (`RestrictedExternalPrincipalsOrThrowAsync`
   now selects only `sprk_isexternal`), provisioning (creator rule, colleagues), the `/user-shares` marker and the
   systemuser plane's composition (K1).
8. **The owner is read on every Restricted pass** (verifier V3): before any early return, and included in the user read,
   so an external owner who holds NO share is still reported `owner-is-external`. An owner read that fails is no longer
   silent: the pass carries an `owner-unreadable` failure (external sharers are still removed; the pass is not complete,
   so the job retries).
9. **Provisioning never reports a barred person as shared to** (verifier V1/V2): `ShareEnsureResult.Barred` carries
   "nothing is owed"; Step 4.5 no longer counts it as a proven share; after an UNVERIFIED owner move the answer is
   `owner_assignment_unverified` with `creatorShareConfirmed: false`, `creatorShareSkippedReason:
   principal_external_on_restricted` and text that says it was NOT shared. The last-resort unconfirmed share after an
   unverified move is written only when a FRESH flag read (`RestrictedCreatorRule.IsBarredAsync(..., fresh: true)`)
   answers "not barred" — barred or unreadable, nothing is written. A successful response names a barred person in
   `skippedPrincipals` and carries `sharedToCreatorSystemUserId = Guid.Empty` (found while adding V6's tests: it used to
   carry the barred person's id, i.e. claimed a share that was never written).
10. **The SPA/Teams plane** (verifier K1, owner round 67): a systemuser flagged external keeps NO membership-term access to
    a Restricted record — the Restricted veto applies to them as to a contact. `AccessibleRecordSetService` reads the flag
    through the authoritative cached `ISystemUserIdentityResolver.IsExternalAsync` only when a membership candidate is
    Restricted (NFR-02); a read that fails is answered external (fail closed). An internal or blank-flag systemuser is
    unchanged; inactive-only records keep the existing survivor rule.
11. **Known limit — Dataverse business-unit read (owner round 77, accepted, no code change):** a licensed user flagged
    external can still READ a NON-secure Restricted record in their own business unit through Dataverse's role depth
    (MDA, Dataverse API). The BFF removes their direct shares, refuses new ones and vetoes the record on the SPA/Teams
    plane; it does not and will not change Dataverse role depth for them.
12. **Deploy order** (verifier V5): the B2B guest data step runs BEFORE the BFF in an existing environment (§5).

## 4. Tests

| Where | What |
|---|---|
| `tests/integration/auth/UnifiedAccessControl/InternalUserShareTests.cs` | the retired `[Theory]` inverted (true/null/false shared on a non-Restricted record, level + mask read back, `created`); Restricted + true → 422 `user_not_internal`, nothing written; Restricted + blank/false → shared; flags unreadable (throw / absent) → 500 `read_failed`; disabled / support / delegated-admin / application user flagged external on Restricted keep their own 422; the rule table |
| `tests/integration/contract/Api/ExternalAccess/InternalUserShareContractTests.cs` | the retired 422 inverted to 200 created; Restricted + external → 422 `user_not_internal`; `externalNoAccess` on `/user-shares` |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessMaterializerTests.cs` | linked external/blank → shared on Standard; Restricted: external → Skipped(restricted), blank → shared; the transition (shared → Restricted → removed → Skipped(restricted) → Standard → shared again) |
| `tests/integration/auth/UnifiedAccessControl/RestrictedExternalShareRemoverTests.cs` (new) | removal + read-back + cache key; only a stored true (blank, internal, team kept); not Restricted → no read; flags / shares / users unreadable → nothing written; unconfirmed revoke; the last reader removed + `noInternalReader` (secure) / never on a non-secure record; external owner; the shared lease (held, not renewable, released); idempotent |
| `tests/integration/auth/UnifiedAccessControl/AssignedAccessSyncEndpointTests.cs` | through the real filter pipeline: Restricted + external share removed and named; unconfirmed → 500 `restricted_external_incomplete` |
| `tests/unit/Sprk.Bff.Api.Tests/Services/ExternalAccess/AssignedAccessReconciliationJobTests.cs` | a Restricted-only root's external share removed (counts); the last reader removed → `noInternalReader` 1, run ok; external owner counted, run ok; unconfirmed → failed run |
| `tests/integration/seam/Communication/FanOutTargetingSecuritySeamTests.cs` | (h) through the real resolver |
| `RestrictedExternalShareRemoverTests.cs` (verifier round) | an external owner with NO share (alone / beside internal sharers) is reported; a blank-flag owner is not; an unreadable owner → removals still made, `owner-unreadable`, not complete; a DISABLED external sharer is removed |
| `AssignedAccessMaterializerTests.cs` (verifier round) | a DISABLED external user's auto share removed on Restricted → `Skipped(restricted)`, never `Declined` |
| `SecureRootInheritanceRestrictedTests.cs` (verifier round) | a DISABLED external sharee's inherited share removed on Restricted → `Skipped(restricted)` |
| `ProvisionNoAccessTests.cs` (verifier V6) | resume (`EnsureResumeCreatorShareAsync`) with an external / internal creator on Restricted; Make Secure resume (`ResumeMakeSecureAsync`) by an external caller (caller skipped, internal creator shared); unverified owner move with a barred creator (V2 answer); unverified owner move with an unreadable flag (V1: no last-resort share, `creator_share_failed_resumable`) |
| `UnifiedEvaluatorSeamTests.cs` (K1) | an external-flagged systemuser loses the Restricted record (keeps an open one); an internal one keeps it; an unreadable flag removes it (fail closed); no Restricted candidate → no flag read |
| `src/client/shared/Spaarke.UI.Components/src/__tests__/accessRibbon.restrictedShare.test.ts` (new) | `isShareAllowed` (field / saved value / failed read / unsaved), the on-change ribbon refresh, the summary lines |
| `…/AccessGrantModal/__tests__/AccessGrantModal.userShare.test.tsx` | the "External user — no access" label, only on the marked row |

Unshare of a disabled external account (`Unshare_ADisabledExternalAccountsShare_IsStillRemoved`) stays green.

## 5. Deploy order (manual gates; none run by this task)

1. **Data step FIRST**, per existing environment (verifier V5): `scripts/Set-ExternalFlagForB2BGuests.ps1` dry run →
   owner review → `-Apply` → `-Verify` (exit 0) — **before** the BFF. The BFF before this task already reads a blank flag
   as external, so this changes nothing for it; the BFF of this task reads blank as INTERNAL, so a guest still blank when
   it starts would be shared with on Restricted records and receive internal-only messages. If the BFF went first: run
   the data step at once and allow **10 minutes** after `-Apply` (the identity resolver's `sprk_isexternal` cache — which
   now also decides a licensed user's Restricted-record access on the SPA/Teams plane, K1) before relying on it.
   ⚠️ Within 5 minutes of `-Apply` on a BFF that carries this task, the job removes each newly flagged guest's share on
   every Restricted record. A NEW environment has no users at BFF deploy; Phase 7b follows user provisioning (H11).
2. **BFF** (fresh short-path master worktree, `Deploy-BffApi.ps1`). From this moment blank-flag users are internal for
   sharing and messaging, and the 5-minute job converts their contacts' Assigned-To grants into shares.
3. **Web resources**: `sprk_/scripts/access_ribbon.js` 1.6.0, `sprk_/scripts/assignedaccess_postsave.js` 1.1.0.
4. **Ribbon**: `Set-AccessRibbon.ps1` dry run → `-Apply` → `-Verify` (the Share command rule rides the existing Access
   group import; same `-SecureTransitionDeployed` rule as before).
5. **PCF**: TrackingFieldTrio 1.0.36 (the "External user — no access" label) — `npm run build:prod`, solution import.
6. **Administrator action, after the job's first runs**: (a) a secure Restricted record left with NO internal reader
   (`no-internal-reader` warnings, `noInternalReader > 0` in the job result, or a provisioning response with
   `noInternalReader: true`) must be shared with an internal user by an administrator; (b) a Restricted record OWNED by a user flagged external keeps that
   user's access by ownership — no share revoke can remove it, and the BFF never reassigns. Look for `owner-is-external`
   warnings (`[RESTRICTED-EXTERNAL]`) or `ownerIsExternal > 0` in the Assigned-To job's result, and reassign each named
   record to an internal owner (or team).
