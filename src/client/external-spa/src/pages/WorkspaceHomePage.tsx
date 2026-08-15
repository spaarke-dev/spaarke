/**
 * WorkspaceHomePage — the module-host workspace shell host (task 011; entitlement wiring task 012).
 *
 * Repurposed from the former R1 outside-counsel dashboard (now preserved as
 * `OutsideCounselDashboard.tsx`, to be re-homed as widgets in task 016). This is
 * the SHELL HOST: it fetches `/me` entitlements, seeds the role-defaulted widget
 * tabs, and owns the tab + assistant-dock state, rendering `PortalWorkspaceShell`
 * (the tabbed, dockable chassis) beneath the App-level branded header.
 *
 * Task 012 replaces task 011's placeholder "Add widget" tab-seeding path with the
 * real entitlement-gated `widgetRegistry` (`src/registry/widgetRegistry.ts`):
 *   - On `/me` resolution, the role-defaulted widget ids (`defaultWidgetIdsFor`)
 *     open as tabs BEHIND the pinned Quick Start tab (which stays active/focused
 *     first — `selectTab(QUICK_START_TAB)` after seeding).
 *   - `onEntitledOpenWidget` is the SOLE tab-open choke point: Quick Start cards
 *     and the widget library both route through it, and it re-checks entitlement
 *     even though callers already filter to entitled items — an unentitled or
 *     unknown widget id is never opened (entitlement-honest negative, NFR-06:
 *     UX-honest only, server remains the enforcement boundary).
 *   - `renderWidget` resolves the active tab's body via the registry's lazy
 *     component cache; an unrecognized id degrades gracefully to a fallback body
 *     instead of crashing (FR-01).
 *
 * Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Spinner } from '@fluentui/react-components';
import {
  PortalWorkspaceShell,
  QuickStartPane,
  AssistantPane,
  WidgetLibraryModal,
  useWorkspaceTabs,
  QUICK_START_TAB,
} from '../components/shell';
import { fetchMeEntitlements, type MeEntitlementsResponse } from '../api/me-client';
import {
  defaultWidgetIdsFor,
  getWidgetDefinition,
  getWidgetLazyComponent,
  isEntitledToWidgetId,
} from '../registry/widgetRegistry';
import { PlaceholderWidgetBody } from '../registry/PlaceholderWidgetBody';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', flexGrow: 1, minHeight: 0 },
  statusRoot: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
  },
  statusMuted: { color: tokens.colorNeutralForeground3 },
  widgetFallback: {
    display: 'flex',
    flexGrow: 1,
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '200px',
  },
});

export interface WorkspaceHomePageProps {
  /** Whether the app is running inside the Teams host (workforce SSO) — threaded from App.tsx. */
  teamsHost?: boolean;
}

