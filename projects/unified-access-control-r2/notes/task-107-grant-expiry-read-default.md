# Task 107 — ISS-009 (#974) option A: invert the read default so an undated grant confers NOTHING

> **Date**: 2026-09-21 · **Register**: ISS-009 · **Owner decision**: D-1 (2026-09-19) ·
> **Rescope**: task 115 follow-on, RETIRED the original Dataverse-plugin premise entirely

---

## 1. What changed, and why

`ExternalParticipationService.ExpiryPredicate` (the OData `$filter` string, the READ path) and
`ExternalParticipationService.ConfersAccessOn` (its in-memory mirror, the WRITE path, task 106) both
treated a null `sprk_expiresdate` as "never expires" — a rule task 007 added deliberately, at a time
when the BFF was the only writer and needed to avoid a false outage on pre-existing undated rows.

Since task 097 the BFF itself never writes a grant without `sprk_expiresdate` (FR-33: absent → today +
90 days). But the column stays optional in Dataverse, and users hold Create on
`sprk_externalrecordaccess`, so a row created through a form, the Web API, a flow, or an import can
still omit it — and the old rule let that row confer access **forever**.

The owner ruled out every Dataverse-side fix on 2026-09-19 (D-1): **"We do not use Dataverse plugins."**
No plugin, no entity-scope business rule, no ADR-002 exception. This task closes the gap from the READ
side instead — option A of D-1's three-part decision (B = task 117's scheduled reconciliation job, C =
task 118's Create-privilege retirement, both separate, already-authored tasks).

**The change**: drop the `eq null` disjunct from `ExpiryPredicate`, and flip `ConfersAccessOn` from
`expiresDate is null || …` to `expiresDate is not null && …`. A null `sprk_expiresdate` now confers
**NOTHING**. Undated moved from fail-OPEN to fail-CLOSED (ADR-003).

## 2. The load-bearing detail: two definitions, one commit

`ExpiryPredicate` and `ConfersAccessOn` are deliberately adjacent in
`Infrastructure/ExternalAccess/ExternalParticipationService.cs` (`:108-109` and `:133-134`) because they
answer the same question — "does this row confer access" — for two different shapes: a `$filter` string
for the read path, and a materialized row for the write path (`/grant`'s ADR-003 conferral check,
`GrantExternalAccessEndpoint.cs:307`; `/set-record-share-expiry`'s log count,
`SetRecordShareExpiryEndpoint.cs:244`). Changing one without the other would let `/grant` report a
null-expiry row as live while the reader denied it — exactly finding A-5's shape, reintroduced in the
opposite direction. Both changed in this one commit, and a new test
(`ExpiryPredicateAndConfersAccessOn_AgreeOnTheNullCase`) pins them to the same answer for the null case
so they cannot drift apart again.

## 3. Verified: exactly one `eq null` site (Step 0 census)

```
grep "sprk_expiresdate eq null" src/**/*.cs
→ ExternalParticipationService.cs:100  (the one line, pre-edit)
```

No second hand-rolled copy existed; the hoist from task 007 held. The escalation trigger for "more than
one site" did not fire.

## 4. Where `ConfersAccessOn`'s null branch is (and isn't) load-bearing today

Tracing both production call sites:

- **`GrantExternalAccessEndpoint.cs:307`** — `ConfersAccessOn(effectiveExpiry, today)`, where
  `effectiveExpiry` comes from `ExternalGrantLifecycle.EffectiveExpiry(explicit, rowExpiry, today) =>
  explicit ?? rowExpiry ?? DefaultExpiry(today)`. Because of that trailing `?? DefaultExpiry(today)`
  fallback, `effectiveExpiry` is **never actually null** at this call site — FR-33's default-fill already
  resolves it before `ConfersAccessOn` is asked. So today, this call site's behaviour is unaffected by the
  inversion; it will matter the moment any future caller passes a raw row's `ExpiresDate` here directly
  instead of through `EffectiveExpiry`.
