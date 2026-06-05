# Phase D Deploy Report — task 034

> **Date**: 2026-06-03
> **Environment**: `https://spaarkedev1.crm.dynamics.com` (Spaarke Development Environment)
> **Operator**: Ralph Schroeder
> **Project**: spaarke-datagrid-framework-r1

---

## Scope

Phase D deliverables published to DEV in this run:

1. **EventsPage Custom Page** (`sprk_eventspage.html`) — the rewritten 1868 → 161 line thin shell from task 031 + the host filter pipeline from tasks 033a/033b's framework extension.
2. **`sprk_event` `sprk_gridconfiguration` record** (`e15c2b93-a05f-f111-a825-70a8a59455f4`) — verified intact from task 030; no re-deploy needed.
3. **LegalWorkspace Custom Page** (`sprk_corporateworkspace.html`) — the rebuilt bundle containing the migrated SpaarkeAi Calendar workspace widget from task 033b.

---

## EventsPage deploy

```
Command:     scripts\Deploy-EventsPage.ps1 (DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com)
Web resource: sprk_eventspage.html (id: 606fc817-1c02-f111-8407-7ced8d1dc988)
Source:      src/solutions/EventsPage/dist/index.html (built via npm run build)
Bundle size: 1,229 KB (published)
Status:      ✅ Updated + PublishXml succeeded
```

The bundle includes:
- Task 031 thin App.tsx (`<DataGrid configId={EVENT_CONFIG_ID} />`)
- Task 031 framework command handlers (`registerEventHandlers.ts` — BulkUpdateEventStatus etc.)
- Task 031 Calendar pane orchestrator + Xrm helpers
- Task 033a `<DataGrid hostFilters/>` framework extension (transitive via `@spaarke/ui-components`)

---

## sprk_event config record verification

```
GET /api/data/v9.2/sprk_gridconfigurations(e15c2b93-a05f-f111-a825-70a8a59455f4)?$select=sprk_name,sprk_configjson

Name: Event Default
configjson length: 863 bytes
Contains "webResource": true       ← rowOpen.type = "webResource" (load-bearing for record-link bug closure)
Contains "sprk_eventdetailsidepane": true  ← side pane web resource reference intact
```

No re-deploy required — record is intact from task 030.

---

## LegalWorkspace (Calendar widget) deploy

```
Command:     scripts\Deploy-CorporateWorkspace.ps1 (DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com)
Web resource: sprk_corporateworkspace.html (id: 8b7e8863-020d-f111-8342-7ced8d1dc988)
Source:      src/solutions/LegalWorkspace/dist/corporateworkspace.html (built via npm run build)
Bundle size: 2,162 KB (published)
Status:      ✅ Updated + PublishXml succeeded
```

The bundle is the LegalWorkspace Custom Page (registers all sections including Calendar). The Calendar section delegates rendering to `@spaarke/events-components`'s `CalendarWorkspaceWidget` (see `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts`), so the rebuilt widget code from task 033b is now live in this bundle.

**LegalWorkspace prerequisites that surfaced during this run**:
- `node_modules/` was missing — ran `npm install --legacy-peer-deps --no-audit --no-fund` per CLAUDE.md §11 ("Avoid `npm ci` for Vite solutions"). Installed 226 packages in ~3 min.
- `vite` binary unavailable until install completed.

---

## Smoke tests

Both Custom Pages are now patched + published. Per the task 034 POML scope ("smoke only — full UAT is task 035"), no functional verification was performed in this run — that's the explicit boundary between 034 and 035.

Manual smoke targets the operator/UAT phase should hit:

| Surface | Open path | Expected |
|---|---|---|
| EventsPage standalone | MDA sitemap → Events page | Grid renders; +New/Delete/Refresh in command bar; filter chips work; record-link opens IN FRONT of dialog (record-link bug closure) |
| EventsPage embedded (dialog mode) | Open from a Matter form via VisualHost drill-through | Same as above; parent-context filter narrows to Matter's events |
| LegalWorkspace Calendar section | Open Legal Workspace Custom Page → Calendar section | Filter row + calendar strip preserved AS-IS; toolbar GONE; grid is the new DataGrid; day-click filters by date; +New/Delete/Refresh available |

---

## Cycle time

| Stage | Time |
|---|---|
| EventsPage build (existing) | (cached — clean from task 033b verification) |
| EventsPage deploy | ~10s |
| sprk_event verification | ~3s |
| LegalWorkspace `npm install` | ~3 min |
| LegalWorkspace build | ~12s |
| LegalWorkspace deploy | ~15s |
| Total | ~3.5 min |

---

## Next: task 035

The record-link bug closure is the headline UAT target — that's the immediate user-visible motivation for Phase D. Verify in DIALOG MODE (the failing case): from a Matter form, drill into the Event grid via VisualHost, click an event row; the event detail side pane should open IN FRONT of the dialog, not behind.

Calendar widget visual regression UAT: open SpaarkeAi workspace → Calendar section. Compare to pre-migration screenshots (if any captured). Filter row + calendar strip + Apply/Clear should be UNCHANGED. Toolbar is GONE (Q1 sign-off). Grid is the new DataGrid (column header chevron menus, command bar at the top).

If any UAT failure: document in `notes/drafts/035-deviations.md` and decide remediation scope.
