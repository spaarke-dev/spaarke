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
  Settings16Regular,
  WarningRegular,
} from "@fluentui/react-icons";
import { WidgetErrorBoundary } from "@spaarke/ui-components";
import type { WorkspaceTab } from "./WorkspaceTabManager";
import type { WorkspaceWidgetProps } from "@spaarke/ai-widgets";
import { useDispatchPaneEvent } from "@spaarke/ai-widgets";
import { SPAARKEAI_TEMPLATE_FILTER } from "../../constants/workspaceTemplateFilter";
import { resolveRuntimeConfig } from "@spaarke/auth";

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
    // FIX #6 (spaarkeai-compose-r2) — the "Visible to assistant" toggle was
    // removed; this bar now holds only the workspace-layout edit gear.
    columnGap: tokens.spacingHorizontalS,
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
}

// ---------------------------------------------------------------------------
// Wizard launch helper — R2 UAT §4.1 (2026-07-03). Gear icon in the visibility
// bar opens the WorkspaceLayoutWizard at the Choose Layout step for the
// active workspace tab. Reuses the existing `sprk_workspacelayoutwizard`
// webresource + navigateTo pattern from ManageWorkspacesPane, plus the R2
// `startAtStep` URL param wired into wizard main.tsx.
// ---------------------------------------------------------------------------

function getXrmForWizard(): unknown {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  return w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
}

const WIZARD_RESULT_STORAGE_KEY = "spaarke:workspace-wizard:last-result";
const WIZARD_RESULT_MAX_AGE_MS = 60_000;

interface WizardDialogResult {
  confirmed: boolean;
  layoutId?: string;
  at?: number;
}

