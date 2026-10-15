# Task 129 - sprk_finalduedate in Dataverse definitions (spaarkedev1)

> Inventory run 2026-10-08, read-only, via Dataverse MCP read_query. Status: APPLIED 2026-10-08 under owner approval D-85 (definitions) and D-86 (import). Live browser checks outstanding.

## Queries used (the "query used to find them")
- `savedquery`: `fetchxml LIKE '%sprk_finalduedate%'` (4 rows); `layoutxml LIKE '%sprk_finalduedate%' AND returnedtypecode = 10544` (same 4).
- `userquery`: `fetchxml LIKE '%sprk_finalduedate%'` -> 0 rows (no personal views to report).
- `sprk_chartdefinition`: LIKE `%finalduedate%` over sprk_fetchxmlquery, sprk_fetchxmlparams, sprk_optionsjson, sprk_groupbyfield, sprk_aggregationfield, sprk_contextfieldname, sprk_drillthroughviews, sprk_onclickrecordfield -> 2 rows.
- `sprk_gridconfiguration`: `sprk_configjson LIKE '%finalduedate%'` -> 0 rows.
- `savedqueryvisualization` (system charts): datadescription / presentationdescription -> 0 rows.
- `sprk_reportingview` has no query columns (nothing to scan).
- Owning solutions: `solutioncomponent` join. Both OR-filter views are in unmanaged SPRKDOCINTELLIGENCE. The two other views and the two chart definitions returned no solution component rows (chart definitions are data rows, not solution components; the two display-only views are not in any solution found by that query).

## Rows that JUDGE or ORDER by sprk_finalduedate (to change)

| # | Definition (id) | Used by | Current | Proposed |
|---|---|---|---|---|
| 1 | savedquery "All Tasks Open 7 Days" `491e5733-fb04-f111-8407-7ced8d1dc988` (SPRKDOCINTELLIGENCE, unmanaged) | The view itself; chart defs "Due Date Card List" `54ccfb01-3805-f111-8406-7c1e525abd8b` and "Due Date Count Card" `4929d1ed-3705-f111-8406-7c1e525abd8b` both use it as sprk_baseviewid (VisualHost) | `<filter type="or"><condition sprk_duedate next-seven-days/><filter type="or"><condition sprk_finalduedate next-seven-days/><condition sprk_finalduedate olderthan-x-days value=1/></filter></filter>` | `<condition attribute="sprk_duedate" operator="next-seven-days"/>` and `<condition attribute="sprk_duedate" operator="olderthan-x-days" value="1"/>` inside one `or` filter. Keep `<attribute sprk_finalduedate/>` and the layout cell (informational column) |
| 2 | savedquery "Matter All Tasks Open 7 Days" `1e26ed14-0512-f111-8342-7ced8d1dc988` (SPRKDOCINTELLIGENCE, unmanaged) | Matter-scoped view of the same list (no chart references it) | identical filter to #1 | identical replacement to #1 |
| 3 | sprk_chartdefinition "TASKS & EVENTS" (Calendar) `154bd4a4-f359-f111-a825-3833c5d9bcab`, sprk_fetchxmlquery | VisualHost calendar | filter `or`: `sprk_duedate next-x-days 5` OR `sprk_finalduedate next-x-days 5`; `<order sprk_finalduedate asc/>` then `<order sprk_duedate asc/>` | filter: `sprk_duedate next-x-days 5` only (drop the OR arm); orders: `sprk_duedate asc` only. Keep `<attribute sprk_finalduedate/>` select |
| 4 | sprk_chartdefinition "Matter Tasks" (Metric Card) `c4feb098-f359-f111-a825-3833c5d9bcab`, sprk_fetchxmlquery | Matter overview "overdue" count | `<condition sprk_finalduedate lt {currentDate}/>` | `<condition sprk_duedate lt {currentDate}/>` |

Meaning changes beyond the swap, for the owner to see:
- #1 and #2 today show a task if EITHER date is in the next 7 days OR the final date is more than 1 day old. After: due date in the next 7 days OR due date more than 1 day old. A task whose due date is overdue by 1 day or less and not in the next 7 days is excluded (same as today for the final-date arm; note olderthan-x-days 1 on sprk_duedate also admits all older due dates, including very old overdue tasks, which the old final-date arm admitted only by final date). A task with a null sprk_duedate no longer appears in these views.
- #3: a task with only a final due date (null sprk_duedate) no longer appears on the calendar.
- #4: rescheduled tasks (due date in the future, final date passed) stop counting as overdue; tasks overdue by due date start counting.

## Rows that only DISPLAY the column (no change; informational per D-63)

| Definition (id) | Why unchanged |
|---|---|
| savedquery "All Tasks Open subgrid" `9268f905-bb02-f111-8407-7ced8d1dc988` | `sprk_finalduedate` is only a select attribute and a layout cell; ordering is already `sprk_duedate` |
| savedquery "All Deadlines" `db70b5a9-ea78-f111-ab0e-7ced8ddc4a05` | same: select + layout cell only; ordered by `sprk_duedate` |

Not in scope and not touched: sprk_playbooknode (tasks 068/120), personal views (none found), grid configurations (none found).

