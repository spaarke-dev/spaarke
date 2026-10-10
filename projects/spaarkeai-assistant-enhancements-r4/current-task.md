# Current Task State — spaarkeai-assistant-enhancements-r4

> **Last Updated**: 2026-10-08 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Standing directives + deploy gotchas are in the project `CLAUDE.md` → "Standing directives & gotchas".

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No task active. 001–080 ✅; **090 (wrap-up + `/test-diet`) 🔲** |
| **Mode** | Owner UAT in progress — fix-forward on findings as they arrive |
| **Status** | Clean: all work committed, pushed, merged to master; latest fixes deployed to dev |
| **Next Action** | Wait for the owner's next UAT finding and fix forward. When the owner signs off UAT → run task 090 via `task-execute` (`/test-diet` gate). |

### Files Modified This Session (all merged — PR #1430)
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/{SprkChat.tsx,types.ts,hooks/useChatPlaybooks.ts}` — playbook discovery opt-in (`enablePlaybookDiscovery`, default off)
- `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` — new `clearPlaybookId()`
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — "New session" clears the persisted playbook
- Tests: `SprkChat.test.tsx` (+2), `ConversationPane.new-session.test.tsx` (+1), `ConversationPane.file-attach-session-prompt.test.tsx` (mock)

### Critical Context
Worktree = `origin/master` (0 ahead / 0 behind). Code page `sprk_spaarkeai` was last deployed from master + these fixes (2026-10-08). The main repo checkout `C:/code_files/spaarke` has 5 uncommitted changes and is behind origin — **not touched**; owner to commit/stash then `git pull --ff-only`.

---

## Open Items

| Item | State |
|---|---|
| 090 wrap-up (`/test-diet`) | 🔲 awaits owner UAT sign-off |
| D-UAT-01 — `SelectTextProjectable` ignores `sprk_surfaces` | Deferred, tracked: issue #957 (`notes/defer-issues.md`) |
| Orphan Dataverse column `sprk_grounded_tool_allow_list` | Owner to delete in maker portal |
| Test playbooks ("New Playbook" ×5, unnamed) | Owner cleanup (optional) |
| Rename misleading `nda_sample*.pdf` test files | Owner (optional) — they are not NDAs |

## Owner UAT reference
Checklist: `notes/uat-checklist.md`. Deploy/verify runbook: `notes/deploy-verify.md`.
