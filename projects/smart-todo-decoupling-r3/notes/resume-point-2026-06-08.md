# R3 Resume Point — 2026-06-08

> **Use this doc to resume R3 work after context compaction or new session.**

## Where we left off

- **PR #373 open** with auto-merge enabled (SQUASH): https://github.com/spaarke-dev/spaarke/pull/373
- Branch `work/smart-todo-decoupling-r3` is 23 commits ahead of master + 0 behind (latest: `8382964b` merge of master into branch).
- **32/46 tasks complete (70%)**. All consumer code is sprk_todo-clean; schema cut for `sprk_event` 4 fields done; `sprk_eventtodo` entity orphan-but-not-deleted; BFF + SmartTodo Code Page deployed to spaarkedev1.
- Anthropic **org monthly usage limit hit on sub-agents** at end of session — may need new billing cycle (or do remaining work inline in main session).

## What's deployed (spaarkedev1)

- **BFF** at `https://spaarke-bff-dev.azurewebsites.net` — commit `4224e474` + `748e5b1d` (msal-browser fix). Includes 2 new Office endpoints (`/by-message-id/{id}` + `/{commId}/linked-todos`). Hash-verify passed; healthz 200; new routes return 401.
- **SmartTodo Code Page** — web resource `f85a1884-962b-f111-88b5-7ced8d1dc988`, 1655 KB bundle, published. Includes `useLaunchContext` hook for Outlook ribbon launch flow.
- **Dataverse schema** in SpaarkeCore solution: `sprk_todo` entity (44 attrs incl. 11 regarding lookups + 4 resolver + 5 sync state fields) + statuscode customized (Open/In Progress/Completed/Dismissed) + `sprk_recordtype_ref` row + 4 `sprk_event.sprk_todo*` fields removed.

## What's NOT deployed yet (deferred)

- **Outlook add-in** (task 072 deploy): code committed, deploy pending (via `/office-addins-deploy` skill).
- **10 parent-form ribbons** (task 040 follow-up): draft XML at `infrastructure/dataverse/ribbon/<Entity>Ribbons/createtodo-button.xml` for Project/Event/Communication/WorkAssignment/Invoice/Budget/Analysis/Organization/Contact/Document. Only Matter has live ribbon. Per-entity unmanaged solution import needed per `ribbon-edit` skill convention.
- **`sprk_eventtodo` entity** (task 005): orphan in spaarkedev1, blocked by 26 `appmodulecomponent` refs that Dataverse won't direct-DELETE. See `notes/task-005-schema-cut.md` for full recovery options (maker portal cleanup vs PAC CLI vs defer). User authorized deferral.

## What's blocked on external action

- **Task 015 (AAD `Tasks.ReadWrite` scope add)** — requires tenant admin. Instructions provided in earlier conversation (Azure CLI + Portal options). Once added: unblocks **task 016** (`GraphClientFactory` scope wire) → unblocks **Phase 7 entire** (tasks 061-066: outbound sync pipeline + loop prevention + provisioner + subscription manager + webhook endpoint + initial backfill). Feature gate `Spaarke:Graph:TodoSync:Enabled` stays `false` until tasks done.

## Remaining task work (14 tasks)

| Task | Status | Blocker / Notes |
|---|---|---|
| 005 | 🔲 partial | Entity delete deferred per user; see notes/task-005-schema-cut.md |
| 015 | 🔲 | User adds AAD scope (instructions provided) |
| 016 | 🔲 | Blocked on 015 |
| 024 | 🔲 | SmartTodo Code Page deploy — already done; just needs ✅ in TASK-INDEX |
| 061 | 🔲 | Phase 7 — blocked on 015 |
| 062 | 🔲 | Phase 7 — blocked on 015 |
| 063 | 🔲 | Phase 7 — blocked on 015 |
| 064 | 🔲 | Phase 7 — blocked on 015 |
| 065 | 🔲 | Phase 7 — blocked on 015 |
| 066 | 🔲 | Phase 7 — blocked on 015 |
| 072 | 🔲 | Outlook add-in deploy — pending office-addins-deploy invocation |
| 082 | 🔲 | Architecture doc supersede `event-to-do-architecture.md` + new `spaarke-todo-architecture.md` |
| 083 | 🔲 | CLAUDE.md §16 pointer table update |
| 085 | 🔲 | Final repo-wide legacy-reference grep sweep |
| 090 | 🔲 | Final wrap-up: code-review + repo-cleanup + lessons-learned + README Complete |

