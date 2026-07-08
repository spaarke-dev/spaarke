# Worktree delta pre-scan — 2026-07-05

Method: for each of the 24 non-agent worktrees, `git diff --name-only $(merge-base HEAD master)..HEAD`,
counted total + AI-pattern-matching files. Full command + output in session log.

## Headline

**17 of 24 worktrees are fully merged (ahead:0)** — their AI code is already in master and
is covered by the master baseline audit. Only 7 branches carry unmerged commits; of those,
only r7 has a substantive AI delta.

## Per-branch verdicts (unmerged branches only)

| Worktree | Ahead | Files | AI verdict (verified by reading diffs) |
|---|---|---|---|
| `spaarke-wt-spaarke-ai-platform-unification-r7` | 14 | 22 (13 AI) | **The substantive AI delta** — Wave 12.3 summarize fixes + canonical architecture doc. Dedicated Explore agent auditing. |
| `spaarke-wt-fix-daily-briefing-shared-lib` | 1 | 4 | Build-config-only fix (package.json/tsconfig/build script) for `Spaarke.DailyBriefing.Components`. AI-adjacent lib, **no AI-logic change**. |
| `spaarke-wt-spaarke-dataset-grid-framework-r2` | 11 | 106 | Grid framework project. AI touches: `Spaarke.AI.Widgets/DataverseEntityViewWidget.tsx` gains grid `pageSize`/`availableViews` override plumbing (grid config, not AI logic); `Spaarke.Compose.Components` changes are Prettier + simplification. **No AI-logic change.** Side-finding: master contains `Spaarke.Compose.Components/src/orchestrators/executeComposeSummarize.ts` — a distinct client-side summarize orchestrator (inventory item; potential dispatch-helper duplicate of SpaarkeAi's `executeLinearDispatch.ts`). |
| `spaarke-wt-spaarke-daily-update-service-r4` | 2 | 2 | Only adds r7 `design.md`/`spec.md` project docs. **No AI code.** |
| `spaarke-wt-ai-spaarke-ai-workspace-UI-r2` | 1 | 1 | README portfolio pointer. **No AI code.** |
| `spaarke-wt-set-regarding-and-field-mapping-resolver-r1` | 25 | 136 | FieldMappingAdmin Dataverse solution + ribbons + resolver JS. **Non-AI** (deterministic field mapping, no LLM). |
| `spaarke-wt-fix-events-smarttodo-cross-imports` | 1 | 11 | Package-boundary fix for Events/SmartTodo components. **Non-AI.** |

## Fully-merged worktrees (ahead:0 — covered by master baseline)

action-engine-r1, insights-engine-r2, insights-engine-widgets-r1, ci-cd-unit-test-remediation-r1,
customer-provisioning-orchestration-r1, email-communication-solution-r3, record-header-and-notepad-r1,
smart-todo-r4, spaarkeai-compose-r1, spaarkeai-compose-r2, ai-platform-unification-r6,
daily-update-service-r2, daily-update-service-r3, devops-project-tracking-r1,
platform-foundations-r3, redis-cache-remediation-r1, redis-cache-remediation-r2.

**Consequence for the inventory**: the historical AI projects' code lives in master; the
"27 projects of technical debt" question is answered by the master baseline audit, not by
per-worktree archaeology.
