# Governance baseline — 2026-09

> **Task**: 003 (spec FR-04) · **Measured**: 2026-09-04 · **Tree**: `work/code-quality-and-assurance-r4` @ `0ea19d408`, level with `origin/master`
> **Status**: **All 6 measures published.** Measure (c) was briefly escalated and the escalation withdrawn — see [`task-003-escalation.md`](task-003-escalation.md).
> **Purpose**: a denominator — but **check which measure feeds what before assuming**. Verified 2026-09-04: **(d)** feeds FR-07's prioritisation. **(c)** feeds FR-19b's drift check (and nothing else — it had no consumer at all until FR-19b was added). **FR-12's usage-weight terciles do NOT come from this document** — they come from FR-11's hook logging which `.claude/` primitives get read, a different subject entirely. An earlier draft of this file asserted otherwise; that was wrong and is corrected here.

**Observation only.** No threshold, target, or gate is derived or proposed from any number here, and none may be. A count-proxy for a judgment question is the retired God-class ratchet.

**Scope of every command below**: relative paths from this worktree root. No command walks `.claude/worktrees/` (which does not exist in this worktree) or any of the ~17 sibling worktrees under `c:/code_files/`.

---

## (a) `<extension>` yes/no ratio across POML justifications

| Value | Count | Share of `<extension>` elements |
|---|---|---|
| `<justification>` blocks | **470** | — |
| `<extension>` elements | **459** | 100% |
| … answering **No** (cannot extend → new component justified) | **186** | 40.5% |
| … answering **Yes** (extend the existing) | **124** | 27.0% |
| … **no leading verdict** — prose that does not begin yes/no | **149** | 32.5% |

```bash
grep -rho "<justification>" projects/*/tasks/*.poml | wc -l
grep -rho "<extension>" projects/*/tasks/*.poml | wc -l
grep -rhoE "<extension>[[:space:]]*\**[Yy]es" projects/*/tasks/*.poml | wc -l
grep -rhoE "<extension>[[:space:]]*\**[Nn]o[.,— ]"  projects/*/tasks/*.poml | wc -l
```

**Drift vs spec's 2026-09-03 figure**: spec said **448** justifications; measured **470** (+4.9%). Under the 10% trigger. Consistent with new projects landing — the count only grows.

**Finding worth carrying**: nearly **a third of `<extension>` answers do not lead with a verdict.** The leading tokens in that third are `cannot` (16), `N/A` (6), `extend`/`extends` (25), `this`/`the` (20), `reuse` (5), `partial` (5). Semantically most are answerable, but the field is not machine-readable as authored, so any future mechanism reading `<extension>` mechanically will silently mis-bucket ~32% of it. Recorded here rather than acted on — this is an observation task.

## (b) `<existing>none` count

**49** justifications assert no existing overlap.

```bash
grep -rhoiE "<existing>[[:space:]]*(none|no existing)" projects/*/tasks/*.poml | wc -l
```

**Drift vs spec**: spec said **53 of 448**; measured **49 of 470** (−7.5% absolute, and a larger relative fall as a share: 11.8% → 10.4%). Under the 10% trigger, but note the direction — a *decrease* in an append-only corpus means the two counts were not derived identically, or that POMLs were edited after authoring. Flagged, not resolved.

## (c) Per-package import fan-in — consuming deployables

> **Resolved 2026-09-04.** The escalation was **withdrawn** — see [`task-003-escalation.md`](task-003-escalation.md) for why it should not have been raised. Short version: nothing consumed this measure, so no downstream mechanism could be broken by choosing a recipe. The spec's 2026-09-03 figures are **unreproducible and superseded**; they are recorded below as history, not as a target.

**Recipe (canonical for r4)**: count `package.json` files that declare the package as a dependency, **excluding the package itself and its shared-library siblings** — i.e. how many *deployables* (PCF controls, solutions, SPA, add-ins) depend on it.

```bash
grep -rl "\"@spaarke/{package}\"" --include=package.json src/ \
  | grep -v node_modules | grep -v "^src/client/shared/" | wc -l
```