- **`SetRecordShareExpiryEndpoint.cs:244`** — `ConfersAccessOn(s.ExpiresDate, today)`, log-count only ("how
  many shares had already lapsed and are renewed"). A null `ExpiresDate` here now counts as "renewed" in
  the log line even though nothing was renewed — a cosmetic log-text change, not a behavioural one.

This is why `GrantLifecycleCharacterizationTests.Upsert_OverAnExpiredRowWithAnUnboundedDuplicateOnTheSameKey_DoesNotReportNoAccess`
(`:958`) did **not** need its assertions inverted — its outcome was never actually driven by
`ConfersAccessOn`'s null branch, only by `ConferralRank`/`EffectiveExpiry`'s own `?? DefaultExpiry(today)`
fallbacks, which are unrelated code paths this task does not touch. Its doc comment was stale (cited "null
never expires" as the reason) and has been corrected in place to explain the real mechanism.

## 5. Tests inverted/added, and the real failure counts

Two production-behaviour assertions pinned the retired rule and were inverted (never deleted), plus one
new agreement test was added, all in `GrantExpiryCharacterizationTests.cs`:

| Test | Change | Reason (one line, cites D-1) |
|---|---|---|
| `ExpiryPredicate_TreatsAGrantWithNoExpiryAsNeverExpiring` → **renamed** `ExpiryPredicate_TreatsAGrantWithNoExpiryAsConferringNothing` | Assertion inverted: `Contain("eq null")` → `NotContain("eq null")` | Name itself stated the retired rule; task 107 / D-1 inverts it |
| `ConfersAccessOn_MirrorsTheReadFiltersExpirySemantics` | Null-case assertion inverted: `BeTrue()` → `BeFalse()` | Task 107 / D-1: null now confers nothing |
| `ExpiryPredicateAndConfersAccessOn_AgreeOnTheNullCase` (**new**) | N/A — new test | Acceptance criterion 2: pins both definitions to the same null-case answer so they cannot drift apart again |

`GrantLifecycleCharacterizationTests.cs:958`'s doc comment was corrected (not its assertions — see §4).
`ExternalAccessQueryIntegrityGuardTests.cs` (the source-scan fitness function) was re-verified unchanged —
it asserts that grant queries call the builder functions, not their string content, so it does not pin the
retired rule and needed no change. Confirmed: 3/3 pass, unaffected.

**Exact counts, measured empirically (all via `dotnet test tests/unit/Sprk.Bff.Api.Tests/... --filter
"FullyQualifiedName~GrantExpiryCharacterizationTests|FullyQualifiedName~GrantLifecycleCharacterizationTests"`)**:

| Scenario | Result |
|---|---|
| New code (both members inverted) + ORIGINAL (un-inverted) tests, restored from `git show HEAD` | **2 of 52 fail**: `ExpiryPredicate_TreatsAGrantWithNoExpiryAsNeverExpiring`, `ConfersAccessOn_MirrorsTheReadFiltersExpirySemantics`. (Task 007's original measurement was 1 of 11 — expected to be higher post-097/100/106, confirmed: 2, not 1, and off a larger base of 52.) |
| Final state: new code + inverted/new tests | **53/53 pass** (0 failed) |

## 6. Perturbation (mandatory, review-2026-08-24) — both directions, measured independently

Files were backed up to a scratchpad temp copy before each perturbation and restored from that copy
(never via `git checkout` or `git stash`, per this session's explicit constraint).

| Perturbation | Result |
|---|---|
| **Full revert** (both `ExpiryPredicate` and `ConfersAccessOn` restored to `git show HEAD`), new tests in place | **3 of 53 fail**: the renamed predicate test, the mirror test, and the new agreement test — all three redden together |
| **Predicate-only revert** (`ExpiryPredicate` restored to the old `eq null` form; `ConfersAccessOn` left fixed) | **2 of 53 fail**: `ExpiryPredicate_TreatsAGrantWithNoExpiryAsConferringNothing` + `ExpiryPredicateAndConfersAccessOn_AgreeOnTheNullCase`. `ConfersAccessOn_MirrorsTheReadFiltersExpirySemantics` stays GREEN — confirms the mirror-only test is independent of the predicate |
| **Mirror-only revert** (`ConfersAccessOn` restored to the old `is null \|\|` form; `ExpiryPredicate` left fixed) | **2 of 53 fail**: `ConfersAccessOn_MirrorsTheReadFiltersExpirySemantics` + `ExpiryPredicateAndConfersAccessOn_AgreeOnTheNullCase`. `ExpiryPredicate_TreatsAGrantWithNoExpiryAsConferringNothing` stays GREEN — confirms the predicate-only test is independent of the mirror |
| All restored to final (fixed) state | **53/53 pass**, confirmed again after restore |

Both directions redden the agreement test independently, satisfying the mandatory perturbation
constraint. Full suite (`Sprk.Bff.Api.Tests`, all categories) after final restore: **0 failed / 12,538
passed / 58 skipped / 12,596 total**. `Spaarke.ArchTests` (`ExternalAccessQueryIntegrityGuardTests`):
**3/3 pass**, unaffected as predicted.

## 7. 🔴 Pre-deploy gate — NOT TAKEN this session

D-1's note says "blast radius zero today, but COUNT before deploy." That is a claim about **live data**,
not about code, and it was **not re-measured this session** — the `dataverse` MCP server was not
authenticated in this session, so the live COUNT could not be taken. Per this task's own escalation
trigger: deliver the code + tests + docs and state explicitly that the pre-deploy gate is **UNMET**. Do
**not** repeat "blast radius zero today" as though verified.

The COUNT command is now documented in
[`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` §4.2a](../../../docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md)
for the operator to run before deploying this change:

```
GET /api/data/v9.2/sprk_externalrecordaccesses?$filter=statecode eq 0 and sprk_expiresdate eq null&$count=true
```

A non-zero count means real grantees lose real access on deploy; the operator must review and either
backfill an expiry or confirm the removal is intended, per §4.2a.

## 8. Placement Justification (CLAUDE.md §10) + §11

**§10 BFF Hygiene**: in-place semantic edit of two existing `internal static` members in
`Infrastructure/ExternalAccess/ExternalParticipationService.cs`. No new endpoint, service, DI
registration, package, or background work. Per [`.claude/constraints/bff-extensions.md`](../../../.claude/constraints/bff-extensions.md)
§A.1, the placement decision is "stays where it is" — trivially in-BFF, no new surface to place.

**Publish size**: 🔴 **NOT MEASURED this session.** CLAUDE.md §10 requires a fresh-worktree publish
comparison against a fresh `origin/master` build (never the recorded baseline number), using the same
`Compress-Archive` tool `scripts/Deploy-BffApi.ps1` uses, from short paths on both sides. That multi-step
procedure was not run — the change is two doc-comment-heavy method bodies (no new files, no new
dependencies, no new packages), so a material publish-size delta is not expected, but per this task's own
constraint ("if you do not run the fresh-worktree publish measurement, say so explicitly rather than
asserting a number") no number is asserted here. An operator or a later task should run the documented
procedure before merge if a size gate is required.

**No new HIGH CVE**: no package references were touched by this task.

**§11 Component Justification**: not applicable — this task adds no new component. `ExpiryPredicate` and
`ConfersAccessOn` already existed as the single hoisted definition pair (task 007 / task 106); this task
changes their semantics in place.

## 9. Deliberately NOT done (per task scope + D-1)

- **No Dataverse plugin, business rule, or schema default** — `src/dataverse/**` is untouched by this
  task (verified: `git status --porcelain -- src/dataverse/` returns nothing).
- **No scheduled reconciliation job** — that is task 117 (D-1 option B), which stamps existing undated
  rows, reusing `ExternalGrantLifecycle.DefaultExpiry` rather than a second literal 90-day constant.
- **No Create-privilege change** — that is task 118 (D-1 option C).
- **The live pre-deploy COUNT and the deploy itself** — operator steps, documented but not executed (§7).

## 10. Follow-on obligations

| # | Obligation | Owner |
|---|---|---|
| 1 | Run the §4.2a COUNT against live Dataverse before deploying this change; review any non-zero result with record owners | operator, pre-deploy |
| 2 | Run the fresh-worktree publish-size comparison per CLAUDE.md §10 before merge, if the project's merge gate requires it | operator / reviewer |
| 3 | Task 117 (D-1 option B) — scheduled job to stamp undated rows, closing the gap this task's read-side fix creates for any row an operator chooses not to hand-backfill | task 117 |
| 4 | Task 118 (D-1 option C) — retire the Create-privilege proxy that lets non-BFF writers create undated rows in the first place | task 118 |
