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

### What I did NOT merge — and ⚠️ the reason I first gave for it was FALSE

`expiryToWrite` (renamed from `requestedExpiry`, see §16/F4) stays nullable and separate from
`effectiveExpiry`. Its null means **"write nothing"** (task 097 — a re-grant from a surface with no date
field must not move a date someone set), whereas `effectiveExpiry` is the value the row will carry.

🔴 **Correction (review finding F2).** I originally wrote here, and in a code comment, that merging them
"would start rewriting dates that already match and break
`Upsert_NoExpiryOnAGrantWithALongerExpiry_LeavesItUnchanged`'s `ExpiryUpdateCount == 0`". **Both claims
are false**, and the review traced all three branches to show it: the write guard is
`expiryChanged = expiryToWrite.HasValue && expiryToWrite != survivor.ExpiresDate`, and in the one branch
where `expiryToWrite` is null (`request.ExpiryDate is null && survivor.ExpiresDate is not null`) the
merged form evaluates `effectiveExpiry != survivor.ExpiresDate` — which is `E != E`, i.e. false, so no
write. The merge is behaviour-preserving, and that test would still pass.

The honest reason to keep them apart is that **the nullable type expresses the write/don't-write intent**,
not that merging is unsafe. This matters beyond pedantry: a comment that guards a non-hazard teaches the
next reader to discount its neighbours, and the neighbouring comments (the ADR-003 ordering, the task-097
keep-the-date rule) are load-bearing and true. The durable guard is F4's rename, which removes the
name collision that made the merge tempting in the first place.

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

**Test-design note.** Each duplicate test asserts its own precondition (`expired.Id.CompareTo(live.Id)`
is negative). The tests depend on `FakeGrantTable`'s sequential GUIDs sorting as expected; a test seeded
the other way round would pass *without ever exercising the defect*. Asserting the setup is the same
"prove the instrument registered something" discipline that this session's earlier steps needed.

🔴 **Correction (review finding F9).** That sentence was **not true when I wrote it**: two of the seven
duplicate tests — `…WhoseDuplicatesAreAlsoExpired…` and `Upsert_WhenCollapsingDuplicates…` — had no such
assertion, while the other five did. So the claim was accurate about the design and false about the code,
which is the worse of the two failure modes: a reader checking the invariant finds it held in the places
they happened to look. Both now assert it. The review also verified the underlying instrument is sound —
`aaaaaaaa-0000-0000-0000-{seq:D12}` does sort sequentially under `Guid.CompareTo`, the same comparator
`OrderBy(r => r.Id)` / `ThenBy` uses — so the assertions pin a real property rather than papering over a
shaky one.

**A ninth test was added after review**: `Upsert_ElectsTheSameSurvivor_WhetherOrNotTheRequestCarriesAnExpiry`,
the V1/F1 regression (§16). And `…_ElectsTheLowestIdExactlyAsBefore` was **rewritten**: it had encoded the
defect as an invariant, asserting the request-dependent tie that *was* the bug.

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

---

## 16. 🔴 Step 9.5 found a defect I INTRODUCED — V1, determinism (fix designed, NOT yet applied)

`adr-check` returned **1 violation / 9 warnings**. The violation is real, I verified its algebra
independently before accepting it, and **it is a regression this task created**. Recorded here in full
because it is the most valuable thing this task produced.

### The defect

`ElectSurvivor` ranked rows by `EffectiveExpiry(requestedExpiry, row.ExpiresDate, today)` — and
`requestedExpiry` is a **per-request value**. So the total order is *parameterised by the request*, and
two concurrent callers do not share it. Rows A (lower id, `today+10`) and B (higher id, `today+200`):

| Request | A's rank | B's rank | Elects | Collapse deactivates |
|---|---|---|---|---|
| no expiry | +10 | **+200** | **B** | A |
| `expiryDate: today+30` | +30 | +30 → tie → lower id | **A** | B |

Both read `{A,B}` before either writes → request 1 deactivates A, request 2 deactivates B →
**ZERO active rows, and both callers receive HTTP 200** naming a row that is now inactive. The grantee
silently loses all access on the key. There is no ETag or conditional update on the deactivation, and it
does not self-heal.

**This is worse than the bug task 106 set out to fix.** Pre-106 the election was `existing[0]` — ascending
id, request-INDEPENDENT — so any two racers necessarily agreed and the interleaving was *impossible*. The
false 409 was inert; this is destructive. It is also precisely the failure
`QueryActiveRowsAsync`'s own remarks exist to prevent.

**And I asserted the opposite in three places** (`GrantExternalAccessEndpoint.cs` ~:211,
`ExternalGrantLifecycle.cs` ~:209 and ~:239): *"both apply the same total order"*. I reasoned about the
**tie-break** being deterministic and never noticed the **ranking function itself** varies with the
request. The task-106 POML asked the reviewer to check exactly this, and it found the answer I had
assumed away. Path A is unavailable: I would be documenting my own regression.

### The fix — drop the request from the RANKING (not from the conferral decision)

```
ConferralRank(rowExpiry, today) = rowExpiry ?? DefaultExpiry(today)     // request-INDEPENDENT
ElectSurvivor(rows, today)      = max by ConferralRank, then ascending id
```

`requestedExpiry` is **removed from `ElectSurvivor`'s signature**, so request-independence becomes
structural — a caller *cannot* reintroduce it. The conferral decision keeps
`EffectiveExpiry(request.ExpiryDate, survivor.ExpiresDate, today)`: that is a per-request question, not
an ordering, so it carries no determinism obligation.

