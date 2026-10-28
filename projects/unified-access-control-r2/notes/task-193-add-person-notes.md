# Task 193: one "Add person" entry (#2338): notes

Branch `fix/uac-193-add-person` (worktree C:/wt193), based on 078f62bcad (R3 #2360 merged). Runs A to F-final built the one entry (an internal colleague got a share only). Owner round 131 changed that: an internal colleague added either way gets a share **and** a paired, dated grant on their linked contact, and removing the share ends the grant. Runs G1 to G3c built round 131. The detail of each G run is below the summary tables.

## Runs
| Run | Head | What |
|---|---|---|
| A | fee1056ad2, 1c3b5b3360 | One contact → user rule (`ContactUserRepresentation`); the materializer, notifier and personal To Do assignee use it |
| B | ea01dc6f66 | `InternalUserShareCore` extracted from /share-user; caller-rights probe uses the gate question, with equalised timing |
| C | 78f5e7124b | /grant and /invite-and-grant decide share vs grant (`ContactInternalUserShare`) |
| C-fix | 298cbf57b9 | Required decision service; emails removed from two log lines |
| C-fix2 | 0427011e16 | Expiry ruling (c); no emails in any invite log template |
| D1 | 200bf9b89f | Modal: one "Add person" lookup; renders share vs grant |
| D1-fix | d6d78cde4b | /invite-and-grant → /grant parity; dead 503 branch removed; host tsc passes |
| D1-fix2 | 833fddbcdd | A shared contact is no longer offered (ledger adopt + Available hide) |
| D2 | a7d0f8186d | TrackingFieldTrio 1.0.55, builds, guide |
| V-fix | e09dc75dd9 | F1-a children_incomplete share path; delegation-filter timing (#2334); No Access on the contact route; invite logs; "Shared with X"; TrackingFieldTrio 1.0.56 |
| F-final | 9e0bfdcb88, 528daa090d | No invitee email in CIAM provisioner/mailer logs or send correlation ids; stale comments; unused params; user shares lead with "Shared with X"; TrackingFieldTrio 1.0.57 |
| G1 | 6e110453fe, 957b8257d8, 6a73134482 | Round 131: pairing column `sprk_pairedsystemuser` and `Deploy-ExternalRecordAccessPairedSystemUser.ps1` (not run anywhere); Add person writes the share, then the paired grant (`workforceApp`); reverse lookup for /share-user; R5 cleanup; `PairedSystemUserGuardTests` |
| G2 | a0f00919f8, a9dd8ce420 | `/unshare-user` ends the paired grant (`pairedGrantEnded`); `pairedWith` on `/user-shares` and `/record-grants` |
| G2b | 4f67347b0a, a970a05b33 | No Access, Restricted, secure-root inheritance and the Assigned-To materializer end the paired grant (`ExternalGrantRevocationService.RevokePairedWithAsync`, now registered); `PairedGrantFollowsShareRevokeGuardTests` |
| G3a | 1b4c2365ba, c2512a8075 | Item 4 pinned (Add person and Assigned-To in either order: not a defect); one pairing-column check and one Warning per pass |
| G3b | bdb12c0f16, f6eb5acd3f | Modal says workforce-app access ("visible … until", or why not); Current Access lists the share and its paired grant together; revoke confirmations; TrackingFieldTrio 1.0.58. Owner accepted the three wording choices ("in this app"; `would_lower_existing` its own sentence; unknown codes → "could not be given") |
| G3c | 8d0d0f078c (merge), 4ec9a1601a (guide), the notes commit | Master merged (8d0d0f078c, no conflicts); UAC guide describes round 131 as built; these notes; re-gate after the merge; publish delta |

## Behaviour changes (owner sign-off table for the PR)
| Case | Before | After |
|---|---|---|
| Materializer: an oid linked to two contacts | — | Contact grant (the oid is not trusted as one user) |
| Materializer: the oid read fails | — | Nothing written (link unreadable) |
| To Do assignee: inactive contact | — | No single user |
| To Do assignee: oid-bound, unlinked, confirmed user | — | That user |
| To Do assignee: contact read fails | — | Throws (fail closed) |
| /grant on a contact representing exactly one internal user | Contact grant | Share at the requested level; `sharedWithUser`; plus the paired grant (round 131, rows below); no invite |
| …representing several users | Contact grant | 409 `contact_represents_several_users`; nothing written |
| …a disabled user, or a non-person | Contact grant | 422 (the share core's codes); nothing written |
| …a flagged-external user, on a Restricted record | Refused (Restricted) | 422 `user_not_internal`; nothing written |
| …representation unreadable | Contact grant | 503 `contact_representation_unreadable`; nothing written |
| Share requested with an expiry | (n/a) | The share has no end date; `expiryNotApplied: true`. The paired grant takes the date (round 131). The modal: "The share itself has no end date, so the Expiration was not applied to it." |
| /invite-and-grant for an email resolving to one active contact that represents a user | CIAM invite + grant | Share plus the paired grant; `onboardStatus: SharedWithInternalUser`; no CIAM account or email |
| Modal | "+ Contact" / "+ User" | One "Add person" lookup; on a Restricted record, users only |
| No Access on a contact answered with a share | 422 `grantee_denied` (grant path) | `/grant` and `/invite-and-grant`, before any share, on every record (not only secure ones): an entry naming the contact, its organizations or the firm on the request → 422 `grantee_denied`; a list that cannot be read → 503; nothing written. A user picked directly through `/share-user` keeps Q4 (the wall applies on secure records only). |
| A request to an unknown record vs a denied one, at the delegation filter | Different timing | Same timing (`GetCallerRightsForAuthorizationGateAsync`, #2334) |
| Visibility in the workforce SPA/Teams of a colleague added (as a user or through their contact) | Visible when added through the contact (contact grant); not visible when picked as a user (share only) | **Owner round 131, as built:** visible either way, through the paired grant on their one linked contact (`workforceApp.visible: true`, `until`). Not visible, with a reason the modal names, when the record is Restricted, there is no single linked contact, the pairing column is not deployed, a read or write failed, their contact is on the No Access list, or their contact already holds more |
| Add person for an internal colleague (round 131) | Share only (runs C to F-final) | Share first; once confirmed, a paired grant at the same level on the one contact that represents them, through the grant core (ceiling, never lower, No Access, lapsed row restored), named in `sprk_pairedsystemuser`; the Expiration sent, else today + 90; no invite, no contact notification; response `workforceApp {visible, reason, until, accessRecordId}` (also on `children_incomplete`) |
| …on a Secure record | Share only | Share plus the paired grant (decision 1) |
| …on a Restricted record | Share only | Share only; `workforceApp.visible: false`, `record_restricted` |
| …no contact, several contacts, the link or pairing unreadable, the pairing column missing, the grant write failed | Share only | Share only, `visible: false` plus the reason; column missing logged at Error `PAIRING-COLUMN-MISSING` |
| …No Access flags unreadable | Refused, nothing written | Unchanged: refused before anything is written |
| Re-add of a colleague already shared | Share kept or raised | Also the grant core's re-add: raises the level, keeps a live date, restores a lapsed row with today's date, stamps the pairing on an unpaired row |
| `/unshare-user` (Current Access Revoke on a share) | Removes the share | Removes the share, then ends the paired grant; `pairedGrantEnded` true / false (false: left for R5), absent when nothing is paired; also in `children_incomplete` |
| No Access enforcement, Restricted enforcement, secure-root inheritance, Assigned-To withdrawal | Remove the share | Remove the share, then end the paired grant; a failed end makes the pass not complete |
| Remove Secure (`/unsecure-project`) | Sweeps the shares | Unchanged: no paired grant is ended (owner round 135; task 196 maps unsecure) |
| `/revoke` on the paired grant (Current Access Revoke on the grant row) | (n/a) | Ends the grant only; the share stays, and the confirmation says so |
| A share removed outside Spaarke | (n/a) | R5 in the daily reconciliation job (05:00 UTC) ends the paired grant, only while `ExternalAccess:Reconciliation:WritesEnabled` is on |
| Current Access | Share and grant rows listed separately | The paired grant sits under its share (`pairedWith`, ids and kind only); the share row notes "(workforce app access until {date})"; one Revoke removes both |
| The notice after Add person | "Shared with X" | "Shared with X — visible in their workforce app until {date}", or "They can open the record in this app, but it won't show in their workforce app because …" (one sentence per reason group; `would_lower_existing` its own sentence) |
| The notice after removing a share whose grant could not be ended | (n/a) | Adds "Workforce-app access will be removed within a day." |

## Security verifiers (a7d0f8186d)
- A (server): ready. Authorization parity with /share-user verified mechanically (the core diff is 2 lines). Fail-closed holds.
- B (client/contract): not ready. F1-a: the children_incomplete share path drops sharedWithUser, expiryNotApplied and the adopt. F1-b: SPA visibility (owner). **F1-b is answered by owner round 131 and built in G1 to G3b.**
- V-fix round: F1-a; the delegation-filter timing oracle (#2334) fixed in scope; No Access on the contact route; no emails in ExternalInviteService logs or exception messages; a ceiling test on /grant; "Shared with X" wording; stale comments; guide line 212.
- Link columns (`contact.sprk_externalobjectid`, `systemuser.sprk_primarycontact`) are field-secured, with the BFF as the only writer (task 141, `Set-ContactIdentityBindingSchema.ps1`). That bounds who can make a contact "represent" a user (System Administrators excepted).

## Known limits
- K: Available hides a represented-and-shared role contact only through its Assigned-To ledger entry. A role contact with no ledger row (the Assigned-To sync never ran on that record) stays offered after a share to its colleague. Re-adding it is harmless: /grant shares again at the same level and names the colleague.
- K: A ledger-adopt failure after a confirmed share keeps the share and returns 200. It is logged at Error through the materializer's shared mark path (no email, no name).
- K: The SpaarkeMaster solution-export copies of the TrackingFieldTrio bundle and the `sprk_spaarkeai` web resource update at the next release export, not on this branch.
- K: a stale ledger link (contact relinked from user A to user B while A holds a share) briefly hides the candidate, until the next Assigned-To sync.
- K: the /invite Retry doesn't ask who the contact represents (rare: the contact gets linked between the grant and the Retry).
- K (G1): **R5 counts direct shares only.** A paired user who loses the direct share but still reaches the record another way (ownership, a team) has the paired grant ended; they keep their model-driven-app access.
- K (G1): a share removed outside Spaarke leaves the paired grant live until R5's next run (the owner's cleanup rule).
- K (G1): grant gone, share kept: nothing happens; the share stands on its own (owner ruling).
- K (G1): deleting a systemuser empties the lookup (RemoveLink); the grant then reads as unpaired and stands as a deliberate grant.
- K (G1): a caller can't set the pairing column (no free-form DTO fields; field security; the app-only create path refuses field-secured columns).
- K (G2): the `SpeOutcome == Failed` branch of the paired end has no test (it needs a secure record with a container); reviewed by reading.
- K (G2b): No Access adds one pairing read per (covered record, covered user) per enforcement. Since G3a the missing column is checked once per pass, so one Warning per pass until the script runs.
- K (G2b): a parent that now passes nothing on (`PassesNothingOn`) still ends the pairing when it removes a leftover inherited share; only the unsecure cascade itself is skipped. Goes to task 196.
- **K (G3c, new): R5 writes only while `ExternalAccess:Reconciliation:WritesEnabled` is `true`, and it defaults to report-only** (owner round 7; DATAVERSE-WRITE-PATH-ARCHITECTURE I-8 records writes off). With writes off, a paired grant that `/unshare-user` or a system path could not end, or whose share was removed outside Spaarke, stays active until its own date (at most 90 days by default) or a manual Revoke, although the modal says "Workforce-app access will be removed within a day." Reported to the coordinator for the owner: enable R5 writes before this ships, or change the sentence. Not changed in G3c (wording the owner accepted in G3b).
- Filed: #2371 (inactive contact: /grant writes a dormant row; the contact-side route refuses); #2377 (an internal user's email with no active contact still gets a CIAM invite).
- Superseded by round 131: "re-adding a represented contact that already holds a contact grant shares and leaves the grant row as it was". The grant core now re-adds over it (raise, keep the live date, stamp the pairing).

## Gates (G3c)
After merging master `bafe310ca4` (merge 8d0d0f078c, no conflicts; master's overlap: `ContactIdentityStore`, `SecuredColumnReadProof`, the contact security schema agreement test, `Set-ExternalAccessContactSecuritySchema.ps1` and the wizard services, none of them on the pairing or share paths).

- BFF build: 0 errors, 0 warnings.
- Unit (touched, filtered: the `AccessControl` and `DataMutation` namespaces plus `ExternalAccess`, `AccessGrant`, `Share`, `Unshare`, `RecordGrants`, `PairedGrant`, `ContactUserRepresentation`, `Materializer`; master's new `SdkColumnReadPermissionSourceTests` and the changed identity-store tests fall inside it): 12129 passed / 0 failed / 0 skipped, 0 build warnings.
- ArchTests (full): 1620/1620.
- `Sprk.Bff.Api.IntegrationTests`: 186 passed, 0 failed, 8 skipped. `Spe.Integration.Tests`: 353 passed, 0 failed, 23 skipped.
- jest `AccessGrantModal`: 19 suites, 335/335. tsc: Spaarke.UI.Components 0 errors, TrackingFieldTrio 0 errors. `client-lint.mjs check --package ui-components`: ok.
- PCF `Invoke-PcfBuildProd.ps1` TrackingFieldTrio: PASS ("[build] Succeeded"; bundle 1.03 MiB; webpack's usual size warnings only).
- Publish (`dotnet publish -c Release`, folder total): origin/master 132,492,896 bytes; this branch 132,593,488 bytes; **delta +100,592 bytes (+98 KiB)**, of which `Sprk.Bff.Api.dll` +101,376. No package changes.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages, on the branch and on master.

## Gates (F-final, 528daa090d)
Unit (touched) 9167/0/3 · ArchTests 1577 · BFF integration 186/0/8 · Spe integration 353/0/23 · jest (modal) 311 · tsc 0 · lint ratchet ok · PCF build PASS (1.0.57) · 0 warnings. Remaining demo-registration log sites added to #2134.

## Gates (D1-fix2 / D2)
Unit (touched): 8921/0/2 · ArchTests 1577 · BFF integration 186/0/8 · Spe integration 353/0/23 · jest (modal) 306 · lint ratchet ok · PCF build PASS (1.03 MiB, +9.4 KB) · no CVE. Publish delta: +7.4 KB at Run C.

---

# Run detail: G1 to G3a

## What G1 does

- **Add person for an internal colleague** (`/grant` and `/invite-and-grant` on a contact who represents a colleague; `/share-user` on a systemuser) shares the record with the colleague, as before. After a confirmed share it now also grants the ONE contact that represents them in the workforce app, through the grant core (`GrantExternalAccessEndpoint.CreateGrantAsync`, `/grant` semantics: policy, ceiling, never-lower, No Access, re-add restores a lapsed row). The grant row is PAIRED with the user in `sprk_pairedsystemuser`.
- **Expiry (a):** the paired grant takes the request's date, else today + 90 (`ExternalGrantLifecycle.DefaultExpiry`). A live re-add keeps the grant's date, and a lapsed one is restored as today's grant. The share has no end date. `expiryNotApplied` now speaks for the share only.
- **The response** carries `workforceApp { visible, reason, until, accessRecordId }`, a nested object rather than the flat `workforceAppVisible` the brief named. It appears on the `children_incomplete` problem too (`extensions.workforceApp`). The top-level `AccessRecordId` stays `Guid.Empty` on a share.
- **Share only** (no grant, `visible: false` plus a reason) applies on a Restricted record (`record_restricted`), when the pairing column is missing (`workforce_pairing_column_missing`, logged at Error), when the pairing can't be read, when the grant write fails, and, for `/share-user`, when there is no contact, several contacts, the link can't be read, or the one contact also represents others.
- **Decision 1:** a Secure record gets the share plus the grant, the same as a Standard one.
- **Unreadable flags:** the share itself is refused before anything is written, because the share core's No Access guard fails closed. So "share only plus the flag" can't happen for unreadable flags: nothing is written at all. This is pinned by `G1_Grant_WhenTheFlagsAreUnreadable_RefusesTheShare_AndWritesNoGrant`.
- **Decision 2 (reverse lookup,** `ContactUserRepresentation.ResolveContactForUserAsync`): the candidates are the user's primary contact plus the contacts bound to their oid. Only those whose forward `ResolveAsync` includes the user are kept, and the user is paired only if exactly one remains.
- **The paired grant sends no invite and no contact notification.** `MarkContactShareAdoptedAsync` is unchanged, and the ledger's `MarkGrantAdoptedAsync` is not called for it (decision 3 is superseded).
- **`/set-record-share-expiry`** still writes the record-wide date to every active row, so a paired grant takes the record's date. This is kept as is.
- **Cleanup R5** (`ExternalAccessReconciliationJob`):
  - An active paired grant whose user no longer holds a direct share on the record is deactivated, and its pairing is kept. Each record's shares are read once.
  - A share read fault leaves the row unchanged; it is counted (`paired.shareUnreadable`), the run is partial, and the row is retried next run.
  - If the column is missing, R5 alone is skipped (`paired.columnMissing`, a Warning).
  - A probe fault is a failed scan.
  - Counts are logged at Information.
  - R1 to R4 are unchanged.

## §11 justification

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Column `sprk_externalrecordaccess.sprk_pairedsystemuser` (lookup → systemuser, field-secured) | Ledger rows tied to Assigned-To; the grant row's columns (`sprk_grantedby`, `sprk_grantedbycontact`) | The ledger can't hold the pairing without changing what a ledger row means. `sprk_grantedby` records who decided, not whom the grant mirrors. | Revoke and cleanup can't tell a paired grant from a deliberate one, so they would either end deliberate grants or leave paired ones behind. |
| `ExternalGrantLifecycle.ReadPairingsAsync` / `ProbePairingColumnAsync` | `RowSelect` reads | Adding the column to `RowSelect` would 400 every grant read before the script runs (deploy order) | With no separate read, nothing can fail closed on a missing column |
| `ContactUserRepresentation.ResolveContactForUserAsync` | The forward `ResolveAsync` (contact → users) | It reuses `ResolveAsync` as its filter; only the reverse candidate set is new | `/share-user` can't find the contact to grant |
| `ContactInternalUserShare.GrantPairedForUserAsync` and its private `GrantPairedAsync` | The grant core (`CreateGrantAsync`) | A thin caller of the core; it adds only the pairing read and the reason mapping | No paired grant for F1-b |
| `WorkforceAppAccess` (response field) | `expiryNotApplied` / `childrenIncomplete` | Those speak for the share; the grant needs its own outcome | The modal can't say whether the colleague can see the record in the workforce app |
| Reconciliation rule R5 | R1 to R4 in the same job (no new job) | A rule in the existing job and its existing deactivation path | A paired grant outlives a removed share until G2's revoke exists, and indefinitely for a share removed outside Spaarke |

## Known limits (K)

- **The cleanup gap:** a share removed outside Spaarke leaves the paired grant live until the next R5 run, which is up to a day later (the owner's cleanup rule).
- **Grant gone, share kept:** nothing happens. The share stands on its own, as the owner ruled.
- **Direct shares only:** R5 reads direct shares (POA) only. A paired user who loses the direct share but still reaches the record another way (ownership, a team) has the paired grant ended. They keep their Console access; only the workforce-app grant ends.
- **Deleting a systemuser** empties the lookup (Delete RemoveLink). The grant then reads as unpaired and stands as a deliberate grant.
- **Re-add:** the grant core can raise the level, keeps a live date, restores a lapsed row, and stamps the pairing on a row that lacks it.
- **Caller payloads:** a caller can't set the column. The grant DTOs have no free-form fields; a caller writing as themselves is refused by field security; and the app-only create path (`CallerWriteAuthorization`) refuses any field-secured column.

## Deploy order (for the owner's go at deploy time; NOT applied to dev in G1)

1. `scripts/Deploy-ExternalRecordAccessPairedSystemUser.ps1` (dry run) → `-Apply` → `-Verify` (exit 0).
2. Then the BFF. A BFF deployed first still works: it pairs nothing, gives share only (`workforce_pairing_column_missing`), and R5 is skipped.

## Guards and tests

- `tests/Spaarke.ArchTests/PairedSystemUserGuardTests.cs` checks five things: that the names agree with the script, that the column is not in `RowSelect`, membership through the table, field security in the solution source, and that the grant core is the only writer.
- Behavioural tests:
  - `ContactGrantSharesInternalUserTests` (the G1_* tests);
  - `GrantAccessNotificationTests` (three tests updated);
  - `GrantSendsInviteTests.Grant_ToAContactAnInternalUserLinksTo…` (it now expects the one paired grant; still no invite, one notice);
  - `ExternalAccessReconciliationTests`: the R5_* tests and the paired-scan FetchXML test, plus two existing tests updated for the fifth rule and the second grant scan.

### Perturbations (each must-fire seen red, then restored)

Each batch ran in a fresh write with a `--no-incremental` build over the filter `ContactGrantSharesInternalUserTests | GrantAccessNotificationTests | ExternalAccessReconciliationTests` (148 tests). The restored tree passed 148/148.

| Batch | Perturbation | Red |
|---|---|---|
| A | Column missing falls through to the grant, and reverse lookup Several → One | 2: …PairingColumnIsNotProvisioned…, …TwoContactsRepresentThem… |
| B | Pairing unreadable falls through, and `pairWrite` never true | 3: …PairingCannotBeRead…, …ReAdd_OverAnUnpairedGrant…, Grant_ToARepresentedContactHoldingAContactGrant… |
| C | The create omits the pairing bind | 7 results (6 tests): StandardOrSecure (×2), ReAdd_KeepsTheOnePairedGrant, ShareUser_WithTheOneContact, Grant_ContactRepresentingOneUser…, Grant_WhenTheBindingColumnIsMissing…, InviteAndGrant_ExistingContactLinkedToOneInternalUser… |
| D | R5 treats every mask as held, and job column-missing is no longer skipped | 5: R5 IsEnded, SomeoneElse, ReportOnly, ColumnNotProvisioned, SharesCannotBeRead |
| E | Fault not counted | 1: R5 SharesCannotBeRead |
| F | A share read fault is treated as "no share" | 1: R5 SharesCannotBeRead |
| G | Reverse lookup None reported as Several | 1: ShareUser_WithNoContact |
| AR1 | Pairing column added to `RowSelect` | 1 of 5 ArchTests (deploy order) |
| AR2 | `IsSecured` 0 in Entity.xml | 1 of 5 (field-secured) |
| AR3 | A second writer of the bind, in another file | 1 of 5 (only the grant core) |
| AR4 | The script's `$Column` changed | 1 of 5 (names agree) |

## G2: paired revoke and `pairedWith`

### Revoke paths (every BFF call that removes a user's share on a matter, project or work assignment)

| Path | What removes the share | Ends the paired grant in G2? |
|---|---|---|
| `POST /unshare-user` (`InternalShareEndpoints.UnshareAsync`): Manage Access "remove" on a person row, which is Current Access's remove for an internal user | The operator | **Yes.** The share is removed first, then the grant. |
| `UnsecureProjectEndpoint.RevokeAllSharesAsync` (making a record not secure) | The record leaves secure isolation; every explicit share is swept and the user keeps business-unit access | **No.** R5 ends it within a day. Owner question below. |
| `NoAccessShareEnforcer` (a No Access user's share on a secure record) | Enforcement | **No.** R5 ends it within a day. Owner question below. |
| `RestrictedExternalShareRemover` (a flagged-external user's share on a Restricted record) | Enforcement | **No.** R5 ends it within a day. Owner question below. |
| `SecureRootInheritance` (an inherited share withdrawn when the parent's share goes) | The parent's change | **No.** R5 ends it within a day. Owner question below. |
| `AssignedAccessMaterializer` (an Assigned-To auto share withdrawn) | The assignment changed | **No.** R5 ends it within a day. Owner question below. |
| `ProvisionProjectEndpoint.RestoreCreatorShareAsync` (undoing provisioning's creator share) | An undo of the BFF's own share; never an "Add person" share | Not applicable |
| `SecureChildShareSynchronizer` / `GateDecisionRecordSharing` / `PlaybookSharingService` | Child rows, gate decisions, playbooks: never a record a grant is paired on | Not applicable |

Grant-side revokes never remove the share:
- `/revoke` (Current Access remove on a grant row);
- the contact-side `/contact-grants/revoke`, which can't reach a paired grant anyway, since a paired row is issued by an internal user;
- person removal;
- project closure.

This is what the cleanup ruling implies ("grant gone, share stays: do nothing"), and it is pinned by `G2_RevokingOnlyThePairedGrant_LeavesTheShare`.

### Behaviour

- **Order:** the share first, then the grant. `/unshare-user` ends the paired grants only once the user's share on the record is confirmed gone. That covers the 200 (removed, or held none, so a repeat completes an earlier half-done revoke) and `children_incomplete`, which is sent only after the record's own share is removed.
- **If the share revoke fails or is refused,** nothing is changed.
- **Each paired grant goes through the one grant revoke** (`ExternalGrantRevocationService.RevokeAsync`): the rows, the SPE cleanup, the cache and the ledger marking. They are found by `sprk_pairedsystemuser` plus the record (`ReadPairingsOnRecordAsync`), with the user checked again in memory. A deliberate grant, or a grant paired with someone else, is never ended.
- **Response:** `pairedGrantEnded` is `true` when the grant was ended. It is `false` when the pairing couldn't be read, a row wasn't deactivated, or the SPE cleanup failed. In that case the share stays removed, the failure is logged at Error with ids only, and R5 ends the grant on its next run. It is absent when no grant is paired or the column is missing (missing column logged as a Warning). It also appears in the `children_incomplete` extensions.
- **`pairedWith`** (`RecordAccessPairing {id, kind}`):
  - A share row (`/user-shares`) carries the paired grant's `accessRecordId`, kind `grant`. If two contacts' grants are paired with the same user, the lowest id is shown.
  - A grant row (`/record-grants`) carries the paired user's `systemUserId`, kind `share`.
  - Ids and kind only, never a name, email or UPN. Both lists already require Write and Share on the record, so a caller who can see one can see the other.
  - **It fails quiet:** a missing or unreadable column means no `pairedWith` (Warning) and the list still answers. This is acceptable because no access decision reads it; it is display only.

### Owner question (not decided in G2)

Should the five system share-removal paths (unsecure, the No Access enforcer, the Restricted remover, secure-root inheritance, Assigned-To) also end the paired grant right away? Today R5 ends it within a day.

The unsecure path in particular removes the share while the user keeps access through the business unit, so ending their workforce-app grant there may not be wanted.

### §11 (G2)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ExternalGrantRevocationService.RevokePairedWithAsync` | `RevokeAsync`, the one grant revoke | A method on that service that calls `RevokeAsync` per paired row; only the find-by-pairing read is new | A removed share leaves a live workforce-app grant for up to a day |
| `ExternalGrantLifecycle.ReadPairingsOnRecordAsync` | `ReadPairingsAsync` (by grant key) | It shares the same private reader; only the filter differs | Neither the revoke nor the lists can find a user's paired rows on a record |
| `UnshareRecordWithUserResponse.PairedGrantEnded`, `RecordAccessPairing` / `PairedWith` | `removed`; `inheritedFrom` | Additive optional fields | The caller can't tell a half-done revoke, and Current Access can't tie the two rows together |

### Tests

- `ContactGrantSharesInternalUserTests` G2_* (15):
  - unshare ends the paired grant;
  - grant end fails → `false`, share stays removed;
  - share revoke fails → nothing changed;
  - column missing → share only;
  - pairing unreadable → `false`;
  - only the user's paired grant is ended;
  - a repeat with no share still ends it;
  - no pairing → as before;
  - `children_incomplete` carries the flag;
  - grant revoke leaves the share;
  - `pairedWith` both ways with no email or name;
  - no pairing on deliberate rows;
  - unreadable or missing column → lists unpaired (two cases).
- Not covered by a test: the `SpeOutcome == Failed` branch. It would need a secure record with a container. That branch is perturbation O's sibling and is reviewed by reading.

### Perturbations (G2)

Each batch ran in a fresh write with a `--no-incremental` build over the filter `ContactGrantSharesInternalUserTests | InternalUserShareTests | AssignedAccessMarkerTests | RecordGrants` (226 tests). The restored tree passed 226/226.

| Batch | Perturbation | Red |
|---|---|---|
| H | Paired end runs whether or not the share was removed | 1: …WhenTheShareRemovalFails_ChangesNothing… |
| I | `pairedGrantEnded` not added to `children_incomplete` | 1: …WhenRelatedRecordsAreIncomplete… |
| J | The 200 never carries `pairedGrantEnded` | 4: RemovesTheShare_ThenEnds…, PairedGrantCannotBeEnded…, PairingCannotBeRead…, UserHoldsNoShare… |
| K | Column missing reported as Failed | 1: …PairingColumnIsNotProvisioned… |
| L | Unreadable pairing reported as none paired | 1: …PairingCannotBeRead… |
| M | The user filter dropped (in the read and in memory) | 1: …EndsOnlyTheGrantPairedWithThatUser |
| O | A failed grant revoke counted as ended | 1: …PairedGrantCannotBeEnded… |
| P | Grant row `pairedWith` dropped | 1: …EachNameTheOther… |
| Q | Share row `pairedWith` dropped | 1: …EachNameTheOther… |
| R | Display pairing read fails loud | 14 results (11 tests): the G2 unreadable-pairing Theory plus 10 existing `/user-shares` and `/record-grants` tests whose fakes don't answer the pairing read |

## G2b: system share removals end the paired grant at once

Coordinator ruling: a system path that removes a share because the person must no longer see the record ends their paired grant in the same operation; waiting for R5 (up to a day) leaves workforce-app access on a record the person was walled off. Owner round 135 (via the coordinator): unsecure keeps all access, shares and grants; that change is tracked separately, so Unsecure is not touched here.

### The one helper

`ExternalGrantRevocationService.RevokePairedWithAsync`, the same pairing read and grant revoke `/unshare-user` uses, is now registered (scoped, `ExternalAccessModule`) instead of built by hand per caller. `/unshare-user` takes it as one `[FromServices]` parameter (its four SPE/time/identity parameters are gone). The class became `public` with `internal` members only so the public share-removal services can take it in their constructors. A logical-name overload maps `sprk_matter` etc. to the root type; a table that is not a grant root has nothing paired.

### Paths (updates the G2 table)

| Path | Ends the paired grant? | When it fails | Incomplete contract |
|---|---|---|---|
| `/unshare-user` | Yes (G2) | `pairedGrantEnded: false` | the response says so |
| `NoAccessShareEnforcer` | **Yes**, for every covered user whose direct share is confirmed gone (removed now, or held none). Not when S5 keeps the share | `paired-grant-not-ended` failure | `Complete` false: never "fully enforced" while the grant may be active |
| `RestrictedExternalShareRemover` | **Yes**, for each user whose share this pass removed | `paired-grant-not-ended` failure | `Complete` false |
| `SecureRootInheritance.EndInheritedSourceAsync` | **Yes**, when the inherited share is removed outright (not put back to a prior level) for a user. **Not on the unsecure cascade** (`endingParent` set), per owner round 135 | end not done, detail names the paired grant | the pass is not complete (`/unshare-user` answers `children_incomplete` with `filedRecordsNotUpdated`) |
| `AssignedAccessMaterializer` | **Yes.** It CAN remove a share that has a paired grant: an "Add person" share at the level the rule wrote reads as the rule's (`AssignedAccessMaterializer.cs`, the `Shared` case: `row.GrantedLevel == current` then `RemoveOrRestoreShareAsync`). Resolved through its scope factory (the revoke depends on the materializer) | `paired-grant-not-ended` failure | `Complete` false |
| `UnsecureProjectEndpoint` | No: declared (owner round 135) | | |
| `ProvisionProjectEndpoint`, `SecureChildShareSynchronizer`, `GateDecisionRecordSharing`, `PlaybookSharingService` | No: declared (undo of its own creator share; child rows; not grant roots) | | |

Order everywhere: the share first, then the grant. Grant-end failure: the revoke logs Error with ids only; the share stays removed; R5 ends the grant. Missing column: the revoke's Warning; share-only behaviour as before.

No Access "either way": the enforcer ends no contact grant itself (contacts are vetoed at read time). Its covered users are the user named, the users a named contact represents, and the users an organization's member contacts represent, so the pairing ends whichever the entry names.

### Known limits

- No Access adds one pairing read per (covered record, covered user) per enforcement, beside the share read it already makes per pair. With the column missing, each such read logs the revoke's Warning, which on dev (schema not applied) is a Warning per walled pair every 5 minutes until the script runs.
- The secure-root inheritance's net path for a parent that now passes nothing on (unsecured or deleted, `PassesNothingOn`) still ends the pairing when it removes a leftover inherited share; only the unsecure cascade itself is skipped. Coordinator: this goes to **task 196** (Unsecure keeps all access), which maps every removal on unsecure; left as it is here.
- ~~Pre-existing: "Add person" at the same level as an existing Assigned-To auto share leaves the share the rule's.~~ **Corrected in G3a: not a defect.** The G2b claim was read from the materializer alone; the share core adopts on its no-write path (see G3a below).

### Guard

`tests/Spaarke.ArchTests/PairedGrantFollowsShareRevokeGuardTests.cs` (compiled IL scan): every BFF type that references `IDataverseRecordShareService.RevokeAccessAsync` also references `RevokePairedWithAsync`, or is on the declared list with a reason. The five system paths must end the pairing, and a declared entry that no longer revokes, or now ends the pairing, must leave the list. Must-fire: a bare revoke and a revoke in a lambda. Must-not-fire: revoke plus paired end, a declared type, a grant-only type. Known limit: per type, not per removal.

### Tests

- `PairedGrantFollowsShareRemovalTests` (11): No Access (removes then ends; contact subject; holds no share; end fails → incomplete; column missing → share only; S5 keeps share → grant kept), Restricted (ends; end fails → incomplete), materializer (ends; end fails → incomplete; share kept → grant kept).
- `SecureRootInheritanceRound31Tests.G2b_*` (2): the matter's unshare ends the grant paired with the filed record's inherited share; end fails → `children_incomplete`, share stays removed, grant left for R5.

### Perturbations (G2b)

Each batch: a fresh write, a `--no-incremental` build, over the filter `PairedGrantFollowsShareRemovalTests | SecureRootInheritanceRound31Tests | ContactGrantSharesInternalUserTests` (130 tests), or the guard (6). The restored tree passed 130/130 and 6/6.

| Batch | Perturbation | Red |
|---|---|---|
| S | No Access: the paired end call removed | 4: removes-then-ends, contact subject, holds no share, end fails |
| T | No Access: a failed end not recorded | 1: end fails → incomplete |
| AA | No Access: the paired end runs when S5 kept the share | 1: S5 keeps share → grant kept |
| U | Restricted: the paired end call removed | 2: ends, end fails |
| V | Restricted: a failed end not recorded | 1: end fails → incomplete |
| W | Inheritance: the paired end disabled | 2: both G2b_* |
| X | Inheritance: a failed end not reported not-done | 1: end fails → `children_incomplete` |
| Y | Materializer: the paired end skipped | 2: ends, end fails |
| Z | Materializer: a failed end not recorded | 1: end fails → incomplete |
| AB | `/unshare-user` on the injected helper: share-gone check inverted | 6 G2_Unshare_* |
| AC | Guard: the Restricted remover stops calling `RevokePairedWithAsync` | 2 guard facts (the rule, and "the system paths end the pairing") |

### Gates (G2b)

- 0 warnings: the BFF, the unit project, ArchTests and both integration projects.
- The touched filter (the `AccessControl`, `DataMutation` and `ExternalAccess` test namespaces): 10636/10636.
- Full ArchTests: 1604/1604. `Sprk.Bff.Api.IntegrationTests`: 186 passed, 8 skipped. `Spe.Integration.Tests`: 353 passed, 23 skipped.
- One host fixture changed: `NoAccessEnforceEndpointTests`' host substitutes the grant revoke with the production one over an empty grant table. Its Web API client double answers no grant read, so the enforcer correctly failed closed (`paired-grant-not-ended`) until the boundary was substituted like the others.

## G3a: Add person and Assigned-To in either order; one pairing-column check per pass

### Item 4: not a defect (three tests pin it)

The share core, which Add person reaches through `ContactInternalUserShare`, records the operator's provenance in the Assigned-To ledger in both orders. No second store is involved.

| Order | What the ledger records | On unassign | Pinned by |
|---|---|---|---|
| Added first, then assigned | The rule finds the share already covering its target: `CoveredByExisting` (`AssignedAccessMaterializer.FreshShareAsync`) | The `default` case: nothing of the rule's to remove. The share and its paired grant stay | `G3a_AddedFirst_ThenAssigned_ThenUnassigned_…` |
| Assigned first, then Add person at the same level | Nothing is written, but `InternalUserShareCore` calls `MarkShareAdoptedAsync` on its no-write path, so the row becomes `Adopted` | `KeptAdopted`. The share and its paired grant stay | `G3a_AssignedFirst_ThenAddedAtTheSameLevel_…` |
| `/unshare-user` while still assigned | `MarkShareRemovedAsync` sets the row `Declined` (and puts it back if the revoke is not confirmed) | The materializer's contract: a `Declined` row is never re-created while the assignment persists, and a later re-assignment is new. The removal stands and the paired grant follows the share (G2) | `G3a_AssignedAndAdded_ThenUnsharedWhileAssigned_…` |

All three passed on the code as it was, with no production change.

### Item 2: one check and one Warning per pass

- `ExternalGrantRevocationService` remembers a missing pairing column in its instance. After the first 400 it reads no more pairings and logs nothing more. An unreadable read is transient, so it is not remembered.
- The per-user pairing read runs only while the column exists.
- **Scope per pass, confirmed:**
  - `/unshare-user`: the request scope.
  - `NoAccessShareReconciliationJob`, `AssignedAccessReconciliationJob` (the Restricted remover and the materializer) and `SecureRootInheritanceJob`: one `CreateScope()` per run.
  - The enforce, sync and record routes: the request scope.
- **Fixed:** the materializer resolved the revoke from a NEW scope for every removed share, which meant a new instance and a Warning per removal. It now resolves it from its own scope's `IServiceProvider` (an optional constructor parameter; DI gives a scoped service its scope's provider). The materializer and the Restricted remover in the same job pass, and the share routes in a request, now share one revoke. This is not a constructor dependency: the revoke depends on the materializer.

### Tests (G3a)

- `ContactGrantSharesInternalUserTests.G3a_*` (3): the order cases above.
- `PairedGrantFollowsShareRemovalTests`:
  - `NoAccess_ManyUsersOnManyRecords_WithoutTheColumn_…`: an organization entry, 3 users × 3 secure records, exactly one Warning and one pairing read.
  - Its positive twin: nine pairing reads, no Warning.
  - `Materializer_TwoSharesRemovedInOnePass_WithoutTheColumn_…`: one Warning and one read.

### Perturbations (G3a)

Over `PairedGrantFollowsShareRemovalTests | ContactGrantSharesInternalUserTests | SecureRootInheritanceRound31Tests` (136 tests). The restored tree passed 136/136, and the guard 6/6.

| Batch | Perturbation | Red |
|---|---|---|
| G1 | The share core's no-write path stops adopting | 2: `G3a_AssignedFirst_…`, and an existing round-31 raise test |
| G2 | The materializer records its own share over an existing one (no `CoveredByExisting`) | 1: `G3a_AddedFirst_…` |
| G3 | `/unshare-user` marks no `Declined` row | 2 existing round-31 tests. `G3a_…UnsharedWhileAssigned` stays green, because the materializer's out-of-band rule records the removal as `Declined` (`RemovedOutOfBand`) on its next pass, which is defence in depth |
| G3b | The materializer's `Declined` stops being sticky | 1: `G3a_…UnsharedWhileAssigned` |
| G4 | The missing-column cache disabled | 2: both one-Warning tests |
| G5 | The materializer resolves the revoke from a new scope per removal | 1: the materializer one-Warning test |

### Gates (G3a)

- 0 warnings: the BFF, the unit project, ArchTests and both integration projects.
- The touched filter (the `AccessControl`, `DataMutation` and `ExternalAccess` test namespaces): 10642/10642.
- Full ArchTests: 1604/1604. `Sprk.Bff.Api.IntegrationTests`: 186 passed, 8 skipped. `Spe.Integration.Tests`: 353 passed, 23 skipped.
