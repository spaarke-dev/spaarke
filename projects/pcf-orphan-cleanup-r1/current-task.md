# Current Task — pcf-orphan-cleanup-r1

> Last updated: 2026-06-22 (Wave P1-W1 partial complete; paused for PR review)

## Active Task

**None — paused for PR review.**

## Session 1 Outcomes (2026-06-22)

### ✅ Completed

| Task | Outcome |
|---|---|
| Project scaffolding | PR **#411** open — `chore(pcf-orphan-cleanup-r1): project scaffolding + research artifacts` |
| Task 002 — Source deletion | PR **#412** open — `chore(pcf): retire 3 orphan PCFs (UQC + DrillThroughWorkspace + SpeDocumentViewer)` |

### 🔲 Remaining (in dependency order)

| Task | Status | Blocker |
|---|---|---|
| 001 — Pre-flight backups (spaarkedev1) | NOT started | None — PAC CLI confirmed authenticated to spaarkedev1; ready to execute |
| 003 — Dataverse cleanup spaarkedev1 | Blocked on 001 | Single managed maker-portal session, ~4-6 hours |
| (7-day soak) | — | Calendar gate after 003 |
| 004 — Shared lib `@types/react` peerDep | Blocked on 003 + soak | |
| 005 — VisualHost re-pin React 16 | Blocked on 004 | |
| 006 — Dataverse cleanup spaarkedev2 | Blocked on 005 + soak | |
| 007 — Cleanup-log finalize | Blocked on 006 | |

## Branches in flight

- `chore/pcf-orphan-cleanup-setup` → PR #411 (project scaffolding)
- `chore/pcf-orphan-cleanup-source-delete` → PR #412 (source deletion)

## Next Action

After PR #411 and #412 are reviewed and merged:

1. **Schedule a 4-6 hour focused block** for Task 001 + Task 003 (Dataverse cleanup spaarkedev1).
2. Resume execution by saying one of the trigger phrases:
   - "work on task 001" → preflight backups
   - "continue" → loads TASK-INDEX, picks first 🔲, dispatches `task-execute`

3. Reference materials remain in:
   - `projects/pcf-orphan-cleanup-r1/` (this project)
   - `projects/ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md` (canonical procedure)

## Notes for the next session

- PAC CLI is authenticated to **spaarkedev1** (profile [3] = `ralph.schroeder@spaarke.com` @ `https://spaarkedev1.crm.dynamics.com/`). No re-auth needed.
- Husky pre-commit hook + lint-staged require `npm install` at repo root (already done this session — `node_modules/` exists at root with prettier).
- Setup PR (#411) introduces the `backups-2026-06-22/` folder — empty at session end; Task 001 fills it.
- DocumentUploadWizard Code Page imports services from `@spaarke/ui-components/services/document-upload`. Those services stay in the shared lib. They were created for UQC but the successor Code Page consumes them too.

## Blockers

- (none active — paused by owner choice for PR review)

## Recovery Notes

If context is lost mid-execution next session:

1. Read [`CLAUDE.md`](CLAUDE.md) for project context
2. Read this `current-task.md` for session-1 outcomes + branch state
3. Read [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for what's done / pending
4. Resume via `task-execute` on the next 🔲 task (001 is the natural starting point)
