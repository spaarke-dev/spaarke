# Round 8 — Dispatch Plan

> **Status**: In progress
> **Created**: 2026-05-22
> **Operator decision**: Option B for system workspaces — Dataverse-seeded `sprk_workspacelayout` records, NOT hard-coded frontend list. The goal is architectural unity: one workspace concept, one rendering pipeline.

## Operator feedback being addressed (Round 8)

1. Remove the Daily Briefing modal that pops on app load.
2. Identify + remove the mysterious `webresourceName=sprk_spaarkeai` popup (likely the same bug as 1).
3. Semantic Search criteria left-aligned (was centered with 480px max-width per task 103).
4. Workspace pane tab overflow: replace horizontal scrollbar with ◀ ▶ arrow buttons.
5. Remove visible vertical scrollbar (keep scroll functional).
6. Rename `Home` tab → `Daily Briefing`; make it a system workspace (not a hard-coded tab).
7. New system workspace: **Smart To Do List** (loads LegalWorkspace's SmartToDo section).
8. New system workspace: **My Work** (LegalWorkspace QuickSummary + new cards for `sprk_communication` and `sprk_invoices`; 2×3 grid).
9. New system workspace: **Documents** (LegalWorkspace Active Documents; 2×10; no scrollbar; `+ add` + expand to modal).
10. Context Tools dropdown: remove the active-tool checkmark (R7 follow-up; we know what's active because it's what's loaded).

## Architectural decision (Q1 — Option B confirmed)

System workspaces are real `sprk_workspacelayout` Dataverse records (`isSystem=true`). They flow through the existing pipeline:
`WorkspacePaneMenu.handleLayoutSelect → widget_load → WorkspaceLayoutWidget → LegalWorkspaceApp(initialWorkspaceId, embedded) → section factories`

No parallel frontend rendering path. No "virtual workspace" concept. The hard-coded `WorkspaceHomeTab.tsx` goes away; its replacement is auto-installing the BFF's flagged default system layout (Daily Briefing).

## Wave structure

### Wave 1 — Polish (3 parallel agents)
- **105** Popup investigation + removal (Daily Briefing modal + `sprk_spaarkeai` self-popup; likely the same bug)
- **106** Context pane: Semantic Search left-align + drop active checkmark
- **107** Workspace pane: hide vertical scrollbar + tab overflow with ◀ ▶ arrows

### Wave 2 — Frontend infrastructure (1 agent serial after Wave 1)
- **108** Remove hard-coded `WorkspaceHomeTab`; replace auto-install-Home with auto-install-default-system-layout. Frontend must handle isSystem default discovery. No Dataverse changes yet — this is pure prep.

### Wave 3 — Section + script work (3 parallel agents after Wave 2)
- **109** LegalWorkspace `QuickSummary` section: add `sprk_communication` + `sprk_invoices` cards (click → entity modal). Verify BFF endpoints; if missing, add per CLAUDE.md §10 with Placement Justification.
- **110** LegalWorkspace `Documents` (Active Documents) section: hide scrollbars, ensure `+` add-document + expand-to-modal affordances work. Verify smartToDo + dailyBriefing section factories embed cleanly in SpaarkeAi.
- **111** Build `scripts/Deploy-SystemWorkspaceLayouts.ps1` deploy script + JSON template definitions for the 4 system layouts (Daily Briefing, Smart To Do List, My Work, Documents). Idempotent (check-then-create). Document creation path (direct Dataverse Web API vs BFF POST).

### Wave 4 — Seed + deploy + smoke (serial)
- **112** Run seed script against dev Dataverse. Deploy SpaarkeAi. Smoke each system layout via the embed pipeline. Verify FR-25 LegalWorkspace standalone is unchanged.

## Constraints carrying forward

- **CLAUDE.md §10 (BFF Hygiene)**: any new BFF endpoint requires Placement Justification. Prefer reusing existing endpoints.
- **ADR-021** Fluent v9 tokens only (no hex/rgba).
- **ADR-022** React 19.
- **ADR-028** Auth via `useAiSession() → authenticatedFetch`.
- **FR-25** Standalone LegalWorkspace bundle byte-identical or near-identical.
- **Sub-agent write boundary** (root CLAUDE.md §3): sub-agents cannot write to `.claude/` paths.

## Pre-existing follow-ups (NOT addressed in Round 8 unless they block)

- `.gitignore` entries for `deploy/api-publish/*.dll` tracked artifacts + `src/client/shared/Spaarke.AI.Outputs/src/**/*.{js,d.ts,*.map}` stray tsc output.
- `@spaarke/ai-widgets` tsc cross-rootDir build error (Vite production builds succeed).
- Telemetry rename `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE → TELEMETRY_HISTORY_LOAD_FAILURE`.
- BFF `WorkspaceLayoutDto` missing `modifiedOn` (Manage pane shows fallback today).
- BFF `PUT /api/workspace/layouts/{id}` is full-overwrite, not PATCH (no ETag concurrency).
