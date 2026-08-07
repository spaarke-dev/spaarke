/**
 * PortalWorkspaceShell — the module-host workspace chassis BODY (task 011 §3–§5).
 *
 * The SpaarkeAi-style workspace foundation for the external Legal Service Portal:
 * a tab host (pinned non-closable "Quick Start" home + closable widget tabs via
 * `TabStrip`) over a pane layout (tab strip → active-tab body), with a persistent,
 * collapsible AI Assistant that docks LEFT or RIGHT. The branded portal header is
 * owned by `AppHeader` (App-level chrome); this component is the body beneath it.
 *
 * Ported (Xrm-free, token-only) from the owner-approved P0 prototype
 * (`.../2026-08-external-access-module-host/src/components/WorkspaceShell.tsx`),
 * adapted from the prototype's mock `me`/widget-registry to production props: a
 * `WidgetTab[]` + a `renderWidget(id)` seam that task 012 fills with the real
 * entitlement-gated widget registry. Named `PortalWorkspaceShell` to avoid
 * collision with the shared config-driven `WorkspaceShell` in `@spaarke/ui-components`
 * (a dashboard renderer, not a tabbed/docked chassis).
 *
 * §11: no shared-library tabbed+docked shell exists (the shared `WorkspaceShell` is
 * a config dashboard; SpaarkeAi's `WorkspacePane` is Xrm/ai-widgets-coupled and not
 * exported). This chassis is built from Fluent v9 primitives + the bespoke `TabStrip`
 * (its own §11 justification). Semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import {
  makeStyles,
  tokens,
  mergeClasses,
  Text,
  Title3,
  Button,
  Tooltip,
} from '@fluentui/react-components';
import {
  AddRegular,
  AppsAddInRegular,
  SparkleRegular,
  HomeRegular,
  PanelLeftRegular,
  PanelRightRegular,
  PanelLeftContractRegular,
  PanelRightContractRegular,
  PanelLeftExpandRegular,
  PanelRightExpandRegular,
} from '@fluentui/react-icons';
import { TabStrip, type TabItem } from './TabStrip';
import { QUICK_START_TAB, type WidgetTab } from './useWorkspaceTabs';

const useStyles = makeStyles({
  bodyRow: { display: 'flex', flexGrow: 1, minHeight: 0, minWidth: 0 },
  main: { display: 'flex', flexDirection: 'column', flexGrow: 1, minWidth: 0 },
  tabBar: {
    display: 'flex',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexWrap: 'wrap',
  },
  tabActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
    paddingBottom: tokens.spacingVerticalS,
  },
  widgetBody: {
    flexGrow: 1,
    minWidth: 0,
    minHeight: 0,
    overflowY: 'auto',
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalXXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },
  empty: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    minHeight: '300px',
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  emptyIcon: { fontSize: '40px', color: tokens.colorNeutralForeground3 },
  // ── Assistant dock (left OR right) ─────────────────────────────────────────
  dock: {
    width: '400px',
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  dockRight: {
    borderLeftWidth: tokens.strokeWidthThin,
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorNeutralStroke2,
  },
  dockLeft: {
    borderRightWidth: tokens.strokeWidthThin,
    borderRightStyle: 'solid',
    borderRightColor: tokens.colorNeutralStroke2,
  },
  dockHead: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  dockTitle: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  dockActions: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXXS },
  dockBody: {
    flexGrow: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalM,
  },
  dockRail: {
    width: '44px',
    flexShrink: 0,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalM,
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  railRight: {
    borderLeftWidth: tokens.strokeWidthThin,
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorNeutralStroke2,
  },
  railLeft: {
    borderRightWidth: tokens.strokeWidthThin,
    borderRightStyle: 'solid',
    borderRightColor: tokens.colorNeutralStroke2,
  },
});

export interface PortalWorkspaceShellProps {
  /** Closable widget tabs (the pinned Quick Start home is added internally). */
  widgetTabs: WidgetTab[];
  /** Active tab id — `QUICK_START_TAB` or a widget id. */
  activeTabId: string;
  onSelectTab: (id: string) => void;
  onCloseTab: (id: string) => void;
  /** Invoked by the "Add widget" affordance (task 012 opens the widget library). */
  onAddWidget: () => void;
  /** Quick Start home node — rendered in the body when the pinned Quick Start tab is active. */
  quickStart: React.ReactNode;
  /** Assistant pane body — omit when the persona has no assistant (FR-26 gating). */
  assistant?: React.ReactNode;
  /** Render the active widget's body (task 012 supplies real widgets). */
  renderWidget?: (widgetId: string) => React.ReactNode;
  assistantCollapsed: boolean;
  onToggleAssistant: () => void;
  /** Assistant dock placement — left OR right. */
  assistantDock: 'left' | 'right';
  /** Move the assistant dock to the other side. */
  onMoveAssistant: () => void;
}

