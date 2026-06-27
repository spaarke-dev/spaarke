# Phase F — Deploy + UAT Report

> **Date**: 2026-06-04
> **Target**: `https://spaarkedev1.crm.dynamics.com` (DEV)
> **Phase**: F — Legacy Retirement (tasks 050–054)

---

## Scope

Phase F retires the legacy DataGrid view components and the UDG PCF. Per the upstream deviations (`050-datasetgrid-consumer-audit.md`, `052-deviations.md`, `053-deviations.md`), the original POML scope was significantly revised:

| Original POML claim | Reality after audit | Effect on this deploy |
|---|---|---|
| 5 DatasetGrid view components have unknown consumers | Zero external consumers | Direct delete (task 051) — only barrel exports needed updating |
| DashboardPage binds UDG PCF | DashboardPage uses Fluent v9 `<DataGrid>` directly | No migration needed (task 052 closed as no-op + stale comment scrubbed) |
| UDG lives in `SpaarkeControls` solution; needs 4-location version bump | `SpaarkeControls` solution does not exist; UDG is self-contained in `SpaarkeUniversalDatasetGrid`; retirement ≠ republish | `git rm -r` the directory (task 053); operator uninstalls the Dataverse-side solution |

So Phase F's actual deploy reduces to:

1. **Rebuild + redeploy** the 4 active framework consumers (their Vite bundles include `Spaarke.UI.Components` and must flush the deleted view components).
2. **Sanity-build** SpeAdminApp to confirm the docblock scrub in task 052 didn't break anything.
3. **Operator-side**: uninstall the deployed `SpaarkeUniversalDatasetGrid` solution from Dataverse.

---

## Deploy results

### Framework consumer redeploy via `Deploy-AllDataGridConsumers.ps1`

Command:

```powershell
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
.\scripts\Deploy-AllDataGridConsumers.ps1
```

| Consumer | Built artifact | Size | Web resource | Status |
|---|---|---|---|---|
| EventsPage | `dist/index.html` | 1231 KB | `sprk_eventspage.html` (id `606fc817-1c02-f111-8407-7ced8d1dc988`) | ✅ PATCHed |
| sprk_invoicespage | `dist/index.html` | 1226 KB | `sprk_invoicespage.html` (id `b329ddcf-9e5e-f111-ab0c-7c1e521b425f`) | ✅ PATCHed |
| sprk_kpiassessmentspage | `dist/index.html` | 1226 KB | `sprk_kpiassessmentspage.html` (id `8329ddcf-9e5e-f111-ab0c-7c1e521b425f`) | ✅ PATCHed |
| LegalWorkspace | `dist/corporateworkspace.html` | 2158 KB | `sprk_corporateworkspace` (id `8b7e8863-020d-f111-8342-7ced8d1dc988`) | ✅ PATCHed |

Single atomic `PublishXml` issued at the end of Phase 3. All four consumers became visible to the runtime simultaneously. Exit code 0; no errors.

### SpeAdminApp sanity build

The only Phase F change to SpeAdminApp source was a 2-line docblock removal in `src/solutions/SpeAdminApp/src/components/dashboard/DashboardPage.tsx` (task 052) — scrubbing a stale comment that referenced both a defunct `RecentActivityGrid` sibling and the now-retiring UDG PCF.

Build command: `npm install --legacy-peer-deps --no-audit --no-fund && npm run build`

Result: ✅ Clean build. Exit code 0; Vite reports `✓ built in 12.82s`; final bundle `dist/index.html` is 1889 KB (gzip 489 KB), inlined via `vite-plugin-singlefile`. The Rollup warnings shown during the build (3 `/*#__PURE__*/` comment-position complaints from `@microsoft/applicationinsights-*` packages) are pre-existing — they predate Phase F and are an upstream telemetry-SDK packaging quirk, not a Phase F regression.

### Acceptance criteria

| Criterion | Status | Evidence |
|---|---|---|
| Deploys succeed; zero errors | ✅ | `Deploy-AllDataGridConsumers.ps1` exit 0; all 4 PATCHes + single PublishXml succeeded |
| DashboardPage renders correctly | ✅ (by inference) | Task 052 deviation established DashboardPage was never on UDG; the only source change was a comment removal. No render-affecting code path was touched. |
| Pre/post visual diff zero regression | N/A | Re-scoped per task 052 deviation — there is no "pre-migration UDG render" to compare against. SpeAdminApp's grid surfaces use Fluent v9 primitives directly, unchanged by Phase F. |
| UDG PCF absent from Dataverse solution | ⏸ Operator-pending | The source directory is deleted (task 053). Removing the deployed `SpaarkeUniversalDatasetGrid` solution from the Dataverse environment is a maker-portal action (see operator runbook below). |

---

## Operator action: uninstall `SpaarkeUniversalDatasetGrid` from Dataverse

Phase F's source-side retirement is complete. To finish the retirement on the deployed environment(s), the operator must:

1. Open Power Apps maker portal for the target environment (e.g. https://make.powerapps.com → `spaarkedev1`).
2. Navigate to **Solutions**.
3. Find **`SpaarkeUniversalDatasetGrid`** (last shipped version: 2.3.1).
4. **⋮ → Delete**. Confirm.

Repeat for any non-DEV environment where the solution was previously installed.

**Verification**: After delete, opening any Custom Page or form that *previously* embedded the UDG PCF will surface a missing-control banner. As of the 050 audit, **no surface in the codebase binds UDG** — so no such banner should appear. If one does, it signals an undocumented consumer that should have been caught in the audit; file as a bug against the audit.

---

## Phase F summary

After Phase F, the legacy code surface is gone:

- 7 source files removed from `Spaarke.UI.Components/src/components/DatasetGrid/` (5 view components + composition root + 1 test file) — task 051.
- Barrel `components/index.ts` reduced from 7 DatasetGrid exports to 1 (`ViewSelector` survives).
- DashboardPage docblock cleaned of stale UDG reference — task 052.
- `src/client/pcf/UniversalDatasetGrid/` directory removed (64 tracked source files) — task 053.
- All 4 active DataGrid framework consumers redeployed cleanly to DEV — task 054.

Per the audit, **no consumer code path imports any retired symbol**. The remaining 9 stray comment-only references in unrelated files are docrot (catalogued in `053-deviations.md` table) and slated for the task 090 wrap-up doc-scrub pass.

---

*See also: `050-datasetgrid-consumer-audit.md`, `052-deviations.md`, `053-deviations.md`.*
