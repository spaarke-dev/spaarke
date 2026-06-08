/**
 * DataverseEntityViewWidget — embeds a Dataverse entity view (configured via
 * a `sprk_gridconfiguration` row) as a single workspace pane tab.
 *
 * ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08):
 *   Built as the canonical "Dataverse list" widget so any future system widget
 *   that wants to surface an existing Dataverse view can register a thin
 *   wrapper around this component with a hardcoded `configId`. Today the same
 *   wrapper is instantiated 4× (Documents, Projects, Invoices, Work Assignments)
 *   in `register-workspace-widgets.ts`.
 *
 * Pattern D (dual-use):
 *   The widget is registered as a Direct workspace widget here AND surfaced as a
 *   `ContentSectionConfig` section shim in `src/solutions/LegalWorkspace/src/sections/`.
 *   The same component renders in both — Calendar's canonical Pattern D applied to
 *   tabular data instead of calendar UI. See
 *   `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §4 for the framing.
 *
 * Data contract:
 *   `{ configId: string, title?: string }` — `configId` is the GUID of an
 *   existing `sprk_gridconfiguration` Dataverse row. The DataGrid framework
 *   reads everything else (entity, savedquery, columns, filter chips, command
 *   bar) from that row.
 *
 * Xrm dependency:
 *   The widget frame-walks for Xrm (window → parent → top) and instantiates an
 *   `XrmDataverseClient`. In dev (no Xrm), it renders a graceful empty state.
 *
 * Standards:
 *   - ADR-012: shared lib widget (`@spaarke/ai-widgets`). Reuses DataGrid +
 *     XrmDataverseClient from `@spaarke/ui-components`.
 *   - ADR-021: Fluent v9 semantic tokens only.
 *   - ADR-022: React 19 functional component.
 *   - ADR-028: no token snapshots. `Xrm.WebApi` is Xrm-mediated auth — no
 *     `authenticatedFetch` involvement.
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { DataGrid } from '../../../../Spaarke.UI.Components/src/components/DataGrid';
import { XrmDataverseClient } from '../../../../Spaarke.UI.Components/src/services/XrmDataverseClient';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Widget data contract
// ---------------------------------------------------------------------------

export interface DataverseEntityViewWidgetData {
  /**
   * GUID of the `sprk_gridconfiguration` row that drives the grid. Operators
   * pre-create one per entity-view combination (Documents, Projects, Invoices,
   * Work Assignments). See `register-workspace-widgets.ts` for the 4 baked-in
   * configIds and the operator setup notes.
   */
  configId: string;
  /** Optional debug label — not currently rendered (DataGrid owns its header). */
  title?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  emptyState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    padding: tokens.spacingVerticalXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Xrm frame-walk — copied from WorkspaceLayoutWidget pattern (intentionally
// duplicated to keep this widget's bundle independent of LegalWorkspace's
// xrmProvider helper).
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
function locateXrm(): any | null {
  if (typeof window !== 'undefined' && (window as any).Xrm?.WebApi) {
    return (window as any).Xrm;
  }
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.WebApi) return p;
  } catch {
    /* cross-origin */
  }
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.WebApi) return t;
  } catch {
    /* cross-origin */
  }
  return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DataverseEntityViewWidget: React.FC<
  WorkspaceWidgetProps<DataverseEntityViewWidgetData>
> = ({ data }) => {
  const styles = useStyles();

  const xrm = React.useMemo(() => locateXrm(), []);
  const dataverseClient = React.useMemo(() => {
    if (!xrm?.WebApi) return null;
    return new XrmDataverseClient(xrm.WebApi);
  }, [xrm]);

  if (!dataverseClient) {
    return (
      <div className={styles.emptyState}>
        <Text>Dataverse is unavailable in this context (no Xrm.WebApi).</Text>
      </div>
    );
  }

  if (!data?.configId) {
    return (
      <div className={styles.emptyState}>
        <Text>No grid configuration was supplied (data.configId is required).</Text>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      <DataGrid configId={data.configId} dataverseClient={dataverseClient} />
    </div>
  );
};

DataverseEntityViewWidget.displayName = 'DataverseEntityViewWidget';
