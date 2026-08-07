# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-06 (context-handoff — P1 code complete through 016; next = 018)
> **Recovery**: read Quick Recovery below, then `design.md` §12 + this file's "Key integration seams". Resume with `/project-continue` or "work on task 018".

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | none active — **P1 nearly done (010–017 ✅ except 018/019)** |
| **Status** | not-started (016 ✅; ready for 018) |
| **Next Action** | **018** — remove inert `ExternalCallerAuthorizationFilter` + transitional `/api/v1/collab` group (+ WorkforceCaller* endpoints) once no client calls it; BFF deletion, O/x FULL; deps 015,016 ✅. Then **019** — deploy P1 (SWA + Teams + BFF; deps 012,013,014,016,017 ✅). |
| **Pre-conditions** | `dotnet build src/server/api/Sprk.Bff.Api/` green ✅; `npm run build` (external-spa) green ✅; `/conflict-check` before the 018 BFF PR. **BFF publish baseline now 48.29 MB compressed** (was 46.91; +1.38 at 016 — likely dependency/restore drift, under all §10 thresholds). |

### Task 016 — COMPLETE (2026-08-06)
Outside Counsel widgets (CIAM plane) on the shell. **Reused existing `<DataGrid configId>` + `BffDataverseClient`** (both unmodified — zero hand-rolled grids, §11). New: `services/gridDataverseClient.ts`, `widgets/{GridWidgetBody,Projects,Matters,WorkAssignments,Documents,Invoices}Widget.tsx`; registry lazyLoaders now real; BFF `AddExternalModule` ×5 + `TryGetRecordId` EntityReference case; 5 `sprk_gridconfiguration` records authored. Lookup labels via flat FetchXML formatted values (no `<link-entity>` — respects 015's guard). **D-016-1 (§6.5 Path A)**: Matters Tier-2 predicate intentionally always-empty/fail-closed (no `sprk_matter`→project lookup + no Contact→Org affiliation resolver) → R1 "coming soon" empty state (R1 parity, no over-exposure). Verify: tsc/npm build green, dotnet build 0-err, 9803 tests pass, publish 48.29 MB, no CVE. Notes: `notes/task-016-deviations.md`.

## P1 status (workspace-shell foundation)
| Task | Status | Notes |
|------|--------|-------|
| 010 ADR-028 A3 | ✅ | dual-plane module-host platform + principal-agnostic endpoints ratified (concise-only) |
| 011 shell scaffold | ✅ | PortalWorkspaceShell + TabStrip + QuickStartPane + AssistantPane(placeholder) + useWorkspaceTabs |
| 012 widget registry | ✅ | me-client + widgetRegistry (11 defs) + WidgetLibraryModal(FormModal) + entitlement-honest choke point; entitlements mocked pending **022** |
| 013 dual-plane auth bootstrap | ✅ | realm chooser (ChoiceModal) + StandaloneBootstrap in main.tsx; **§6.5 Path C** — did NOT touch @spaarke/auth (A3 forbids); CIAM sessionStorage + Teams NAA byte-for-byte preserved |
| 014 Teams packaging | ✅ | inherited from teams-app-r1; branding-only manifest bump; **ISS-001/#744** high-contrast theme gap deferred |
| 015 FR-22 framework generalization | ✅ | additive ExternalModuleRegistry + ExternalModuleDataEndpoints; shipped CallerPrincipalResolver seam unchanged; code-review caught+fixed a Critical link-entity over-read; build 0-err, **9803 tests pass**, publish **46.91 MB** (+0.01), no CVE |
| 017 Power Pages cleanup | ✅ | dead vite proxy/plugin/config removed; escalation not fired (grep-verified dead); left `format:'iife'` (follow-up) |
| 016 Outside Counsel widgets | 🔲 | **NEXT** (deps 012,015 ✅) |
| 018 cleanup inert filter + /api/v1/collab | 🔲 | deps 015,016 |
| 019 deploy P1 | 🔲 | deps 012,013,014,016,017 |

## Commits (ALL pushed to origin/work/spaarke-SPA-external-access-platform-r2)
- `9646d10a2` P0 outcome + task re-decomposition
- `037b1fba8` #010 ADR-028 A3
- `c94068c05` #011 workspace-shell scaffold
- `ca5aa53d3` #012/#014/#017 Group B
- `8b7bda37c` #013/#015 dual-plane auth bootstrap + FR-22 framework generalization
- `2bdc461da` #016 Outside Counsel widgets ← HEAD
- Working tree clean. No open PR (worktree workflow — `/merge-to-master` when ready).

## Key integration seams for 016
- Client widget: register a widget def in `src/registry/widgetRegistry.ts`; its body consumes the BFF via the **BffDataverseClient** read group `/api/v1/external/api/dataverse/*` (015) — Tier-2 scoped, app-only, un-forked (set `bffBaseUrl={host}/api/v1/external`). Do NOT re-host the Xrm-bound `pages/OutsideCounselDashboard.tsx` — build new widgets on BffDataverseClient (design.md §12 / §11).
- BFF: add each Outside-Counsel module (matters/work-assignments/documents/invoices) via `AddExternalModule(descriptor)` in `ExternalAccessModule.cs` — one registration each, Tier-2 predicate over `CallerPrincipal`; no route/filter/handler changes (the A3 seam). External fetch forbids `<link-entity>` joins (015 D-015-1) — use formatted values for lookup labels, or add a per-module link allow-list (named future seam).

## Deferrals / issues
- **ISS-001 / GH #744** — Teams high-contrast theme → plain dark (shared cascade 2-state); ADR-021 gap; cross-cutting shared-lib fix.
- 015 D-015-1 (no external `<link-entity>` joins), D-015-3 (fetch happy-path needs live ServiceClient — unit+contract covered).
- 017 `format:'iife'` bundle-format change deferred; `tsconfig.node.json` 8 pre-existing @types/node errors (predate).
- Tier-1 module-entitlement routability deny + real `/me` = **P2 (022)** (A3 defers it; Tier-2 is fail-closed today).

## Notes
Per-task detail: `notes/task-01{1,2,3,4,5,7}-deviations.md`, `notes/defer-issues.md`. Group B + 013/015 were run as parallel sub-agents (sonnet for 012/014/017, opus for 013/015); each ran its own Step 9.5 gates; main session ran authoritative wave builds + aggregated TASK-INDEX/status.
