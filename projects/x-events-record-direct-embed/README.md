# Events Record Direct Embed

Adapt the existing `EventsPage.html` web resource to serve as a context-aware, embedded Events tab inside any Dataverse entity form (Matter, Project, Invoice, Work Assignment, etc.).

## Quick Links

| Document | Purpose |
|----------|---------|
| [spec.md](spec.md) | Full specification |
| [plan.md](plan.md) | Implementation plan (8 tasks) |

## What This Does

When `sprk_eventspage.html` is added as a web resource tab on an entity form with `data=mode=embedded&entityName=sprk_matter&recordId={!entityid}`:

1. **Grid auto-filters** to events related to the current record
2. **View selector** shows entity-specific views (e.g., `Matter-All Tasks`, `Matter-Active Events`)
3. **+New Event** pre-fills the parent record as the regarding record
4. **Calendar side pane** works (additive date filtering)
5. **Event detail side pane** works (edit/save events)
6. **Tab-switch cleanup** — side panes close when user navigates to another tab

## Adding to a New Entity

No code changes required:

1. **Form tab**: Add `sprk_eventspage.html` web resource tab to the entity form
   - Data: `mode=embedded&entityName={entity_logical_name}&recordId={!entityid}`
   - Uncheck "Restrict cross-frame scripting"
2. **Views** (optional): Create entity-specific views with prefix naming convention
   - Example: `Invoice-All Events`, `Invoice-All Deadlines`
   - Falls back to system views if none found

## Key Files

| File | Role |
|------|------|
| `src/solutions/EventsPage/src/App.tsx` | Main page — mode detection, context filter, view discovery |
| `src/solutions/EventsPage/src/config/eventConfig.ts` | View config + entity view discovery |
| `src/solutions/EventsPage/src/hooks/useSidePaneLifecycle.ts` | Reusable tab-switch pane cleanup |

## Modes

| Mode | Trigger | Behavior |
|------|---------|----------|
| **system** | No `mode` param | Full system Events page (entity view) |
| **dialog** | `mode=dialog` | Drill-through popup — no side panes |
| **embedded** | `mode=embedded` | Context-aware tab in entity form |
