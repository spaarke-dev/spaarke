# Implementation Plan — Spaarke Ontology Platform R1

> **Source**: [`spec.md`](spec.md) (44 FRs, 9 NFRs, 11 success criteria) · decisions in [`design.md`](design.md) rev 11
> **Created**: 2026-10-03 by `/project-pipeline`
> **Branch**: `docs/ontology-platform-design` · PR [#1111](https://github.com/spaarke-dev/spaarke/pull/1111) (draft)

---

## 1. Strategy

**Build the one differentiated predicate first, then the mechanism around it.** The spec says only one
item carries product risk — the cross-source evaluator. Everything else is mechanism that already has a
shipped reference implementation somewhere in the repo.

Three sequencing facts shape the whole plan:

1. **The schema is nearly done but not done.** Five tables exist and are privilege-verified; **four columns
   do not exist** and two of them block a path outright (the deny path, and criterion 10). So Phase 0 is
   schema completion, not schema design.
2. **`notExists` has no prior art in this repo.** `EXISTS` does (`DataversePrecedentBoard.cs:182-190`), but
   every `ConditionOperator.Null` filters the *primary* entity and every `LeftOuter` enriches rather than
   anti-joins. The predicate compiler is therefore **new code with no template**, gets the top model tier,
   and is proved on real data **before** anything is built on top of it.
3. **Three cleanup items gate the worklist row.** C-1 in particular is a misdirection hazard: two live
   comments point a reader at `appnotification` read-state as the dismiss path, which would record
   bell-panel state instead of a Decision Record — appearing to work while breaking row-contract
   requirement 5 and starving criterion 4.

## 2. Architecture context

### Discovered resources

| ADR | Why it applies |
|---|---|
| **ADR-002** | No plugins. Append-only and immutability are **privileges**; each invariant has one BFF owner |
| **ADR-004** | Queue `IJobHandler` — the two event triggers (58 files already use this seam) |
| **ADR-036** | Scheduled `IScheduledJob` — the nightly evaluator. Six live jobs; `SecureRecordIsolationCensusJob` is the nearest shape |
| **ADR-052** | Workload placement, decided per workload; tie-breaker **fewer moving parts**. No new timer `BackgroundService` |
| **ADR-024** | Polymorphic regarding — the subject seam, reused unchanged |
| **ADR-039** | Binding owns dispatch — **tension, path A** (project-scoped exception) |
| **ADR-040** | Session ledger — **tension, path B** (amendment: siblings) |
| **ADR-015** | Privilege flagged, never decided — carried forward, nothing branches on it |
| **ADR-013** | `PublicContracts` facade — no AI-internal types in the evaluator |
| **ADR-038** | Testing strategy — plus the project's own two rules (NFR-05) |
| **ADR-045** | Association engine — the tokenizer repair changes its precision/cost profile |
| **ADR-050** | Canonical modal shell — `SprkModal` + presets |

| Skill | Use |
|---|---|
| `task-execute` | Every task, mandatory (CLAUDE.md §4) |
| `bff-deploy` | BFF deploy tasks — hash-verify + 120s Linux health window |
| `code-page-deploy` | SpaarkeAi / shared-component deploy |
| `conflict-check` | **Before every BFF PR** (design §2) |
| `adr-check` · `code-review` | Step 9.5 gates on FULL-rigor tasks |
| `test-diet` | Project-close gate on the wrap-up task (CLAUDE.md §7) |

| Knowledge doc | Topic |
|---|---|
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` | Invariants on the server, WP-1..WP-8, fail-closed |
| `docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md` | The narrative-output pattern to reuse for prose |
| `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` | Widget hosting, the two wrappers |
| `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` | Widget archetypes + decision tree |
| `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` | The worklist row is a **card** |
| `docs/standards/MODAL-DESIGN-SYSTEM.md` · `MODAL-DECISION-CRITERIA.md` | Modal shell + when OOB vs proprietary |
| `docs/standards/COMPONENT-COMPLEXITY.md` | Complexity and cohesion, never line count |
| `.claude/constraints/bff-extensions.md` | Binding pre-merge checklist for BFF additions |

| Canonical implementation | Reuse as |
|---|---|
| `CommunicationRuleGate.cs:108-188` | Scope match, ordering, **fail-closed** — copy verbatim |
| `DataversePrecedentBoard.cs:182-190` | `EXISTS` via INNER `AddLink` + `LinkCriteria` |
| `SecureRecordIsolationCensusJob` | Scheduled census/sweep shape |
| `PolymorphicResolverService.applyResolverFields` | Typed lookup **and** denormalized trio in one call |
| `WorkspaceShell/MetricCard.tsx:24-222` | Count-filter cards — already clickable |
| `DocumentRowMenu.tsx:150-208` | The row ⋮ menu — action-descriptor table |
| `SprkChat/OutcomeCard.tsx:93-367` | Post-action outcome surface |
| `dailyBriefing.registration.ts` | Widget registration template |

### Schema state (environment `spaarkedev1`)

Five tables live in solution `OntologyPlatformSolution`, publisher **Spaarke** — `sprk_signal` (59 cols),
`sprk_decisionrecord` (22), `sprk_policy` (17), `sprk_policyversion` (17), `sprk_budgetrevision` (15). Both
alternate keys **Active**. Auditing on all five **and** at org level. Three roles created, assigned,
privilege-verified. **All five tables at 0 rows.**

**Gaps Phase 0 closes**: `sprk_decisionrecord.sprk_action` · `sprk_servicerequest.sprk_direction` ·
`sprk_servicerequest.sprk_disposition` · `sprk_servicerequest.sprk_responseduedate`.

## 3. Phase breakdown

| Phase | Name | Tasks | Gate out |
|---|---|---|---|
| **0** | Foundations — schema, ADR resolution, seed data | 001–005 | The schema supports every path in the spec; criterion 2 has input |
| **1** | Cleanup that gates the row | 010–012 | C-1/C-3/C-4 closed; the row can be built without stepping on a trap |
| **2** | Policy and rule bodies | 020–023 | An `Existence` policy is authorable and savable; invalid bodies refused |
| **3** | The evaluator and Signal lifecycle | 030–034, 035-deploy | Path B fires on seeded data and closes when it stops holding |
| **4** | The Decision Record | 040–042 | Every authorize and deny writes a record; append-only verified |
| **5** | The worklist surface | 050–054, 055-deploy | One row component renders every shape; membership from configuration |
| **6** | Do lane and Briefing fold-in | 060–064 | Decide above Do; the Briefing's items are Work Items |
| **7** | Inquiry and the classifier | 070–074 | Something happens in the world; recall floor measured |
| **8** | Remaining cleanup and repairs | 080–083 | C-1..C-27 all fixed or filed; two repairs landed |
| **9** | Wrap-up | 090 | `/test-diet` run, lessons-learned written, README set to Complete |

### Phase 0 — Foundations

| Task | Deliverable |
|---|---|
| 001 | The four missing columns, via the Web API recipe (**not** MCP `create_table`) |
| 002 | Privilege re-verification after the column adds, incl. the union check |
| 003 | **ADR-040 amendment** (D-9 path B) + record the ADR-039 exception. ⚠️ touches `.claude/` → **sequential, main session only** |
| 004 | Policy + taxonomy seed rows (`sprk_policy`, `sprk_policyversion`, guidance rows enabled) |
| 005 | §8.1 dev data **plus assumption A-3's two negative controls** |

### Phase 1 — Cleanup that gates the row

| Task | Deliverable |
|---|---|
| 010 | **C-1** — delete the three dead briefing hooks; fix the two stale comments |
| 011 | **C-3** — disambiguate `MetricCard` (two unrelated components share the name) |
| 012 | **C-4** — the generic status/severity badge in `@spaarke/ui-components` |

### Phase 2 — Policy and rule bodies

| Task | Deliverable |
|---|---|
| 020 | `Existence` added to the closed rule-type set + its JSON Schema |
| 021 | 🔴 **The predicate compiler** — two ANDed clauses, no cross-clause variables, `exists` + `notExists`. **New code, no template** |
| 022 | Rule-body validation that refuses to save an invalid body |
| 023 | Scope semantics copied verbatim + policy authoring defaults + the `modifiedon` prohibition |

### Phase 3 — The evaluator and Signal lifecycle

| Task | Deliverable |
|---|---|
| 030 | The Signal writer — dedupe upsert, resolver fields, **and owner from the grouping matter** |
| 031 | The single re-evaluating `IScheduledJob` (nightly, both lanes) |
| 032 | The two `IJobHandler` event triggers (classification; budget-revision create) |
| 033 | Closure semantics — `ConditionCleared` / `Superseded` / `PolicyRetired`, incl. aged-out |
| 034 | Suppression — per (policy, matter), 30-day expiry, `countstowardsuppression` respected |
| 035 | **Deploy** BFF to dev + verify |

### Phase 4 — The Decision Record

| Task | Deliverable |
|---|---|
| 040 | The writer — every human resolution, `sprk_recordclass`, mandatory `sprk_factsnapshot` |
| 041 | Append-only verified by privilege; relationship direction 1 → N with the FK on the Signal |
| 042 | Wire the writer into `RuleGatedAssessedConsumer` on **both** paths — one line before the branch |

### Phase 5 — The worklist surface

| Task | Deliverable |
|---|---|
| 050 | The `sprk_gridconfiguration` row — membership, columns, actions |
| 051 | 🔴 **One row component**, all five row-contract requirements, evidence tiers + disclosure |
| 052 | Extend `MetricCard`/`MetricCardRow`, `DocumentRowMenu`, `OutcomeCard` — do not rebuild |
| 053 | The gate host + the action that changes something |
| 054 | Reconciliation as a Console tab — one registration + one aggregate Work Item |
| 055 | **Deploy** SpaarkeAi + shared components |

### Phase 6 — Do lane and Briefing fold-in

| Task | Deliverable |
|---|---|
| 060 | **ISS-003** — 48 `sprk_event` rows stranded in `Draft`. Blocks the Do lane |
| 061 | BR-2's three temporal policies as a **predicate migration, not a collector rewrite** |
| 062 | Know → narrative + Context pane; retire *Critical Today*; remove the LLM "Top action" |
| 063 | The first Know-promotion rule — new matter with no budget after 5 days |
| 064 | Lane order, count filters per lane, and the Briefing-tab replacement (BR-4) |

### Phase 7 — Inquiry and the classifier

| Task | Deliverable |
|---|---|
| 070 | The Inquiry — one Action row + one Binding, through the gate, carrying an SLA |
| 071 | Disposition accrual per matter and per outside firm |
| 072 | Guidance injection — `name — guidance`, schema `enum` unchanged |
| 073 | `sprk_memo` as signal source #2, same Signal shape, no consumer change |
| 074 | **The recall measurement** — ≥80% on ≥50 labelled items (D-10). Exit gate |

### Phase 8 — Remaining cleanup and repairs

| Task | Deliverable |
|---|---|
| 080 | The six 🔴 hazards — C-19, C-21, C-22, C-5, C-10, C-23 |
| 081 | The duplication cleanup — `cleanGuid`, the Xrm frame-walk, `Spaarke.Events.Components` |
| 082 | The space-bearing matter-number tokenizer, with a measured query-count delta |
| 083 | The false association `reason` string |

### Phase 9 — Wrap-up

| Task | Deliverable |
|---|---|
| 090 | `/test-diet`, lessons-learned, README → Complete, archive |

## 4. Parallel execution groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **A** | 010, 011, 012 | 001 | Different files; 012 is new shared-lib surface |
| **B** | 020, 023 | 003 | 021 is deliberately **serial** — it is the risk item |
| **C** | 033, 034 | 031 | Lifecycle semantics, separate code paths |
| **D** | 041, 042 | 040 | Privilege verification vs consumer wiring |
| **E** | 052, 053, 054 | 051 | Separate components once the row exists |
| **F** | 062, 063 | 061 | Configuration + one policy row |
| **G** | 071, 072, 073 | 070 | Independent surfaces |
| **H** | 080, 081, 082, 083 | none | Cleanup and repairs — independent of the critical path |

**Not parallel-safe**: 003 (touches `.claude/`), 021 (the risk item — serial by choice), 035 and 055 (deploys).

## 5. Critical path

```
001 → 003 → 020 → 021 → 030 → 031 → 040 → 042 → 050 → 051 → 074 → 090
      (ADR)   (type) (predicate) (writer) (job) (record) (wire) (config) (row) (recall)
```

**021 is the longest-pole single task** and the only one whose value is unproven. **074 can fail the
project** — it is a measurement, not an implementation, and the floor is a gate.

## 6. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **The cross-source rule fires on nothing real** | **High** | Criterion 2 is a gate. Seeded data proves it *evaluates*; only unseeded data proves it *fires*. Keep the two readings apart when reporting |
| **Classifier recall is the predicate's weakest link** | **High** | Task 074, floor 80% on ≥50 items. Every other criterion can pass while the claim misses a third of real cases |
| **`notExists` has no in-repo template** | **High** | Task 021 is serial, top model tier, and proves the FetchXML on real data before 030 depends on it |
| Hot-path collision — 56 of 62 active projects declare a hot path | Medium | `/conflict-check` before **every** BFF PR (design §2) |
| Signals never auto-resolve, so the worklist rots | Medium | One evaluator, two triggers (D-12). Tasks 031 + 033 are the mitigation |
| Work surfaced in passing gets lost | **High** | §5.0: every item **fixed or scheduled**, never merely listed. `push-to-github` Step 1.6 enforces it |

## 7. Timeline

**Estimated effort**: 42 tasks, mostly 2–4 hours. Phases 0–1 are small and unblock everything. Phase 3 and
Phase 5 carry the bulk. Phase 8 is independent and can absorb slack at any point.

## 8. References

- [`spec.md`](spec.md) — 44 FRs, the requirement IDs every task cites
- [`design.md`](design.md) rev 11 — §8.0c (D-9..D-12), §8.3 (why evaluation is scheduled *and* event-driven)
- [`notes/schema-draft.md`](notes/schema-draft.md) — the five tables + the creation recipe and its traps
- [`notes/security-roles.md`](notes/security-roles.md) — privilege matrix; §7 second verification pass
- [`notes/reuse-verification-2026-10-02.md`](notes/reuse-verification-2026-10-02.md) — C-1..C-27
- [`notes/daily-briefing-ontology-fit.md`](notes/daily-briefing-ontology-fit.md) — why Briefing items become Work Items
- [`notes/ontology-component-model.md`](notes/ontology-component-model.md) — §3 vocabulary, §3.1 the chain
