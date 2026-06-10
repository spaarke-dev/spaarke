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
import { DataGrid, XrmDataverseClient } from '@spaarke/ui-components';
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
    // min-width: 0 + width: 100% — ensures this widget respects its host
    // container's width regardless of the inner DataGrid's column-sum width.
    // Without min-width: 0, the flex chain inflates this widget to fit the
    // grid (operator round 5: section card grew to 1548px). See matching
    // fix on DataGrid's root/innerCard/gridScroll for the bottom of the chain.
    minWidth: 0,
    width: '100%',
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

export const DataverseEntityViewWidget: React.FC<WorkspaceWidgetProps<DataverseEntityViewWidgetData>> = ({ data }) => {
  const styles = useStyles();

  // XrmDataverseClient resolves Xrm lazily on each call — no constructor arg.
  // We still frame-walk here so the widget can show an empty state in dev
  // (where Xrm is not present) instead of crashing on the first BFF call.
  const xrm = React.useMemo(() => locateXrm(), []);
  const dataverseClient = React.useMemo(() => {
    if (!xrm?.WebApi) return null;
    return new XrmDataverseClient();
  }, [xrm]);

  // ai-spaarke-ai-workspace-UI-r1 iter 2 round 9 (2026-06-09):
  // EventsPage Code Page works because its index.html sets `overflow: hidden`
  // on html/body/#root, so the FluentDataGrid (which renders at column-sum
  // width via `min-width: fit-content`) gets clipped at the page boundary.
  // The embedded workspace path has no such boundary — the section card +
  // every flex/grid ancestor can grow with the table's content. Round 8 tried
  // a Griffel !important override on the FluentDataGrid root; it apparently
  // didn't take effect at the user's browser.
  //
  // This round measures the WIDGET's own outer container width via a
  // ResizeObserver attached to the widget root and applies it as an EXPLICIT
  // pixel `maxWidth` on the inner DataGrid wrapper. An explicit pixel cap
  // beats any intrinsic content sizing the inner FluentDataGrid does — the
  // widget itself becomes the constraint boundary the EventsPage `index.html`
  // body-level `overflow: hidden` provides for the modal.
  const widgetRootRef = React.useRef<HTMLDivElement | null>(null);
  const [widgetWidth, setWidgetWidth] = React.useState<number>(0);
  React.useLayoutEffect(() => {
    const el = widgetRootRef.current;
    if (!el) return;
    setWidgetWidth(el.clientWidth);
  }, []);
  React.useEffect(() => {
    const el = widgetRootRef.current;
    if (!el || typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver(entries => {
      for (const entry of entries) {
        setWidgetWidth(entry.contentRect.width);
      }
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

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
    <div ref={widgetRootRef} className={styles.root}>
      {/*
        Explicit pixel-cap wrapper. The outer widget root has min-width:0 +
        width:100% so it follows its parent. We measure THIS root and feed
        the value to a fixed-width inner div that the DataGrid mounts into —
        the DataGrid (and its FluentDataGrid descendant) inherit this pixel
        cap as their containing block. No CSS class hacks; just an inline
        style on a stable wrapper.
      */}
      <div
        style={{
          width: widgetWidth > 0 ? `${widgetWidth}px` : '100%',
          maxWidth: widgetWidth > 0 ? `${widgetWidth}px` : '100%',
          flex: 1,
          minHeight: 0,
          minWidth: 0,
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <DataGrid configId={data.configId} dataverseClient={dataverseClient} />
      </div>
    </div>
  );
};

DataverseEntityViewWidget.displayName = 'DataverseEntityViewWidget';