| Package | Consuming deployables |
|---|---|
| `@spaarke/ui-components` | 43 |
| `@spaarke/auth` | 30 |
| `@spaarke/sdap-client` | 18 |
| `@spaarke/smart-todo-components` | 16 |
| `@spaarke/communication-components` | 7 |
| `@spaarke/events-components` | 5 |
| `@spaarke/daily-briefing-components` | 4 |
| `@spaarke/document-operations` | 2 |
| `@spaarke/compose-components` | 2 |
| `@spaarke/ai-widgets` | 2 |
| `@spaarke/ai-outputs` | 2 |
| `@spaarke/visuals` | 1 |
| `@spaarke/notifications` | 1 |
| `@spaarke/legal-workspace` | 1 |
| `@spaarke/ai-context` | 1 |

**Nothing follows from these numbers, and that is deliberate.** A package with one consumer is legitimate (ADR-012 sanctions anticipatory promotion); a package with 43 is not thereby virtuous. NFR-05 forbids gating on a count-proxy for a judgment question, and "shared packages must have ≥N consumers" is exactly that. **Do not derive a rule from this table.**

**Superseded figures** — spec.md recorded on 2026-09-03: `ui-components` 54 · `auth` 37 · `communication-components` 8 · seven at 2 · three at 1 · two at 0. No recipe reproduces both that head and that tail (five were tried; the closest matches the head within tolerance but puts `sdap-client` at 18 where the tail requires ≤2, and puts nothing at 0). Its command was never recorded, so the difference cannot be attributed. **Treat the 2026-09-03 figures as unreproducible.** The table above supersedes them.

