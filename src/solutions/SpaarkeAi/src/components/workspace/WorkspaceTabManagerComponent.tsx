/**
 * WorkspaceTabManagerComponent.tsx — Tab bar + active widget renderer for WorkspacePane.
 *
 * Renders the Fluent v9 TabList tab bar and the active workspace widget below it.
 * Each tab shows the widget's displayName and a close button. The active tab's
 * resolved widget component is mounted; inactive tabs are unmounted (not hidden)
 * to avoid accumulating memory and network connections from multiple live widgets.
 *
 * Props are driven entirely by WorkspaceTabManagerState from WorkspaceTabManager.
 * This component has no internal state — it is a pure renderer driven by WorkspacePane.
 *
 * Loading state: when a tab's Component is null (registry promise still pending),
 * a Fluent Spinner is rendered in the content area so the user has immediate
 * feedback that the widget is being loaded.
 *
 * @see WorkspacePane          — owner component that manages tab state
 * @see WorkspaceTabManager    — plain TS class that manages tab array state
 * @see ADR-021 — Fluent v9 tokens only, dark mode, no hardcoded colors
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  TabList,
  Tab,
  Spinner,
  Text,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import {
  ChevronLeft20Regular,
  ChevronRight20Regular,
  Dismiss12Regular,
  WarningRegular,
} from "@fluentui/react-icons";
import { WidgetErrorBoundary } from "@spaarke/ui-components";
import type { WorkspaceTab } from "./WorkspaceTabManager";
import type { WorkspaceWidgetProps } from "@spaarke/ai-widgets";
import { AddToAssistantToggle } from "./AddToAssistantToggle";

// NOTE (task 098 — 2026-05-22): the per-tab pin button was removed from
// every tab row. Pin state is still owned by `services/pinnedWorkspaces.ts`
// (localStorage `spaarke:workspace:pinned-list`), but the only UI surface for
// toggling it is now the WorkspacePaneMenu dropdown. Auto-open of pinned
// workspaces on cold load is unchanged (see WorkspacePane mount effect).

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
  },

  // Tab bar strip — sits at the top, never shrinks.
  //
  // Task 107 (2026-05-22): the previous `overflowX: 'auto'` on the bar itself
  // produced a visible horizontal scrollbar when tabs overflowed. The new
  // layout is [arrowLeft] [tabScroll (overflow + hidden bar)] [arrowRight];
  // the bar itself no longer scrolls — it is a flex container with three
  // children. Arrow visibility is computed in the component from
  // scrollLeft/scrollWidth/clientWidth of `tabScroll`.
  tabBar: {
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    minHeight: "40px",
    overflow: "hidden",
  },

  // Inner scroll container for the TabList — the element whose scrollLeft we
  // drive with the arrow buttons. Hidden scrollbar (Fix 1 / Fix 2 shared
  // pattern): scrollbarWidth: none + ::-webkit-scrollbar { display: none }
  // hides the native bar while keeping the element scrollable (programmatic
  // and wheel/trackpad scroll still work).
  tabScroll: {
    flexGrow: 1,
    overflowX: "auto",
    overflowY: "hidden",
    scrollbarWidth: "none",
    "::-webkit-scrollbar": {
      display: "none",
    },
  },

  // Arrow buttons at the start/end of the tab bar (task 107).
  // `flexShrink: 0` so they never collapse when the tab strip is full.
  // Reserve space when hidden so the tab strip width doesn't jitter as
  // arrows appear/disappear — we use `visibility: hidden` rather than
  // unmount (see `arrowHidden`).
  arrowButton: {
    minWidth: "28px",
    width: "28px",
    height: "28px",
    padding: "0",
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  arrowHidden: {
    visibility: "hidden",
    pointerEvents: "none",
  },

  // The TabList itself — let it grow so tabs lay out naturally inside
  // `tabScroll`; the scroll container handles horizontal overflow.
  tabList: {
    flexGrow: 1,
  },

  // Individual Tab inner wrapper — label + close button.
  tabContent: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    maxWidth: "160px",
  },

  // Tab title — task 098 (2026-05-22): bumped one Fluent v9 step
  // (fontSizeBase200 → fontSizeBase300) per operator feedback. The tab is
  // still visually a tab (TabList size="small") but the label is now slightly
  // more prominent, matching the pane title proportions polished in Wave 1.
  tabLabel: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },

  // Loading badge inside the tab (replaces label while resolving).
  // Kept at fontSizeBase200 — the spinner + ellipsis row is intentionally
  // less prominent than the resolved title; bumping it would crowd the row.
  tabLoadingBadge: {
    display: "inline-flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },

  // Close button — task 098 (2026-05-22): downsized via Dismiss12Regular
  // (the 12px icon variant) so the × is visually subordinate to the bumped
  // tab title. Button hit area kept at 16×16 for accessibility (WCAG min
  // target ~24px is relaxed for icon-inside-tab affordances per Fluent v9
  // tab pattern; the surrounding tab itself is the primary 40px target).
  closeButton: {
    minWidth: "unset",
    height: "16px",
    width: "16px",
    padding: "0",
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },

  // Content area — grows to fill remaining height.
  //
  // Task 107 (2026-05-22) Fix 1: hide the visible vertical scrollbar while
  // keeping the area scrollable (wheel/trackpad/keyboard still work). The
  // Assistant pane chat scroll is intentionally NOT touched (visible bar is
  // part of its UX); the Context pane is owned by sibling task 106 — this
  // change is surgically scoped to the WorkspacePane content wrapper.
  //
  // R4-110 (2026-06-23) — chain robustness: added `display: flex,
  // flexDirection: column, minHeight: 0`. Without these, the wrapper is
  // implicitly `display: block`, which IGNORES any `flex: 1` declared on
  // child widget roots. Widgets had to self-anchor via `height: 100%`
  // (the round 11 rescue) — a trap for future widget authors. With this
  // change, the widget chain is FORGIVING: a widget root can use either
  // `flex: 1` or `height: 100%` and the chain propagates correctly.
  content: {
    display: "flex",
    flexDirection: "column",
    minHeight: 0,
    flex: 1,
    overflowY: "auto",
    overflowX: "hidden",
    scrollbarWidth: "none",
    "::-webkit-scrollbar": {
      display: "none",
    },
    backgroundColor: tokens.colorNeutralBackground2,
  },

  // R6 Pillar 9 / task 098 — visibility-toggle strip above the active widget.
  // Subtle thin bar, semantic tokens only (ADR-021 dark-mode parity).
  visibilityBar: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  // Loading state within the content area.
  loadingState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },

  // Error state within the content area.
  errorState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: tokens.spacingVerticalS,
    color: tokens.colorPaletteRedForeground1,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    textAlign: "center",
  },

  errorIcon: {
    fontSize: "32px",
    color: tokens.colorPaletteRedForeground1,
  },

  // Widget wrapper — fills the content area.
  widgetWrapper: {
    height: "100%",
    width: "100%",
  },
});

// ---------------------------------------------------------------------------
// WorkspaceTabManagerComponentProps
// ---------------------------------------------------------------------------

export interface WorkspaceTabManagerComponentProps {
  /** Current ordered list of tabs from WorkspaceTabManager.getSnapshot(). */
  tabs: WorkspaceTab[];
  /** Id of the currently active tab, or null if no tabs exist. */
  activeTabId: string | null;
  /** Called when the user clicks a different tab in the tab bar. */
  onTabChange: (tabId: string) => void;
  /** Called when the user clicks the close button on a tab. */
  onTabClose: (tabId: string) => void;

  /**
   * Current chat session identifier. Required for the per-tab
   * AddToAssistantToggle (R6 Pillar 9 / task 098) so the dispatched
   * `workspace.tab_edited` PaneEventBus event is scoped to the active
   * session per ADR-015.
   *
   * When null, the toggle is not rendered (no session = no visibility
   * contract to project).
   */
  chatSessionId?: string | null;

  /**
   * Called when the user toggles the per-tab "Visible to assistant"
   * switch. Receives the tabId and the NEW visibility value. The host
   * (WorkspacePane) persists the change via PATCH to
   * `/api/ai/chat/sessions/{id}/tabs/{tabId}` AND updates the local
   * WorkspaceTabManager so the next system-prompt snapshot reflects
   * the new flag.
   *
   * R6 Pillar 9 / task 098 — server projection already wired; this
   * callback is the missing UI mount point.
   */
  onToggleVisibility?: (tabId: string, visibleToAssistant: boolean) => void;
}

