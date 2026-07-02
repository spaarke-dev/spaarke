# Task Index — AI SpaarkeAi Workspace UI R2

> **Purpose**: Registry of all tasks, dependencies, statuses, and parallel-execution groups.
> **Updated by**: `task-execute` when a task starts (🔲 → 🔄) and completes (🔄 → ✅).

## Legend

- 🔲 pending
- 🔄 in-progress
- ✅ completed
- 🚫 blocked
- 🔁 needs retry

## Phase 1 — Framework + widget migration (PR 1)

| Task | Status | Depends on | Est. | Notes |
|---|---|---|---|---|
| [`001-audit-existing-config-records.poml`](001-audit-existing-config-records.poml) | ✅ | — | 1 h | Audit 5 existing `sprk_gridconfiguration` records; document current `rowOpen` state |
| [`002-extend-datagrid-defaultRecordOpen.poml`](002-extend-datagrid-defaultRecordOpen.poml) | ✅ | 001 | 3 h | FR-01/02/03 — extend schema + framework `defaultRecordOpen` for `formId` + Layout 1 unification |
| [`003-verify-widget-migrations.poml`](003-verify-widget-migrations.poml) | ✅ | 002 | 2 h | FR-04–08 verification — all 5 widgets adopt Layout 1 via framework change |
| [`004-phase1-pr-and-merge.poml`](004-phase1-pr-and-merge.poml) | 🔄 | 003 | 1 h | Phase 1 PR — CI green, code-review, merge to master — PR #530 open |

## Phase 2 — Communications widget (PR 2)

| Task | Status | Depends on | Est. | Notes |
|---|---|---|---|---|
| [`010-communications-config-record.poml`](010-communications-config-record.poml) | ✅ | 004 | 1 h | FR-11 — create `sprk_gridconfiguration` record for Communications via `dataverse-create-schema` skill — GUID `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` |
| [`011-communications-section-registration.poml`](011-communications-section-registration.poml) | ✅ | 010 | 1 h | FR-09 — `communications.registration.ts` + `sectionRegistry.ts` update + `sectionMetadataCatalog.ts` |
| [`012-communications-direct-widget.poml`](012-communications-direct-widget.poml) | ✅ | 010 | 1 h | FR-10 — `communications-list` direct widget in `register-workspace-widgets.ts` |
| [`013-phase2-pr-and-merge.poml`](013-phase2-pr-and-merge.poml) | 🔄 | 011, 012 | 1 h | Phase 2 PR — Communications visible as section + direct widget — folded into PR #530 |

## Phase 3 — SmartTodoModal retirement + documentation (PR 3)

| Task | Status | Depends on | Est. | Notes |
|---|---|---|---|---|
| [`020-enumerate-smarttodomodal-callsites.poml`](020-enumerate-smarttodomodal-callsites.poml) | ✅ | 013 | 1 h | FR-12 — final callsite enumeration; document in `notes/smart-todo-modal-callsites.md` — LOW complexity confirmed |
| [`021-migrate-smarttodomodal-callsites.poml`](021-migrate-smarttodomodal-callsites.poml) | ✅ | 020 | 2 h | FR-13 — every callsite converts to `Xrm.Navigation.navigateTo` (Layout 1); LegalWorkspace 80%→85% + fallback removed |
| [`022-delete-smarttodomodal-component.poml`](022-delete-smarttodomodal-component.poml) | ✅ | 021 | 1 h | FR-14 — Modal folder deleted (SmartTodoModal.tsx + buildTodoIframeUrl.ts + tests + barrel); useLaunchContext.ts kept (non-iframe branches); RecordNavigationModalShell intact |
| [`023-documentation-updates.poml`](023-documentation-updates.poml) | ✅ | 022 | 3 h | FR-15..FR-19 — sharpened 5 doc surfaces (MODAL-DECISION-CRITERIA + record-modal-selection pattern + BUILD-A-NEW-WORKSPACE-WIDGET § 6.6 + SPAARKE-DATAGRID § 6.5 + SPAARKEAI-DASHBOARD § 6.5); CHANGELOG entry added |
| [`024-phase3-pr-and-merge.poml`](024-phase3-pr-and-merge.poml) | 🔄 | 023 | 1 h | Phase 3 PR — folded into PR #530; FR-20 (85%×85%) + FR-21 (RichFilePreviewDialog 1280px × 85vh preserved) grep-verified |

## Wrap-up

| Task | Status | Depends on | Est. | Notes |
|---|---|---|---|---|
| [`090-project-wrap-up.poml`](090-project-wrap-up.poml) | ✅ | 024 | 2 h | README status → Complete pending PR #530 merge; `notes/lessons-learned.md`, `notes/test-diet-report.md` (9 MAINTAIN / 0 SCAFFOLDING), `notes/success-criteria-verification.md` (10 of 14 static-verified); `projects/INDEX.md` updated |

## Dependency graph (critical path)

```
001 → 002 → 003 → 004 (PR 1 merges)
                     ↓
                    010 → 011 → 013 (PR 2 merges)
                     ↓
                    010 → 012 → 013 (parallel with 011)
                                    ↓
                                   020 → 021 → 022 → 023 → 024 (PR 3 merges)
                                                              ↓
                                                             090 (wrap-up)
```

**Critical path**: 001 → 002 → 003 → 004 → 010 → 011 → 013 → 020 → 021 → 022 → 023 → 024 → 090 (~19 hours).

## Parallel execution groups

Most tasks are sequential (each PR depends on the previous). Only one parallel opportunity within Phase 2:

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| Phase-2-parallel | 011, 012 | 010 complete | Section registration + direct widget registration are independent; can run concurrently |

All other tasks are serial due to PR-level dependencies.

## Rigor levels (per task-execute Step 0.5)

- **Tasks touching `.ts` / `.tsx` code** (002, 003, 011, 012, 021, 022): **FULL rigor** — code-review + adr-check at Step 9.5
- **Tasks touching only Dataverse or docs** (001, 010, 023): **STANDARD rigor**
- **Tasks touching only PR shepherding** (004, 013, 024, 090): **STANDARD rigor** with test-diet on 090

## Files at risk of conflict (cross-worktree)

**None expected**. Per `projects/INDEX.md` hot-path check, other worktrees touching SpaarkeAi don't overlap with the specific files R2 modifies. If a merge conflict surfaces, coordinate via `/conflict-check` skill.
