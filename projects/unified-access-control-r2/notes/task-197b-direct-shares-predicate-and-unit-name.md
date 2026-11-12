# Task 197b: Direct-message shares through the predicate; stored flag only; one unit name

> Owner and coordinator decisions 2026-11-12 on task 191c's follow-ups: piece 1 = option B (participant-scoped NoRoot
> exemption), piece 2 = stored flag only, piece 3 = one source for the unit name with a minimal startup warning.
> Nothing was applied live: no Dataverse, Entra, Key Vault or App Service change was made.

**Recorded decision: flagged participants keep Direct messaging; NoRoot exemption is participant-scoped.**

## 1. Piece 1: Direct-message participant shares for users flagged external

An unfiled Direct message reaches no root, so THE predicate (191b) answers Undecided with reason `NoRoot`. "Undecided" means
"no grant", so applying the predicate unchanged would have ended Direct messaging for every flagged user. Option B keeps it,
inside bounds that are enforced in code:

| Bound | Where it is enforced |
|---|---|
| (i) the message's owner is positively recognized as the Direct-message owner team | `DirectMessageParticipantShares` steps 2-4 (an unknown or refused lookup grants nothing). On the live grant, the Direct-team branch of `DirectThreadAccessService.GrantDirectTeamMessageAsync`. |
| (ii) the participant pair is valid | Step 5 (`ParticipantsOf`): an invalid or missing pair is settled with nothing granted. |
| (iii) the grantee is one of that message's named participants | Only `participants ∩ FlaggedExternal` are decided; a non-participant is never granted (and loses a share on the message, as before). |

All three hold only on the Direct-team path, which passes `directTeamOwnedWithPair: true`. Every other caller passes `false`,
so NoRoot stays Undecided there and nothing is granted.

### The per-row answer (`DecideForDirectParticipantsAsync`, one predicate call per message)

| Predicate answer for the row | Flagged participant |
|---|---|
| Hidden (internal-only 065, thread not Open, Never table such as the review log, Restricted or tainted parent) | **Hidden**: not granted; a held share is revoked (owner re-read first, revoke then strict read-back). |
| Visible | Grant |
| Undecided `NoRoot`, decision not faulted, Direct-team bound true | Grant |
| Undecided and transient (`ExternalTeamShareSynchronizer.IsTransient`: ReadFailed, RootUnreadable, RowNotFound, DocumentUndecided, InvoiceUndecided, NotAsked) | **Defer**: not granted, reconcile incomplete, retried by the job |
| Undecided and structural (any other reason) | **Withhold**: not granted, settled |
| No seam, seam throws, decision faulted (a non-Hidden row) | Defer |

Content rows follow their message: Hidden if either is Hidden, the row's own answer if the message is Grant, Defer if either
defers, otherwise Withhold. A content row has no exemption of its own.

Unflagged participants never ask the predicate and are granted exactly as before. A participant whose flag cannot be read is
not eligible on the reconcile path (`RestrictedFilingShareBar`, round 70: never granted, never revoked on an unknown flag).

### Where each path stands

| Path | Behaviour after 197b |
|---|---|
| Share job (`SecureChildShareReconciliationJob` Direct pass) | Seam taken from the run scope; full table above, including the Hidden revoke. |
| Live grant, Direct-team message (`DirectThreadAccessService`) | Seam resolved in its own scope (the service is a singleton). Grants only; it never revokes (the job does). |
| Live grant, other Direct message (user-owned or another team's) | A flagged participant, or one whose flag cannot be read, is granted only when the message is **Visible**. No NoRoot exemption: bound (i) does not hold. |
| Refile path (`SecureChildReconciler.DirectParticipantShares`, task 197a's file) | No seam is passed, so flagged participants are deferred there; the job grants them on its next run (about 2 minutes). |

## 2. Piece 2: `GateDecisionRecordSharing` reads the stored flag only

| | Before | After |
|---|---|---|
| Columns read per sharee | `sprk_isexternal`, `domainname` | `sprk_isexternal` |
| External test | `sprk_isexternal == true` OR a guest-shaped domain name | `sprk_isexternal == true` (ADR-028 A9 Model 1) |
| A guest-shaped name with the flag false or blank | skipped (no share) | internal: shared |

Unchanged and noted only: "an unreturned sharee row is skipped" and "a read fault shares nothing".

## 3. Piece 3: one source for the External Collaborators unit name

- `ExternalTeamShareSynchronizer.ResolveExternalTeamAsync` now uses `ExternalCollaboratorPlacement.ResolveUnitAsync`. That lookup
  takes its name from the compiled constant, requires exactly one unit, and requires the unit to be a direct child of the root.
  A unit that is not under the root is now refused (before, any unit with the name was accepted).
- **Setting removed (name only):** `ExternalAccess:ExternalTeamShare:BusinessUnitName` (environment form
  `ExternalAccess__ExternalTeamShare__BusinessUnitName`). The duplicate default constant is also gone.
- No checked-in appsettings, Bicep or workflow file carried the key, so no environment-config file changed. A new ArchTest scans
  checked-in configuration for both forms, with a control that proves the scan finds them.
- Startup check: `StartupValidationService` calls `ExternalTeamShareSynchronizer.ReportRetiredSettings`. It logs one Warning
  naming the KEY only (never the value) when the setting still has a value. Without it, a leftover slot value would be silently
  ignored and nobody would know the config is stale. The coordinator checks slot setting names before the next deploy.
- ArchTest `TheUnitName_AndTheRoleName_AreTheRoleFiles` pins `ExternalCollaboratorPlacement.BusinessUnitName` and `RoleName` to
  `config/external-collaborator-role.json`.

## 4. Known limits (reported, not fixed here)

1. **The thread create share and the record-anchored branch** of `DirectThreadAccessService` do not consult the predicate (the
   allow-list entry says so). The thread row is not message content; record-anchored grants follow the record's audience.
2. **Deploy-order consequence.** `DirectMessage:BusinessUnitName` and `OwnerTeamName` are unset in every environment today
   (until fm 054b is verified), so every Direct message is user-owned. On those messages a flagged recipient now gets no live
   share unless the message is Visible, and an unfiled Direct message is never Visible. Flagged users regain Direct messaging
   once the Direct team is configured.
3. **191c engine and user-owned Direct messages.** When 191c's writes are turned on (191i), its engine will treat user-owned
   Direct threads and messages as NoRoot and revoke flagged users' shares there. This agrees with limit 2 but should be decided
   explicitly before 191i. To be filed and reported.
4. **Refile path** defers flagged participants (no seam there, 197a's file); the job grants them within one run.
