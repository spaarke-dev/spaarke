# Task Index — Spaarke Daily Update Service R4

> **Project**: spaarke-daily-update-service-r4
> **Last Updated**: 2026-06-25 (scaffolded via `/project-pipeline`)
> **Total Tasks**: 46 (45 implementation + 1 mandatory wrap-up)
> **Estimated Effort**: ~65 engineering hours across 5 phased PRs

---

## Status Legend

- 🔲 Not started
- 🟡 In progress
- ✅ Completed
- 🔄 Needs retry
- ⏸ Blocked
- ❌ Cancelled

---

## Task Registry

### PR 1 — W0 JPS Action rows + EntityNameValidator (Tasks 001–009)

| ID | Title | Status | Dependencies | Blocks | Parallel | Rigor |
|----|-------|--------|--------------|--------|----------|-------|
| 001 | Project setup + investigate sprk_playbookconsumer dispatch path | ✅ | none | 002, 030 | — | STANDARD |
| 002 | Add ExecutorActionType 141 = EntityNameValidator to INodeExecutor enum | ✅ | 001 | 003, 007 | Group A | FULL |
| 003 | Author EntityNameValidatorNodeExecutor.cs + xUnit tests | ✅ | 002 | 007 | Group A | FULL |
| 004 | Author EntityNameValidatorForm.tsx PlaybookBuilder property panel | ✅ | 002 | 007 | Group A | FULL |
| 005 | Deploy SYS-LOOKUP-MEMBERSHIP sprk_analysisaction row (ActionType 52) | ✅ | 001 | 011 | Group B | STANDARD |
| 006 | Deploy BRIEF-NARRATE-TLDR + BRIEF-NARRATE-CHANNEL action rows | ✅ | 001 | 011 | Group B | STANDARD |
| 007 | Deploy BRIEF-VALIDATE-ENTITY-NAMES action row (ActionType 141) | ✅ | 002, 003, 004 | 011 | — | STANDARD |
| 008 | Run jps-scope-refresh + PR 1 smoke test | ✅ | 005, 006, 007 | 009 | — | STANDARD |
| 009 | PR 1 wrap — commit + conflict-check + open PR | ✅ | 008 | 010 | — | STANDARD |

### PR 2 — W0 JPS Narrate playbook + reconcile 7 (Tasks 010–018)

| ID | Title | Status | Dependencies | Blocks | Parallel | Rigor |
|----|-------|--------|--------------|--------|----------|-------|
| 010 | Author DAILY-BRIEFING-NARRATE playbook sprk_configjson node graph | ✅ | 009 | 011 | — | FULL |
| 011 | Deploy + validate DAILY-BRIEFING-NARRATE playbook to spaarkedev1 | ✅ | 005, 006, 007, 010 | 017 | — | STANDARD |
| 012 | Audit deployed sprk_configjson for PB-016, PB-018, PB-019 | ✅ | 011 | 015 | Group C | STANDARD |
| 013 | Audit PB-020, PB-021 (need W1 membership migration) | ✅ | 011 | 015 | Group C | STANDARD |
| 014 | Audit PB-017, PB-022 stub playbooks | ✅ | 011 | 015 | Group C | STANDARD |
| 015 | Re-scoped: Correct repo entities + document deployed canonical state | ✅ | 012, 013, 014 | 016 | — | STANDARD |
| 016 | Run jps-scope-refresh after PR 2 deployments | ✅ | 015 | 017 | — | MINIMAL |
| 017 | Smoke test BFF wrapper dispatch against DAILY-BRIEFING-NARRATE | ✅ | 011, 016 | 018 | — | STANDARD |
| 018 | PR 2 wrap — commit + conflict-check + open PR | ✅ | 017 | 020 | — | STANDARD |

### PR 3 — W1 Producer customData + stubs + membership (Tasks 020–029)

| ID | Title | Status | Dependencies | Blocks | Parallel | Rigor |
|----|-------|--------|--------------|--------|----------|-------|
| 020 | Enrich CreateNotificationNodeExecutor.BuildNotificationEntity (viaMatter/regardingName/source) | ✅ | 018 | 021, 022, 023, 024, 025, 026, 028 | — | FULL |
| 021 | Ensure sprk_category column dual-write (audit + fix if missing) | ✅ | 020 | 022, 023, 024, 025, 028 | — | FULL |
| 022 | Migrate notification-tasks-overdue.json to membership-scope union FetchXml | ✅ | 020, 021 | 026 | Group D | STANDARD |
| 023 | Migrate notification-tasks-due-soon.json to membership-scope | ✅ | 020, 021 | 026 | Group D | STANDARD |
| 024 | Implement notification-matter-activity playbook (stub → full) | ✅ | 020, 021 | 026 | Group D | STANDARD |
| 025 | Implement notification-work-assignments playbook (stub → full) | ✅ | 020, 021 | 026 | Group D | STANDARD |
| 026 | Standardize enriched customData across all 7 notification playbooks | 🔲 | 022, 023, 024, 025 | 028 | — | STANDARD |
| 027 | Add structured member_skipped warning logging for Contact-only members | ✅ | 020 | 028 | — | FULL |
| 028 | Add customData schema-conformance xUnit fixture | 🔲 | 020, 021, 026, 027 | 029 | — | STANDARD |
| 029 | PR 3 wrap — code-review + adr-check + publish-size + CVE + open PR | 🔲 | 028 | 030 | — | STANDARD |