## Defect reports (no parking)
1. `src/client/pcf/VisualHost/Solution/Controls/.../bundle.js` on master is still the 1.4.38 bundle (still contains 3 `sprk_finalduedate` strings) although the manifest, solution.xml and pack.ps1 say 1.4.39 (PR #1413 body already notes this). Packing without a fresh `build:prod` and copy ships the OLD code under a 1.4.39 label. Fix owner: whoever next touches VisualHost packaging; pack.ps1 should fail when bundle.js does not contain the manifest version.
2. Chart "Matter Tasks" `optionsJson` description uses `{upcoming}` but its FetchXML defines only the `overdue` alias (pre-existing, unrelated to D-63; informational).
3. The branch `docs/ontology-platform-design` does NOT contain #1413 (885d0c5f9). A build from this worktree would produce 1.4.38. The build for this task was therefore done in a throwaway worktree of origin/master at `C:\wt129`.

## Build (2026-10-08)
- Worktree `C:\wt129` = origin/master 885d0c5f9. `npm install --legacy-peer-deps` in Spaarke.Auth, SdapClient, UI.Components, Visuals, VisualHost.
- `pwsh scripts/Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/VisualHost` -> `PASS VisualHost: [build] Succeeded` (webpack 5.104.1, 3 size warnings only).
- `out/controls/control/bundle.js`: 783,088 B (764 KiB), contains `1.4.39`; `sprk_finalduedate` occurrences 0 (1.4.38 bundle: 3). Copied into Solution/Controls and packed with pack.ps1: `C:\wt129\src\client\pcf\VisualHost\Solution\bin\VisualHostSolution_v1.4.39.zip` (250,670 B).

## Per-environment steps recorded (spaarkedev1 only, 2026-10-08, as ralph.schroeder@spaarke.com via az token + Web API)

Backups of every "before" body: scratchpad `before-view-<id>.xml`, `before-chart3.xml`, `before-chart4.xml`, `before-chart4-options.json` (full text is the "Current" column above and the FetchXML printed in the task transcript).

### Definition edits (D-85)
| # | Row | Call | Before | After (read back) |
|---|---|---|---|---|
| 1 | View `491e5733-...` All Tasks Open 7 Days | PATCH savedqueries(id) fetchxml | `or( duedate next-seven-days, or( finalduedate next-seven-days, finalduedate olderthan-x-days 1 ) )` | `or( duedate next-seven-days, duedate olderthan-x-days 1 )`; 0 finalduedate conditions; select attribute kept |
| 2 | View `1e26ed14-...` Matter All Tasks Open 7 Days | same | same as 1 | same as 1 |
| 3 | Chart `154bd4a4-...` TASKS & EVENTS | PATCH sprk_chartdefinitions(id) sprk_fetchxmlquery | `or(duedate next-x-days 5, finalduedate next-x-days 5)`; orders `finalduedate asc`, `duedate asc` | `duedate next-x-days 5` in the existing and-filter; order `duedate asc` only; `<attribute sprk_finalduedate/>` kept |
| 4 | Chart `c4feb098-...` Matter Tasks | PATCH sprk_chartdefinitions(id) sprk_fetchxmlquery + sprk_optionsjson | `sprk_finalduedate lt {currentDate}`; cardDescription `{upcoming} upcoming this month` | `sprk_duedate lt {currentDate}`; cardDescription `{overdue} overdue` (alias the FetchXML defines) |

Gotcha: a PATCH to `savedquery` is NOT visible to a plain GET until published (the first read-back after the PATCH still showed the old FetchXML). `RetrieveUnpublished` as a function URL is not available in this endpoint. Chart rows are plain data and read back immediately.

Publish calls for the definitions (exactly one):
`POST /api/data/v9.2/PublishXml` with `<importexportxml><entities><entity>sprk_event</entity></entities></importexportxml>` -> success. Entity-scoped; PublishXml has no per-view element, so this also publishes any other pending sprk_event customization. No PublishAllXml. Chart rows needed no publish. After it both views read back as in the table.

### VisualHost import (D-86)
1. `pac solution import --environment https://spaarkedev1.crm.dynamics.com --path VisualHostSolution_v1.4.39.zip --force-overwrite` (no `--publish-changes`) -> "Solution Imported successfully." Solution VisualHostSolution (unmanaged) now version 1.4.39; its only component is the custom control (type 66) `14c0701e-242e-417a-8999-62694c3cdcac`.
2. Read back immediately after import (no publish): `customcontrols` version still 1.4.38 (modifiedon 2026-08-03); web resource `cc_Spaarke.Visuals.VisualHost/bundle.js` (id 299fd560-6378-443d-97f5-2dd78ebb590e) had been rewritten (modifiedon 19:43:39Z) but its PUBLISHED content was still the old 781,488 B bundle without 1.4.39. So the import alone does not serve 1.4.39.
3. `POST PublishXml` with `<importexportxml><webresources><webresource>{299fd560-6378-443d-97f5-2dd78ebb590e}</webresource><webresource>{272ce1fb-b711-441c-bd43-1dc0ffe58f24}</webresource></webresources></importexportxml>` (the control's bundle.js and styles.css web resources) -> success. Published bundle.js is now 783,088 B and contains 1.4.39.
4. After step 3 the `customcontrol.version` column still reads 1.4.38 (and its manifest attribute 1.4.38). PublishXml has no element for custom controls, so that metadata row did not flip without a publish-all, which was NOT run. The served code (web resource) is 1.4.39; whether the form/runtime reads the control version from the customcontrol row is for the owner's browser check to settle.
