# Cutover record — task CICD-071

> **Date**: 2026-09-29
> **Outcome**: ✅ Cutover complete
> **Rollback**: not triggered, not needed

---

## 1. The honest characterization: this task changed no configuration

Task 071 was written as "the ~4-hour synchronized window" with three settings flips. By the time it
executed, **none of the three were a change**:

| Planned flip | What actually happened |
|---|---|
| Branch protection → required check `Router` | **Already done 2026-08-29** via ruleset `21824191`. Task 071 VERIFIED it. |
| Restore `Release` matrix | **Withdrawn** — violates a binding spec MUST NOT (§2 below) |
| Enable merge queue | **Declined by owner** on measured cost (§3 below) |

**So the cutover, in substance, happened on 2026-08-29** when branch protection began requiring
`Router`. It has run as the enforcing gate for a month. Task 071 is the formal record of that, plus
two scope decisions. Recording it as "a 4-hour window we executed today" would be false.

---

## 2. `Release` matrix — withdrawn to task 075

Step 2 instructed restoring `Release` to `sdap-ci.yml`. That contradicts the project's own spec:

- `spec.md:197` — ❌ MUST NOT restore `Release` before Phase 2 deletion merged **AND surviving suite
  green ≥7 days**
- `spec.md:128` (FR-B07) — same 7-consecutive-day rule
- `spec.md:225` — records that this clock is explicitly **not** amended
- `ci-tier2-advisory.yml:249-250` — "Debug-only is BINDING through Phase 2 per spec MUST §125 /
  FR-B07. Restore Release here ONLY after surviving suite green >= 7 days **(CICD-075)**."

The 7-day soak **is task 075**, which runs *after* 071. Per project `CLAUDE.md`, `spec.md` binds
where it conflicts with a task file.

It also would have been self-defeating: **task 077 deletes `sdap-ci.yml` ~14 days later.** Restoring
`Release` there costs ~35 extra job-minutes per push for two weeks and then vanishes, leaving no
Release coverage at all. The original step read "sdap-ci.yml (or its successor)" — the successor is
the operative half. **Release belongs in `ci-tier2-advisory.yml`, at task 075.**

---

## 3. Merge queue — declined, with the measurement

| | |
|---|---|
| Full CI run on a code change | **17–27 min** wall-clock |
| Non-draft open PRs at decision time | **17** |
| Projected time to drain at `batch=1` | **~5–7.5 hours** |

`batch=1` runs every PR's CI *inside* the queue, serially. That is the same serialization the owner
rejected on 2026-06-05 when choosing `strict=false` — and this task's own step-4 correction says of
`strict=true`: *"that serializes merging, which is exactly north star #2."* A merge queue is
**automated** serialization rather than manual. Better, but still serialization.

**The benefit was not demonstrated on this repo.** A merge queue guards the *semantic conflict* — two
PRs that each pass alone and break together. The only incident this project recorded, **PR #934, was
not that shape**: it changed `IOfficeService.QuickCreateAsync`'s signature without updating its caller
**within the same PR**. A merge queue would not have caught it. Widening Tier 1's compile scope to the
whole solution (**PR #944**) did, and that has shipped.

Declining is cheap because it is reversible: enabling a merge queue is a minutes-long settings change.
If a genuine semantic conflict lands later, turn it on then — and prefer `batch>1` over `batch=1`,
accepting bisect-on-failure as the trade.

---

## 4. Stability evidence — measured, not a 4-hour vigil

Step 8 asked for a 4-hour monitoring window. There is a **month** of data instead, because the gate
has been live since 2026-08-29. Every rollback trigger from spec §152:

| Trigger | Threshold | Measured | |
|---|---|---|---|
| Tier 1 flake rate | >2% sustained 24h | **0.0%** — 80 Tier 1 jobs, 0 failures | ✅ |
| Master green rate | <90% over 24h | **100%** — 60 CI runs, 0 failures, 2026-08-30 → 09-29 | ✅ |
| False-positive blocks | >3 within 24h | **0** — 0 false reds in the shadow window | ✅ |
| Merge queue stalls | >2h with >4 PRs | N/A — queue declined | — |

---

## 5. Shadow window — closed, and the gate that could never close

The window reported **WINDOW SATISFIED** at 8/8 agreeing, 0 false greens, 0 false reds, 24.1/5
calendar days, measured against the post-#944 configuration.

Two defects in `scripts/ci/shadow-window-status.ps1` had to be fixed first (PR #1028):

1. **The gate could never fire.** It documented that a false green resets the count, and implemented
   that reset — but `$ready` separately required `$falseGreens.Count -eq 0` across the *whole* window.
   The window's one false green (#934) is permanently in that set, so the script would have printed
   "Window still open" at 20/20, **forever**. The reset moved the displayed number, never the verdict.
2. **The window start was stale.** The script's own rule puts `-Since` immediately after the last
   change to the configuration under observation. #944 changed Tier 1's blocking scope and the date
   had not moved.

A third change added rate + projected-close reporting: comparable-PR volume is **0.33/day**, roughly
a third of what the 20-PR target assumed, which put the projected close at ~2026-11-05. Nothing
surfaced that. The target was then lowered 20 → 8 as a recorded **owner decision**, on the reasoning
that cutover does **not** delete `sdap-ci.yml` — the legacy oracle runs through the 7-day soak, so the
soak *is* this comparison, and waiting five more weeks buys an observation the soak provides anyway
while paying **56 job-minutes per master push** (vs 15 for the real gate).

---

## 6. State after cutover

- ✅ Branch protection: ruleset `21824191`, required check **`Router`** (exact name, NOT `CI / Router`),
  `strict=false`, PR required, force-push + deletion blocked
- ✅ Tier 1 blocking, Tier 2 advisory, path-aware dispatch skipping both on docs-only changes
- ✅ **`sdap-ci.yml` still running in parallel** — NOT deleted (that is task 077, gated on 075)
- ⛔ Merge queue: declined
- ⏭️ `Release` matrix: deferred to task 075, in `ci-tier2-advisory.yml`

**Next**: `075 soak (7d)` → `077 retire sdap-ci.yml` → `076 (30-day metrics)` → `090 wrapup`.
