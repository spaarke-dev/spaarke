# Task 053 — Deviation: scope-corrected retirement

> **Created**: 2026-06-04
> **Task**: 053 — Retire UniversalDatasetGrid PCF + solution manifest version bump
> **Status**: ✅¹ Completed with corrected scope (the POML's manifest-edit + version-bump steps did not apply to actual repo structure).

---

## Original POML premise

> "Update solution manifest: remove UDG from `Solution.xml`; bump solution version per PCF-DEPLOYMENT-GUIDE.md (4 locations)."
>
> `<relevant-files>` listed: `src/dataverse/solutions/SpaarkeControls/Solution.xml`

Two factual errors in this premise:

1. **There is no `SpaarkeControls` solution.** `src/dataverse/solutions/` contains only `spaarke_containers`, `spaarke_core`, and `spaarke_documents` — none of which reference the UDG PCF.
2. **The 4-location version bump rule from PCF-DEPLOYMENT-GUIDE.md applies to *republishing* a control with a new version**, not to retiring it. We are deleting the source + uninstalling the deployed solution, not shipping a new build.

## What the audit + this task actually established

- The UDG PCF is its own **self-contained solution** named `SpaarkeUniversalDatasetGrid`. Its source, control, manifest, and `Solution/` packaging script all live under `src/client/pcf/UniversalDatasetGrid/`. No central manifest references it.
- The workspace MSBuild project `src/client/pcf/controls.pcfproj` auto-globs all sub-directories (`<None Include="$(MSBuildThisFileDirectory)\**" ... />`), so deleting the UDG directory automatically removes it from the workspace build graph — no edit needed.

## Action taken

1. **Deleted `src/client/pcf/UniversalDatasetGrid/` entirely** via `git rm -r` (64 tracked source files; untracked build artifacts like `node_modules/`, `out/`, `obj/` were already gitignored).
2. **No manifest edits performed** (there is no central solution descriptor to edit; the workspace `.pcfproj` adapts via globbing).
3. **No version bumps performed** (this is retirement, not republish).
4. **Shared library build clean**: `npm run build` in `Spaarke.UI.Components` exited 0 with no errors.

## Surviving comment-only references (not blocking)

9 files outside the deleted directory contain stale comment/docstring references to `UniversalDatasetGrid`. None are imports; none affect compilation.

| File | Line | Type |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts` | 9 | JSDoc comment |
| `src/client/shared/Spaarke.UI.Components/src/types/ConfigurationTypes.ts` | 59 | JSDoc comment |
| `src/client/code-pages/SemanticSearch/src/hooks/useSearchViewDefinitions.ts` | 59 | JSDoc comment |
| `src/client/code-pages/SemanticSearch/src/App.tsx` | 286 | Inline comment |
| `src/client/code-pages/SemanticSearch/src/adapters/searchResultAdapter.ts` | 2 | File-level docstring |
| `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` | 118 | Inline comment (migration was task 033) |
| `src/client/pcf/UniversalQuickCreate/docs/FILE-ACCESS-ENDPOINTS.md` | 278, 602 | Doc reference |
| `src/client/pcf/UniversalQuickCreate/docs/ENHANCEMENT-FILE-URL.md` | 315 | Doc reference |
| `src/client/pcf/EmailProcessingMonitor/control/AuthService.ts` | 17 | JSDoc historical reference |

**Disposition**: left in place. These are historical-context comments documenting design lineage. A separate doc-rot pass (or the task 090 wrap-up) can scrub them; they don't block the retirement.

## Downstream effect

| Task | Effect |
|---|---|
| **054** — Phase F deploy + UAT | The deployment-side action is **uninstalling the deployed `SpaarkeUniversalDatasetGrid` solution from `spaarkedev1.crm.dynamics.com`** via the Power Apps maker portal. This is an operator-side action, not a script. UAT shrinks to: (a) verify the 4 active DataGrid framework consumers still build + redeploy cleanly via `Deploy-AllDataGridConsumers.ps1`, (b) verify SpeAdminApp still renders unchanged. |

---

*Foundation: [050-datasetgrid-consumer-audit.md](./050-datasetgrid-consumer-audit.md).*
