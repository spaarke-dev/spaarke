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
  DismissCircle16Filled,
  WarningRegular,
} from "@fluentui/react-icons";
import { WidgetErrorBoundary } from "@spaarke/ui-components";
import type { WorkspaceTab } from "./WorkspaceTabManager";
import type { WorkspaceWidgetProps } from "@spaarke/ai-widgets";

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

  // UAT round-4 (item #10a): fixed-size leading slot for the tiny background-review spinner on a
  // Compose tab, so the label column stays aligned whether or not the spinner is present.
  reviewSpinnerSlot: {
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
    flexShrink: 0,
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
    color: tokens.colorNeutralForeground2,
  },

  // UAT 2026-07-20: the selected tab reads in brand blue (semibold) so the
  // active workspace is unmistakable. Semantic tokens only (ADR-021) so dark
  // mode inverts correctly.
  tabLabelSelected: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
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

  // Close button — UAT 2026-07-20: the × is now a circular dismiss glyph
  // (DismissCircle16). On the SELECTED tab it fills brand blue with a white
  // glyph (see `closeButtonSelected` + the Filled icon in JSX); on other tabs
  // it is a subtle neutral outline circle that brightens on hover. Button hit
  // area kept at 18×18 (the surrounding tab is the primary 40px target).
  closeButton: {
    minWidth: "unset",
    height: "18px",
    width: "18px",
    padding: "0",
    flexShrink: 0,
    borderRadius: tokens.borderRadiusCircular,
    color: tokens.colorNeutralForeground3,
    ":hover": {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  // Selected-tab close glyph — brand blue so the circled × matches the blue
  // active-tab label. Hover keeps the brand color (the filled circle already
  // reads as an affordance).
  closeButtonSelected: {
    color: tokens.colorBrandForeground1,
    ":hover": {
      color: tokens.colorBrandForeground1,
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

  // UAT round-7 #1 — keep-alive host for the single Compose tab. Fills the
  // content area like a direct ActiveWidgetContent child (flex column) so the
  // mounted ComposeWorkspace lays out identically whether it is the active tab
  // or a hidden keep-alive. The hidden variant collapses it via display:none
  // while KEEPING it mounted, so ComposeWorkspace's local reducer state
  // (docxBytes/seedHtml/sessionId/editor) survives a tab switch with no
  // re-fetch or remount.
  composeKeepAlive: {
    flex: 1,
    minHeight: 0,
    display: "flex",
    flexDirection: "column",
  },
  composeKeepAliveHidden: {
    display: "none",
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
   * Task 025 (spec FR-09) — called when a widget reports a live data-change patch via its
   * `onDataChange` prop (e.g. AnalysisEditorWidget persisting in-progress edit state). Forwards
   * to `WorkspaceTabManager.updateTab(tabId, mergedData)` so the edit rides the existing tab
   * persistence write-through. Optional — omitted in contexts that don't wire tab persistence
   * (e.g. isolated unit tests).
   */
  onTabDataChange?: (tabId: string, patch: unknown) => void;

  /**
   * When true, suppress the tab-bar strip and render only the active
   * widget content area. Used by `spaarkeai-compose-r1` task 100 (Phase 10
   * polish, FR-S7): when SpaarkeAi is launched in `composeMode="editor"`,
   * the user is locked to the Compose surface and MUST NOT see the
   * workspace-tab UI (Matters, Documents, Daily Briefing, etc.).
   *
   * The active widget still renders normally. The widget-add extensibility
   * (via PaneEventBus `widget_load` events) is also preserved — new tabs
   * can still be added to the manager state; they simply have no visible
   * switcher UI in compose mode. Future Compose-focused widgets can layer
   * on top by dispatching `widget_load`.
   *
   * Defaults to `false` so non-compose consumers see no behaviour change.
   */
  hideTabBar?: boolean;

  /**
   * UAT round-4 (item #10a): true while a review whose progress modal was dismissed ("Continue working
   * in background") is STILL running server-side. When true, a tiny circular progress indicator (Fluent
   * `Spinner size="tiny"`) is rendered on every `compose` tab header - the run's liveness after the modal
   * has been dismissed and fully unmounted. Cleared when the run completes (completion then surfaces via
   * the existing `ReviewCompleteToast`). Defaults to `false` (no indicator).
   *
   * The indicator is scoped to `compose` tabs because an agreement review always runs against a document
   * open in a Compose tab; a non-compose tab never hosts a review, so it never shows the spinner.
   */
  composeReviewRunning?: boolean;
}

// ---------------------------------------------------------------------------
// ActiveWidgetContent — renders the active tab's resolved widget
// ---------------------------------------------------------------------------

interface ActiveWidgetContentProps {
  tab: WorkspaceTab;
  styles: ReturnType<typeof useStyles>;
  /**
   * spaarkeai-compose-r2 (multi-Compose-tab): whether this tab is the ACTIVE (visible) tab.
   * Defaults to `true`. Forwarded to the widget so keep-alive widgets (Compose) mounted-hidden
   * while inactive can suppress "I am the active surface" side effects (active-document claim).
   */
  isActiveTab?: boolean;
  /** Task 025 (FR-09) — see WorkspaceTabManagerComponentProps.onTabDataChange. */
  onTabDataChange?: (tabId: string, patch: unknown) => void;
}

function ActiveWidgetContent({
  tab,
  styles,
  isActiveTab = true,
  onTabDataChange,
}: ActiveWidgetContentProps): React.JSX.Element {
  // Loading — registry promise not yet resolved.
  if (tab.isLoading || tab.Component === null) {
    return (
      <div className={styles.loadingState}>
        <Spinner size="medium" label={`Loading ${tab.displayName}…`} />
      </div>
    );
  }

  const Widget = tab.Component as React.ComponentType<WorkspaceWidgetProps>;

  // Task 025 (FR-09): a stable per-tab callback so a widget's live edit-state patch (e.g.
  // AnalysisEditorWidget's in-progress draft) reaches WorkspaceTabManager.updateTab and rides
  // the existing tab-persistence write-through. Undefined when the host didn't wire
  // onTabDataChange (e.g. isolated unit-test render) — the widget's own onDataChange prop is
  // then undefined too, which every widget must already tolerate (optional prop).
  const onDataChange = onTabDataChange
    ? (patch: unknown): void => onTabDataChange(tab.id, patch)
    : undefined;

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
          tabId={tab.id}
          isActiveTab={isActiveTab}
          onDataChange={onDataChange}
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
  onTabDataChange,
  hideTabBar = false,
  composeReviewRunning = false,
}: WorkspaceTabManagerComponentProps): React.JSX.Element {
  const styles = useStyles();

  // Resolve the active tab record.
  const activeTab = tabs.find((t) => t.id === activeTabId) ?? null;

  // ---------------------------------------------------------------------------
  // Compose keep-alive (UAT round-7 #1; multi-tab — spaarkeai-compose-r2)
  //
  // EVERY Compose DIRECT tab (widgetType 'compose') is kept MOUNTED across tab
  // switches and toggled hidden when it is not the active tab. Rendering only
  // the active tab (the default for every other tab type) UNMOUNTS
  // ComposeWorkspace on a switch, destroying its live local reducer state (the
  // loaded doc). Keeping each mounted-hidden preserves that state with zero
  // re-fetch.
  //
  // Multiple Compose tabs can now be open simultaneously — one keep-alive host
  // per compose tab, ALL mounted, only the active one visible. Scoped to
  // 'compose' ONLY: every other inactive tab still unmounts (the memory/
  // connection design for multi-instance widgets is unchanged).
  // ---------------------------------------------------------------------------
  const composeTabs = tabs.filter((t) => t.widgetType === "compose");
  const activeIsCompose =
    activeTab !== null && activeTab.widgetType === "compose";

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
      {!hideTabBar && (
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
            //
            // UAT 2026-07-20: the selected tab's label turns brand blue and
            // its close × becomes a filled brand circle so the active
            // workspace is unmistakable.
            const isSelected = tab.id === activeTabId;
            // UAT round-4 (item #10a): a dismissed-but-still-running review shows a tiny circular
            // progress indicator on its Compose tab header, until the run completes.
            const showReviewSpinner = composeReviewRunning && tab.widgetType === "compose";
            return (
              <Tab
                key={tab.id}
                value={tab.id}
                data-testid={`workspace-tab-${tab.id}`}
              >
                <div className={styles.tabContent}>
                  {showReviewSpinner ? (
                    <span
                      className={styles.reviewSpinnerSlot}
                      data-testid={`workspace-tab-review-spinner-${tab.id}`}
                      role="status"
                      aria-label="Review running in the background"
                      title="Review running in the background"
                    >
                      <Spinner size="extra-tiny" />
                    </span>
                  ) : null}
                  {tab.isLoading ? (
                    <span className={styles.tabLoadingBadge}>
                      <Spinner size="extra-tiny" />
                      <span className={styles.tabLabel}>{tab.displayName}</span>
                    </span>
                  ) : (
                    <span
                      className={mergeClasses(
                        styles.tabLabel,
                        isSelected && styles.tabLabelSelected,
                      )}
                      title={tab.tooltip ?? tab.displayName}
                    >
                      {tab.displayName}
                    </span>
                  )}

                  {/* UAT 2026-07-21: the close affordance shows ONLY on the
                      active tab — inactive tabs render no × (activate first to
                      close). The glyph is a filled brand circle to match the
                      blue active-tab label. */}
                  {isSelected ? (
                    <Tooltip
                      content={`Close ${tab.displayName}`}
                      relationship="label"
                      positioning="below"
                    >
                      <Button
                        className={mergeClasses(
                          styles.closeButton,
                          styles.closeButtonSelected,
                        )}
                        appearance="subtle"
                        icon={<DismissCircle16Filled />}
                        size="small"
                        aria-label={`Close ${tab.displayName}`}
                        data-testid={`workspace-tab-close-${tab.id}`}
                        onClick={(e) => handleCloseClick(e, tab.id)}
                      />
                    </Tooltip>
                  ) : null}
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
      )}

      {/* ------------------------------------------------------------------ */}
      {/* Active tab content                                                   */}
      {/* ------------------------------------------------------------------ */}
      <div className={styles.content}>
        {/* UAT 2026-07-20: the workspace-LAYOUT edit gear was removed from the
            content area per operator request ("remove the workspace layout
            gear"). Editing a workspace layout is still available from the
            Manage Workspaces pane's per-row "⋯ → Edit" action. The per-tab
            "Visible to assistant" toggle had already been removed (FIX #6,
            spaarkeai-compose-r2), so the whole visibility bar is now gone. */}

        {/* Compose keep-alive hosts (UAT round-7 #1; multi-tab — spaarkeai-compose-r2).
            One host PER compose tab, ALL mounted; each hidden (display:none,
            aria-hidden) when it is not the active tab so every ComposeWorkspace's
            live reducer state survives tab switches. The active compose tab's host
            is the visible surface — the active-tab branch below renders nothing for
            a compose active tab (no double-mount). */}
        {composeTabs.map((composeTab) => {
          const isActiveComposeTab = composeTab.id === activeTabId;
          return (
            <div
              key={composeTab.id}
              className={mergeClasses(
                styles.composeKeepAlive,
                !isActiveComposeTab && styles.composeKeepAliveHidden,
              )}
              data-testid="workspace-compose-keepalive"
              data-tab-id={composeTab.id}
              aria-hidden={!isActiveComposeTab}
            >
              <ActiveWidgetContent
                tab={composeTab}
                styles={styles}
                isActiveTab={isActiveComposeTab}
                onTabDataChange={onTabDataChange}
              />
            </div>
          );
        })}

        {activeTab !== null && !activeIsCompose ? (
          <ActiveWidgetContent
            tab={activeTab}
            styles={styles}
            isActiveTab
            onTabDataChange={onTabDataChange}
          />
        ) : activeTab === null ? (
          <div className={styles.errorState}>
            <WarningRegular className={styles.errorIcon} />
            <Text size={300}>No active tab</Text>
          </div>
        ) : null}
      </div>
    </div>
  );
}
