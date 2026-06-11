# TASK-INDEX — Insights Engine Widgets r1

> **Source**: [`plan.md`](../plan.md)
> **Generated**: 2026-06-10 via `/project-pipeline` constrained run
> **Total tasks**: 43 (Phase 0: 5, Phase 1: 5, Phase 2: 6, Phase 3: 8, Phase 4: 5, Phase 5: 4, Phase 6: 8, Phase 7: 2)
> **Status legend**: 🔲 not-started · 🔄 in-progress · ✅ complete · 🚫 blocked · ⏭️ skipped/deferred

---

## Phase 0 — Foundation (5 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 001 | FR-03 pre-flight ratification — DR-001 component reuse | foundation, investigation, ui, decision-record | true | 🔲 |
| 002 | R5 sparkle-wiring baseline grep (Assumption 2/11) | foundation, investigation, dataverse, r5-archaeology | true | 🔲 |
| 003 | `sprk_aitopicregistry` entity design + ADR check | foundation, dataverse, schema-design | true | 🔲 |
| 004 | adr-check spec.md sweep (verify NFR-09 compliance) | foundation, adr-check, quality-gate | true | 🔲 |
| 005 | Build + publish-size baseline (NFR-01 verification) | foundation, build, bff-publish-size | true | 🔲 |

## Phase 1 — Dataverse schema (5 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 010 | Author `sprk_aitopicregistry` entity definition (9 fields) | dataverse, schema, entity-design | true | 🔲 |
| 011 | Deploy entity to dev Dataverse via dataverse-create-schema | dataverse, deploy, schema | true | 🔲 |
| 012 | Generate model-driven app form (SME-editable) | dataverse, form, mda | true | 🔲 |
| 013 | Seed `matter-health` / `single` registry row | dataverse, seed-data | true | 🔲 |
| 014 | Schema validation via MCP describe + Web API query | dataverse, validation, mcp | true | 🔲 |

## Phase 2 — Playbook authoring + deployment (6 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 020 | Author Matter Health synthesis prompt via jps-action-create | jps, action-row, prompt | true | 🔲 |
| 021 | Author `matter-health-single` playbook JSON (no `@v1` suffix) | jps, playbook, json | true | 🔲 |
| 022 | UpdateRecord node config — write JSON envelope to Matter | jps, playbook, persistence | true | 🔲 |
| 023 | Deploy playbook via Deploy-Playbook.ps1 + config Guid | jps, deploy, dataverse | true | 🔲 |
| 024 | End-to-end smoke test via `/api/insights/ask` | jps, smoke-test, bff-api | true | 🔲 |
| 025 | Document envelope schema for downstream consumers | docs, schema, downstream | true | 🔲 |

## Phase 3 — UI component `InsightSummaryCard` (8 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 030 | Component scaffold in `@spaarke/ai-widgets` (props per FR-01) | ui, fluent-ui, scaffold | true | 🔲 |
| 031 | State machine (5+ states; compose Popover + Dialog) | ui, fluent-ui, state-machine, portal-gotcha | true | 🔲 |
| 032 | Topic registry mount check (FR-05 no orphan sparkles) | ui, dataverse, registry-check | true | 🔲 |
| 033 | Citation rendering (type-discriminated FR-07) | ui, citation, render | true | 🔲 |
| 034 | Manual refresh button (FR-20 blocking-style + spinner) | ui, refresh, ux | true | 🔲 |
| 035 | Storybook stories — all states + dark mode | ui, storybook, sc-01 | true | 🔲 |
| 036 | Accessibility audit (WCAG 2.1 AA per NFR-04) | ui, a11y, audit | true | 🔲 |
| 037 | ADR-021 dark mode verification (semantic tokens) | ui, fluent-ui, dark-mode, adr-021 | true | 🔲 |

## Phase 4 — Matter form integration (5 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 040 | Author Matter form OnLoad handler (net-new) | form, javascript, xrm | true | 🔲 |
| 041 | Fire-and-forget invocation (FR-18 staleness check) | form, javascript, async | true | 🔲 |
| 042 | Host `InsightSummaryCard` on Matter Health card | form, ui-integration | true | 🔲 |
| 043 | Solution package (FormXml + web resource); deploy | dataverse, deploy, solution | true | 🔲 |
| 044 | End-to-end form test (TTI unblocked NFR-03) | form, e2e-test, ui-test | true | 🔲 |

