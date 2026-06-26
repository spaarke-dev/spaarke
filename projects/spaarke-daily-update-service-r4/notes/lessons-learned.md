# Lessons Learned — Spaarke Daily Update Service R4

> **Project**: spaarke-daily-update-service-r4
> **Completed**: 2026-06-26
> **Tasks**: 46/46 (45 implementation + 1 mandatory wrap-up)
> **PRs**: 1 canonical PR (#456) covering combined PR-1 through PR-5 surface
> **Result**: All 20 FRs delivered, all 6 NFRs pass, R4 graduates

This document captures durable lessons for future R5+ projects.

---

## 1. JPS Deployment-as-First-Class-Concern (R4's core thesis)

**The lesson**: When you ship a new `NodeExecutor`, `ActionType` enum value, or playbook node graph in C# / TypeScript, **the feature is silently inert** until corresponding `sprk_analysisaction` + `sprk_analysisplaybook` rows are deployed to the target environment.

R4 was triggered by exactly this defect: `LookupUserMembershipNodeExecutor` (ActionType 52) shipped in `spaarke-platform-foundations-r3` C#, but its `sprk_analysisaction` row was never deployed to spaarkedev1 — so no playbook could dispatch it, and the membership pattern was silently inert across the entire platform.

**Implications for future projects**:

- Code change is **necessary but not sufficient**. JPS data deployment is a sibling concern, not a downstream concern.
- The §10 BFF Hygiene per-task checklist should be expanded to include a "JPS deployment delta" check whenever a task adds/modifies a NodeExecutor enum value, an Action row, or a Playbook row.
- The W0 workstream pattern from R4 (PRs 1+2) — deploy Action rows + playbooks BEFORE the consumer code that calls them — is the canonical sequencing. R5+ projects shipping new AI primitives should follow this pattern.

**Suggested CLAUDE.md addition**: a §10 sub-bullet titled "JPS Data Deployment Obligation" mirroring the test obligation language.

---

## 2. Spaarke Entity Architecture (surfaced + persisted memory)

**The lesson**: Spaarke does NOT use OOB activity entities (`task`, `email`, `appointment`). The custom entities `sprk_event` (with event-type discriminator including "task"), `sprk_communication` (with type discriminator including "email"), and `sprk_workassignment` are canonical.

During PR 2's parallel 3-way audit of the 7 deployed notification playbooks, sub-agents discovered the repo JSON files were authored against the wrong OOB entities (`task`, `email`, `appointment`), but the deployed playbooks correctly targeted Spaarke custom entities. The owner clarified verbatim:

> "we do not use OOB tasks / activities or email — our corresponding entities are Events (with event type = tasks, for tasks; but we track other event types too) and communications (with type = email). I'm surprised this has come up since this has been a core part of the design from the very beginning."

**Implications**:

- When a project encounters a contradiction between spec assumptions and deployed state, the **audit-first-decide-second** pattern is the right one. Don't reconcile from a wrong source.
- The CLAUDE.md `'🚨 2026-06-25 — Spaarke entity architecture'` section was added with binding rules. Project-tier memory at `~/.claude/projects/.../memory/spaarke-entity-architecture.md` persists this across sessions.
- Spec author intent ("repo JSON = canonical") had to be REVISED to "deployed entity choices are canonical; repo JSON files were authored against a wrong OOB assumption" — and this revision was applied via task 015 re-scope. The decision-revision pattern is normal in long projects; document it explicitly in CLAUDE.md.

---

## 3. `NVARCHAR(10)` `sprk_playbookcode` constraint

**The lesson**: The `sprk_playbookcode` column on `sprk_analysisplaybook` is `NVARCHAR(10)`. When R4 attempted to deploy `DAILY-BRIEFING-NARRATE` (19 chars), Dataverse truncated to `BRIEF-NRRT`. The canonical playbook code was preserved in the `description` field + a `canonicalPlaybookCode` JSON key in `sprk_configjson`.

**Owner action recommended**: expand the column to `NVARCHAR(50)` (or similar) so playbook codes can be human-readable. Until then, the 10-char hard cap is a binding constraint on every new playbook code.

**Where documented**: PR 2 task 011 completion notes + R4 risks log.

---

## 4. Path A.5 hybrid dispatch (existing facade + routing service)

**The lesson**: When designing a new "consumer routes to playbook" path (R4 FR-12 / task 030), survey existing facades BEFORE designing a new method. R4's task 030 surveyed `IPlaybookService` (14 CRUD/lookup methods, none execute playbooks) and discovered `IInvokePlaybookAi.InvokePlaybookAsync` already provides the non-document execution facade we needed.

**Result**: Task 031 became a thin dispatch wrapper instead of a new method authoring task. -340 LOC of inline LLM-prompt construction were removed (`BuildNarrateTldrPrompt`, `BuildChannelNarrationPrompt`, etc.), and the new code was just the 7th caller of `IConsumerRoutingService` + `IInvokePlaybookAi` (both registered for 6 prior consumers per chat-routing-redesign-r1).

**Implications**:

- CLAUDE.md §11 (Component Justification — Default to Reuse) directly catches this — Question 1 ("What does this overlap with?") forced the survey that found the existing facade.
- The "Path A.5" naming was useful project-internal shorthand. R5+ projects adopting a similar hybrid pattern should consider giving it a canonical name in the platform.

---

## 5. Sub-agent stream timeouts on long-running widget tasks

**The lesson**: Tasks 040+041 (PR 5 Wave 1) and task 045 (PR 5 sequential) hit stream-idle timeouts during execution. Work was preserved (committed before timeout) and the parent session recovered.

**Pattern observed**: Long-running widget tasks that modify 4+ files with many test changes seem to trigger stream-idle timeouts more often than BFF tasks of similar duration. Hypothesis: TypeScript + Jest compilation cycles take longer than C# + xUnit cycles, and the sub-agent stays "quiet" during compile.

**Implications**:

- For PR 5-class widget tasks (4+ files, 10+ tests), consider splitting into smaller sub-tasks (one file or one test file at a time).
- The CLAUDE.md `task-execute` Step 8.0 Multi-File Work Decomposition protocol exists for this — use it more aggressively for widget work.
- When timeouts happen, the recovery pattern is: read `current-task.md`, inspect `git log` for prior commits, re-invoke `task-execute` with the remaining steps. R4 recovered successfully every time.

---

## 6. Parallel-wave dispatch worked well

**The lesson**: R4 used parallel sub-agent waves at FOUR boundaries:

- **PR 2 Wave C**: 3-way audit of 7 deployed playbooks (tasks 012/013/014)
- **PR 3 Wave D**: 4-way playbook migration (tasks 022/023/024/025)
- **PR 4 Wave 1**: 3-way consumer work (tasks 033/034/032 — different files)
- **PR 5 Wave 2**: 3-way preferences wiring (tasks 040/041 + 042/043/044 sequenced)

All four waves succeeded. The key to choosing parallel candidates correctly was **file-overlap analysis**: tasks that touch DIFFERENT source files can run in parallel; tasks that touch THE SAME file (even different sections of it) must be sequenced.

**Implications**:

- The CLAUDE.md §4 "Parallel Task Execution" guidance is correct and was applied successfully.
- Sub-agent write-boundary respect (`.claude/` is main-session-only) was honored throughout — sub-agents only READ + AUDIT for the audit waves, then the main session applied corrections.

---

## 7. Project process maturity observation

**The lesson**: R4 was the 3rd consecutive R-series project to use a single long-lived PR (#456) accumulating all phased work. The pattern works when:

- The branch is a single worktree owned by one developer
- The phased PRs land on the same branch and update the same PR description
- Each phase commit is conventional-commits format with the task ID

It does NOT work when:

- Multiple developers need to review intermediate phases (PRs would need to be opened per phase)
- A phase needs to be reverted independently (the long-lived PR doesn't support this cleanly)

R4 had exactly one developer (Claude Code + owner), so the long-lived PR pattern worked. For team-developer projects, prefer phase-per-PR.

---

## 8. JPS data deployment to production environments

**Note**: R4 deployed all JPS data to **spaarkedev1** only. Production deployments (`spaarkeprod1`, etc.) are owner's call and not part of R4 task scope. The deployed Dataverse rows from R4 are:

- 4 `sprk_analysisaction` rows (SYS-LOOKUP-MEMBERSHIP, BRIEF-NARRATE-TLDR, BRIEF-NARRATE-CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES)
- 1 `sprk_analysisplaybook` row (DAILY-BRIEFING-NARRATE, code `BRIEF-NRRT` per NVARCHAR(10) constraint)
- 1 `sprk_playbookconsumer` row (consumer-type `daily-briefing-narrate`)
- 7 updated `sprk_analysisplaybook` rows (notification playbooks PB-016 through PB-022)

When R5+ work needs production deployment, replay the deployment scripts (`scripts/deploy-r4-jps.ps1` or equivalent) against the target environment with the production Dataverse connection.

---

## 9. CLAUDE.md project-level memory grew well

R4 maintained two CLAUDE.md files: root + project. The project-level CLAUDE.md captured live decision records as they happened (e.g., "🚨 2026-06-25 — Spaarke entity architecture") and was loaded by every task-execute invocation. This is the right pattern — short-lived corrections that don't deserve permanent root-level CLAUDE.md placement still need durability within the project.

The "🚨" prefix for binding-but-recently-discovered rules made them immediately visible at the top of context loads.

---

## 10. Outstanding items deferred (for owner / R5+)

- **Production JPS deployment**: owner action (see lesson 8)
- **`sprk_playbookcode` NVARCHAR expansion**: owner action (see lesson 3)
- **Email fallback for Contact-only members**: deferred per spec §Out of Scope (an R5 project)
- **Phase 2 membership infrastructure** (junction-table + Service Bus topic): remain feature-gated OFF per spec §Out of Scope
- **AI Search "matter context" knowledge node for `/narrate`**: deferred per spec §Out of Scope
- **Insights Engine integration**: deferred per spec §Out of Scope