export const WorkspaceHomePage: React.FC<WorkspaceHomePageProps> = ({ teamsHost = false }) => {
  const s = useStyles();
  // Destructure so callbacks depend on the STABLE hook functions (not the
  // per-render result object), keeping useCallback memoization effective.
  const { widgetTabs, activeTabId, selectTab, openTab, closeTab } = useWorkspaceTabs();

  // /me entitlements — live BFF call (task 073) to GET /api/v1/external/me/entitlements, with a
  // plane-derived fallback on failure (see me-client.ts). `teamsHost` seeds the fallback plane.
  const [me, setMe] = React.useState<MeEntitlementsResponse | null>(null);
  const [meError, setMeError] = React.useState<string | null>(null);
  React.useEffect(() => {
    let cancelled = false;
    fetchMeEntitlements(teamsHost)
      .then(res => {
        if (!cancelled) setMe(res);
      })
      .catch((err: unknown) => {
        if (!cancelled) setMeError(err instanceof Error ? err.message : 'Unable to load your access.');
      });
    return () => {
      cancelled = true;
    };
  }, [teamsHost]);

  // Assistant dock: default LEFT per the AI-maturity theory (design.md §12 / ux
  // refinement #5 — the assistant migrates from far-right accessory to first-class
  // left primary pane). Collapsible; movable left/right.
  const [assistantDock, setAssistantDock] = React.useState<'left' | 'right'>('left');
  const [assistantCollapsed, setAssistantCollapsed] = React.useState(false);

  const [libraryOpen, setLibraryOpen] = React.useState(false);

  // Entitlement-honest tab-open choke point (task 012). Every card/library entry that opens a
  // widget routes through this — an unknown OR unentitled id is refused (negative: not routable
  // even by a direct call), not just filtered out of the UI that offers it.
  const onEntitledOpenWidget = React.useCallback(
    (widgetId: string) => {
      if (!me) return;
      const def = getWidgetDefinition(widgetId);
      if (!def) {
        console.warn(`[WorkspaceHomePage] Unknown widget id "${widgetId}" — ignored.`);
        return;
      }
      if (!isEntitledToWidgetId(me, widgetId)) {
        console.warn(`[WorkspaceHomePage] Widget "${widgetId}" is not entitled for this caller — ignored.`);
        return;
      }
      openTab({ id: def.id, title: def.title, icon: def.icon });
    },
    [me, openTab]
  );

  // Seed the role-defaulted widget tabs once `/me` resolves, then restore focus to the pinned
  // Quick Start tab — the defaults open BEHIND it, they don't steal the initial view.
  const seededRef = React.useRef(false);
  React.useEffect(() => {
    if (!me || seededRef.current) return;
    seededRef.current = true;
    for (const id of defaultWidgetIdsFor(me)) {
      onEntitledOpenWidget(id);
    }
    selectTab(QUICK_START_TAB);
  }, [me, onEntitledOpenWidget, selectTab]);

  const onAddWidget = React.useCallback(() => setLibraryOpen(true), []);

  const onAddFromLibrary = React.useCallback(
    (id: string) => {
      onEntitledOpenWidget(id);
      setLibraryOpen(false);
    },
    [onEntitledOpenWidget]
  );

  const renderWidget = React.useCallback(
    (widgetId: string) => {
      const def = getWidgetDefinition(widgetId);
      if (!def) {
        // Unknown/unregistered id — degrade gracefully instead of crashing (FR-01).
        return (
          <PlaceholderWidgetBody
            title="Widget unavailable"
            description="This widget isn't recognized. It may have been removed, or you may not have access."
          />
        );
      }
      const Comp = getWidgetLazyComponent(def);
      return (
        <React.Suspense
          fallback={
            <div className={s.widgetFallback}>
              <Spinner label="Loading…" />
            </div>
          }
        >
          <Comp title={def.title} description={def.description} />
        </React.Suspense>
      );
    },
    [s.widgetFallback]
  );

  if (meError) {
    return (
      <div className={s.statusRoot}>
        <Text size={500} weight="semibold">
          Unable to load your workspace
        </Text>
        <Text className={s.statusMuted}>{meError}</Text>
      </div>
    );
  }

  if (!me) {
    return (
      <div className={s.statusRoot}>
        <Spinner label="Loading your workspace…" />
      </div>
    );
  }

  return (
    <div className={s.root}>
      <PortalWorkspaceShell
        widgetTabs={widgetTabs}
        activeTabId={activeTabId}
        onSelectTab={selectTab}
        onCloseTab={closeTab}
        onAddWidget={onAddWidget}
        quickStart={<QuickStartPane me={me} onOpenWidget={onEntitledOpenWidget} />}
        assistant={<AssistantPane />}
        renderWidget={renderWidget}
        assistantCollapsed={assistantCollapsed}
        onToggleAssistant={() => setAssistantCollapsed(v => !v)}
        assistantDock={assistantDock}
        onMoveAssistant={() => setAssistantDock(d => (d === 'left' ? 'right' : 'left'))}
      />
      <WidgetLibraryModal
        open={libraryOpen}
        onClose={() => setLibraryOpen(false)}
        me={me}
        openWidgetIds={widgetTabs.map(t => t.id)}
        onAdd={onAddFromLibrary}
      />
    </div>
  );
};

export default WorkspaceHomePage;