## Phase 5 — Telemetry + cache TTL plumbing (4 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 050 | `InsightWidgetsTelemetry.cs` — meter `Sprk.Bff.Api.InsightWidgets` | telemetry, bff, dotnet | true | 🔲 |
| 051 | Emit `widget.insightcard.invoked` from invocation path | telemetry, event-emission | true | 🔲 |
| 052 | Per-topic TTL plumbing (extend `IInsightsPlaybookExecutionCache`) | bff, cache, dotnet | true | 🔲 |
| 053 | Concurrency dedup verification (FR-22) | bff, concurrency, idempotency | true | 🔲 |

## Phase 6 — UAT + documentation (8 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 060 | UAT scenario authoring (3 personas) | uat, planning, docs | true | 🔲 |
| 061 | End-to-end UAT (SC-05 + SC-06 cache hit) | uat, e2e-test, sc-05 | true | 🔲 |
| 062 | Decline rendering UAT (SC-07) | uat, decline, sc-07 | true | 🔲 |
| 063 | Kill-switch UAT — 503 verification (SC-08) | uat, kill-switch, sc-08, adr-018 | true | 🔲 |
| 064 | Degraded-mode UAT (SC-14 empty index) | uat, degraded, sc-14 | true | 🔲 |
| 065 | Background pre-warm UAT (SC-10) | uat, pre-warm, sc-10 | true | 🔲 |
| 066 | Telemetry verification — App Insights query (SC-11) | uat, telemetry, sc-11 | true | 🔲 |
| 067 | Author `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` tutorial | docs, tutorial, sc-13 | true | 🔲 |

## Phase 7 — Deploy + project wrap (2 tasks)

| ID | Title | Tags | Parallel-safe | Status |
|---|---|---|---|---|
| 080 | Production-ready deploy (BFF + Dataverse solution) | deploy, bff-deploy, dataverse-deploy | true | 🔲 |
| 090 | Project wrap-up (lessons-learned, README → Complete) | wrap-up, lessons-learned | true | 🔲 |

---

## Parallel Execution Groups

| Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **Wave 0** | 001, 002, 003, 004, 005 | Project init ✅ | All Phase 0 tasks are read-only investigations / design notes — fully parallel |
| **Wave 1A** | 010, 020 | Wave 0 complete | Schema design + prompt authoring (different artifacts) |
| **Wave 1B** | 011, 012, 021, 022, 023, 030, 031, 050 | Wave 1A complete | Schema deploy + playbook deploy + UI scaffold + telemetry meter |
| **Wave 2** | 013, 024, 032, 033, 051, 052, 053, 040 | Wave 1B complete | Mid-stage parallel work |
| **Wave 3** | 014, 025, 034, 035, 036, 037, 041, 042 | Wave 2 complete | Polish + integration |
| **Wave 4** | 043, 044, 060 | Wave 3 complete | Solution package + UAT planning |
| **Wave 5** | 061, 062, 063, 064, 065, 066 | Wave 4 complete | UAT scenarios in parallel (read-only) |
| **Wave 6** | 067 | Wave 5 complete | Tutorial doc (same engineer per Q-U7) |
| **Wave 7** | 080 | Wave 6 complete | Production deploy |
| **Wave 8** | 090 | Wave 7 complete | Wrap-up |

**Max concurrency**: 6 agents per wave (root CLAUDE.md §5 Step 5). Wave 1B has 8 tasks — split into two sub-waves if needed.

**Sequential dependencies** (within-wave order matters):

- 020 must complete before 021 (action row → playbook reference)
- 010 → 011 → 013 (entity → deploy → seed)
- 030 → 031 → 032 (component scaffold → state machine → registry check)
- 040 → 041 → 042 → 043 (handler → invocation → wiring → solution)

---

## Critical Path

001 → 010 → 011 → 013 → 020 → 021 → 023 → 024 → 040 → 042 → 043 → 061 → 067 → 080 → 090

(Phase 0 → schema deploy → playbook deploy → form wiring → end-to-end UAT → tutorial → production deploy → wrap-up)

Estimated critical path: ~3 weeks. With Wave parallelism, total project ~4–5 weeks.

---

## Notes

- **All `.claude/` touches are `parallel-safe: false`** per root CLAUDE.md §3 (sub-agent write boundary). r1 does NOT modify `.claude/` files — no tasks need this flag set false on that basis. All tasks currently `parallel-safe: true`.
- **FR-08 (feedback) and SC-12 are DEFERRED** to r2+ per Q-U3. NO task is generated for feedback storage.
- **No `@v1`/`@vN` suffix** in playbook names, action codes, or anywhere else per Q-U1 owner ban.
- **Task 001 ratifies** the FR-03 pre-flight finding already applied at plan time. If the finding is wrong, Task 001 will surface it and may require plan revision.

---

*Generated 2026-06-10. Updated by `task-execute` as tasks complete (status column).*
