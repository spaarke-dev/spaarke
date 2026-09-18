# Task 106 — a re-grant judges "confers access" across the whole key (ISS-008 / #973)

> 2026-09-18, session 15. Register ISS-008, GitHub #973, spec FR-09. Pre-existing since task 023.
> Commit `3d1ce86e6`. **No escalation fired — see §7.**

## 1. Premise verification (POML step 0) — the task file was RIGHT

This project's task files have been wrong **19 times**, so every premise was checked against the code
before anything was obeyed. This one held on every point, which is worth recording as plainly as the
failures have been:

| POML claim | Code | Verdict |
|---|---|---|
| `CreateGrantAsync` at `:178` | `GrantExternalAccessEndpoint.cs:178` | ✅ |
| the 409 at `:143` | `:143`, `reasonCode = "sdap.grant.expired_not_restored"` | ✅ |
| `CollapseDuplicatesAsync` at `:402` | `:402` | ✅ |
| the 409 returns **before** the collapse | `return` at `:272` precedes the collapse at `:278` | ✅ |
| it judges the **survivor only**, elected as lowest id | `var survivor = existing[0];` at `:202` → `effectiveExpiry` at `:264` | ✅ |

The register entry (`notes/defer-issues.md` § ISS-008) matches the POML verbatim, including its warning.

## 2. The defect

`ExternalGrantLifecycle.QueryActiveRowsAsync` filters on `statecode` only — by design, so lapsed shares
can be renewed (task 098). So an **expired** row is still "active" to the upsert. Task 023 added the
right check for the single-row case: if the matched row's effective expiry has passed and the request
carries no new expiry, return the warning → 409 `sdap.grant.expired_not_restored`, *"still confers no
access"*.

But the elected row was the **lowest id**, and the read path unions **every** active unexpired row on
the key (`QueryGrantSetAsync`, `GroupBy(root).Max(level)`). So with duplicates present, an expired
lowest-id row produced a 409 asserting the grantee has no access **while a live duplicate meant they
did**. The 409 was false.

Duplicates are real, not hypothetical: task 097's backfill recorded one contact holding **five** active
rows on one matter (`notes/task-097-mandatory-expiry.md` §Backfill).

## 3. 🚨 Why the obvious fix is WORSE than the bug

Suppress the 409 and let the code fall through, and control reaches `CollapseDuplicatesAsync` — which
deactivates every row **except the expired survivor**. That **revokes the live access it had just
correctly detected.**

The bug's current form is *inert*: a false 409, no data harmed. The naive fix is *destructive*. This is
why the register says "**NOT** 'collapse first'", and it is the single most important thing to carry
forward about this task. A reviewer skimming for "stop returning the 409" would ship a privilege-loss
defect.

## 4. The decision: fix the ELECTION, not the check

`ElectSurvivor` ranks rows by the expiry each would **carry after this request**, ties broken by
ascending id:

```
EffectiveExpiry(requested, row, today) = requested ?? row.ExpiresDate ?? DefaultExpiry(today)
```

Three properties then hold **structurally** rather than by an added check:

1. **The 409 becomes a statement about the whole key.** The elected row has the maximum effective
   expiry, so if *it* confers nothing, no active row on the key does.
2. **The collapse cannot deactivate the conferring row** — that row *is* the survivor.
3. **A request carrying an explicit expiry is unchanged.** An explicit date applies to every row
   equally, so all effective expiries tie and the ascending-id tie-break alone decides — byte-for-byte
   the pre-106 election. Acceptance criterion 5 ("behaves exactly as before") is true *by construction*,
   not by argument.

**The check at `:265` needed no edit.** `EffectiveExpiry(request.ExpiryDate, survivor.ExpiresDate, today)`
is algebraically equal to task 023's `requestedExpiry ?? survivor.ExpiresDate` in all three branches
(requested date / kept date / unbounded→default), so one extracted helper serves both the election and
the conferral decision.

### What I did NOT merge, deliberately

`requestedExpiry` stays nullable and separate. Its null means **"write nothing"** (task 097 — a re-grant
from a surface with no date field must not move a date someone set), whereas `effectiveExpiry` is the
value the row will carry. Collapsing them would start rewriting dates that already match and break
`Upsert_NoExpiryOnAGrantWithALongerExpiry_LeavesItUnchanged`'s `ExpiryUpdateCount == 0`. A comment in
the code says so, because the two look redundant and the "simplification" is inviting.

## 5. The alternative I rejected — and why it is a trap