### PR 4 — W2 Consumer /narrate dispatch + cache + fallback (Tasks 030–036)

| ID | Title | Status | Dependencies | Blocks | Parallel | Rigor |
|----|-------|--------|--------------|--------|----------|-------|
| 030 | Evaluate sprk_playbookconsumer dispatch path; document decision (AC-12c) | 🔲 | 001, 029 | 031 | — | STANDARD |
| 031 | Replace DailyBriefingEndpoints.HandleNarrate body with playbook dispatch wrapper | 🔲 | 011, 030 | 032 | — | FULL |
| 032 | Verify /narrate response shape backward compat (widget parser unchanged) | 🔲 | 031 | 035 | — | STANDARD |
| 033 | Remove hasFetchedRef from useBriefingNarration.ts (FR-15) | 🔲 | none | 035 | — | FULL |
| 034 | Implement ActivityNotesSection empty-narrative fallback (FR-16) | 🔲 | none | 035 | — | FULL |
| 035 | Update xUnit + Jest tests for /narrate + useBriefingNarration + ActivityNotesSection | 🔲 | 032, 033, 034 | 036 | — | STANDARD |
| 036 | PR 4 wrap — quality gates + conflict-check + open PR | 🔲 | 035 | 040 | — | STANDARD |

### PR 5 — W2 Consumer UX + preferences + link (Tasks 040–049)

| ID | Title | Status | Dependencies | Blocks | Parallel | Rigor |
|----|-------|--------|--------------|--------|----------|-------|
| 040 | Wire timeWindow preference to fetchNotifications createdon filter (FR-17a) | 🔲 | 036 | 049 | Group E1 | FULL |
| 041 | Wire dueWithinDays preference to fetchNotifications filter (FR-17b) | 🔲 | 036 | 049 | Group E1 | FULL |
| 042 | Wire disabledChannels server-side $filter on sprk_category (FR-17c) | 🔲 | 036, 040, 041 | 049 | Group E2 | FULL |
| 043 | Wire autoPopup preference to workspace launcher hook (FR-17d) | 🔲 | 036 | 049 | Group E2 | FULL |
| 044 | Remove minConfidence end-to-end + grep verify zero references (FR-17e) | 🔲 | 036 | 049 | Group E2 | FULL |
| 045 | Implement NarrativeBullet three-dot overflow menu (FR-18) | 🔲 | 036 | 046, 049 | — | FULL |
| 046 | Wire overflow menu callbacks in ActivityNotesSection + DailyBriefingApp | 🔲 | 045 | 049 | — | FULL |
| 047 | Link click → Xrm.Navigation.navigateTo modal + 403 fallback toast (FR-19) | 🔲 | 045 | 049 | — | FULL |
| 048 | TL;DR ↔ Activities count reconciliation smoke test (FR-20) | 🔲 | 042, 045 | 049 | — | STANDARD |
| 049 | PR 5 wrap — code-review + adr-check + ui-test + accessibility + open PR | 🔲 | 042, 043, 044, 046, 047, 048 | 090 | — | STANDARD |

### Wrap-up (Task 090)

| ID | Title | Status | Dependencies | Blocks | Parallel | Rigor |
|----|-------|--------|--------------|--------|----------|-------|
| 090 | Project wrap-up — final quality gates, merge-to-master, lessons-learned, portfolio archive | 🔲 | 049 | none | — | STANDARD |

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once their prerequisites complete. The pipeline executes parallel groups concurrently via the task-execute skill (one agent per task). Max concurrency = 6 agents per wave. Build verification runs between waves per CLAUDE.md / task-execute Step 8.0.

### Wave Plan

