# Task 133 - Designer read-only for repo-deployed system playbooks (D-97 / PB-08)

PR: https://github.com/spaarke-dev/spaarke/pull/1497 (branch fix/designer-readonly-system-playbooks, not merged; dev BFF deploy needs owner approval).

## How a repo-deployed playbook is identified (one place: NodeService.EnsureCanvasSyncAllowedAsync)
Protected if ANY of: sprk_issystemplaybook = true; sprk_playbooktype = 2 (Notification); any node of the playbook has no `__canvasNodeId` in sprk_configjson (node-level marker, written only by the Designer sync, absent from every deploy-script node). Fail closed: unreadable playbook or nodes = refuse.
Live check 2026-10-09 (dev): every playbook that has nodes without `__canvasNodeId` is one of the 11 system/notification playbooks; no user-authored playbook is locked (escalation trigger did not fire). The flag is NULL on New Work Assignments and Matter/Project Activity Summary, covered by type 2 and by the node rule.

## Behaviour
- PUT /api/ai/playbooks/{id}/canvas: 409 `playbook_read_only` (problem details names playbook) before the canvas JSON is persisted; 503 `playbook_canvas_unverifiable` when Dataverse can't be read.
- SyncCanvasToNodesAsync enforces the same check before any write (all callers).
- JIT sync (AnalysisOrchestrationService, also reached from AnalysisExecutionHandler): logs "[PLAYBOOK-EXEC] canvas sync refused for protected playbook" and runs on stored nodes.

## Tests / mutations
NodeServiceCanvasSyncGuardTests (14 with JIT test): zero-write assertions, strict IPlaybookService mock, regression for user-authored playbooks. Mutations killed: system-flag clause, type clause, node clause, fail-closed, JIT catch.

## Known limits / reported
- Filed #1498: NodeEndpoints per-node CRUD can still edit/delete nodes of system playbooks (owner decision; deploy tooling may use them).
- K2: guard checked in endpoint then again in sync; transient failure between leaves canvas JSON saved, nodes unsynced.
- Optional later (owner decision): make the sync preserve non-canvas nodes and map every canvas type.
- Project CLAUDE.md gotcha "Do NOT open and save a system playbook" stays until shipped to every environment (main session updates; sub-agent cannot write .claude/).
