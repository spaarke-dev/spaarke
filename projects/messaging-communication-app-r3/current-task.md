# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-20 18:30
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 004 — FR-17 participant naming in `ThreadResolver` + BFF rename endpoint |
| **Step** | not-started (Wave 4) |
| **Status** | not-started |
| **Next Action** | `work on task 004` — serial, **opus**, edits shared `ThreadResolver.cs` + `CommunicationEndpoints.cs` (extends unwired `ReDeriveThreadNameAsync`; NO plugin; edit-preserve via `sprk_nameisautoderived`). Deps 001 ✅. `/conflict-check` before the BFF PR. |

### Files Modified This Session (Wave 1 — completed)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationThreadReadServiceTests.cs` - Extended (2 characterization tests) - task 001
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceEmailSendThreadTests.cs` - Created (2 email-send baseline tests) - task 001
- `docs/data-model/sprk_communication.md` - Modified (Message=100000004 + 6 R1 columns) - task 006
- `projects/messaging-communication-app-r3/notes/task-001-notes.md`, `task-006-notes.md` - baseline + verification trails

### Critical Context
Wave 1 (001 characterize + 006 doc-drift) complete + gates passed 2026-07-20. **001 baseline pins pre-change behavior that FR-16/17/18/19 will intentionally flip** — some of those tests are DESIGNED to break when 002–005 land (that's the characterization intent, confirmed by mutation-testing). Phase 1 backend (002–005) edits shared `Services/Communication/` and is serial (`parallel-safe:false`); `/conflict-check` before each BFF PR. Pre-existing HIGH CVE `System.Security.Cryptography.Xml 8.0.3` noted (NOT introduced by R3).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 003 |
| **Task File** | `tasks/003-list-all-threads-endpoint.poml` |
| **Title** | FR-16 `GET /communications/threads` + `ListThreadsAsync` + seam tests |
| **Phase** | 1 (backend read/thread spine) |
| **Status** | implemented — build/test/publish/CVE green (see `notes/task-003-notes.md`); pending PR + `/conflict-check` |
| **Started** | 2026-07-20 |

### Approach (decided)
- Impersonated `sprk_communicationthreads` query, NO regarding filter (record-less inclusion via impersonation).
- `$select` = id, name, threadtype, createdon; `$orderby=createdon desc` (deterministic, stable paging).
- Name search: `contains(sprk_name,'<escaped>')` — single-quote doubled (OData injection safe).
- Paging: keyset/cursor on `createdon` (Dataverse Web API has no `$skip`; seam drops `@odata.nextLink`).
  Opaque base64 `pageToken` = last row `createdon`; next-page filter `createdon lt <cursor>` (non-overlapping).
- No new DI dependency (service already scoped); NO membership seam (retired union stays retired).

---

## Progress

### Completed Steps

*No steps completed yet*

### Current Step

*No active task*

### Files Modified (All Task)

*No files modified yet*

### Decisions Made

*No decisions recorded yet*

---

## Next Action

**Next Step**: Execute task 001 (Phase 1 — backend read/thread spine)

**Pre-conditions**:
- Tasks generated in `tasks/` (pipeline Step 3)
- R2 participant junction confirmed applied in target env

**Key Context**:
- Refer to `spec.md` FR-16/17/18/19 for the backend increment
- ADR-038 seam tests are DoD; no membership-union (NFR-01)

**Expected Output**:
- Phase 1 backend endpoints + enriched DTO + seam tests

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-20 18:30
- Focus: Project initialization (artifacts generated)

### Key Learnings

- Notification spine (`communication-arrived`, FR-22) is NOT yet in master — keep FR-22 late.

### Handoff Notes

*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: messaging-communication-app-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-045, ADR-046, ADR-024, ADR-026, ADR-028, ADR-038, ADR-021, ADR-006

### Knowledge Files Loaded
- `.claude/constraints/bff-extensions.md`, `docs/standards/CHAT-ATTACHMENT-POLICY.md`

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
