# Task 033 — BLOCKED (S6 ESCALATION)

> **Date**: 2026-06-22
> **Author**: Wave 2-B (gated by main session before sub-agent dispatch)
> **Verdict**: 🚫 **BLOCKED** — awaiting owner decision

---

## Why

Two prerequisites for the "Send to Index" ribbon button are missing in the worktree. Both require owner direction before this task can execute autonomously.

### Blocker 1 — BFF `POST /api/ai/playbooks/{id}/send-to-index` endpoint does not exist

Grep audit of `src/server/api/Sprk.Bff.Api/` for `send-to-index` / `SendToIndex` / `sendToIndex` returned 4 matches, **none of which are the playbook send-to-index endpoint**:

| File | Purpose |
|---|---|
| `Services/Ai/PostUploadIndexingEnqueuer.cs` | Post-upload document indexing (not playbooks) |
| `Services/Ai/NullFileIndexingService.cs` | Null-object pattern for file indexing |
| `Infrastructure/DI/AnalysisServicesModule.cs` | DI registration (no endpoint definition) |
| `Api/Ai/RagEndpoints.cs` | RAG search endpoints (not send-to-index) |

The POML acknowledges this risk: *"If yes, document URL + auth shape. If no, document the gap (separate task may be needed)."* and *"If endpoint absent in step 1, escalate to user — adding a new BFF endpoint expands this task's scope (bff-extensions.md placement decision required)."*

Adding a new BFF endpoint triggers CLAUDE.md §10 BFF Hygiene rules:
- Placement Justification per `bff-extensions.md`
- PublicContracts facade decision
- Test obligation (RB-T028-03/04/05/06)
- Publish-size verification
- Null-Object kill-switch design (ADR-032)

This is non-trivial scope; out-of-band for autonomous Wave 2-B.

### Blocker 2 — `src/dataverse/web-resources/` directory does not exist

The POML references this directory for the JavaScript web resource output. It is absent from the worktree. Spaarke's Power Apps web-resource convention may live elsewhere (likely under `src/solutions/SpaarkeAi/` or similar), but the path is not authoritative without owner clarification.

Power Apps ribbon work also typically requires `pac` CLI authenticated session for solution import/export — that auth setup is unclear in autonomous context.

---

## Owner-decision menu

When the owner is ready to address task 033, options are:

| Option | Description | Approx. effort |
|---|---|---|
| A | **Defer**: shelve task 033 for the post-MVP window; rely on Power Apps maker manual operation for now | 0 |
| B | **Decompose**: split into 033a (stand up BFF `/send-to-index` endpoint per bff-extensions.md) + 033b (Power Apps ribbon JS web resource) | 6h + 3h |
| C | **Re-scope**: replace ribbon button with a server-side batch job that re-indexes on demand via a Dataverse trigger (no UX) | 4h |
| D | **Drop**: rely on task 034 nightly drift job + manual `pac` CLI for one-off re-index needs | 0 |

Recommended: **A or D** for MVP path. The ribbon UX is a polish item; drift detection (task 034) + on-demand admin via `pac` CLI cover the operational need until UX work is scheduled.

---

## Impact on Phase 2 wave sequencing

- Task 034 (drift job, FULL, Wave 2-C) depends on 032 + 033. With 033 blocked, **034 can still execute** because its work is independent (background job comparing `sprk_indexhash` vs recomputed) — it doesn't require the UX button.
- Task 035 (admin view, MINIMAL, Wave 2-C) depends on 032 + 033. **35 can still execute** — admin view is a Power Apps view with filter; doesn't require the button.
- Task 036 (validation gate, STANDARD, Wave 2-C) depends on 032 + 033. **036 partially gates** because it expects the server-side validation that the button would call. The validation gate itself can be implemented as a guard on whatever entry point lands later.
- Task 039 (deploy Phase 2, Wave 2-E) depends on 038 (benchmark). Once 032/034/035/036/037/038 land, 039 can deploy whatever Phase 2 work is ready; task 033 can deploy in a later wave.

**Decision for autonomous mode**: Continue Wave 2-B with only task 032. Mark task 033 ⏭️ BLOCKED. Update task 034/035/036 dependencies to remove the 033 prerequisite (they don't truly need it).

---

## Branch state

- Last commit: `20f4e3000` (Wave 2-A task 031)
- BFF build: 0 errors, 16 warnings (unchanged baseline)
- DEV Dataverse: `sprk_jps_matching_metadata` field live; ready for task 032 consumption

---

## Related artifacts

- POML: `tasks/033-power-apps-send-to-index-button.poml`
- Block ref: `tasks/TASK-INDEX.md` task 033 row
- CLAUDE.md §6 (Human Escalation Triggers), §10 (BFF Hygiene)
