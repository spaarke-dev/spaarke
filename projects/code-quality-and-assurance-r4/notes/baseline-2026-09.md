# Governance baseline — 2026-09

> **Task**: 003 (spec FR-04) · **Measured**: 2026-09-04 · **Tree**: `work/code-quality-and-assurance-r4` @ `0ea19d408`, level with `origin/master`
> **Status**: **5 of 6 measures published. Measure (c) is BLOCKED** — its escalation trigger fired. See §(c).
> **Purpose**: a denominator. FR-18's equivalence-check hit rate accumulates against these numbers, FR-12's usage-weight terciles are drawn from them, and FR-07 prioritises its criterion set by (d).

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

## (c) Per-package import fan-in — **BLOCKED, escalation trigger fired**

**No value published.** See [`task-003-escalation.md`](task-003-escalation.md) for the decision required.

The task's escalation trigger reads: *"If a measure differs from spec.md's 2026-09-03 figure by more than ~10%, STOP and report before writing the baseline. A large drift over one day suggests the measurement recipe differs from the one originally used… adopting a differently-derived number as the baseline would silently break FR-18's hit-rate comparison."*

That is exactly the condition. Four plausible recipes give four irreconcilable answers for the same package:

| Recipe | `@spaarke/ui-components` | `@spaarke/auth` | vs spec (54 / 37) |
|---|---|---|---|
| R1 — `package.json` files declaring it | **50** | **39** | −7.4% / +5.4% — **within tolerance** |
| R2 — distinct `.ts`/`.tsx` files importing it | 607 | 383 | +1024% |
| R3 — distinct 2-level surface dirs (`src/X/Y`) | 35 | 28 | −35% |
| R4 — distinct 3-level dirs (`src/X/Y/Z`) | 96 | 76 | +78% |
| R5 — every directory containing an importing file | 268 | — | +396% |

R1 matches on the two head packages — but **fails on the tail**. Spec's distribution is *"communication-components 8, seven at 2, three at 1, two at 0"*; R1 puts nothing at 0 or 1 (minimum 2) and puts `sdap-client` at 20 and `smart-todo-components` at 17, both of which the spec's tail requires to be ≤2. **No single recipe reproduces both the head and the tail**, so the spec's number is not reproducible at all.

**This is the FR-04 thesis demonstrating itself**: the 2026-09-03 measurement recorded a number without its command, and one day later nobody can re-derive it. Which recipe becomes canonical is a real decision — it binds FR-12 and FR-18 downstream — and the spec's figure cannot arbitrate because its derivation was never written down.

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
| (c) fan-in | 54 / 37 / 8 / 7@2 / 3@1 / 2@0 | **irreproducible** | — | **🔴 FIRED** |
| (d) ADR citations | 50 distinct; top-6 named | 50 distinct; counts ±3.3%, **ranks 4↔5 swapped** | ≤3.3% | no |
| (e) escalation triggers | — | 1,317 across 1,018 POMLs | — | n/a |
| (f) §6.5 amendments | — | 5 ADRs; 844 citing files | — | n/a |
