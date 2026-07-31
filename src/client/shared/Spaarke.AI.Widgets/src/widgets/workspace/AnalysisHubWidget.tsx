/**
 * @spaarke/ai-widgets — AnalysisHubWidget
 *
 * The Analysis platform's list surface (ai-advanced-capabilities-analysis-hub-r1).
 * As of the tabbed-Quick-Start UX change (owner, 2026-07-29/30) this is now a
 * PLAIN dataset-grid widget — "just like Documents / Matters / Projects":
 *
 *   - Grid + toolbar ONLY. It renders the existing `sprk_analysis` records via
 *     the shared DataGrid framework (`<DataGrid configId=… />`, through the
 *     canonical `DataverseEntityViewWidget` wrapper — NOT re-implemented here).
 *   - The prior top row of THREE "create new" work-type cards was REMOVED and
 *     relocated to the shared `AnalysisCardsWidget` (the "Analysis" tab of the
 *     one Quick Start modal). Analysis creation now flows through Quick Start.
 *   - The prior task-031 "reopen an analysis session in place" row-click
 *     behavior was DROPPED. Row-click uses the DataGrid DEFAULT — the OOB
 *     `sprk_analysis` form opened as a modal (`Xrm.Navigation.openForm` Layout 1,
 *     85%×85%), identical to opening the record from a subgrid (owner decision:
 *     row-click Option (a)). No custom `onRecordOpen` override is passed.
 *
 * `+ New` toolbar button:
 *   Overridden via the DataGrid `onCreateNew` seam (added to `DataGrid`/`CommandBar`
 *   for this project) so New OPENS THE TABBED QUICK START MODAL instead of the OOB
 *   create form. Because this widget lives in the WORKSPACE pane and the Quick Start
 *   modal is owned by the CONVERSATION pane (`ConversationPane`), the override
 *   dispatches the additive `conversation.open_quick_start` PaneEventBus intent
 *   (ADR-030) with `quickStartTab: 'analysis'` so Quick Start lands on the Analysis
 *   tab. `ConversationPane` opens the modal; the modal's "Agreement Review" card then
 *   drives the Create Analysis wizard modal (hosted by `WorkspacePane`).
 *
 * Pattern D dual-use (docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md §4):
 *   Renders as BOTH a Direct workspace widget (SpaarkeAi shell — bus present) AND a
 *   LegalWorkspace `analysis` section (bus may be absent in a non-SpaarkeAi host /
 *   MDA / dev). The PaneEventBus is read OPTIONALLY (`useOptionalDispatchPaneEvent`)
 *   so `+ New` degrades to a no-op rather than throwing where no bus exists — the
 *   grid itself still lists records via Xrm.
 *
 * Standards:
 *   - ADR-012: shared-lib composition (DataverseEntityViewWidget) — no fork.
 *   - ADR-021: Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component.
 *   - ADR-030: cross-pane coordination via typed PaneEventBus intents.
 *
 * @see DataverseEntityViewWidget — this package (DataGrid framework composition)
 * @see AnalysisCardsWidget       — the relocated create-analysis cards (Quick Start tab)
 * @see QuickStartModal.tsx       — the tabbed Quick Start host (SpaarkeAi solution)
 * @see docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md
 * @see docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md
 */

import * as React from 'react';
import { useCallback } from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import { useOptionalDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import { useOptionalPaneEventBus } from '../../events/PaneEventBusContext';
import { DataverseEntityViewWidget } from './DataverseEntityViewWidget';

// ---------------------------------------------------------------------------
// Grid configuration — sprk_gridconfiguration for the Analysis grid
// ---------------------------------------------------------------------------

/**
 * GUID of the `sprk_gridconfiguration` row driving the Analysis grid. Seeded in
 * spaarkedev1 (duplicate-component-audit deployment, 2026-07-29) with four
 * sibling saved queries (All / Agreement Review / Legal Research / Patent), so
 * the DataGrid framework's own `ViewSelector` renders the "view by type"
 * dropdown automatically. Recipe + GUIDs in
 * `projects/ai-advanced-capabilities-analysis-hub-r1/notes/hub-grid-config-deployment.md`.
 */
const ANALYSIS_HUB_GRID_CONFIG_ID = 'e7c8126a-968b-f111-8077-7ced8ddc4a05';

/** Widget-type string passed to the nested `DataverseEntityViewWidget` for debug/sub-routing only. */
const ANALYSIS_HUB_GRID_WIDGET_TYPE = 'analysis-hub-grid';

// ---------------------------------------------------------------------------
// Data contract
// ---------------------------------------------------------------------------

export interface AnalysisHubWidgetData {
  /**
   * Override for the `sprk_gridconfiguration` GUID driving the Analysis grid.
   * Optional — defaults to {@link ANALYSIS_HUB_GRID_CONFIG_ID}. Present so a
   * future dispatcher (or test) can point the grid at an alternate config record
   * without a code change.
   */
  configId?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    width: '100%',
    minWidth: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * AnalysisHubWidget — the Analysis platform's list workspace tab (plain grid).
 *
 * @example
 * ```tsx
 * // Resolved via WorkspaceWidgetRegistry under 'analysis-hub':
 * <AnalysisHubWidget data={{}} widgetType="analysis-hub" />
 * ```
 */
export const AnalysisHubWidget: React.FC<WorkspaceWidgetProps<AnalysisHubWidgetData>> = ({ data, className }) => {
  const styles = useStyles();
  // Pattern D dual-use: read the PaneEventBus OPTIONALLY so `+ New` degrades to a
  // no-op in a bus-less host (LegalWorkspace section / MDA / dev) instead of throwing.
  const dispatch = useOptionalDispatchPaneEvent();
  // The bus INSTANCE (not the no-op dispatch) tells us whether we're hosted in the
  // SpaarkeAi shell — used to decide the row-click behavior below.
  const bus = useOptionalPaneEventBus();

  const gridConfigId = data?.configId ?? ANALYSIS_HUB_GRID_CONFIG_ID;

  // `+ New` → open the tabbed Quick Start modal on the Analysis tab. The modal is
  // owned by ConversationPane, so this is a cross-pane intent (ADR-030). Regarding
  // is NOT threaded here — WorkspacePane resolves it from the host entityContext
  // when it hosts the wizard modal (keeps user-content identity off the bus).
  const handleCreateNew = useCallback(() => {
    dispatch('conversation', { type: 'open_quick_start', quickStartTab: 'analysis' });
  }, [dispatch]);

  // Row-click: when HOSTED in SpaarkeAi (bus present), open the picked analysis as a
  // HEADLESS SpaarkeAi code-page modal — WorkspacePane routes this intent to
  // `openSpaarkeAi({ analysisId }, 2)` (target:2, no OOB `sprk_analysis` form chrome).
  // In a bus-less host (LegalWorkspace section / MDA / dev) leave `onRecordOpen`
  // UNDEFINED so the DataGrid keeps its OOB-form default (openForm) — no dead click.
  const onRecordOpen = React.useMemo(
    () =>
      bus
        ? (recordId: string): void => {
            dispatch('workspace', { type: 'open_analysis_headless', analysisId: recordId });
          }
        : undefined,
    [bus, dispatch]
  );

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="analysis-hub-widget">
      <DataverseEntityViewWidget
        data={{ configId: gridConfigId, onCreateNew: handleCreateNew, onRecordOpen }}
        widgetType={ANALYSIS_HUB_GRID_WIDGET_TYPE}
      />
    </div>
  );
};

AnalysisHubWidget.displayName = 'AnalysisHubWidget';

export default AnalysisHubWidget;