export const PortalWorkspaceShell: React.FC<PortalWorkspaceShellProps> = ({
  widgetTabs,
  activeTabId,
  onSelectTab,
  onCloseTab,
  onAddWidget,
  quickStart,
  assistant,
  renderWidget,
  assistantCollapsed,
  onToggleAssistant,
  assistantDock,
  onMoveAssistant,
}) => {
  const s = useStyles();

  // Quick Start home is always available; fall back to it when no valid widget tab is active.
  const active =
    activeTabId === QUICK_START_TAB || widgetTabs.some((t) => t.id === activeTabId)
      ? activeTabId
      : QUICK_START_TAB;
  const isQuickStart = active === QUICK_START_TAB;

  // Pinned Quick Start (home) tab first + the open widget tabs (closable).
  const tabs: TabItem[] = [
    { id: QUICK_START_TAB, title: 'Quick Start', icon: HomeRegular, closable: false },
    ...widgetTabs.map((w) => ({ id: w.id, title: w.title, icon: w.icon, closable: true })),
  ];

  // Assistant dock (expanded) node — side-aware border.
  const dockNode =
    assistant && !assistantCollapsed ? (
      <aside
        className={mergeClasses(s.dock, assistantDock === 'left' ? s.dockLeft : s.dockRight)}
        aria-label="AI Assistant"
      >
        <div className={s.dockHead}>
          <span className={s.dockTitle}>
            <SparkleRegular aria-hidden="true" />
            <Text weight="semibold">Ask Legal</Text>
          </span>
          <div className={s.dockActions}>
            <Tooltip
              content={assistantDock === 'left' ? 'Move assistant to the right' : 'Move assistant to the left'}
              relationship="label"
            >
              <Button
                appearance="subtle"
                size="small"
                icon={assistantDock === 'left' ? <PanelRightRegular /> : <PanelLeftRegular />}
                aria-label={assistantDock === 'left' ? 'Move assistant to the right' : 'Move assistant to the left'}
                onClick={onMoveAssistant}
              />
            </Tooltip>
            <Tooltip content="Collapse assistant" relationship="label">
              <Button
                appearance="subtle"
                size="small"
                icon={assistantDock === 'left' ? <PanelLeftContractRegular /> : <PanelRightContractRegular />}
                aria-label="Collapse assistant"
                onClick={onToggleAssistant}
              />
            </Tooltip>
          </div>
        </div>
        <div className={s.dockBody}>{assistant}</div>
      </aside>
    ) : null;

  // Collapsed rail node — side-aware border + expand affordance.
  const railNode =
    assistant && assistantCollapsed ? (
      <div className={mergeClasses(s.dockRail, assistantDock === 'left' ? s.railLeft : s.railRight)}>
        <Tooltip content="Open assistant" relationship="label">
          <Button
            appearance="subtle"
            icon={assistantDock === 'left' ? <PanelLeftExpandRegular /> : <PanelRightExpandRegular />}
            aria-label="Open assistant"
            onClick={onToggleAssistant}
          />
        </Tooltip>
        <SparkleRegular aria-hidden="true" />
      </div>
    ) : null;

  const activeWidgetBody = !isQuickStart ? renderWidget?.(active) : null;

  return (
    <div className={s.bodyRow}>
      {/* Assistant docked LEFT (expanded or collapsed rail). */}
      {assistantDock === 'left' && (dockNode ?? railNode)}

      <main className={s.main}>
        <div className={s.tabBar}>
          <TabStrip tabs={tabs} activeId={active} onSelect={onSelectTab} onClose={onCloseTab} />
          <div className={s.tabActions}>
            <Button appearance="primary" icon={<AddRegular />} onClick={onAddWidget}>
              Add widget
            </Button>
          </div>
        </div>

        <div className={s.widgetBody}>
          {isQuickStart ? (
            quickStart
          ) : activeWidgetBody ? (
            activeWidgetBody
          ) : (
            <div className={s.empty}>
              <AppsAddInRegular className={s.emptyIcon} aria-hidden="true" />
              <Title3>Nothing to show here</Title3>
              <Text>Add a widget, or head back to Quick Start.</Text>
              <Button appearance="primary" icon={<AddRegular />} onClick={onAddWidget}>
                Add widget
              </Button>
            </div>
          )}
        </div>
      </main>

      {/* Assistant docked RIGHT (expanded or collapsed rail). */}
      {assistantDock === 'right' && (dockNode ?? railNode)}
    </div>
  );
};

export default PortalWorkspaceShell;
