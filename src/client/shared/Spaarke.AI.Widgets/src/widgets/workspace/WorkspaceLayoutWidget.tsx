/**
 * WorkspaceLayoutWidget — embeds the default workspace renderer
 * (`LegalWorkspaceApp` today) as a single workspace pane tab.
 *
 * Operator's reuse principle (Round 4 Fix 4, 2026-05-21):
 *   "When we have working components, reuse them" — taken to its logical
 *   conclusion. The prior investigation found that copying LegalWorkspace's
 *   5 section factories into SpaarkeAi would drag in ~30 files / ~10K LOC
 *   plus DataverseService runtime state, FeedTodoSyncContext, and the
 *   @hello-pangea/dnd peer dependency. Rather than fight that dependency
 *   closure, this widget embeds the WHOLE working renderer inside a workspace
 *   tab. The pane-level `<WorkspacePaneMenu>` dispatches a `widget_load`
 *   event for this widget type when the user picks a workspace from the
 *   "Switch Workspace" dropdown.
 *
 * R4 task 052 (C-4) renderer seam:
 *   This widget no longer imports `LegalWorkspaceApp` directly. It accepts an
 *   optional injected `renderer?: WorkspaceRenderer` prop OR consults the
 *   default-renderer slot exposed by `@spaarke/ui-components`. The host
 *   (SpaarkeAi `main.tsx`) calls `setDefaultWorkspaceRenderer(LegalWorkspaceApp)`
 *   at bootstrap, so default behaviour is unchanged from pre-C-4. Future
 *   hosts can register an alternate renderer without modifying this widget.
 *
 * Embedded mode:
 *   The renderer is mounted with `embedded={true}`. Concrete renderers MUST
 *   suppress their own chrome when embedded (see `WorkspaceRenderer.ts` docs).
 *   `LegalWorkspaceApp` already does this.
 *
 * Data shape:
 *   `{ layoutId: string, layoutName: string }` — comes from the WorkspacePaneMenu
 *   when the user clicks an item under "Switch Workspace". The `layoutId` is
 *   passed to the renderer as `initialWorkspaceId`.
 *
 * Xrm dependencies:
 *   The renderer requires `webApi` + `userId`. We obtain these from the same
 *   xrmProvider walk pattern LegalWorkspace itself uses (window → parent → top).
 *   When running in dev (no Xrm), the widget renders an empty-state message
 *   instead of crashing.
 *
 * Standards:
 *   - ADR-012: Context-agnostic widget — no direct import of LegalWorkspace.
 *   - ADR-021: Fluent v9 tokens only.
 *   - ADR-022: React 19 functional component.
 *   - ADR-028: Renderer-owned auth — this widget never carries token snapshots.
 */

import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { getDefaultWorkspaceRenderer, type WorkspaceRenderer } from '@spaarke/ui-components';
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
// Widget extra props (R4 task 052 / C-4 renderer seam)
// ---------------------------------------------------------------------------

/**
 * Optional props the widget accepts BEYOND the standard `WorkspaceWidgetProps`.
 * Used primarily by tests and by future hosts that want to inject a custom
 * renderer without registering a default. Production callers (the SpaarkeAi
 * registry pipeline) omit these — the widget consults the default-renderer
 * slot via `getDefaultWorkspaceRenderer()`.
 */
export interface WorkspaceLayoutWidgetExtraProps {
  /**
   * Optional injected renderer. When omitted, the widget falls back to the
   * default registered via `setDefaultWorkspaceRenderer()` in
   * `@spaarke/ui-components`. When neither is available, the widget renders
   * a graceful empty state instead of crashing.
   */
  renderer?: WorkspaceRenderer;
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
 * Renders the embedded workspace renderer for the chosen workspace layout.
 *
 * The widget is rendered inside a tab managed by `WorkspaceTabManager`;
 * the tab's `displayName` is supplied by `WorkspacePaneMenu` (the layout
 * name) so the tab label matches the workspace.
 *
 * Renderer resolution (R4 task 052 / C-4):
 *   1. Use the injected `renderer` prop if provided.
 *   2. Otherwise, consult `getDefaultWorkspaceRenderer()` from
 *      `@spaarke/ui-components` — the host has typically registered
 *      `LegalWorkspaceApp` at bootstrap.
 *   3. If neither is available, render a graceful "no renderer" empty state.
 */
export const WorkspaceLayoutWidget: React.FC<
  React.ComponentProps<WorkspaceWidgetComponent<WorkspaceLayoutWidgetData>> & WorkspaceLayoutWidgetExtraProps
> = ({ data, renderer }) => {
  const styles = useStyles();

  // Resolve Xrm dependencies once on mount — the renderer's WorkspaceGrid
  // requires both.
  const webApi = React.useMemo(() => getWebApiSafe(), []);
  const userId = React.useMemo(() => getUserIdSafe(), []);

  // Resolve the renderer: injected prop wins; otherwise consult the default slot.
  // The slot is populated by the host at bootstrap (e.g. SpaarkeAi `main.tsx`
  // calls `setDefaultWorkspaceRenderer(LegalWorkspaceApp)`).
  const Renderer: WorkspaceRenderer | null = React.useMemo(() => renderer ?? getDefaultWorkspaceRenderer(), [renderer]);

  // Dev fallback: when Xrm isn't available (e.g. `npm run dev` in Vite),
  // render an empty-state message instead of crashing inside the renderer.
  if (!webApi) {
    return (
      <div className={styles.root} data-testid="workspace-layout-widget-no-xrm">
        <div className={styles.emptyState}>
          <Text size={300}>Workspace requires the Dataverse host (Xrm.WebApi unavailable in this context).</Text>
        </div>
      </div>
    );
  }

  // R4 task 052 graceful degradation: if no renderer is registered (and none
  // was injected), surface a developer-targeted empty state rather than crashing.
  // Production hosts (SpaarkeAi) register the default at bootstrap so this branch
  // should never trip outside misconfigured environments.
  if (!Renderer) {
    return (
      <div className={styles.root} data-testid="workspace-layout-widget-no-renderer">
        <div className={styles.emptyState}>
          <Text size={300}>
            No workspace renderer registered. The host application must call <code>setDefaultWorkspaceRenderer()</code>{' '}
            from <code>@spaarke/ui-components</code> at bootstrap.
          </Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root} data-testid="workspace-layout-widget-root">
      <Renderer
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