Ranking by the **raw `sprk_expiresdate`** column (the intuitive reading, and my own first instinct)
makes `null` sort as "never expires" and win the election. FR-33 then bounds that winner at today + 90
— and collapses away a sibling dated **+200**. Access leaves the call **shorter than it arrived**, by
110 days, from a change whose whole purpose was to stop access being mis-stated.

Pinned by `Upsert_WithAnUnboundedRowAndALaterDatedDuplicate_…`, which seeds the unbounded row **first**
so it discriminates against *both* wrong designs (raw-column ranking **and** lowest-id election).

## 6. Why the election is UNRESTRICTED

A narrower fix — re-elect only when the lowest-id row is expired — was rejected. Two **live** duplicates
at +20 and +200 days would still collapse onto +20, letting a deduplication silently decide how long
access lasts. Same defect family as the false 409. Pinned separately by
`Upsert_WithTwoLiveDuplicates_KeepsTheLongerLivedRow`.

Trade-off accepted and recorded: a collapse can still lower the effective *level* when duplicates differ
in level, because the requested level is written to the survivor. That is task 010's existing
convergence semantics and the operator's stated intent ("grant X"), not something 106 introduces.

## 7. Escalation trigger — did NOT fire

The POML's trigger: *"If the honest response when a live duplicate exists needs a status code or
reasonCode the contract does not have, STOP and escalate."*

It does not fire. The honest response is **200 + the surviving row id** — both already in the contract,
and `<returns>` already documents that id as "the single surviving active row". No status code and no
reasonCode was added, changed, or removed. Checked rather than assumed, per the constraint
"Status codes and reasonCodes are unchanged; needing a new one is an escalation."

## 8. The expiry definition (constraint: reuse the read path's, do not write a second)

`ExpiryPredicate` is an **OData string** and cannot be reused over materialized rows, so the write path
genuinely needs an in-memory form. Resolution: exactly **one** in-memory copy,
`ExternalParticipationService.ConfersAccessOn`, placed **immediately adjacent** to the predicate it
mirrors so a reader changing one sees the other.

It is pinned to the same two semantics the OData form is already pinned to — `null` never expires, and
the expiry date **itself** still confers (`ge`, never `gt`). That test matters because the existing
expiry tests assert the **string**: a mirror reading `> today` leaves every one of them green while
shortening every dated grant on the write path by a day. Perturbation **P3** confirms it catches exactly
that.

## 9. What changed

| File | Change |
|---|---|
| `Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs` | `EffectiveExpiry` + `ElectSurvivor`; `QueryActiveRowsAsync` remarks corrected — ascending id is now the **tie-break**, not the election |
| `Infrastructure/ExternalAccess/ExternalParticipationService.cs` | `ConfersAccessOn` — the sole in-memory mirror, adjacent to `ExpiryPredicate` |
| `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` | match path + post-create path elect via `ElectSurvivor`; the 409 check routed through the shared helper and the mirror; `CreateGrantAsync` remarks describe the election |
| `tests/integration/auth/UnifiedAccessControl/GrantLifecycleCharacterizationTests.cs` | 8 tests (§10) |
| `tests/integration/auth/UnifiedAccessControl/GrantExpiryCharacterizationTests.cs` | the mirror-consistency test |

**Docs-vs-reality repair.** `QueryActiveRowsAsync`'s remarks claimed the ascending-id ordering *is* how
the survivor is elected. After this change it is the tie-break. Leaving it would have been drift #13 in
this project — committed by the very task that keeps counting them.

## 10. Tests (KEEP path `tests/integration/auth/**`, security-auth)

A closed set, one test per acceptance criterion plus the two decisions §5–§6 make. No count is stated as
a target (`.claude/constraints/testing.md` §2b).

| Test | Criterion / purpose |
|---|---|
| `…WithALaterDuplicateOnTheSameKey_DoesNotReportNoAccess` | 1 — later-dated duplicate |
| `…WithAnUnboundedDuplicateOnTheSameKey_DoesNotReportNoAccess` | 2 — null is never-expiring |
| `…WhoseDuplicatesAreAlsoExpired_StillReportsNoAccess` | 3 — the discriminating negative; without it, simply deleting the warning would also pass |
| `Upsert_WhenCollapsingDuplicates_LeavesTheRowThatConfersAccessActive` | 4 — the revoke-live-access failure §3 |
| `Upsert_WithANewExpiryOverDuplicates_ElectsTheLowestIdExactlyAsBefore` | 5 — task 023 path intact |
| `Upsert_WithTwoLiveDuplicates_KeepsTheLongerLivedRow` | §6 — unrestricted election |
| `Upsert_WithAnUnboundedRowAndALaterDatedDuplicate_…` | §5 — the rejected design |
| `ConfersAccessOn_MirrorsTheReadFiltersExpirySemantics` | §8 — mirror drift |

