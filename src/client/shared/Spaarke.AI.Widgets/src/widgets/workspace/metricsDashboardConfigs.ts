/**
 * metricsDashboardConfigs.ts — In-code dashboard configurations.
 *
 * ai-spaarke-ai-workspace-UI-r1 #7 (2026-06-08):
 *   Per operator (2026-06-08): start with code-based configs to avoid the
 *   ceremony of a `sprk_dashboardconfiguration` Dataverse entity for the MVP.
 *   If we ever need maker-authored dashboards, promote this catalog into
 *   Dataverse using the same shape pattern as `sprk_gridconfiguration`.
 *
 * Each dashboard maps cards to FetchXML queries + visual types + drill-through
 * targets + playbook hooks. `<MetricsDashboardWidget data={{ dashboardId }} />`
 * looks up the config by id and renders accordingly.
 *
 * Adding a dashboard:
 *   1. Append an entry to `METRICS_DASHBOARDS` below.
 *   2. Register a Direct widget in `register-workspace-widgets.ts` (or wrap an
 *      existing widget-type via `createMetricsDashboardFactory(dashboardId)`).
 *   3. Optionally add a Dashboard section shim in `LegalWorkspace/src/sections/`.
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Visual type for a dashboard card. */
export type MetricsCardVisual = 'number' | 'bar' | 'donut';

/** Filter-tab kinds. The active tab applies to all cards on the dashboard. */
export type MetricsFilterKind = 'date-range' | 'person' | 'area' | 'custom';

export interface MetricsFilterTab {
  /** Stable identifier — used in state. */
  id: string;
  /** Label shown in the tab list. */
  label: string;
  /** Filter kind — drives optional default filter wiring. */
  kind: MetricsFilterKind;
}

export interface MetricsCardDrillThrough {
  /**
   * GUID of the `sprk_gridconfiguration` row to open as a drill-through Custom
   * Page. The widget navigates via `Xrm.Navigation.navigateTo` to the page
   * carrying this `configId`. Same pattern the KPI Assessment + Invoices
   * drill-throughs use (see DataGrid Framework architecture §5).
   */
  gridConfigId?: string;
  /** Optional human-readable label for the drill action. */
  actionLabel?: string;
}

export interface MetricsCardAiSummary {
  /**
   * GUID of the playbook the AI summary action invokes. The playbook owns the
   * analysis scopes; the widget hands it `{ fetchXml, results, cardContext }`.
   * Phase A logs the payload; Phase B wires it through the playbook engine.
   */
  playbookId?: string;
  /** Optional prompt template seed — the playbook may or may not consume it. */
  promptHint?: string;
}

export interface MetricsCard {
  /** Stable identifier. */
  id: string;
  /** Label rendered under / above the visual. */
  label: string;
  /** Visual type. */
  visual: MetricsCardVisual;
  /**
   * FetchXML executed for this card. The widget pulls the entity name out of
   * the root `<entity name="…">` and runs `XrmDataverseClient.retrieveMultipleRecords`.
   */
  fetchXml: string;
  /**
   * For `bar` / `donut`: the result field to group + count by. Aggregation is
   * client-side. For `number`: ignored (count is `results.length`).
   */
  groupByField?: string;
  /** Drill-through to the underlying grid view. */
  drillThrough?: MetricsCardDrillThrough;
  /** AI-summary playbook hook. */
  aiSummary?: MetricsCardAiSummary;
}

export interface MetricsDashboardConfig {
  /** Stable identifier — referenced by widgetData. */
  id: string;
  /** Title rendered at the top of the widget. */
  title: string;
  /**
   * Filter tabs above the cards. Exactly one is active at a time. Phase A
   * tracks the active tab in component state but does not yet rewrite each
   * card's FetchXML based on it — that's a Phase B enhancement.
   */
  filterTabs: readonly MetricsFilterTab[];
  /** Cards rendered in a horizontal row. */
  cards: readonly MetricsCard[];
}

// ---------------------------------------------------------------------------
// Catalog — register dashboards here
// ---------------------------------------------------------------------------

/**
 * Matters dashboard — operator mockup target (2026-06-08).
 *
 * **DEPLOYMENT NOTE** — the FetchXML below uses placeholder field names that
 * MUST match the actual matter entity schema in `spaarkedev1`. The widget
 * renders a clear empty state if a query returns no records. Adjust the
 * field names + drill-through gridConfigId to point at real resources before
 * the dashboard is operator-facing.
 */
const MATTERS_DASHBOARD: MetricsDashboardConfig = {
  id: 'matters-dashboard',
  title: 'Matters Dashboard',
  filterTabs: [
    { id: 'date-range', label: 'By Date Range', kind: 'date-range' },
    { id: 'person', label: 'By Person', kind: 'person' },
    { id: 'area', label: 'By Area', kind: 'area' },
  ],
  cards: [
    {
      id: 'open-matters',
      label: 'Open Matters',
      visual: 'number',
      fetchXml: `
        <fetch>
          <entity name="sprk_matter">
            <attribute name="sprk_matterid" />
            <filter>
              <condition attribute="statecode" operator="eq" value="0" />
            </filter>
          </entity>
        </fetch>
      `.trim(),
      drillThrough: { actionLabel: 'Open in grid' },
      aiSummary: { promptHint: 'Summarize the open-matters portfolio.' },
    },
    {
      id: 'new-matters',
      label: 'New Matters',
      visual: 'bar',
      // Last ~3 months of created matters — bar chart groups by createdon-month.
      fetchXml: `
        <fetch>
          <entity name="sprk_matter">
            <attribute name="sprk_matterid" />
            <attribute name="createdon" />
            <filter>
              <condition attribute="createdon" operator="last-x-months" value="3" />
            </filter>
          </entity>
        </fetch>
      `.trim(),
      groupByField: 'createdon-month',
      drillThrough: { actionLabel: 'Open in grid' },
      aiSummary: { promptHint: 'Summarize new-matter intake velocity.' },
    },
    {
      id: 'by-area',
      label: 'By Area',
      visual: 'donut',
      fetchXml: `
        <fetch>
          <entity name="sprk_matter">
            <attribute name="sprk_matterid" />
            <attribute name="sprk_practicearea" />
            <filter>
              <condition attribute="statecode" operator="eq" value="0" />
            </filter>
          </entity>
        </fetch>
      `.trim(),
      groupByField: 'sprk_practicearea',
      drillThrough: { actionLabel: 'Open in grid' },
      aiSummary: { promptHint: 'Summarize practice-area distribution.' },
    },
  ],
};

export const METRICS_DASHBOARDS: readonly MetricsDashboardConfig[] = [
  MATTERS_DASHBOARD,
] as const;

export function getMetricsDashboardConfig(
  id: string,
): MetricsDashboardConfig | undefined {
  return METRICS_DASHBOARDS.find(d => d.id === id);
}
