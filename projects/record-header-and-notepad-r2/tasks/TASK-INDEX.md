# Task Index — Configurable Record Header (R2)

> **Generated**: 2026-08-25 by `/project-pipeline` Step 3
> **Source**: [`plan.md`](../plan.md) ← [`spec.md`](../spec.md) (27 FRs · 11 NFRs · 24 success criteria)
> **Total**: 30 tasks · 7 phases · ~9.25–13.5 dev-days
> **Status legend**: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked · ⏭️ deferred

---

## Status Board

### Phase 0 — Spike + Baseline (BLOCKS everything)

| Task | Title | Rigor | Tier | Parallel | Deps | Status |
|---|---|---|---|---|---|---|
| [001](001-layoutjson-ergonomics-spike.poml) | `layoutJson` ergonomics spike | STANDARD | sonnet | ❌ | — | ✅ † |
| [002](002-capture-matter-parity-baseline.poml) | Capture Matter v1.0.20 parity baseline | STANDARD | sonnet | ❌ | 040 † | 🔲 |

> † **001 closed by escalated deviation** (owner-approved 2026-08-27): the of-type decision was already shipped and was verified against the LIVE Project form by read-only query rather than by the written scratch-control procedure. Checks 1-2 PASS; check 3 (solution round-trip) is **OPEN** — the Project form is in NO shippable solution, only the Default catch-all. See [`notes/spike-layoutjson-ergonomics.md`](../notes/spike-layoutjson-ergonomics.md).

> † **Ordering trap**: the shipped Matter header currently returns HTTP 400 (see 040), so there may be nothing to screenshot. Run 040 first, or capture the baseline from a pre-deletion build.

### Phase 1 — Renderers

| Task | Title | Rigor | Tier | Parallel | Deps | Status |
|---|---|---|---|---|---|---|
| [010](010-renderer-datefield.poml) | `DateField` (date + datetime modes) | FULL | sonnet | ✅ A | 002 | ✅ |
| [011](011-renderer-numberfield.poml) | `NumberField` (incl. Money) | FULL | sonnet | ✅ A | 002 | ✅ |
| [012](012-renderer-booleanfield.poml) | `BooleanField` | FULL | sonnet | ✅ A | 002 | ✅ |
| [013](013-optionsetfield-edit-mode.poml) | `OptionSetField` edit mode + typography | FULL | sonnet | ✅ A | 002 | ✅ |
| [014](014-textfield-emdash-alignment.poml) | `TextField` em-dash alignment | FULL | sonnet | ✅ A | 002 | ✅ |
| [015](015-renderer-barrel-and-contract-tests.poml) | Barrel + renderer-contract tests | FULL | opus | ❌ serial | 010–014 | ✅ |

### Phase 2 — Metadata + Machinery

| Task | Title | Rigor | Tier | Parallel | Deps | Status |
|---|---|---|---|---|---|---|
| [020](020-metadata-targets-and-cache.poml) | `targets` projection + metadata cache | FULL | sonnet | ✅ B | 002 | ✅ |
| [021](021-shared-getxrmpage.poml) | Shared `getXrmPage()` + `xrmContext.Page` | FULL | sonnet | ✅ B | 002 | ✅ |
| [022](022-hoist-record-header-fields.poml) | Hoist `useRecordHeaderFields` | FULL | opus | ❌ | 021 | ✅ |
| [023](023-oob-lookup-cell.poml) | OOB lookup cell (`lookupObjects`) | FULL | sonnet | ❌ | 020, 022 | ✅ |
| [024](024-toolbar-slot-autohide-and-agreement.poml) | Slot auto-hide + Agreement map entries | FULL | sonnet | ✅ B | 002 | ✅ |

### Phase 3 — Resolver + Control

| Task | Title | Rigor | Tier | Parallel | Deps | Status |
|---|---|---|---|---|---|---|
| [030](030-config-schema-and-guard.poml) | Config types + non-throwing guard | FULL | sonnet | ✅ C | 015 | ✅ |
| [031](031-resolve-header-config.poml) | `resolveHeaderConfig` (pure) | FULL | opus | ❌ | 030 | ✅ |
| [032](032-shell-columns-prop.poml) | `RecordHeaderShell` `columns` prop | FULL | sonnet | ✅ C | 015 | ✅ |
| [033](033-recordheader-pcf-control.poml) | **`RecordHeader` PCF control** | FULL | opus | ❌ | 001, 031, 023 | ✅ |
| [034](034-sparkle-recordsummary-wiring.poml) | Sparkle → `sprk_recordsummary` | FULL | sonnet | ❌ | 033 | ✅ |

