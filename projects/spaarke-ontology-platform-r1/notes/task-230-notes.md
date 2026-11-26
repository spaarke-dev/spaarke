# Task 230: repo-deployed system playbooks are read-only in the BFF (#1698, #1498)

Branch `fix/ontology-230-system-playbooks-read-only` from `origin/master` d43fc9e16d, PR to master. Rigor FULL, opus @ high.

## What changed

1. **PR #1497 brought onto master (#1698).** Its three commits (2569d14cd9, 38e5d9b529, b2d1d776b3) were cherry-picked,
   not merged. One conflict: `AnalysisOrchestrationServiceTests.cs`, where master had already removed the skipped
   `ExecutePlaybookAsync_WithToolScopes_ResolvesToolsFromPlaybook` (test diet). Resolution keeps master's removal note
   and adds #1497's `ExecutePlaybookAsync_ProtectedPlaybookCanvasSyncRefused_ContinuesOnStoredNodes`. The rest applied
   cleanly. Behaviour, unchanged from #1497 round 2:
   - `NodeService.EnsureCanvasSyncAllowedAsync`: a playbook is protected when `sprk_issystemplaybook` is true, OR
     `sprk_playbooktype` = 2 (Notification), OR any node has no `__canvasNodeId`. It fails closed: an unreadable record
     or node list counts as Unverifiable.
   - `PUT /api/ai/playbooks/{id}/canvas` checks once before it persists anything. It returns 409 `playbook_read_only` or
     503 `playbook_canvas_unverifiable` (ProblemDetails, `correlationId`), then syncs from the permit's node snapshot.
   - JIT sync (`AnalysisOrchestrationService`) logs the refusal and runs on the stored nodes.
2. **Per-node routes (#1498).** `Api/Ai/NodeEndpoints.cs`: create, update, delete and scopes now call the same guard
   after the existing Write-privilege and owner filters (and, for update/delete/scopes, after the node-belongs check).
   They refuse with the same 409/503 body. The response builder is now shared:
   `PlaybookEndpoints.ReadOnlyPlaybookProblem`, which the canvas handler also uses. Handlers became `internal` and take
   `HttpContext` (for `correlationId`), like #1497's `SaveCanvasLayout`.
3. **Per-node writes no longer lock user playbooks (#1498 second half).** `NodeService.CreateNodeAsync` always stores a
   config with `__canvasNodeId`. If the sent config has a marker, it is stored as sent; otherwise one is added
   (`api-<guid>`) and every other key is kept. `UpdateNodeAsync` keeps the node's stored marker when it replaces the config
   with one that has none (one extra `$select=sprk_configjson` read, only in that case). A config that is not a JSON
   object can't carry the marker, so it is now refused with 400 (`ValidateAsync` on create, a check in the update
   handler) before any read or write. A stamped node left off the canvas is deleted by the next canvas sync of that user
   playbook. That is what happened to such a node before #1497 (the canvas is the source of truth for user-authored
   playbooks).
4. `ProtectedPlaybookCanvasSyncException` wording: the summary covers per-node routes, and the Unverifiable message
   says "the change was refused" (it said "the canvas save was refused").

## Caller search (escalation check, step 1)

The question: does deploy tooling or the Designer use the per-node routes (or the canvas route) on system playbooks?
Searched at d43fc9e16d:

- `scripts/**`: no reference to `/nodes`, `/canvas` or `/scopes` routes. Deploy scripts (`Deploy-Playbook.ps1`,
  `Deploy-NotificationPlaybooks.ps1`, the seeders) write `sprk_playbooknode` through the Dataverse Web API directly.
- `src/client/**` (`*.ts`, `*.tsx`, `*.js`, excluding built bundles) and `src/solutions/**`: no call to
  `/api/ai/playbooks/{id}/nodes…` or `/canvas`. `code-pages/PlaybookBuilder` no longer hosts canvas authoring
  (FR-P4-04, retired). The client-side `playbookNodeSync.ts` that `NodeService` comments mention is gone from the repo.
- `src/dataverse/**` web resources: no `/nodes` call.
- `src/server/**`: the only callers of `CreateNodeAsync` / `UpdateNodeAsync` / `DeleteNodeAsync` /
  `UpdateNodeScopesAsync` are the four `NodeEndpoints` handlers. `NodeService` is the only BFF writer of
  `sprk_playbooknodes`.