function readWizardDialogResult(): WizardDialogResult | null {
  try {
    const raw = window.sessionStorage?.getItem(WIZARD_RESULT_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as WizardDialogResult;
    if (typeof parsed.at === "number" && Date.now() - parsed.at > WIZARD_RESULT_MAX_AGE_MS) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

function consumeWizardDialogResult(): void {
  try {
    window.sessionStorage?.removeItem(WIZARD_RESULT_STORAGE_KEY);
  } catch { /* storage may be disabled */ }
}

/**
 * Launch the edit wizard for an existing workspace layout. Opens at the
 * wizard's default step for edit mode (Arrange Sections, per §3.1). After the
 * wizard closes, if it reports a successful save via sessionStorage, invoke
 * `onSaved` so the caller can refresh the affected tab (§3.3).
 *
 * Renamed from `launchEditWizardForTab` after operator feedback
 * (2026-07-03 round 2): user wanted the gear icon to open at Arrange rather
 * than Choose Layout.
 */
async function launchEditWizardForTab(
  layoutId: string,
  onSaved?: (savedLayoutId: string) => void,
): Promise<void> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const xrm = getXrmForWizard() as any;
  if (!xrm?.Navigation?.navigateTo) {
    console.warn(
      "[WorkspaceTabManagerComponent] Xrm.Navigation.navigateTo not available — running outside Dataverse host. Gear-icon launch is a no-op.",
    );
    return;
  }

  const bffBaseUrl = (await resolveRuntimeConfig()).bffBaseUrl ?? "";
  const parts: string[] = [
    "mode=edit",
    `bffBaseUrl=${encodeURIComponent(bffBaseUrl)}`,
    `layoutId=${encodeURIComponent(layoutId)}`,
    `templateFilter=${encodeURIComponent(SPAARKEAI_TEMPLATE_FILTER.join(","))}`,
    // Default edit behavior: land on Arrange Sections. No `startAtStep`
    // override needed (App.tsx sets it internally from `mode === "edit"`).
  ];
  const data = parts.join("&");

  try {
    await xrm.Navigation.navigateTo(
      {
        pageType: "webresource",
        webresourceName: "sprk_workspacelayoutwizard",
        data,
      },
      {
        target: 2,
        width: { value: 60, unit: "%" },
        height: { value: 70, unit: "%" },
        title: "Edit Workspace",
      },
    );
  } catch (err: unknown) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const code = (err as any)?.errorCode;
    if (code !== 2) {
      console.warn("[WorkspaceTabManagerComponent] Wizard launch error:", err);
    }
  }

  const dialogResult = readWizardDialogResult();
  if (dialogResult?.confirmed && onSaved) {
    onSaved(dialogResult.layoutId ?? layoutId);
    consumeWizardDialogResult();
  }
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
  hideTabBar = false,
}: WorkspaceTabManagerComponentProps): React.JSX.Element {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // Resolve the active tab record.
  const activeTab = tabs.find((t) => t.id === activeTabId) ?? null;

  // ---------------------------------------------------------------------------
  // Compose keep-alive (UAT round-7 #1)
  //
  // The single Compose DIRECT tab (widgetType 'compose', allowMultiple:false)
  // is kept MOUNTED across tab switches and toggled hidden when it is not the
  // active tab. Rendering only the active tab (the default for every other tab
  // type) UNMOUNTS ComposeWorkspace on a Daily Briefing ↔ Compose switch,
  // destroying its live local reducer state (the loaded doc). Keeping it
  // mounted-hidden preserves that state with zero re-fetch.
  //
  // Scoped to 'compose' ONLY — every other inactive tab still unmounts (the
  // memory/connection design for multi-instance widgets is unchanged). Only
  // one compose tab can exist, so this keeps at most one extra widget mounted.
  // ---------------------------------------------------------------------------
  const composeTab = tabs.find((t) => t.widgetType === "compose") ?? null;
  const activeIsCompose =
    activeTab !== null && composeTab !== null && activeTab.id === composeTab.id;

  // R2 UAT §3.3 (2026-07-03): after gear-icon edit wizard closes with a save,
  // close the affected tab and dispatch a fresh widget_load so the tab
  // remounts with the new layout. Force-remount is simpler than plumbing a
  // per-widget "refresh" method through the registry.
  const handleGearIconClick = React.useCallback(
    (tabId: string, layoutId: string, layoutName: string) => {
      void launchEditWizardForTab(layoutId, (savedLayoutId) => {
        // Close the old tab; re-dispatch widget_load with the saved id so a
        // fresh tab renders the updated layout.
        onTabClose(tabId);
        dispatch("workspace", {
          type: "widget_load",
          widgetType: "workspace",
          widgetData: { layoutId: savedLayoutId, layoutName },
          displayName: layoutName,
        });
      });
    },
    [onTabClose, dispatch],
  );

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
      )}

      {/* ------------------------------------------------------------------ */}
      {/* Active tab content                                                   */}
      {/* ------------------------------------------------------------------ */}
      <div className={styles.content}>
        {/* FIX #6 (spaarkeai-compose-r2) — the per-tab "Visible to assistant"
            toggle was REMOVED. The Assistant's active document now follows the
            ACTIVE tab (WorkspacePane drives register/withdraw on tab activation
            via the compose visibility conduit) — visibility is implicit, no UI.
            What remains here is the workspace-LAYOUT edit gear (R2 UAT §4.1):
            it opens the edit wizard for the active workspace layout. It is
            shown ONLY for workspace-layout tabs (widgetType === "workspace")
            with a resolvable layoutId — so it never appears on the Compose
            surface (widgetType 'compose'), and the bar itself does not render
            for compose tabs. */}
        {activeTab !== null &&
         activeTab.widgetType === "workspace" &&
         (activeTab.widgetData as { layoutId?: string } | null)?.layoutId ? (
          <div className={styles.visibilityBar}>
            <Tooltip content="Edit workspace layout" relationship="label" withArrow>
              <Button
                appearance="subtle"
                size="small"
                icon={<Settings16Regular />}
                aria-label="Edit workspace layout"
                onClick={() =>
                  handleGearIconClick(
                    activeTab.id,
                    (activeTab.widgetData as { layoutId: string }).layoutId,
                    (activeTab.widgetData as { layoutName?: string }).layoutName
                      ?? activeTab.displayName
                      ?? "Workspace",
                  )
                }
                data-testid="workspace-edit-gear"
              />
            </Tooltip>
          </div>
        ) : null}

        {/* Compose keep-alive host (UAT round-7 #1). Mounted whenever a compose
            tab exists; hidden (display:none, aria-hidden) when it is not the
            active tab so ComposeWorkspace's live state survives tab switches.
            When compose IS the active tab this host is the visible surface —
            the active-tab branch below renders nothing for it (no double-mount). */}
        {composeTab !== null ? (
          <div
            className={mergeClasses(
              styles.composeKeepAlive,
              !activeIsCompose && styles.composeKeepAliveHidden,
            )}
            data-testid="workspace-compose-keepalive"
            aria-hidden={!activeIsCompose}
          >
            <ActiveWidgetContent tab={composeTab} styles={styles} />
          </div>
        ) : null}

        {activeTab !== null && !activeIsCompose ? (
          <ActiveWidgetContent tab={activeTab} styles={styles} />
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