### Phase 4 — Schema-drift remediation (independent — schedule EARLY)

| Task | Title | Rigor | Tier | Parallel | Deps | Status |
|---|---|---|---|---|---|---|
| [040](040-fix-rs1-matter-summary-select.poml) | 🔴 **RS-1** — Matter header 400s in production | FULL | opus | ❌ | — | ✅ |
| [041](041-fix-rs2-topic-registry-target.poml) | 🔴 **RS-2** — topic-registry target field | STANDARD | sonnet | ❌ | — | ✅ |

### Phase 5 — Rollout (operator / maker tasks)

| Task | Entity | Exercises | Tier | Deps | Status |
|---|---|---|---|---|---|
| [050](050-bind-project-header.poml) | `sprk_project` | date · boolean · optionset | sonnet | 034 | 🔲 |
| [051](051-bind-workassignment-header.poml) | `sprk_workassignment` | date · boolean · optionset | sonnet | 034 | 🔲 |
| [060](060-bind-invoice-header.poml) | `sprk_invoice` | **currency** · date · boolean | sonnet | 050, 051 | 🔲 |
| [061](061-bind-event-header.poml) | `sprk_event` | **datetime** · lookup · optionset | sonnet | 050, 051 | 🔲 |
| [070](070-bind-agreement-header.poml) | `sprk_agreement` | toolbar map; **seed a record first** | sonnet | 024, 060, 061 | 🔲 |
| [080](080-migrate-matter-header.poml) | `sprk_matter` | **parity QA** vs the 002 baseline | opus | 002, 070 | 🔲 |
| [081](081-retire-matterheaderpcf.poml) | — | Retire old control (irreversible) | opus | 080 | 🔲 |

### Phase 6 — Docs + Wrap

| Task | Title | Rigor | Tier | Parallel | Deps | Status |
|---|---|---|---|---|---|---|
| [085](085-rewrite-authoring-guide.poml) | Rewrite authoring guide from shipped code | STANDARD | sonnet | ❌ | 080 | 🔲 |
| [086](086-refresh-record-header-pattern.poml) | Refresh pattern pointer (**main session only**) | MINIMAL | sonnet | ❌ | 085 | 🔲 |
| [090](090-project-wrap-up.poml) | Wrap-up + `/test-diet` | MINIMAL | sonnet | ❌ | 086 | 🔲 |

---

## Validation

`scripts/Validate-TaskPoml.ps1` — **PASS**: 30 POMLs scanned, **0 errors**, 20 clean, 10 warnings. All 30 are well-formed XML and carry the required canonical field set (`<rigor>`, `<model-tier>`, `<effort>`, `<parallel-group>`, `<parallel-safe>`, `<steps mode>`, plus `<justification>` on new-surface tasks).

**The 10 warnings are triaged and deliberately left as-is.** All fire the same lint heuristic — "declares a `<file role="new">` but has no `<justification>`" — and in every case the new file is a **test file or a notes/QA artifact**, not new component surface:

| Task | The `role="new"` file | Why no justification |
|---|---|---|
| 001, 002, 050, 051, 060, 061, 070, 080, 081 | `notes/*.md` (spike result, parity baseline, per-wave QA records, retirement log) | Documentation output, not a component |
| 013, 014 | `__tests__/*.test.tsx` | Tests for existing surface — §11 exempts these explicitly |

CLAUDE.md §11 governs new **services / abstractions / interfaces / endpoints / DI registrations / packages / Dataverse columns**. Adding `<justification>` blocks here to silence the lint would produce precisely the hollow answers §11 warns against and `code-review` Step 6.6 rejects. The lint keys on `role="new"` without distinguishing artifact kind — a heuristic limitation, not a defect in these tasks.

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Goal-eligible | Notes |
|---|---|---|---|---|
| **A** | 010, 011, 012, 013, 014 | 002 | ✅ yes | Distinct renderer files. **None may edit `fields/index.ts`** — that is task 015. Machine-verifiable end state (5 components + tests). |
| **B** | 020, 021, 024 | 002 | ✅ yes | Distinct services/hooks files |
| **C** | 030, 032 | 015 | ✅ yes | Types file + shell prop |
| — | 040, 041 | none | ❌ no | Independent; **schedule first** — 040 is live breakage |
| — | 050+051, then 060+061 | 034 | ❌ no | Operator/maker work; not agent-automatable |