Properties re-checked under the new ranking (not assumed):
- **Conferral implication holds.** A row confers ⟺ its rank ≥ today (an unbounded row ranks at
  `today+90`, a dated row at its date). The survivor has the maximum rank, so if the survivor does not
  confer, no row does — the 409 still speaks for the whole key.
- **Collapse safety holds.** If any row confers, the maximum-rank row confers, and that row is the
  survivor.
- **Access is still never shortened.** `{unbounded, +200}` ranks +90 vs +200 → the dated row wins, so
  §5's test still passes.

### ⚠️ The reviewer's own suggested fix is itself flawed — do not apply it as written

It proposed ranking `r.ExpiresDate ?? DateOnly.MaxValue`. That makes an unbounded row rank **highest**,
FR-33 then bounds the elected row to `today+90`, and a sibling dated `+200` is collapsed — access leaves
the call **shorter than it arrived**. That is exactly the trap §5 documents and
`Upsert_WithAnUnboundedRowAndALaterDatedDuplicate_…` pins, so it would fail that test. Ranking at
`DefaultExpiry(today)` instead of `MaxValue` is the difference, and it is the whole point.

### Test consequences

- `Upsert_WithANewExpiryOverDuplicates_ElectsTheLowestIdExactlyAsBefore` **encodes the buggy invariant**
  — it asserts the request-dependent tie that IS the defect. It must be rewritten to assert
  contract-level equivalence (no 409, one active row, the requested expiry written) rather than a
  particular surviving id.
- **New test needed**: the same row set must elect the same survivor whether or not the request carries
  an expiry. This is the V1 regression, and **none of P1–P4 could have caught it** — every perturbation
  was single-request, so the whole class was outside the harness's reach. A suite can be 4/4 CAUGHT and
  still be blind to an entire dimension.
- **P5 needed**: reintroduce `requestedExpiry` into the ranking; the new determinism test must fail.

### Other findings verified rather than accepted on trust

| # | Finding | Verified | Disposition |
|---|---|---|---|
| W7 | `ElectSurvivor`'s "must be non-empty" is documentation-only | Both call sites ARE guarded (`Count > 0`, `Count > 1`); if reached, `InvalidOperationException` → 500, fail-closed but opaque | **Take path C** — one-line guard turns a comment into an enforced precondition |
| W8 | "the only in-memory copy" is over-broad | TRUE — `SetRecordShareExpiryEndpoint.cs:240` also compares in memory, but it sits **inside a `LogInformation` argument list** (counting lapsed-and-renewed shares), so it is telemetry, not a conferral decision | **Take path C** — narrow the claim to "the only in-memory copy used for a conferral decision" |
| W5 | ADR-038 ban B8 covers `InternalsVisibleTo`, not just reflection | TRUE, verbatim in `docs/adr/ADR-038-testing-strategy.md`: *"B8 bans `InternalsVisibleTo` as well as reflection, so the only compliant fix is giving the logic a public surface — a production refactor across several subsystems"*, filed under **"Blocked on a production refactor"**. The documented census of 12 sites is *reflection* sites; these are `internal` + `InternalsVisibleTo`, in the ban's text but not that inventory | **Path A now** (cite the ADR's own acknowledgement); **path B flagged** to the ADR owner. Amending a testing ADR needs sign-off and is not task 106's to do — but §6.5 forbids silently ignoring it |
| W1 | ADR-019 `reasonCode`/`traceId` vs `errorCode`/`correlationId` | Pre-existing (task 097 S6/S7); this change touched the 409's `detail` only, neither extension key | Pre-existing; repo-wide, so the ADR is what is out of date → path B candidate, not this task |
| W3 | A collapse can still lower the effective LEVEL | Pre-existing task 010 convergence; §6 records it | Path A — promote §6's rationale into the PR so it is reviewer-visible |
| W2 | `accessRecordId` now names a different row | The field's documented meaning ("the single surviving active row") is unchanged; only the value moved, and the old value pointed at a row about to be deactivated | Note in the PR: a client holding a previously-returned id may hold a deactivated row |
| W6 | No `[Trait("status", …)]` on the touched test files | Systemic — only 6 of 53 files under `tests/integration/auth/**` carry any trait, and the taxonomy (`repaired`/`real-bug-pending-fix`/`flaky-quarantined`) has no value for new green tests | Path A/B to the constraint owner; applying it to 2 files while 47 siblings lack it buys nothing |
| W9 | The 409 path returns before cache invalidation | Traced unreachable as a stale read: the cached set is built from a query carrying `ExpiryPredicate`, so an expired row is never in the cache to go stale | Comment only, so the invariant is not broken later |

### Status

**Task 106 is NOT complete.** The V1 fix is designed and its consequences traced, but **not applied** —
the `code-review` gate was still reading these files, and editing underneath it would make its findings
describe a state that no longer exists, the same class of non-result as a perturbation that fails to
compile. Order from here: apply V1 + W7 + W8 → rewrite the one bad test, add the determinism test →
re-run the affected classes, ArchTests and the full suite → **P1–P5** → publish size vs a fresh master →
fill §14 → POML + TASK-INDEX + drift check.

**The gate earned its keep.** Build, 51/51 tests, and 4/4 perturbations all passed over a real
privilege-loss defect, because every one of those instruments was single-request and the defect lives in
the interaction between two.
