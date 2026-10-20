# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-19
> **Phase**: Pipeline in progress (`/project-pipeline`, user approved "y"). Task: none yet. Status: planning.

## State

- Pre-flight passed 2026-10-19. Master merged into the branch (0 behind), pushed; main repo master synced; BFF
  build 0 errors.
- Step 0.5: 15 work branches hold unmerged commits (ebilling-r1 340, UAC-r2 111, …). Warning only.
- Step 1 + 1.7: spec valid. ADR-008 path A tension accepted (concrete rationale).
- Step 1.5: open-PR overlaps:
  - UAC-r2 #2110 (`OwnedChildWrite.cs`, `ChildRecordEndpoints.cs`, `CommunicationService.cs`, `RefileWriter` guard,
    write-path doc);
  - #2078 (route-auth guards);
  - #2104 (`CreateProjectWizard` closure files);
  - #1501 (`CreateAnalysisWizardWidget`, `ChildRecordEndpoints`);
  - #1911 (write-path doc).

  All recorded in CLAUDE.md §4.
- Step 2 done:
  - README.md (rewritten), plan.md, CLAUDE.md and notes/decisions.md created;
  - `projects/INDEX.md` row added;
  - hot-path BFF overlaps with UAC-r2, ontology-r1, SPA-r3, word-add-in-r1 and compose (warning).
- Step 3 in progress:
  - tasks/001 (exemplar) and tasks/TASK-INDEX.md (43 tasks) written;
  - 4 sub-agents are generating the other 42 POMLs from the scratchpad manifest
    (`.../scratchpad/task-manifest.md`).

## Exact next action

1. When the 4 generator agents finish: run `pwsh scripts/Validate-TaskPoml.ps1 projects/field-mapping-server-write-path-r1/tasks`;
   fix FAILs; spot-check 013, 021, 052.
2. Step 4: commit + push on the existing branch `work/field-mapping-server-write-path-r1` (no new feature/ branch).
3. Step 4.2: README has the Portfolio pointer (#1164) → `/devops-project-sync`; `gh label create
   field-mapping-server-write-path-r1 --force`; `python scripts/ai-cost/backfill-tasks-planned.py
   field-mapping-server-write-path-r1 --apply`; check that the Epic parent and board fields are set.
4. Step 4.5: ask for the Target Date.
5. Stop before Step 5: ask the user to start 001 + 006 + 010. W2 code waits on 010 (UAC-r2 sign-off).
