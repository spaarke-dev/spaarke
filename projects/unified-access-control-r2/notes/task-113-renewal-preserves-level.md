# Task 113: a refused /grant writes nothing (ISS-028, #1008)

**Scope**: narrowed by the 2026-10-05 amendment (owner round 59, item 5) to ISS-028 only. The ISS-023 (#1002) half was cut. #1002 was closed on 2026-10-05, citing `RecordShareExpiryTests.cs:133` and `GrantorCeilingTests.cs:232`. The `/grant` explicit-downgrade guard that the original POML asked for already exists as `Grant_AnExplicitDowngradeByACallerHoldingEverything_StillDowngrades`.
**Branch**: `fix/uac-r2-113-renewal-level`, from origin/master `9c68f79918`.

## Premises re-verified at HEAD (Step 0)

| POML claim | At HEAD |
|---|---|
| Update block :250-260, check :306-319 | Drifted: the update block is now `:391-445` and the check `:485-506` (the amendment said :362-374 / :420-421; it drifted again) |
| 409 emitted at :137-145 | Now `:168-180`, unchanged in content |
| The check depends on nothing the write produces | TRUE. A contact-issuer branch was added (task 140): `effectiveExpiry = expiryToWrite ?? survivor.ExpiresDate ?? DefaultExpiry`, and `expiryToWrite` is computed BEFORE the update |
| The same-level test at :812-825 cannot see the defect | TRUE, now `Upsert_OverAnExpiredRowWithNoNewExpiry_DoesNotReportSuccess` (:840) |
| **Not in the POML**: other callers of `CreateGrantAsync` | `/invite-and-grant`, `ContactGrantEndpoints`, and **`AssignedAccessMaterializer`** (3 call sites). The materializer's raised-grant restore (`:1230`) **depends on write-then-warn**. See "Design" |

## RED first (Step 1, AC 2)

The pins were written against unmodified production code (HEAD `9c68f79918`) and run before the reorder: 4 failed, 1 passed (the negative twin, as intended). Each failure was for the right reason, namely that the level had been written:

```
Failed GrantLifecycleCharacterizationTests.Upsert_OverAnExpiredRowWithNoNewExpiry_AtADifferentLevel_IsRefusedAndWritesNothing(stored: FullAccess, requested: ViewOnly, contactIssued: False)
   Expected seeded.AccessLevel to be 100000002 because a refused request must not have changed the level, but found 100000000
Failed ...(stored: ViewOnly, requested: Collaborate, contactIssued: True)
   Expected seeded.AccessLevel to be 100000000 ... but found 100000001
Failed ...(stored: ViewOnly, requested: FullAccess, contactIssued: False)
   Expected seeded.AccessLevel to be 100000000 ... but found 100000002
Failed GrantorCeilingTests.Grant_OverAnExpiredGrantAtADifferentLevelWithNoNewExpiry_Is409_AndWritesNothing
   Expected _dataverse.Updates to be empty because the request did not take effect, so it changed nothing, but found ...
Failed!  - Failed: 4, Passed: 1, Total: 5
```

In the handler-level test, the 409's status, title, detail, reasonCode, traceId and accessRecordId assertions all PASSED against old code before the `Updates` assertion failed. So the 409 shape is pinned as it was, not as the fix made it.

## Design: why not a blanket hoist

A blanket hoist ("refuse before writing whenever the key will not confer") breaks the Assigned-To rule. When an assignment ends, `AssignedAccessMaterializer` puts a raised grant back to the operator's own level AND date by sending an **explicit** date. If that date has passed, the write is what ENDS the Collaborate access the rule had extended, and the ledger records `PriorLevelRestoredLapsed` / Revoked. Under a blanket hoist nothing would be written, the row would keep Collaborate, and the ledger would say it ended. That is a fail-open. Later, `set-record-share-expiry` (which renews lapsed rows too) would revive it at the rule's level for a person who is no longer assigned.

The rule implemented: **a request that supplied NO expiry, over a key that confers nothing, is refused before any write.** A request that supplied a date is written as asked, and the warning follows the write as before. On `/grant`, `/invite-and-grant` and the contact route, a past date is a 400 and today confers, so on every HTTP route a 409 now means nothing was written. Only the in-process materializer can send a past date, and it does so deliberately.

The discriminator is `request.ExpiryDate is null`, not `!expiryChanged`. The materializer's restore over a row that already carries the operator's date has `expiryChanged == false` but still needs the level put back. Perturbation P4 proves this.

The comment block that vouched for the ordering ("the ADR-003 ordering below ... load-bearing and true") is replaced. The new text says what the ordering carries (no-expiry refusals write nothing) and what it does not (an explicit, non-conferring date is still written, and why).

## Perturbations (Step 4, AC 8). The fix was committed first and each perturbation compiled

| # | Perturbation | Caught by |
|---|---|---|
| P1 | Original order (early refusal removed) | the 3 new core cases, the new handler 409 test, and the materializer lapse-race test's new level assertion (5 failed) |
| P2 | Compensating write-then-restore | the 3 new core cases and the new handler test (4 failed) |
| P3 | Blanket hoist | `ARaisedGrantRenewedPastTheOperatorsDate_…`, `ARaisedGrantThatLapsedBeforeTheAssignmentEnded_…` (2 failed) |
| P4 | Discriminate on `!expiryChanged` | `ARaisedGrantThatLapsedBeforeTheAssignmentEnded_…` (1 failed) |

The cut "max(requested, existing)" perturbation (ISS-023) was not run: that half is out of scope, and `GrantorCeilingTests.Grant_AnExplicitDowngradeByACallerHoldingEverything_StillDowngrades` is its standing guard.

## Changes

- `GrantExternalAccessEndpoint.cs`: the conferral answer (`effectiveExpiry`, `confersAccess`) is computed before the update. The early refusal applies when `request.ExpiryDate is null`. The 409 outcome is built by one local function, so the detail text is byte-identical on both returns. The `<returns>` and `GrantUpsertOutcome` docs no longer say "written".
- `AssignedAccessMaterializer.cs` (`:1085`): the lapse-race branch said "the core wrote" and told the user "was written but does not take effect yet". That became false under this change, so the comment, the log line and the user text are corrected, and `run.Writes++` moved below the warning branch (no write happened there).
- Tests: 3 core cases + 1 negative twin (`GrantLifecycleCharacterizationTests`), one handler-level 409 pin (`GrantorCeilingTests`; the only handler harness, and no test pinned the 409's shape before), and one assertion plus a corrected comment in the materializer lapse-race test.

Beyond the stated contract, each with a reason:
- The contact-issued case: the refused write also took the row over (it cleared the contact issuer), so the same defect had a second field.
- The handler-level test: AC 3 asks for the 409's shape, and nothing pinned it.

## Owner round 80 (2026-10-08, binding): a re-add over a lapsed grant restores it at the picked level

**Trigger.** Verifier pass 1 on PR #1406 raised an F2. After the first commit, `/invite-and-grant` over a LAPSED grant with no `expiryDate` wrote nothing, yet still answered 200 "granted <level>" and adopted the Assigned-To ledger entry. The modal's "+ Contact" re-add never sends a date. A later `set-record-share-expiry` renewal would then revive the OLD level, possibly a higher one. That is fail-open relative to what the UI reported.

**Owner decision (round 80, recorded in `notes/session27-owner-decisions-and-research.md`):** "Restore at picked level". A re-add over a lapsed grant is an explicit SET. It restores the grant at the PICKED level, with the default 90-day expiry.

**`/grant`: same rule applies. Evidence it is a re-add route.** `AccessGrantModal.tsx` calls `/grant` with no `expiryDate` from three re-add paths:
- "+ Contact" for an internal contact or one with no email (:1058);
- "+ Organization" (:1142);
- a suggestion's "Grant" (:1355).

There is no other client: a repo grep finds only the modal and its bundle. No client handles `expired_not_restored` (the one grep hit is an unrelated provisioning code).

**Implementation.**
- **Grant core.** `CreateGrantAsync` gains an optional `reAddRestoresLapsed` parameter (default `false`). With it, a request with no date over a survivor that confers nothing gets `expiryToWrite = today + 90`, and the level is written as granted. Because the survivor is elected by conferral, a lapsed survivor means no row on the key confers.
  - `effectiveExpiry` is now `EffectiveExpiry(expiryToWrite, row, today)`. This equals the old value in every case except the restore.
  - A still-LIVE key keeps its date (task 097).
- **The two routes.** `/grant` and `/invite-and-grant` pass `true`.
- **The Assigned-To rule** passes nothing, so it keeps the task-113 behaviour: refuse and write nothing when no date is sent, and write as asked when it sends an explicit date (its restore path). Making the restore core-wide would have changed the rule's lapse-race path, whose ledger bookkeeping assumes nothing was written. Perturbation Q3 confirms it.
- **The bounds still hold.**
  - Grantor ceiling: the level is capped (narrowed, reported).
  - Never-lower (task 139): a narrowed re-add over a lapsed HIGHER row is still 409 `would_lower_existing` and writes nothing.
  - Contact issuer (round 42 item 2 / G2): the restored date is capped at the grantor's own. That route always sends a date, so it needs no flag.
- **`/invite-and-grant`.** A non-conferring outcome is now a 409 `expired_not_restored`, with the contact id, never a 200. Nothing is adopted. With the restore, no known request reaches that branch; it is a fail-closed backstop.
- **`/grant`'s own 409** is likewise now a backstop. A dateless request is restored, and a past date gets a 400.
- **The 200 reports what was written.** `GrantedAccessLevel`/`Narrowed` are the written level. The responses carry no expiry field, and adding one would be a contract change nobody asked for. The restored date is the FR-33 default the UI already assumes.
- **K log text, fixed.** The 409 builder words each case for what actually happened: no date (refused, nothing written, the detail text unchanged), or a supplied date that has passed (written as asked; only the Assigned-To restore sends one).

**Tests.**
- **Core** (`GrantLifecycleCharacterizationTests`): re-add over lapsed, lower, higher and same → picked level, today + 90, no warning. A live key keeps its date (the negative twin). The ISS-028 refusal pins now document the core's default mode.
- **Handler** (`GrantorCeilingTests`; the task-113 409 pin was replaced, because round 80 changes that answer):
  - `/grant` lapsed re-add, lower and higher → 200 at the picked level with today + 90;
  - a Collaborate caller → restored at Collaborate, narrowed;
  - a narrowed re-add over a lapsed higher row → 409 `would_lower_existing`, nothing written;
  - `/invite-and-grant` lapsed re-add, lower and higher → 200 reports the written level, and the row carries it.
  - The fake now applies `sprk_expiresdate`.
- **Ledger** (`AssignedAccessMarkerTests`): a lapsed re-add is restored and only then adopted. A refused one (No Access, 422) writes nothing and is not adopted.
- **Contact route** (`ContactGrantAuthorizationTests`): a lapsed own-row re-add → restored at the picked level, with the date capped at the grantor's own (+30, not +90).

**Perturbations (committed first, each compiled).**

| # | Perturbation | Caught by |
|---|---|---|
| Q1 | `/grant` does not opt in | 3 `/grant` restore tests and the adoption test (4 failed) |
| Q2 | `/invite-and-grant` does not opt in | both invite restore cases (2 failed) |
| Q3 | Core restores regardless of the flag | 5 core default-mode refusal tests and the materializer lapse-race test (6 failed) |
| Q4 | Restore keeps the stored level | 7 restore tests |
| Q5 | Restore resets every date, live or not | `ReAdd_OverALiveGrantWithNoExpiry_KeepsItsDate` |
| Q6 | `/grant` adopts before checking the refusal | `ARefusedReAddOverALapsedAutoGrant_WritesNothing_AndIsNotAdopted` |

**§11 (new surface inside an existing method).**
- *Existing:* the FR-33 expiry default in the same expression (`survivor.ExpiresDate is null ? DefaultExpiry : null`).
- *Extension:* yes. The parameter extends that one expression, and nothing else is added.
- *Cost of doing nothing:* the modal's re-add over a lapsed grant answers 409 on `/grant`, and 200-without-a-write on `/invite-and-grant`. The owner's round-80 rule fails, and a later renewal revives a level nobody picked.

## Known limits (K-class)

- K2: the `/invite-and-grant` and `/grant` 409 backstops for a non-conferring outcome are unreachable by construction after round 80, so no test reaches them. They are kept so that ADR-003's "never report success over a grant that confers nothing" holds on every route.

- None on the contact-issuer route: it always sends a date (requested, or today + 90, capped), so the early refusal never applies there and its behaviour is unchanged.
- ~~K4: `/invite-and-grant` over an expired key answered 200 and logged~~: this was the verifier's F2. Fixed by round 80 (above).

## Defects found in passing (filed + reported)

- **#1404**: the FR-12 misfile guard fails OPEN on a regex timeout. `NewRecordIntentDetector.cs:136` returns null, meaning "no intent", so `IdentifierReverseLookupRung.cs:305-320` keeps 0.90, which is auto-file confidence. Seen as `IdentifierReverseLookupRungTests.Fr12_…` failing in the full unit run under load (0.9 instead of 0.65). It passes 21/21 in isolation. This belongs to email-communication-intelligence and is unrelated to this change.
- ~~K4: the 409 builder's log text on the materializer's explicit-date path~~: fixed in the round-80 commit.

## Carried forward (not this task's scope)

- Task 099 must separately gain the level constraint (owner, non-negotiable; the original POML notes record the text). That is 099's amendment.