// ---------------------------------------------------------------------------
// ActiveWidgetContent — renders the active tab's resolved widget
// ---------------------------------------------------------------------------

interface ActiveWidgetContentProps {
  tab: WorkspaceTab;
  styles: ReturnType<typeof useStyles>;
}

function ActiveWidgetContent({ tab, styles }: ActiveWidgetContentProps): React.JSX.Element {
  // Loading — registry promise not yet resolved.
  if (tab.isLoading || tab.Component === null) {
    return (
      <div className={styles.loadingState}>
        <Spinner size="medium" label={`Loading ${tab.displayName}…`} />
      </div>
    );
  }

  const Widget = tab.Component as React.ComponentType<WorkspaceWidgetProps>;

  // ai-spaarke-ai-workspace-UI-r1 brittleness Phase D.2 (2026-06-09):
  // Per-widget isolation — a render error in this widget is caught and
  // displayed inline so sibling tabs keep rendering normally. Without this,
  // a crashing widget propagates to AppErrorBoundary at the surface root
  // and blanks the whole SpaarkeAi page.
  return (
    <div className={styles.widgetWrapper}>
      <WidgetErrorBoundary
        widgetType={tab.widgetType}
        displayName={tab.displayName}
        surface="SpaarkeAi"
      >
        <Widget
          data={tab.widgetData}
          widgetType={tab.widgetType}
          isLoading={false}
        />
      </WidgetErrorBoundary>
    </div>
  );
}