**Test-design note.** Each duplicate test **asserts its own precondition** (`expired.Id.CompareTo(live.Id)`
is negative). The tests depend on `FakeGrantTable`'s sequential GUIDs sorting as expected; a test seeded
the other way round would pass *without ever exercising the defect*. Asserting the setup is the same
"prove the instrument registered something" discipline that this session's earlier steps needed.

## 11. Perturbations (mandatory — harness `p106.js`, run against the COMMITTED tree)

| # | Perturbation | Result |
|---|---|---|
| P1 | revert the match-path election to lowest id (the bug, restored) | **CAUGHT** — 6 failed / 45 passed |
| P2 | keep the election, collapse onto `existing[0].Id` (isolates the collapse) | **CAUGHT** — 4 failed / 47 passed |
| P3 | mirror uses `> today` instead of `>= today` | **CAUGHT** — 1 failed / 50 passed |
| P4 | rank by raw `ExpiresDate`, null as max | **CAUGHT** — 2 failed / 49 passed |

**4 CAUGHT / 0 MISSED / 0 INVALID.** The *spread* is the signal: four different blast radii mean the
suite distinguishes four different guarantees. Had all four failed the same six tests, it would only be
detecting "something changed".

Harness properties, each earned from a prior failure in this project: it refuses to run on a dirty tree;
it asserts each anchor matches **exactly once** before perturbing; it reports **INVALID** — never CAUGHT
or MISSED — when a perturbation fails to compile or emits no test summary; and it verifies each revert
landed before continuing.

## 12. Placement justification (CLAUDE.md §10)

**In the BFF, in the existing endpoint.** No new endpoint, service, DI registration, interface, package
or Dataverse column. Two `internal static` helpers added to the file that already owns grant-row
semantics (`ExternalGrantLifecycle`) and one to the file that owns the expiry predicate
(`ExternalParticipationService`). Root §11's three-question justification does not apply — it governs
NEW surface, and this task only modifies existing files.

## 13. Conflict check (§10, run before the BFF change)

Clean. `HEAD..origin/master` = **0 commits** — master has not moved at all — and none of the other open
non-dependabot PRs (#960 word-add-in, #956 email-r3, #935 code-quality-r4) touch `Api/ExternalAccess/**`
or `Infrastructure/ExternalAccess/**`. `projects/INDEX.md` already declares BFF=Y for this project, so
no hot-path drift to record.

⚠️ **My first measurement was wrong and is worth recording.** I ran
`git diff --name-only HEAD origin/master | grep ExternalAccess`, which is **bidirectional** — it listed
this branch's own 457 changed files back at me and read as "master touched our file". The right question
is what is in `HEAD..origin/master`. Same error class as the three from session 14: *an observation taken
outside the thing being observed.*

## 14. Verification

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | **0 errors / 0 warnings** |
| `GrantLifecycle` + `GrantExpiry`, post-format forced rebuild | **51 / 51** |
| Perturbations | **4 / 4 CAUGHT**, 0 MISSED, 0 INVALID |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages; **no package added** |
| `Spaarke.ArchTests` | _pending — see §15_ |
| Full `Sprk.Bff.Api.Tests` | _pending — see §15_ |
| Publish size vs a FRESH master | _pending — see §15_ |

**The expected test count was computed from the source attributes (39 + 12 = 51) independently of the
run, which then reported Total: 51.** This is not ceremony: session 14 lost real time to a filter that
reported "Failed: 0, Passed: 16" while selecting the **wrong** 16 — a filter selecting nothing reports
identically to one selecting everything. The count is the proof the instrument registered.

**A pre-commit hook reformatted the source after the first green run.** `lint-staged` ran
`dotnet format` over all five C# files and applied the modifications, so the 51/51 I first measured was
against *pre-format* source with *pre-format* binaries. Re-verified on the committed state with
`--no-incremental` (52s of real build, not a 1-second cache hit) → 51/51, and all four perturbation
anchors confirmed still present exactly once before the harness ran. Session 14 hit the same hook and
did not notice until a 1-second test duration gave it away.

## 15. Still pending at the time of writing

ArchTests, the full unit suite, and the publish-size measurement against a fresh `origin/master`
(CLAUDE.md §10 hazards 1–4: re-measure master, `Compress-Archive`, short paths on **both** sides,
file-count parity). Step 9.5 gates (`code-review` + `adr-check`) are unconditional here — FULL rigor
**and** a test-modifying task. Rows above are marked _pending_ rather than pre-filled, because an
unfilled number that reads as a result is exactly how session 14 nearly recorded task 043 complete with
superseded figures.
