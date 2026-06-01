/**
 * WorkspaceLayoutWidget — embeds the full LegalWorkspaceApp as a single
 * workspace pane tab.
 *
 * Operator's reuse principle (Round 4 Fix 4, 2026-05-21):
 *   "When we have working components, reuse them" — taken to its logical
 *   conclusion. The prior investigation found that copying LegalWorkspace's
 *   5 section factories into SpaarkeAi would drag in ~30 files / ~10K LOC
 *   plus DataverseService runtime state, FeedTodoSyncContext, and the
 *   @hello-pangea/dnd peer dependency. Rather than fight that dependency
 *   closure, this widget embeds the WHOLE working LegalWorkspaceApp inside
 *   a workspace tab. The pane-level `<WorkspacePaneMenu>` dispatches a
 *   `widget_load` event for this widget type when the user picks a workspace
 *   from the "Switch Workspace" dropdown.
 *
 * Embedded mode:
 *   LegalWorkspaceApp accepts an `embedded` prop (added in this round) that
 *   suppresses its internal `<PageHeader>` (which carries its own workspace
 *   dropdown), footer, outer `<FluentProvider>`, and theme-sync side effects.
 *   The SpaarkeAi shell owns all of those — the embedded tree is just the
 *   workspace grid (sections rendered via WorkspaceShell + buildDynamicWorkspaceConfig).
 *
 * Data shape:
 *   `{ layoutId: string, layoutName: string }` — comes from the WorkspacePaneMenu
 *   when the user clicks an item under "Switch Workspace". The `layoutId` is
 *   passed to LegalWorkspaceApp as `initialWorkspaceId`, which WorkspaceGrid
 *   then passes to `useWorkspaceLayouts(initialWorkspaceId)` to deep-link the
 *   chosen layout on first paint.
 *
 * Xrm dependencies:
 *   LegalWorkspaceApp's WorkspaceGrid requires `webApi` + `userId`. We obtain
 *   these from the same xrmProvider walk pattern LegalWorkspace itself uses
 *   (window → parent → top). When running in dev (no Xrm), the widget renders
 *   an empty-state message instead of crashing.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local widget that consumes from @spaarke/legal-workspace.
 *   - ADR-021: Fluent v9 tokens only.
 *   - ADR-022: React 19 functional component.
 *   - ADR-028: LegalWorkspace's existing BFF call surface (useWorkspaceLayouts)
 *              is preserved; SpaarkeAi does not need to inject auth.
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { LegalWorkspaceApp } from '@spaarke/legal-workspace';
import type { WorkspaceWidgetComponent } from '../../types/widget-types';

// ---------------------------------------------------------------------------
// Widget data contract
// ---------------------------------------------------------------------------

export interface WorkspaceLayoutWidgetData {
  /** Workspace layout id (Dataverse `sprk_workspacelayout` GUID). */
  layoutId: string;
  /** Human-readable layout name for the tab title (already set by the menu). */
  layoutName: string;
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
// Xrm provider (frame-walk) — mirrors LegalWorkspace's xrmProvider so the
// embedded app has access to the same Xrm.WebApi reference it would use
// standalone. Kept inline to avoid pulling LegalWorkspace's xrmProvider
// (which lives outside the shared barrel) into the widget bundle directly.
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
function locateXrm(): any | null {
  // 1. Current window
  if (typeof window !== 'undefined' && (window as any).Xrm?.WebApi) {
    return (window as any).Xrm;
  }
  // 2. Parent window (iframe inside Custom Page)
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.WebApi) {
      (window as any).Xrm = p;
      return p;
    }
  } catch {
    /* cross-origin */
  }
  // 3. Top window
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.WebApi) {
      (window as any).Xrm = t;
      return t;
    }
  } catch {
    /* cross-origin */
  }
  return null;
}

function getWebApiSafe(): any | null {
  return locateXrm()?.WebApi ?? null;
}

function getUserIdSafe(): string {
  const xrm = locateXrm();
  if (xrm?.Utility?.getGlobalContext) {
    const ctx = xrm.Utility.getGlobalContext();
    const raw = ctx.getUserId?.() ?? ctx.userSettings?.userId ?? '';
    return String(raw).replace(/[{}]/g, '');
  }
  if (xrm?.userSettings?.userId) {
    return String(xrm.userSettings.userId).replace(/[{}]/g, '');
  }
  return '';
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// WorkspaceLayoutWidget
// ---------------------------------------------------------------------------

/**
 * Renders the embedded LegalWorkspaceApp for the chosen workspace layout.
 *
 * The widget is rendered inside a tab managed by `WorkspaceTabManager`;
 * the tab's `displayName` is supplied by `WorkspacePaneMenu` (the layout
 * name) so the tab label matches the workspace.
 */
export const WorkspaceLayoutWidget: WorkspaceWidgetComponent<WorkspaceLayoutWidgetData> = ({ data }) => {
  const styles = useStyles();

  // Resolve Xrm dependencies once on mount — LegalWorkspaceApp's
  // WorkspaceGrid requires both.
  const webApi = React.useMemo(() => getWebApiSafe(), []);
  const userId = React.useMemo(() => getUserIdSafe(), []);

  // Dev fallback: when Xrm isn't available (e.g. `npm run dev` in Vite),
  // render an empty-state message instead of crashing inside LegalWorkspaceApp.
  if (!webApi) {
    return (
      <div className={styles.root} data-testid="workspace-layout-widget-no-xrm">
        <div className={styles.emptyState}>
          <Text size={300}>Workspace requires the Dataverse host (Xrm.WebApi unavailable in this context).</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root} data-testid="workspace-layout-widget-root">
      <LegalWorkspaceApp
        version="embedded"
        allocatedWidth={0}
        allocatedHeight={0}
        webApi={webApi}
        userId={userId}
        initialWorkspaceId={data.layoutId}
        embedded
      />
    </div>
  );
};

WorkspaceLayoutWidget.displayName = 'WorkspaceLayoutWidget';