**What this measure is for, as of 2026-09-04.** Until today it had **no consumer** — no FR read it, and the one job it ever did was settled at design time (it killed a proposal to raise ADR-012's promotion trigger from 2 to 3, since six packages sat exactly at 2). It now feeds **FR-19b**, the nightly boundary-crossing drift check: the counts are the "before" against which a component acquiring a second consumer becomes visible. A measure with no consumer is a candidate for deletion, not maintenance — worth remembering at wrap-up for the other five.

**The FR-04 thesis demonstrating itself**: a number recorded on 2026-09-03 without its command was unverifiable on 2026-09-04. That is the argument for this whole requirement, and it appeared inside r4's own spec on day one.

## (d) ADR citation counts across POML tasks — ranked descending

**50 distinct ADRs cited.** This ordering is what FR-07 consumes to prioritise its criterion set.

| Rank | ADR | Citations | Spec 2026-09-03 | Δ |
|---|---|---|---|---|
| 1 | ADR-021 | **3,062** | 3,061 | +1 |
| 2 | ADR-013 | **2,111** | 2,131 | −20 (−0.9%) |
| 3 | ADR-038 | **1,776** | 1,745 | +31 (+1.8%) |
| 4 | **ADR-028** | **1,523** | 1,521 | +2 |
| 5 | **ADR-010** | **1,477** | 1,527 | −50 (−3.3%) |
| 6 | ADR-012 | **1,249** | 1,210 | +39 (+3.2%) |
| 7 | ADR-015 | 1,100 | — | — |
| 8 | ADR-029 | 1,048 | — | — |
| 9 | ADR-022 | 918 | — | — |
| 10 | ADR-039 | 858 | — | — |
| 11 | ADR-008 | 743 | — | — |
| 12 | ADR-001 | 702 | — | — |
| 13 | ADR-024 | 583 | — | — |
| 14 | ADR-007 | 559 | — | — |
| 15 | ADR-019 | 554 | — | — |

```bash
grep -rhoE "ADR-[0-9]{3}" projects/*/tasks/*.poml | sort | uniq -c | sort -rn
grep -rhoE "ADR-[0-9]{3}" projects/*/tasks/*.poml | sort -u | wc -l   # distinct
```

**Drift vs spec**: every count within ±3.3% — all well under the trigger. **But the ORDER changed**: spec ranked ADR-010 (1,527) above ADR-028 (1,521), a 6-citation gap. They have now **swapped** — ADR-028 leads ADR-010 by 46.

This matters because FR-07 consumes the *ordering*, not the counts. Two ADRs separated by 6 out of ~1,520 were never meaningfully ranked against each other; the swap is noise being read as signal. **FR-07 should treat ranks 4 and 5 as a tie** rather than inheriting whichever order the day's measurement produced.

## (e) `<escalation><trigger>` — declared, and how many fired

| Measure | Count |
|---|---|
| POML files carrying `<escalation>` | **1,018** |
| `<trigger>` elements | **1,317** |
| Project notes recording a trigger that **fired** | 97 files |
| Project notes explicitly recording *"neither fired" / "did not fire" / "no escalation"* | 197 files |

```bash
grep -rl  "<escalation>" projects/*/tasks/*.poml | wc -l
grep -rho "<trigger>"    projects/*/tasks/*.poml | wc -l
grep -rliE "trigger (fired|fires)|escalation fired|fired an escalation" projects/*/notes/ | wc -l
grep -rliE "neither fired|did not fire|no escalation" projects/*/notes/ | wc -l
```

**Honest caveat on the firing counts** — these two are the weakest numbers in this document, and should not be treated as a rate. They are keyword matches over free-form notes, so they (i) count *files*, not *events*, (ii) match prose *discussing* triggers as well as prose *reporting* one, and (iii) miss any firing recorded in wording the patterns don't anticipate. No structured record of trigger firings exists — which is itself the finding. A real firing rate would need a structured field, and creating one is **out of scope for r4** (no new POML block, per the scope fence).

Spec gave no 2026-09-03 reference figure for this measure, so there is no drift to report.

## (f) CLAUDE.md §6.5 amendment records

| Measure | Count |
|---|---|
| Files citing §6.5 across `.claude/`, `docs/`, `projects/`, root `CLAUDE.md` | **844** |
| ADRs carrying an explicit **path-B amendment** note (`.claude/adr/`) | **5** + `INDEX.md` |

The five ADRs amended under path B: **ADR-012** (shared components — twice: 2026-07-12 `@spaarke/visuals`, 2026-09-04 closed enumeration), **ADR-024** (polymorphic resolver), **ADR-039** (grounded execution / closed catalogs), **ADR-048** (communication participant index), **ADR-049** (compose shadow document — amended 3×).

```bash
grep -rl "§6.5\|section 6.5" .claude/ docs/ projects/ CLAUDE.md | wc -l
grep -rliE "path.b amendment|Path B amendment" .claude/adr/
```

**Reading**: §6.5 is cited very widely (844 files) but has produced only **5 amended ADRs** in the ~2 months since it became binding (2026-06-29). That gap is not evidence of a broken protocol — path A (project exception) and path C (comply) are both legitimate outcomes and leave no ADR edit — but there is **no count of paths A and C at all**, so the ratio between the three paths is unknown. Recorded as a gap, not a defect.

Spec gave no 2026-09-03 reference figure for this measure.

---

## Summary of drift vs spec's 2026-09-03 figures

| Measure | Spec | Now | Δ | Trigger (>10%)? |
|---|---|---|---|---|
| (a) justifications | 448 | 470 | +4.9% | no |
| (b) `<existing>none` | 53 | 49 | −7.5% | no |
| (c) fan-in | 54 / 37 / 8 / 7@2 / 3@1 / 2@0 | 43 / 30 / 18 / 16 / 7 … | spec's figures **unreproducible**, superseded | fired, **withdrawn** |
| (d) ADR citations | 50 distinct; top-6 named | 50 distinct; counts ±3.3%, **ranks 4↔5 swapped** | ≤3.3% | no |
| (e) escalation triggers | — | 1,317 across 1,018 POMLs | — | n/a |
| (f) §6.5 amendments | — | 5 ADRs; 844 citing files | — | n/a |
