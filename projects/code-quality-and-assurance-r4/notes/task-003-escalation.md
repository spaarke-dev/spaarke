# Task 003 — escalation raised on measure (c), then WITHDRAWN

> **Raised** 2026-09-04 · **Withdrawn** 2026-09-04, same day, after the owner asked what the measure was actually for.
> **Outcome**: task 003 completed. Measure (c) published in [`baseline-2026-09.md`](baseline-2026-09.md) §(c).
> **Kept as a record** because the reason it was wrong is more useful than the escalation was.

---

## What was raised

The trigger fired: measure (c) (per-package import fan-in) differed from `spec.md`'s 2026-09-03 figure by far more than 10%. Five plausible recipes gave `@spaarke/ui-components` as 50 / 607 / 35 / 96 / 268 against the spec's 54, and none reproduced both the head and the tail of the spec's distribution. The spec never recorded its command, so the difference could not be attributed.

That much was correct, and is still correct. The spec's figures are unreproducible.

## Why the escalation was wrong

**I claimed the choice "binds FR-12, FR-16/17 and FR-18 downstream." That was false, and I had not checked it.**

Grepping the spec, the design, and all 33 tasks for `fan-in`:

| Location | What it says |
|---|---|
| `spec.md` FR-04(c) | the definition of the measure |
| `spec.md` §Dependencies | "already taken 2026-09-03 — P1 confirms rather than derives" |
| `design.md` §rejected alternatives | the **one** use it ever had (below) |
| `design.md` §measured | restates the figures |
| **any task** | **nothing** |

**Nothing consumed it.** FR-12's usage-weight terciles come from FR-11's hook — which logs reads of `.claude/` governance files, not TypeScript package dependencies. Different subject entirely. I inferred the dependency from the spec's general sentence about the baseline being "a denominator" and applied it to this measure without verifying.

So the premise of the stop — *"choosing silently would break a downstream comparison"* — was not true. There was no downstream comparison to break.

**And the measure had already done its only job.** At design time it settled one question: should ADR-012's promotion trigger rise from 2 consumers to 3? Answer no — six packages sit exactly at 2, several of which nobody would argue against. That decision is closed, and the owner separately rejected raising the trigger. Nothing else ever read the number.

## The error class

This is the same failure I had spent the previous two tasks documenting in the POMLs: **asserting a fact without measuring it.** Tasks 001 and 002 each carried a false premise ("LegalWorkspace has no `package.json`", "`@spaarke/visuals` has 0 consumers"), and I caught both by checking. Then I produced one of my own — an invented dependency — and escalated on it.

Worth stating plainly because the correction is cheap and the pattern is not: **a dependency claim is a factual claim, and it is greppable.**

## What the owner's question exposed

Asked *"if it's used a lot, then what? if a little, then what?"*, the honest answer was **nothing** — by design. ADR-012 sanctions anticipatory promotion at one or zero consumers, and NFR-05 forbids gating on a count-proxy for a judgment question. So neither a high nor a low number implies an action.

That is a stronger finding than the escalation was: **measure (c) was a number collected for its own sake.** It also reframed the project's objective, which the owner then stated directly:

> The important point is that the investigation and assessment is done during the planning phase; and revisited in CI in case there was drift or new approach that brings the feature into reuse territory.

Checked against the spec: the planning half was covered (FR-16 + FR-17 + CLAUDE.md §11); the **CI-revisit half was not**. That gap became **FR-19b**, which is now measure (c)'s first real consumer.

## Resolution

- Measure (c) published using the *consuming deployables* recipe, with its command recorded.
- The spec's 2026-09-03 figures marked **unreproducible and superseded**, kept as history.
- **FR-19b added to P4** (task 046) — the nightly boundary-crossing drift check, carrying an anti-handwaving constraint set: dispositions must stick, it must be proven to fire on a real instance before shipping, and it carries a kill criterion (three quiet months → delete, not tune).
- Task 003 → ✅.

## Carry to wrap-up

**Five of the six FR-04 measures still have no named consumer.** (c) now has one. Before r4 closes, each of (a), (b), (d), (e), (f) should either be pointed at a mechanism that reads it or dropped. A measure nobody reads is maintenance cost wearing the costume of rigor — precisely the "window dressing" the owner warned against.
