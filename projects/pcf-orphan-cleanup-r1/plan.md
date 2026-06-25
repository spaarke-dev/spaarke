# PCF Orphan Cleanup — Plan

> **Project**: pcf-orphan-cleanup-r1
> **Plan version**: 1.0 (2026-06-22)

## Phased delivery — 14-day plan

```
P1: Days 1-3  — Source delete PR + pre-flight backups (Tasks 001, 002 in parallel)
P2: Day 4     — Dataverse cleanup spaarkedev1 (Task 003, blocking — single session)
P3: Days 4-10 — 7-day soak (no work — monitor for regressions)
P4: Day 11    — Shared lib peerDep PR (Task 004)
P5: Day 12    — VisualHost re-pin PR + deploy + smoke test (Task 005)
P6: Day 13    — Dataverse cleanup spaarkedev2 replay (Task 006)
P7: Day 14    — Cleanup-log finalize + inventory refresh (Task 007)
```

## Parallel execution groups

| Group | Tasks | Why parallel-safe |
|---|---|---|
| **P1-W1** | 001 + 002 | Disjoint write paths: 001 writes `backups-2026-06-22/`; 002 writes `src/client/pcf/{UQC,DTW,SDV}/` (deletions). No shared files. |
| (sequential) | 003 | Blocking — Dataverse mutation session, must run alone. Depends on 001 + 002 complete. |
| (soak) | — | 7-day calendar wait. No task active. |
| (sequential) | 004 | Depends on 003 complete (cleanup must be done before peerDep changes hit the build matrix). |
| (sequential) | 005 | Depends on 004 (PR-D2 needs PR-D1's peerDep range to be in place for type resolution to work). |
| (sequential) | 006 | Depends on 005 complete + 7-day soak complete from 003. spaarkedev2 mutations. |
| (sequential) | 007 | Depends on 006 complete (final log writes cover both environments). |

## Task dependency graph

```
001 (pre-flight)         002 (source PR)
       \                    /
        +------+   +-------+
               \  /
                v
              003 (DV cleanup spaarkedev1)
                |
                | [7-day soak]
                v
              004 (shared lib peerDep PR)
                |
                v
              005 (VisualHost re-pin PR)
                |
                v
              006 (DV cleanup spaarkedev2)
                |
                v
              007 (cleanup log + inventory refresh)
```

## Discovered resources (from chat-routing-redesign-r1 + ai-procedure-quality-r1 inventory work)

- **Inventory data**: [`../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md) — 25 PCF tracked; 11 confirmed orphans
- **Operational procedure**: [`../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md) — 9-section step-by-step
- **Bundle-size baseline**: [`../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md`](../ai-procedure-quality-r1/notes/inventory/pcf-bundle-sizes.md) — 2026-05-14 reference (needs refresh in Task 007)
- **Test-rot audit**: [`../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md) — complementary view (test-rot, not deployment)
- **Compiled research**: [`notes/research-findings-2026-06-22.md`](notes/research-findings-2026-06-22.md) — this project's chronicle of how the scope was established

## Risk-driven sequencing rationale

Tasks 001 + 002 run in parallel because they're the lowest-risk highest-value work — backups protect everything else, and source deletion is fully reversible via `git revert`.

Task 003 is the single highest-blast-radius task. Runs alone in a focused 4-6 hour block. Followed by mandatory soak.

Tasks 004 + 005 are low-blast-radius (PRs that affect build matrix, not production data). Run sequentially because PR-D2's success depends on PR-D1's peerDep range.

Task 006 is a replay of 003 in a different environment. Same shape, lower risk (we've already done it once successfully and waited a week).

Task 007 is purely documentation — final state capture.

## What would change the plan

- **§1.2 check finds unexpected references for any control** → reduce scope by removing that control from the FR-02/FR-03 deletion list; document deviation in `notes/`.
- **Smoke test fails post-deletion** → halt; trigger resurrection (§3.5 in procedure doc); revisit the §1.2 check that missed the dependency.
- **Soak surfaces a regression** → halt spaarkedev2 replay; investigate root cause; resume only after fix lands.
- **VisualHost re-verify finds NEW React 18 API usage** → escalate; do not merge PR-D2 until either replaced with React-16-compatible code OR re-scoped.

## Out of scope explicitly excluded

See [spec.md §4](spec.md#4-explicitly-out-of-scope). Notable: PlaybookBuilderHost, Code Page / Vite SPA cleanup, shared-lib React-18 API removal beyond FR-04 audit, production deployment.