## Next steps when resuming

1. **Verify PR auto-merge happened**: `gh pr view 373 --json state,mergedAt` — if merged, master has all R3 work.
2. **If merged**: switch out of worktree, sync local master, then either:
   - Start a follow-up project for the blocked items (R3.1) OR
   - Pick up the autonomous-safe Phase 9 tasks (082, 083, 085, 090) in this worktree before deleting it
3. **If NOT merged yet** (CI in progress): wait for CI green, then auto-merge fires.
4. **If sub-agent budget restored**: launch tasks 082 + 083 + 085 in parallel (different files, no contention) for Phase 9 cleanup.

## Critical context for resumption

- **Worktree path**: `c:\code_files\spaarke-wt-smart-todo-decoupling-r3\`
- **Worktree lacks `node_modules/` at root** — pre-commit hook can't run; all commits used `--no-verify` (user-authorized). Code quality verified independently each wave (build + tests + lint).
- **Target Dataverse env**: `https://spaarkedev1.crm.dynamics.com/` — but schema is **tenant-portable** via SpaarkeCore solution export/import (mandate per user).
- **Auto-merge method**: SQUASH — all 23 commits collapse to one squash commit on master.
- **Conflict awareness**: 3 file overlaps with PR #369 (R1 multi-container-multi-index, open). After R3 merges, R1 will rebase on master and resolve the 3 overlaps (`workAssignmentService.ts`, `services/index.ts` in shared lib, `appsettings.template.json`).

## Key files / docs for context

- **Spec**: `projects/smart-todo-decoupling-r3/spec.md` (30 FRs, 12 NFRs, MUST + MUST-NOT rules)
- **Design**: `projects/smart-todo-decoupling-r3/design.md`
- **Plan**: `projects/smart-todo-decoupling-r3/plan.md`
- **Task Index**: `projects/smart-todo-decoupling-r3/tasks/TASK-INDEX.md`
- **Audit doc** (task 001 output): `projects/smart-todo-decoupling-r3/notes/eventtodo-reference-audit.md`
- **Task 005 partial state**: `projects/smart-todo-decoupling-r3/notes/task-005-schema-cut.md`
- **Outlook launch contract**: `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md`
- **External-access contract change** (for R1 coord): `projects/smart-todo-decoupling-r3/notes/external-access-contract-change.md`

## Key decisions made (don't re-litigate)

- ADR-030 → ADR-032 (Null-Object Kill-Switch Pattern) — corrected in spec.md + root CLAUDE.md §10
- Entity-set name is `sprk_todos` (NOT `sprk_todoes`) — verified via Dataverse describe in task 040
- `sprk_todo.statuscode` extended to 4 values: 1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed (Graph mapping per FR-24)
- `sprk_userpreference.preferencetype` optionset added `Microsoft To Do Sync = 100000004`
- `CreateWorkAssignmentWizard addTodo` checkbox REMOVED (not refactored to auto-create sprk_todo) — users who want a companion todo use the standalone CreateTodoWizard
- Task 005 entity delete deferred — orphan but not deleted; user authorized
- Tech debt items #1-3 (Outlook end-to-end) fixed; #4 (10 entity ribbons) deferred
- LegalWorkspace's local CreateTodo was discovered DEAD CODE (task 013) — deleted vs refactored

## How to verify the world after compaction

```powershell
# 1. Confirm branch state
git log --oneline -5
git status

# 2. Confirm PR merged
gh pr view 373 --json state,mergedAt

# 3. Confirm BFF deployed
curl https://spaarke-bff-dev.azurewebsites.net/healthz
curl -o /dev/null -w "%{http_code}" https://spaarke-bff-dev.azurewebsites.net/api/office/communications/by-message-id/test
# Expected: 200, 401

# 4. Read task index
cat projects/smart-todo-decoupling-r3/tasks/TASK-INDEX.md
```

---

*This resume point is the authoritative state-snapshot for R3 work after the 2026-06-08 session.*
