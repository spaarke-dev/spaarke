# Task 197b: Direct-message shares through the predicate; stored flag only; one unit name

> Owner and coordinator decisions 2026-11-12 on task 191c's follow-ups: piece 1 = option B (participant-scoped NoRoot
> exemption), piece 2 = stored flag only, piece 3 = one source for the unit name with a minimal startup warning.
> Nothing was applied live: no Dataverse, Entra, Key Vault or App Service change was made.

**Recorded decision: flagged participants keep Direct messaging; NoRoot exemption is participant-scoped.**

## 1. Piece 1: Direct-message participant shares for users flagged external

An unfiled Direct message reaches no root, so THE predicate (191b) answers Undecided with reason `NoRoot`. "Undecided" means
"no grant", so applying the predicate unchanged would have ended Direct messaging for every flagged user. Option B keeps it,
inside bounds that are enforced in code:

Bound (i) was revised in review of #2760 (coordinator, 2026-11-12) from "owned by the Direct team" to (i'): positive
recognition of a Direct message, independent of its owner.

| Bound | Where it is enforced |
|---|---|
| (i') the message is positively recognized as an unfiled Direct message, whoever owns it | `DirectMessageParticipantShares.RecognizeUnfiledDirectMessageAsync`, read at decision time: the message names no filing lookup (its lineage columns other than the thread, the core pair included); its thread is the join's Direct branch (typed Direct, no typed regarding lookup) and not the per-user master thread; its stamped pair (`sprk_sentby`, `sprk_directrecipient`) is valid (`ParticipantsOf`). An unfiled ordinary email is not recognized. A read fault is never recognition. |
| (ii) the predicate answers exactly no root | `ExternalRowVisibility.UndecidedOnlyForNoRoot` (not faulted, Undecided, reason `NoRoot`). |
| (iii) the grantee is in that stamped pair | Share job: only `participants ∩ FlaggedExternal`, and the recognized pair must equal step 5's pair. Live Direct-team grant: the audience is the stamped pair and must equal the recognized pair. Live other Direct grant: the thread's participants are split; only those in the recognized pair are decided with the exemption, the rest without. 191c engine: only holders in the pair keep a share. |

Every caller computes `directPairRecognized` from that recognition. Where it does not hold, NoRoot stays Undecided and nothing
is granted. Owner re-read (review F1) is kept, and when the exemption is in play the recognition is re-read before each grant
step: a message re-filed, moved to another thread or re-stamped in between grants nothing (the live Direct-team path also
needs the same owner; the user-owned path grants nobody on that message, and its new filing's own grant decides).

### The per-row answer (`DecideForDirectParticipantsAsync`, one predicate call per message)

| Predicate answer for the row | Flagged participant |
|---|---|
| Hidden (internal-only 065, thread not Open, Never table such as the review log, Restricted or tainted parent) | **Hidden**: not granted; a held share is revoked (owner re-read first, revoke then strict read-back). |
| Visible | Grant |
| Undecided `NoRoot`, decision not faulted, bound (i') recognized | Grant |
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
| Share job (`SecureChildShareReconciliationJob` Direct pass) | Seam taken from the run scope; full table above, including the Hidden revoke. A recognition read that faults is retried. |
| Live grant, Direct-team message (`DirectThreadAccessService`) | Seam resolved in its own scope (the service is a singleton). Grants only; it never revokes (the job does). |
| Live grant, other Direct message (user-owned or another team's) | A flagged participant, or one whose flag cannot be read (treated as flagged), is granted on Visible, or on exactly no root when it is in the stamped pair of a recognized unfiled Direct message. Hidden grants nothing. |
| 191c engine (`ExternalTeamShareSynchronizer`, writes off until 191i) | On a row that is exactly no root, of a recognized unfiled Direct message or its content row, a flagged holder in the pair keeps the share; every other flagged holder is revoked as before. A recognition fault revokes nobody on the row and carries it. Hidden revokes. |
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
2. **No retry on the user-owned path.** `DirectMessage:BusinessUnitName` and `OwnerTeamName` are unset in every environment
   today (until fm 054b is verified), so every Direct message is user-owned. Bound (i') now grants a flagged pair member there
   on no root, so Direct messaging keeps working before the Direct team is configured. But no job lists user-owned messages:
   a transient Undecided (or a predicate fault, or a recognition read fault) on that path grants the flagged participant
   nothing and is NOT retried. Reported to the coordinator.
3. **191c engine (resolved here).** The engine would have revoked a flagged pair member's share on any non-Visible
   user-owned Direct message, no root included. It now consults the same exemption through the same seam member
   (`RevocableOnNoRootAsync`). Remaining, reported: a TRANSIENT Undecided on a Direct message still revokes the pair member's
   share (the engine fails closed on every Undecided but exactly no root), and on a user-owned message nothing re-grants it
   (limit 2).
4. **Refile path** defers flagged participants (no seam there, 197a's file); the job grants them within one run.
5. **A double flag-read fault on a message the Direct team does not own** is retried once and then logged as an Error. Every
   participant whose flag could not be read is treated as flagged: a member of the stamped pair of a recognized unfiled
   Direct message is granted on Visible or exactly no root, and a Hidden message stays ungranted. A participant outside the
   pair is granted only on Visible.
6. **The hidden-row revoke has no gate-record revoke mirror** (D-215 rule 5). This is low risk: gate records are never shared
   with users flagged external (piece 2), so none is held there.
7. **A structural Withhold on a content row is re-decided only by the rolling sweep.** A change to that row's own parent chain
   does not move the message's `modifiedon`. The cost is missed grants, never leaked ones.
8. **The retired-key ArchTest scan's nested-JSON pattern** stops at the first `}`, so it can miss the key when another nested
   object comes before it inside `ExternalTeamShare`. The startup warning still covers live settings.
9. **No composition test checks that the host injects `IServiceScopeFactory` into `DirectThreadAccessService`.** If it were
   dropped, live grants to flagged participants would defer, and the job would grant them within one run.

## 5. Review

Two Opus adversarial passes ran. Every finding is fixed or recorded in section 4.

| Pass | Finding | Resolution |
|---|---|---|
| 1 | F1: the live Direct-team grant ran well after its owner check (the predicate added reads in between) | The owner is read again after the decision and before the message grant. Test: re-owned during the decision, nothing granted. |
| 1 | F2: on a message the Direct team does not own, a flag-read fault silently left internal recipients ungranted, with no retry | The read is retried once; a second fault is an Error log, fails closed, and is known limit 5. Tests: one fault then granted; two faults, nothing granted, and the Error is logged. |
| 1 | K: gate mirror, a nested unit now refused, structural re-settle, refile tests | Known limits 6 and 7. Section 3 covers the unit. |
| 2 | F1: after a message row that did not settle, the job still granted content rows | After the message row, the owner is re-read and a change returns Unknown (retried). The live path does the same when flagged participants are present. Tests on both paths. |
| 2 | K: no test for a structural answer keeping a held share | Test added (`R197b_AStructuralUndecided_KeepsHeldShares`). |
| 2 | K: the exemption argument had a default | The default was removed, so every call site states it. |
| 2 | K: the retired-key scan pattern, and no `IServiceScopeFactory` composition test | Known limits 8 and 9. |

Coordinator review of #2760 (2026-11-12) and its resolution:

| Item | Resolution |
|---|---|
| Bound (i) to (i'): positive recognition of a Direct message, independent of its owner; on both live paths | `RecognizeUnfiledDirectMessageAsync`; used by the share job, both live paths and the 191c engine. Tests: user-owned unfiled Direct message granted (must-fire); not recognized for six shapes (filed under a matter, thread carries a typed lookup, master thread, thread not Direct, no thread lookup, no recipient stamped), a flagged non-member of the pair, and a Hidden message (must-not-fire); a Direct-team message whose thread carries a typed lookup (must-not-fire). |
| F1 re-read rests on (i') | Owner re-read kept; the recognition is re-read before each grant step (`StillTheSameDirectMessageAsync`, `StillRecognizedAsync`). Tests: filed during the decision (user-owned) and moved off its thread during the decision (Direct team), nothing granted. |
| Limit 5 | Updated (unreadable flag treated as flagged; a Hidden message stays ungranted). Test is a theory: recognized no root granted, no pair and Hidden not granted, Error logged each time. |
| Limit 3 (191c engine) | Small: one call site plus tests, so done here. Tests: no-root user-owned message and its attachment keep the pair member's share and revoke a non-member; Hidden revokes; no pair revokes; transient Undecided revokes. |

Mutation checks for the exemption (each mutation applied alone, the touched suites run, then restored):

| Mutation | Tests failing |
|---|---|
| M1 the exemption term removed | 9 |
| M2 the pair bound removed on the user-owned path | 1 |
| M3 the recognition re-check removed | 2 |
| M4 the engine keep removed | 2 |
| M5 the engine keep not bounded to the pair | 2 |
| M6 the filing check removed from the recognition | 2 |
| M7 the thread typed-lookup check removed | 2 |
| M8 the engine keep on any non-Visible answer | 2 |
| M9 the share job back to an unconditional exemption | 2 |

The share job now uses the same recognition (its pair must equal step 5's pair); a recognition read that faults is retried.
Tests: a Direct-team message whose thread carries a typed lookup is withheld and settled; a recognition fault is retried.

PR #2748 (#2731, skip finished Direct messages in a held window) merges cleanly with this branch. A deferred or owner-changed
outcome is never Complete, so #2748 never records it as finished.