| Wave | Tasks | Prereq | Files Touched | Safe to Parallelize | Notes |
|------|-------|--------|---------------|---------------------|-------|
| **W1.0** | 001 | — | research/decision notes | n/a — single task | Foundation; dispatch investigation only |
| **W1.A** | 002, 003, 004 | 001 ✅ | INodeExecutor.cs (002) / EntityNameValidatorNodeExecutor.cs + tests (003) / EntityNameValidatorForm.tsx (004) | ✅ Yes | Group A — 3 independent files |
| **W1.B** | 005, 006 | 001 ✅ | 3 independent Dataverse Action rows (no shared file) | ✅ Yes | Group B — Action-row deploys; 007 deferred (depends on 002+003+004) |
| **W1.C** | 007 | 002, 003, 004 ✅ | 1 Dataverse Action row referencing EntityNameValidator | n/a — single task | Sequential after W1.A |
| **W1.D** | 008 | 005, 006, 007 ✅ | scope-refresh + smoke | n/a — single task | Aggregation point for W0 PR 1 |
| **W1.E** | 009 | 008 ✅ | PR open + conflict-check | n/a — single task | PR 1 wrap |
| **W2.0** | 010 | 009 ✅ | repo JSON for new playbook | n/a — single task | Sequential anchor for PR 2 |
| **W2.1** | 011 | 005–007, 010 ✅ | 1 Dataverse playbook row | n/a — single task | Deploy + validate |
| **W2.C** | 012, 013, 014 | 011 ✅ | 3 independent audit reports (different files in notes/audit/) | ✅ Yes | Group C — 3 parallel audits |
| **W2.D** | 015 | 012, 013, 014 ✅ | divergent playbook redeploy | n/a — single task | Consolidation |
| **W2.E** | 016 | 015 ✅ | scope-refresh | n/a — single task | Catalog refresh |
| **W2.F** | 017 | 011, 016 ✅ | smoke test artifact | n/a — single task | Wrapper smoke |
| **W2.G** | 018 | 017 ✅ | PR open | n/a — single task | PR 2 wrap |
| **W3.0** | 020 | 018 ✅ | CreateNotificationNodeExecutor.cs | n/a — single task | Foundation for PR 3 |
| **W3.1** | 021 | 020 ✅ | CreateNotificationNodeExecutor.cs (different concern) | n/a — single task | Sequential after 020 (same file) |
| **W3.D** | 022, 023, 024, 025 | 020, 021 ✅ | 4 independent playbook JSON files | ✅ Yes | Group D — 4 parallel migrations |
| **W3.E** | 027 | 020 ✅ | LookupUserMembershipNodeExecutor.cs / MembershipResolverService | ✅ Yes (parallel with W3.D) | Different file from 022–025 |
| **W3.F** | 026 | 022, 023, 024, 025 ✅ | 7 playbook JSON files (consolidation) | n/a — single task | Standardization sweep |
| **W3.G** | 028 | 020, 021, 026, 027 ✅ | new test file | n/a — single task | Schema-conformance fixture |
| **W3.H** | 029 | 028 ✅ | PR open | n/a — single task | PR 3 wrap |
| **W4.0** | 030 | 001 ✅, 029 ✅ | dispatch-path decision | n/a — single task | FR-12 investigation finalization |
| **W4.1** | 031 | 011 ✅, 030 ✅ | DailyBriefingEndpoints.cs HandleNarrate | n/a — single task | Wrapper rewrite |
| **W4.2** | 032 | 031 ✅ | integration test | n/a — single task | Backward compat verification |
| **W4.A** | 033, 034 | 036 prereq removed (depends on 031) | useBriefingNarration.ts (033) / ActivityNotesSection.tsx (034) | ✅ Yes | 2 independent widget files; can run concurrently with W4.2 |
| **W4.G** | 035 | 032, 033, 034 ✅ | test files for /narrate + widget | n/a — single task | Test updates |
| **W4.H** | 036 | 035 ✅ | PR open | n/a — single task | PR 4 wrap |
| **W5.E1** | 040, 041 | 036 ✅ | notificationService.ts (both modify; small overlap → run sequentially within the file but parallel agents OK if scope is non-overlapping functions) | ⚠️ Cautious yes | Sub-wave 1; both touch `fetchNotifications` query — run as 2-agent wave but coordinate file edits |
| **W5.E2** | 042, 043, 044 | 036, 040, 041 ✅ | notificationService.ts (042) / DailyBriefingApp.tsx + workspace launcher (043) / PreferencesDropdown.tsx + types/notifications.ts (044) | ✅ Yes | Sub-wave 2; mostly different files |
| **W5.F** | 045 | 036 ✅ | NarrativeBullet.tsx | n/a — single task | Three-dot menu shell; blocks 046 |
| **W5.G** | 046, 047 | 045 ✅ | ActivityNotesSection.tsx + DailyBriefingApp.tsx (046) / NarrativeBullet.tsx handler + DailyBriefingApp.tsx Toaster (047) | ⚠️ Cautious yes | 046 + 047 both touch DailyBriefingApp.tsx — run as 2-agent wave but coordinate |
| **W5.H** | 048 | 042, 045 ✅ | smoke test | n/a — single task | Count reconciliation |
| **W5.I** | 049 | 042, 043, 044, 046, 047, 048 ✅ | PR open | n/a — single task | PR 5 wrap |
| **W6** | 090 | 049 ✅ | wrap-up sweep | n/a — single task | Project closeout (mandatory) |

