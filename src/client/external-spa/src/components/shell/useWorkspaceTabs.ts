/**
 * useWorkspaceTabs — the module-host shell's tab-state seam (task 011 §3).
 *
 * A thin local implementation of the SpaarkeAi `WorkspaceTabManager` PATTERN
 * (`src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts`):
 * an always-present, non-closable pinned "Quick Start" home tab + closable widget
 * tabs, with add / activate / close and a close→fallback-to-home rule.
 *
 * §11 REUSE NOTE: the SpaarkeAi `WorkspaceTabManager` class is the reference model,
 * but it lives in the `src/solutions/SpaarkeAi` solution (not a shared-library
 * export) and its surrounding `WorkspacePane` wiring is Xrm/`@spaarke/ai-widgets`-
 * coupled; importing it would drag an Xrm dependency into the broker-only external
 * SPA (forbidden — design.md §12 / CLAUDE.md §11). This hook reuses the PATTERN
 * with no new abstraction beyond what the shell needs. Task 012 replaces the
 * placeholder widget-open path with the real entitlement-gated widget registry.
 */
import * as React from 'react';
import type { FluentIcon } from '@fluentui/react-icons';

/** The pinned, non-closable home tab id (always index 0 in the strip). */
export const QUICK_START_TAB = 'quick-start';

/** A closable widget tab. In task 011 these are placeholders; task 012 supplies real widgets. */
export interface WidgetTab {
  id: string;
  title: string;
  icon: FluentIcon;
}

export interface UseWorkspaceTabsResult {
  /** Closable widget tabs (excludes the pinned Quick Start home). */
  widgetTabs: WidgetTab[];
  /** Active tab id — `QUICK_START_TAB` or a widget id. */
  activeTabId: string;
  /** Activate a tab by id. */
  selectTab: (id: string) => void;
  /** Open (add if absent) a widget tab and activate it. */
  openTab: (tab: WidgetTab) => void;
  /** Close a widget tab; if it was active, fall back to the last widget or Quick Start. */
  closeTab: (id: string) => void;
}

interface TabsState {
  widgetTabs: WidgetTab[];
  activeTabId: string;
}

export function useWorkspaceTabs(initialTabs: WidgetTab[] = []): UseWorkspaceTabsResult {
  // Single atomic state object so close-with-fallback stays a pure updater (no
  // nested setState across two hooks — correct under React StrictMode).
  const [state, setState] = React.useState<TabsState>({
    widgetTabs: initialTabs,
    activeTabId: QUICK_START_TAB,
  });

  const selectTab = React.useCallback(
    (id: string) => setState((s) => ({ ...s, activeTabId: id })),
    [],
  );

  const openTab = React.useCallback(
    (tab: WidgetTab) =>
      setState((s) =>
        s.widgetTabs.some((t) => t.id === tab.id)
          ? { ...s, activeTabId: tab.id }
          : { widgetTabs: [...s.widgetTabs, tab], activeTabId: tab.id },
      ),
    [],
  );

  const closeTab = React.useCallback(
    (id: string) =>
      setState((s) => {
        const widgetTabs = s.widgetTabs.filter((t) => t.id !== id);
        const activeTabId =
          s.activeTabId === id
            ? widgetTabs[widgetTabs.length - 1]?.id ?? QUICK_START_TAB
            : s.activeTabId;
        return { widgetTabs, activeTabId };
      }),
    [],
  );

  return {
    widgetTabs: state.widgetTabs,
    activeTabId: state.activeTabId,
    selectTab,
    openTab,
    closeTab,
  };
}
