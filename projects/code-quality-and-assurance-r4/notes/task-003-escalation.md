# 🔔 Task 003 — escalation trigger fired on measure (c)

> **Date**: 2026-09-04 · **Task**: 003 Publish the governance baseline
> **Status**: 5 of 6 measures published in [`baseline-2026-09.md`](baseline-2026-09.md). Measure **(c) per-package import fan-in** is blocked pending one decision.
> **This is a legitimate outcome, not a failure** — per `plan.md` §3.5 and CLAUDE.md §6, a fired trigger is a stop, and retrying past it is not permitted.

---

## The trigger, verbatim

> If a measure differs from `spec.md`'s 2026-09-03 figure by more than ~10%, STOP and report before writing the baseline. A large drift over one day suggests the measurement recipe differs from the one originally used, not that the repository changed — and adopting a differently-derived number as the baseline would silently break FR-18's hit-rate comparison.

The trigger fired, and its own stated diagnosis is correct: **the recipe differs.** The repository did not change materially in one day.

## What was found

`spec.md` records the 2026-09-03 fan-in as: **`ui-components` 54 surfaces · `auth` 37 · `communication-components` 8 · seven packages at 2 · three at 1 · two at 0.** It does not record the command.

Five plausible recipes, all defensible readings of "per-package import fan-in":

| Recipe | ui-components | auth | Head match? | Tail match? |
|---|---|---|---|---|
| **R1** — `package.json` files declaring it as a dependency | 50 | 39 | ✅ −7.4% / +5.4% | ❌ |
| **R2** — distinct `.ts`/`.tsx` files importing it | 607 | 383 | ❌ +1024% | ❌ |
| **R3** — distinct 2-level surface dirs (`src/X/Y`) | 35 | 28 | ❌ −35% | ❌ |
| **R4** — distinct 3-level dirs (`src/X/Y/Z`) | 96 | 76 | ❌ +78% | ❌ |
| **R5** — every directory containing an importing file | 268 | — | ❌ +396% | ❌ |

**R1 matches the head and fails the tail.** The spec's tail requires seven packages at 2, three at 1, and two at 0. R1 puts *nothing* at 0 or 1 — its minimum is 2 — and puts `@spaarke/sdap-client` at **20** and `@spaarke/smart-todo-components` at **17**, both of which the spec's tail requires to be ≤2.

**No recipe reproduces both ends.** The spec's figure is therefore not reproducible, and the difference is not repository drift.

## Why this needs a decision rather than a default

The number is not the point — it is a **denominator**. Three downstream mechanisms consume it:

- **FR-18** accumulates an equivalence-check hit rate against it. A hit rate measured against R2 and compared to a baseline taken under R1 is off by a factor of ~12 and will look like a dramatic trend.
- **FR-12** draws its usage-weight terciles from it. Different recipes produce different tercile boundaries and therefore rank different primitives as stale.
- **FR-16/FR-17** (the export index) will want the same notion of "consumer" — if it picks a different one, the two disagree permanently.

Picking silently would bake an arbitrary choice into all three and, per the trigger's own wording, break the comparison **silently** — the failure would surface months later as an inexplicable trend line.

## The decision required

**Which recipe is canonical for "per-package fan-in" for the remainder of r4?**

| Option | What it counts | Argues for |
|---|---|---|
| **A — R1, `package.json` declarations** *(recommended)* | Declared dependency edges between packages | Matches the spec's head figures; counts *packages*, which is the unit ADR-012 and FR-16/17 both reason in; stable under file-level refactoring; cheapest to compute in CI. Its weakness — it counts a declared dep even where nothing imports it — is arguably a feature for a governance measure, since an unused declared dep is itself worth seeing. |
| **B — R2, importing source files** | Actual import sites | The truest measure of real usage; immune to stale `package.json` entries. But it is volatile — a refactor that splits one file into three changes the number without changing anything architectural — which is poor behaviour in a denominator. |
| **C — R3, 2-level surface directories** | Consuming *surfaces* (PCF control, solution, shared package) | Closest to the spec's own word, "surfaces", and the most meaningful unit for "how many places depend on this". But it matched no spec figure, and the 2-vs-3-level boundary is a judgment call that will need its own rule. |
| **D — Publish all three, pick none** | — | Honest, and cheap now. But it defers the decision to whichever of FR-12/FR-16/FR-18 is implemented first, which is how the three end up disagreeing. |

**Recommendation: A (R1).** It reproduces the spec's head figures within tolerance, it counts the unit the rest of P1/P4 reasons in, and it is stable. The spec's tail figures should then be treated as **superseded and unreproducible** rather than as a target to match — recorded as such in the baseline.

Whatever is chosen, the command goes in `baseline-2026-09.md` alongside the number. That is the whole of FR-04, and its absence is precisely what produced this stop.

## Note

This is the FR-04 thesis demonstrating itself on day one: a number recorded without its command became unverifiable in **twenty-four hours**. Worth keeping in the project record — it is a stronger argument for FR-04 than the spec's own motivation section.

## What is NOT blocked

Measures (a), (b), (d), (e) and (f) are measured, reproducible, and published. Task 003 is marked 🔄 (blocked on this decision) rather than ✅. **P1's other work is complete** — tasks 001 and 002 are ✅ — and P2a has no dependency on measure (c), so execution can continue while this is decided.
