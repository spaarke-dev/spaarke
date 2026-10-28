# Task 193 (#2338) — Manage Access "Add person": G1 notes

> Scope of this file: G1 only (owner decisions of 2026-10-28: F1-b, expiry (a), the cleanup rule, pairing column (iii), decisions 1 and 2). G2 (revoke both ways, partial outcome, Current Access "internal user" label) and G3 (notices, modal text, guide, PCF bump, publish delta, CVE) follow.

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