### How to Execute Parallel Groups

1. Check all prerequisites are complete (✅ in Status)
2. Invoke `task-execute` skill in ONE message with MULTIPLE Skill tool calls (one per task in the group)
3. Each task-execute runs its task independently with full context loading
4. Wait for ALL to complete; verify build (`dotnet build` for .cs changes, `npm run build` for .ts/.tsx changes)
5. Update this TASK-INDEX.md: change task status from 🔲 to ✅
6. Proceed to next group

**Failure isolation**: If one agent fails, others continue. Mark failed task 🔄 and decide whether to retry sequentially or report and stop.

---

## Dependency Graph (Critical Path)

```
001 (foundation + dispatch investigation)
  ├─→ 002 → 003 + 004 ──┐
  │      └─→ 007 ───────┤
  └─→ 005 + 006 ────────┤
                        ↓
                       008 → 009 (PR 1 wrap)
                              ↓
                            010 → 011 → 012 + 013 + 014 → 015 → 016 → 017 → 018 (PR 2 wrap)
                                                                              ↓
                                                                            020 → 021
                                                                                   ├─→ 022 + 023 + 024 + 025 → 026 ─┐
                                                                                   │                                │
                                                                                   └─→ 027 ──────────────────────────┤
                                                                                                                    ↓
                                                                                                                   028 → 029 (PR 3 wrap)
                                                                                                                          ↓
001 ─────────────────────────────────────────────────────────────────────────────────────────────────────────→ 030 → 031 → 032 → 035 → 036 (PR 4 wrap)
                                                                                                                033 ──────┤
                                                                                                                034 ──────┤
                                                                                                                              ↓
                                                                                                                            040 + 041 → 042 + 043 + 044 ─┐
                                                                                                                            045 → 046 + 047               │
                                                                                                                                  └─→ 048 ─────────────────┤
                                                                                                                                                          ↓
                                                                                                                                                        049 (PR 5 wrap)
                                                                                                                                                          ↓
                                                                                                                                                        090 (project wrap-up)
```

**Longest path** (estimated effort sum): 001 → 002 → 003 → 007 → 008 → 009 → 010 → 011 → 014 → 015 → 016 → 017 → 018 → 020 → 021 → 022 → 026 → 028 → 029 → 030 → 031 → 032 → 035 → 036 → 045 → 046 → 048 → 049 → 090 ≈ 65 engineering hours (matches spec estimate).

---

## Rigor Level Distribution

- **FULL** (code-review + adr-check at Step 9.5): 18 tasks (002, 003, 004, 010, 020, 021, 027, 031, 033, 034, 040, 041, 042, 043, 044, 045, 046, 047)
- **STANDARD**: 26 tasks (001, 005, 006, 007, 008, 009, 011, 012, 013, 014, 015, 017, 018, 022, 023, 024, 025, 026, 028, 029, 030, 032, 035, 036, 048, 049, 090)
- **MINIMAL**: 1 task (016 — pure scope-refresh skill invocation)

---

## High-Risk Items

- **R3 PR #451 file overlap** (11 files in `Spaarke.DailyBriefing.Components/`) — affects tasks 033, 034, 040–048. See [`notes/risks.md`](../notes/risks.md) R1. Each PR 4 / PR 5 wrap task (036, 049) MUST run `/conflict-check` before merge.
- **`sprk_playbookconsumer` dispatch unknown** — task 030 makes the decision; task 031 implements; AC-12c records rationale. See `notes/risks.md` R3.
- **W0 deployment ordering** — PR 2 MUST NOT merge before PR 1 propagates (spec MUST rule). Task 011 explicitly verifies action rows exist before deploying playbook that references them.

---

## Next Action

▶ Run `/task-execute projects/spaarke-daily-update-service-r4/tasks/001-project-setup-and-dispatch-investigation.poml` to start PR 1.

---

*Generated by `/task-create` via `/project-pipeline`. Total 46 tasks; estimated 65 engineering hours across 5 phased PRs + 1 wrap-up.*