// ---------------------------------------------------------------------------
// WorkspaceTabManagerComponent
// ---------------------------------------------------------------------------

/**
 * Pure presenter — renders the tab bar and the active widget content area.
 *
 * All state is owned by WorkspacePane / WorkspaceTabManager. This component
 * is stateless: every user interaction fires a callback prop (onTabChange,
 * onTabClose) so WorkspacePane can update the manager and pass new props down.
 */
export function WorkspaceTabManagerComponent({
  tabs,
  activeTabId,
  onTabChange,
  onTabClose,
  chatSessionId,
  onToggleVisibility,
}: WorkspaceTabManagerComponentProps): React.JSX.Element {
  const styles = useStyles();

  // Resolve the active tab record.
  const activeTab = tabs.find((t) => t.id === activeTabId) ?? null;

  // ---------------------------------------------------------------------------
  // Tab overflow arrows — task 107 (2026-05-22)
  //
  // The tab strip lives inside `tabScroll` (an element with overflow-x: auto +
  // hidden scrollbar). When tabs overflow the visible width we surface
  // chevron buttons at the start/end of the bar so the user can move through
  // the tabs without a visible horizontal scrollbar. Visibility is driven by
  // scrollLeft / scrollWidth / clientWidth on the scroll container and stays
  // in sync via three observers:
  //
  //   1. `scroll` listener on the container — fires while the user scrolls.
  //   2. ResizeObserver on the container — covers pane width changes.
  //   3. `tabs` dependency on the recompute effect — covers add/close/rename.
  //
  // Both arrows are always rendered (with `visibility: hidden` when
  // unreachable) so the tab strip width doesn't jitter as arrows appear and
  // disappear.
  // ---------------------------------------------------------------------------

  const scrollContainerRef = React.useRef<HTMLDivElement | null>(null);
  const [canScrollLeft, setCanScrollLeft] = React.useState(false);
  const [canScrollRight, setCanScrollRight] = React.useState(false);

  const recomputeScrollState = React.useCallback((): void => {
    const el = scrollContainerRef.current;
    if (!el) return;
    // Tolerance of 1px to absorb subpixel rounding.
    const left = el.scrollLeft > 0;
    const right = el.scrollLeft + el.clientWidth < el.scrollWidth - 1;
    setCanScrollLeft(left);
    setCanScrollRight(right);
  }, []);

  // Recompute when tabs change (add/close/rename can shift overflow state).
  React.useEffect(() => {
    recomputeScrollState();
  }, [tabs, recomputeScrollState]);

  // Wire scroll + ResizeObserver listeners on the scroll container.
  React.useEffect(() => {
    const el = scrollContainerRef.current;
    if (!el) return;

    const onScroll = (): void => recomputeScrollState();
    el.addEventListener("scroll", onScroll, { passive: true });

    let resizeObserver: ResizeObserver | null = null;
    if (typeof ResizeObserver !== "undefined") {
      resizeObserver = new ResizeObserver(() => recomputeScrollState());
      resizeObserver.observe(el);
    }

    // Initial measurement after the layout commits.
    recomputeScrollState();

    return () => {
      el.removeEventListener("scroll", onScroll);
      if (resizeObserver) resizeObserver.disconnect();
    };
  }, [recomputeScrollState]);

  // Arrow click handlers — scroll by ~one "tab width" (200px). The container
  // smooths the scroll for a less abrupt UX. The recompute effect fires via
  // the scroll listener as scrollLeft animates.
  const scrollByDelta = React.useCallback((delta: number): void => {
    const el = scrollContainerRef.current;
    if (!el) return;
    el.scrollBy({ left: delta, behavior: "smooth" });
  }, []);

  const handleScrollLeft = React.useCallback((): void => {
    scrollByDelta(-200);
  }, [scrollByDelta]);

  const handleScrollRight = React.useCallback((): void => {
    scrollByDelta(200);
  }, [scrollByDelta]);

  // Active-tab into view — when the active tab changes (programmatic add,
  // close-restore, restore-from-persistence), bring it into view so users
  // never lose the active tab off the right edge. `inline: 'nearest'` is a
  // no-op if the tab is already visible; only clipped tabs scroll.
  React.useEffect(() => {
    if (!activeTabId) return;
    const container = scrollContainerRef.current;
    if (!container) return;
    const activeEl = container.querySelector(
      `[data-testid="workspace-tab-${activeTabId}"]`,
    ) as HTMLElement | null;
    if (!activeEl) return;
    // Defer to next frame so layout settles before measuring.
    const raf = window.requestAnimationFrame(() => {
      activeEl.scrollIntoView({ inline: "nearest", block: "nearest", behavior: "smooth" });
    });
    return () => window.cancelAnimationFrame(raf);
  }, [activeTabId, tabs]);

  // ---------------------------------------------------------------------------
  // Tab close — stop propagation so clicking the X does not also activate the tab.
  // ---------------------------------------------------------------------------

  const handleCloseClick = React.useCallback(
    (e: React.MouseEvent, tabId: string): void => {
      e.stopPropagation();
      onTabClose(tabId);
    },
    [onTabClose]
  );

  // ---------------------------------------------------------------------------
  // Pin toggle previously lived here (task 092). Removed in task 098 — pin UX
  // now lives only in WorkspacePaneMenu's "Select Workspace" section. The
  // localStorage contract (`spaarke:workspace:pinned-list`) and the cold-load
  // auto-open behavior in WorkspacePane are unchanged.
  // ---------------------------------------------------------------------------
  // Fluent TabList value — must be a string matching the selected Tab's value.
  // ---------------------------------------------------------------------------

  const handleTabListSelect = React.useCallback(
    (_e: React.SyntheticEvent, data: { value: unknown }): void => {
      if (typeof data.value === "string") {
        onTabChange(data.value);
      }
    },
    [onTabChange]
  );

  return (
    <div className={styles.root}>
      {/* ------------------------------------------------------------------ */}
      {/* Tab bar                                                              */}
      {/*                                                                      */}
      {/* Task 107 (2026-05-22) layout:                                        */}
      {/*   [arrowLeft] [tabScroll containing TabList] [arrowRight]            */}
      {/* Arrow buttons stay rendered (visibility: hidden when unreachable)    */}
      {/* so the tab strip width doesn't jitter as overflow state changes.     */}
      {/* ------------------------------------------------------------------ */}
      <div className={styles.tabBar}>
        <Button
          className={mergeClasses(
            styles.arrowButton,
            !canScrollLeft && styles.arrowHidden,
          )}
          appearance="subtle"
          size="small"
          icon={<ChevronLeft20Regular />}
          aria-label="Scroll tabs left"
          aria-hidden={!canScrollLeft}
          tabIndex={canScrollLeft ? 0 : -1}
          onClick={handleScrollLeft}
          data-testid="workspace-tabs-scroll-left"
        />

        <div ref={scrollContainerRef} className={styles.tabScroll}>
          <TabList
            className={styles.tabList}
            selectedValue={activeTabId ?? undefined}
            onTabSelect={handleTabListSelect}
            size="small"
            appearance="subtle"
          >
            {tabs.map((tab) => {
            // Task 098 (2026-05-22): the inline per-tab pin button was
            // removed (operator: "pin belongs in the workspace selection
            // surface, not on every open tab"). Tab rows now contain only
            // the label + close affordance.
            return (
              <Tab
                key={tab.id}
                value={tab.id}
                data-testid={`workspace-tab-${tab.id}`}
              >
                <div className={styles.tabContent}>
                  {tab.isLoading ? (
                    <span className={styles.tabLoadingBadge}>
                      <Spinner size="extra-tiny" />
                      <span className={styles.tabLabel}>{tab.displayName}</span>
                    </span>
                  ) : (
                    <span className={styles.tabLabel} title={tab.displayName}>
                      {tab.displayName}
                    </span>
                  )}

                  <Tooltip
                    content={`Close ${tab.displayName}`}
                    relationship="label"
                    positioning="below"
                  >
                    <Button
                      className={mergeClasses(styles.closeButton)}
                      appearance="subtle"
                      icon={<Dismiss12Regular />}
                      size="small"
                      aria-label={`Close ${tab.displayName}`}
                      data-testid={`workspace-tab-close-${tab.id}`}
                      onClick={(e) => handleCloseClick(e, tab.id)}
                    />
                  </Tooltip>
                </div>
              </Tab>
            );
          })}
          </TabList>
        </div>

        <Button
          className={mergeClasses(
            styles.arrowButton,
            !canScrollRight && styles.arrowHidden,
          )}
          appearance="subtle"
          size="small"
          icon={<ChevronRight20Regular />}
          aria-label="Scroll tabs right"
          aria-hidden={!canScrollRight}
          tabIndex={canScrollRight ? 0 : -1}
          onClick={handleScrollRight}
          data-testid="workspace-tabs-scroll-right"
        />
      </div>

      {/* ------------------------------------------------------------------ */}
      {/* Active tab content                                                   */}
      {/* ------------------------------------------------------------------ */}
      <div className={styles.content}>
        {/* R6 Pillar 9 / task 098 — per-tab "Visible to assistant" toggle.
            Rendered above the widget content (not inline in the tab strip —
            tab labels stay clean). Only shows when:
              - we have an active tab AND
              - we have a chat session (need for ADR-015-scoped event) AND
              - the host wired onToggleVisibility (otherwise it's read-only). */}
        {activeTab !== null && chatSessionId && onToggleVisibility ? (
          <div className={styles.visibilityBar}>
            <AddToAssistantToggle
              tabId={activeTab.id}
              sessionId={chatSessionId}
              visibleToAssistant={activeTab.visibleToAssistant}
              onChange={(next) => onToggleVisibility(activeTab.id, next)}
            />
          </div>
        ) : null}

        {activeTab !== null ? (
          <ActiveWidgetContent tab={activeTab} styles={styles} />
        ) : (
          <div className={styles.errorState}>
            <WarningRegular className={styles.errorIcon} />
            <Text size={300}>No active tab</Text>
          </div>
        )}
      </div>
    </div>
  );
}
