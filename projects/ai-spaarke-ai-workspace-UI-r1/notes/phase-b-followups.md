# Phase B Follow-ups (deferred from r1)

> **Scope**: Items intentionally deferred from `ai-spaarke-ai-workspace-UI-r1`
> after Batches 1–3 shipped. None of these block the in-session deliverables;
> they enrich the architecture and unblock follow-on operator workflows.

## VisualHost ChartRenderer hoist (deferred from Batch 3 #7)

**Current state**: `MetricsDashboardWidget` renders `number` cards using
Fluent v9 `Text` and stubs `bar` / `donut` visuals with a "Chart in Phase B"
placeholder. The dashboard architecture (config → cards → FetchXML execution →
visual rendering) is fully in place.

**Phase B goal**: replace the stubs with real chart rendering by hoisting the
VisualHost chart primitives from PCF-private into the shared library.

### Hoist candidates

Living in `src/client/pcf/VisualHost/control/components/`:

| Component | Use in dashboard |
|---|---|
| `BarChart.tsx` | `bar` card visual |
| `DonutChart.tsx` | `donut` card visual |
| `MetricCard.tsx` / `MetricCardMatrix.tsx` | richer `number` card (with trend, color, threshold) |
| `ChartRenderer.tsx` | the central dispatch (visual → component) |

Supporting modules also need hoisting if `ChartRenderer` is hoisted as a single
unit: `cardConfigResolver`, `valueFormatters`, `tokenSetColors`, type
definitions (`IChartData`, `DrillInteraction`, etc.).

### Dependency note

`BarChart` + `DonutChart` consume `@fluentui/react-charting` (Fluent v8).
`@spaarke/ui-components` does NOT depend on `react-charting` today.
Hoisting requires either:

- Adding `@fluentui/react-charting` to `@spaarke/ui-components/package.json`, or
- Hoisting only the wrapper components and keeping `@fluentui/react-charting`
  as a peer dep declared by each consumer.

The first option is simpler but increases the shared-lib bundle. The second
option is cleaner but introduces a tiny consumer-side install requirement.
Recommendation: option 1 — `@fluentui/react-charting` is small and the
hoisted components are intended to be reused widely.

### Implementation plan

1. Copy (not move) `BarChart.tsx` and `DonutChart.tsx` into
   `src/client/shared/Spaarke.UI.Components/src/components/charts/`.
2. Add the supporting types + helpers (`IAggregatedDataPoint`,
   `DrillInteraction`, `valueFormatters`, `tokenSetColors`).
3. Add `@fluentui/react-charting` to `@spaarke/ui-components/package.json`.
4. Update barrel exports in `@spaarke/ui-components`.
5. Update `MetricsDashboardWidget` to import the hoisted charts and replace
   `ChartPlaceholderBody` with real `<BarChart />` / `<DonutChart />`.
6. (Optional) Update VisualHost PCF to import the hoisted versions and delete
   its local copies. Verify the PCF still builds + renders identically.
7. Document the shared-lib version bump.

Estimated effort: medium (2-4 hour focused PR), gated on operator approval
to add `@fluentui/react-charting` to the shared lib.

## Add to Report artifact (deferred from Batch 3 #7)

**Current state**: Per-card "Add to Report" checkbox state is held in widget
state; the "Add to Report" button at the top of the widget logs the selection
via `console.info`. No artifact is generated.

**Phase B goal**: decide the artifact shape and wire generation. Three
candidates documented in the original design discussion (2026-06-08):

1. **Code Page (HTML) + `sprk_report` Dataverse entity** — generates a
   shareable URL backed by a Dataverse row. Bigger lift (new entity + Code
   Page template + BFF endpoint) but matches the "shareable report" intent
   most precisely.
2. **Server-rendered PDF** — BFF generates PDF using a server-side rendering
   lib. Simpler but not link-shareable without separate storage.
3. **JSON export** — minimal — write a JSON describing the report; user
   downloads. Trivial; minimal UX value but a stepping stone.

Operator decision required before Phase B starts.

## AI Summary playbook wiring (deferred from Batch 3 #7)

**Current state**: per-card sparkle (✨) button is wired to a stub that logs
the payload that would travel to the playbook engine: `{ cardId, playbookId,
promptHint, recordCount, fetchXml }`. No backend dispatch.

**Phase B goal**: route the AI Summary click through the existing JPS
playbook system. The playbook (referenced by `playbookId` in the dashboard
config) owns the analysis scopes; the widget hands it the FetchXML + record
references + card context. The narrative comes back as a stream and renders
in a dialog/side-panel (TBD).

Likely touches:

- New BFF endpoint or extension of an existing playbook-run endpoint to
  accept `{ playbookId, fetchXml, results, cardContext }` payloads.
- New JPS playbook(s) per dashboard card (operator authors).
- New dialog/side-panel UI for streaming narrative back into the widget.

Operator decision required on whether to author per-card playbooks vs a
single "summarize-dashboard-card" general playbook with scope variation.

## Filter tabs functional wiring (deferred from Batch 3 #7)

**Current state**: filter tabs (By Date Range / By Person / By Area) render
and toggle, but the active tab does NOT yet rewrite each card's FetchXML.
The `useCardData` hook's effect already depends on `filterTabId`, so the
plumbing is in place — only the rewrite logic is missing.

**Phase B goal**: define per-tab filter shapes (e.g. By Date Range adds a
`<condition attribute='createdon' operator='last-x-days' value='30' />` to
each card's FetchXML; By Person joins to current user; By Area groups by
practice area). Implement a FetchXML-rewrite helper that takes the base
FetchXML + active filter tab and returns the augmented FetchXML.

## Dataverse promotion of dashboard configs (optional Phase B)

**Current state**: `metricsDashboardConfigs.ts` holds dashboard configs
in code. Adding a new dashboard requires a PR.

**Phase B goal (only if maker-authored dashboards become a requirement)**:
promote to a `sprk_dashboardconfiguration` Dataverse entity. Same pattern
as `sprk_gridconfiguration`. The widget grows a Dataverse-lookup path
alongside the in-code catalog so both can coexist during migration.

Per operator (2026-06-08): defer unless / until maker authoring is needed.