Result: no caller. The escalation trigger does not fire, and no schema change is needed.

## Tests

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/NodeServiceCanvasSyncGuardTests.cs`: #1497's 14 tests plus a per-node
  section. The fake Dataverse handler now serves single-node GETs, stores created and patched configs, and records
  write bodies. Cases:
  - Each route on system-flag, notification-type and deploy-shaped-node playbooks returns 409 with zero writes.
  - Flag-only system playbook (all nodes marked): 409.
  - Unreadable playbook: 503 with zero writes.
  - Must-not-fire: each route on a user-authored playbook succeeds, and the guard still passes afterwards.
  - Create stamps a marker and keeps other keys; a sent marker is stored as is.
  - Update keeps the stored marker; update without a config leaves the config alone.
  - A config that is not a JSON object returns 400 with zero writes.
- `tests/integration/contract/Api/Ai/PlaybookRouteAuthorizationContractTests.cs`: the real-route host.
  `ArrangeWriteRoute` now lets the guard pass for the canvas and node routes, because the host's `INodeService` mock is
  Strict. New theory `Write_OnRepoDeployedSystemPlaybook_IsRefused_AndNothingIsWritten`: canvas plus the four node
  routes return 409, or 503 for Unverifiable, through the real filters and handlers; no save, sync or node write is
  called.
- `AnalysisOrchestrationServiceTests`: #1497's JIT test (the refused sync continues on stored nodes).

**Deviation (testing constraint "regression test under `tests/integration/regression/Issue{N}_*`"):** the route-level
regression theory sits in the contract KEEP path, next to the existing write-route matrix whose private host and
arrangement it reuses. Copying that host into a regression file would duplicate it. The scenario is covered at both
the unit level and the route level.

**Fail-before.** Results are under "Verification" below. On master the new handlers' signatures don't exist (compile
failure), so the behaviour was proved by two mutations applied to the finished code and then reverted:
- M1 removes the guard: the check returns a permit unconditionally, the per-node guard calls are removed, and the JIT
  catch is disabled.
- M2 keeps the guard and removes the marker stamping.

## Known limits (K-class)

- K2: there is a check-then-write window. A deploy that flips a playbook to protected between the guard read and a
  per-node write is not caught. The write is one node on a playbook the caller owns; the guard reads fresh on every
  request.
- K4: `NodeService.Refuse` logs "Canvas sync refused" for per-node refusals too. Each endpoint also logs its own
  "Node … refused" line.
- The error code `playbook_canvas_unverifiable` is reused for per-node 503s, so the Designer handles one contract.

## Found, not fixed (D-106: report, don't fix)

- **Two write models for one node set (pre-existing).** On a user-authored playbook that has a stored canvas, a node
  created, or a config edited, through the per-node routes is not on the canvas. The next canvas save, or the JIT sync
  at the next analysis run, therefore deletes it or overwrites it from the canvas. Before #1497 an unmarked per-node
  node was deleted by that sync the same way. With #1497 alone it would have locked the playbook instead. This task
  keeps the pre-#1497 outcome for user playbooks, which is the "do not lock it" half of #1498. No client calls the
  per-node routes today (see "Caller search"), so this is latent. A real fix would pick one source of truth: retire the
  per-node write routes, or make them update the canvas JSON too. That is an owner decision, outside #1698 and #1498.

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/AiSeedChain/AiContentSet.cs` (~309) and its test
  comment say "a playbook run syncs the canvas into node rows". Once this ships, a protected (system or notification)
  playbook's run uses its stored nodes and no longer syncs the canvas. The behaviour still "behaves as dev does" once
  dev runs this BFF, so this is wording only. It is the provisioning project's file.
- Out of scope, per #1698 and the project gotcha: the browser-side canvas Designer still deployed on demo (F-4, D-105)
  writes nodes through the Dataverse Web API, not the BFF, so this guard does not cover it. The project CLAUDE.md
  gotcha ("never open and save a system playbook in the Designer") stays until this BFF is on every environment; the
  main session owns that edit.

## Verification

(filled in below)

rework: none task 230
