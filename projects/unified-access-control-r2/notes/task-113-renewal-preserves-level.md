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

## Known limits (K-class)

- None on the contact-issuer route: it always sends a date (requested, or today + 90, capped), so the early refusal never applies there and its behaviour is unchanged.
- K4: `/invite-and-grant` over an expired key with no expiry still answers 200 and logs (task 023, pre-existing by design). Its `GrantedAccessLevel` was never the effective access on that path, and now the row also keeps its old level.

## Defects found in passing (filed + reported)

- **#1404**: the FR-12 misfile guard fails OPEN on a regex timeout. `NewRecordIntentDetector.cs:136` returns null, meaning "no intent", so `IdentifierReverseLookupRung.cs:305-320` keeps 0.90, which is auto-file confidence. Seen as `IdentifierReverseLookupRungTests.Fr12_…` failing in the full unit run under load (0.9 instead of 0.65). It passes 21/21 in isolation. This belongs to email-communication-intelligence and is unrelated to this change.
- K4: the 409 builder's log line ("the request supplied no new expiry") is inaccurate on the materializer's explicit-date restore path. It is log-only; no HTTP caller sees it.

## Carried forward (not this task's scope)

- Task 099 must separately gain the level constraint (owner, non-negotiable; the original POML notes record the text). That is 099's amendment.