**Max concurrency**: 6 agents per wave. **Build verification between waves is mandatory** — `npm run build:prod` for PCF (**never** `npm run build`, per root CLAUDE.md §12 / FAILURE-MODES AP-1).

### ⚠️ Cross-group file collisions — do NOT dispatch these together

Group membership alone is not sufficient; these pairs touch the same file and must be serialized regardless of group:

| Files | Tasks | Rule |
|---|---|---|
| `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx` | **040** (RS-1 `$select` fix) and **021** (migrate `getXrmPage` duplicate) | Run **040 first** — it is live breakage and the smaller edit. 021 rebases onto it. |
| `Spaarke.UI.Components/src/hooks/index.ts` | **022** owns this barrel edit | No other Phase-2 task may touch it (same rule as 015 owning `fields/index.ts`) |
| `Spaarke.UI.Components/src/components/RecordHeader/fields/index.ts` | **015** owns it | Group A (010–014) must not touch it |

Group B file sets are otherwise disjoint: 020 → `services/`, 021 → `utils/` + `FieldMappingHandler`, 024 → `hooks/useRecordHeaderToolbarActions` + `toolbarLaunchDefaults`.

**Not goal-eligible** and why: Phase 5 is maker/QA work with human judgment; 080/081 are irreversible; 086 touches `.claude/` (main-session-only per root CLAUDE.md §3).

---

## Critical Path

```
002 (baseline, needs 040 first)
  └→ A: 010-014 ──→ 015 ──→ 030 ──→ 031 ──┐
  └→ B: 020, 021 ──→ 022 ──→ 023 ─────────┤
                        001 ──────────────┤
                                          └→ 033 ──→ 034
                                                      └→ 050+051 → 060+061 → 070 → 080 → 081 → 085 → 086 → 090
```

**Longest chain**: 040 → 002 → 010 → 015 → 030 → 031 → 033 → 034 → 050 → 060 → 070 → 080 → 081 → 085 → 086 → 090 (16 links).

**Blocking hubs** — a slip here delays everything downstream:
- **002** blocks all of Phase 1 and 2 (and is itself blocked by 040 in practice)
- **015** blocks Phase 3
- **033** blocks the entire rollout
- **034** gates every form binding

---

## High-Risk Items

| Task | Risk | Guard |
|---|---|---|
| **040** | Live production breakage; the hotfix-vs-wait call needs owner input | `<escalation>` trigger; schedule first |
| **033** | Highest blast radius — brownfield PCF scaffolding with 10 known R1 gotchas | `opus` tier; `pcf-build-scaffold.md` is required reading |
| **022** | Must preserve R1's form-buffer dirty-state semantics **exactly** (v1.0.7 fixed a full-PCF re-render on every edit) | `opus` tier + escalation trigger |
| **081** | **Irreversible** — two ordered steps; deleting the solution container does not delete the control | `mode="prescriptive"`; rollback window closes here |
| **080** | Parity judgment; the OOB lookup change must not be flagged as a regression | Criterion explicitly excludes the lookup interaction |
| **015** | The `fields/index.ts` race that cost R1 time | Extracted to its own serial task |
| **070** | Agreement has **0 records** | Seed one before QA |

---

## Per-Wave Measurement (NFR-01 / NFR-02)

Bundle size (≤250 KB) and TTI (≤300 ms warm / ≤800 ms cold) are measured **at each Phase 5 wave**, not once at the end. R1 shipped 62.4 KiB, so there is headroom — but four new renderers plus metadata calls are exactly what could erode it.

| Wave | Measure after |
|---|---|
| 050 + 051 | First real config-driven render |
| 060 + 061 | Currency + datetime renderers now loaded |
| 070 | Full renderer set |
| 080 | Parity build — compare against R1's 63,812 bytes |
