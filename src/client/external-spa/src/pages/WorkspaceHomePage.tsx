/**
 * WorkspaceHomePage — the module-host workspace shell host (task 011).
 *
 * Repurposed from the former R1 outside-counsel dashboard (now preserved as
 * `OutsideCounselDashboard.tsx`, to be re-homed as widgets in task 016). This is
 * now the SHELL HOST: it owns the workspace tab state + assistant dock state and
 * renders `PortalWorkspaceShell` (the tabbed, dockable chassis) beneath the
 * App-level branded header.
 *
 * Task 011 delivers the EMPTY chassis: a pinned non-closable Quick Start tab
 * (`QuickStartPane` — shared action cards + ChoiceModal) + a dockable assistant
 * placeholder (`AssistantPane`) + an "Add widget" affordance that opens a
 * demonstrable placeholder widget tab (proves add / activate / corner-× close).
 * Task 012 replaces the placeholder open path + widget body with the real
 * entitlement-gated widget registry and role-defaulted tabs.
 *
 * Fluent v9 semantic tokens only — zero hardcoded hex (ADR-021).
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, Text, Title3 } from '@fluentui/react-components';
import { AppsRegular } from '@fluentui/react-icons';
import {
  PortalWorkspaceShell,
  QuickStartPane,
  AssistantPane,
  useWorkspaceTabs,
  type WidgetTab,
} from '../components/shell';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', flexGrow: 1, minHeight: 0 },
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalXL,
    ...shorthands.border(tokens.strokeWidthThin, 'dashed', tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground2,
  },
});

/** Task-011 placeholder widget body. Task 012 supplies real widgets from the registry. */
const PlaceholderWidget: React.FC<{ title: string }> = ({ title }) => {
  const s = useStyles();
  return (
    <div className={s.placeholder}>
      <Title3>{title}</Title3>
      <Text>
        This is a placeholder widget. Role-defaulted widgets (My Requests, Policy Library,
        Messages, Projects, Documents, …) are wired by task 012 on top of this shell chassis.
      </Text>
    </div>
  );
};

export const WorkspaceHomePage: React.FC = () => {
  const s = useStyles();
  // Destructure so callbacks depend on the STABLE hook functions (not the
  // per-render result object), keeping useCallback memoization effective.
  const { widgetTabs, activeTabId, selectTab, openTab, closeTab } = useWorkspaceTabs();

  // Assistant dock: default LEFT per the AI-maturity theory (design.md §12 / ux
  // refinement #5 — the assistant migrates from far-right accessory to first-class
  // left primary pane). Collapsible; movable left/right.
  const [assistantDock, setAssistantDock] = React.useState<'left' | 'right'>('left');
  const [assistantCollapsed, setAssistantCollapsed] = React.useState(false);

  // "Add widget" (task 011 placeholder): open a demonstrable placeholder tab so the
  // add / activate / corner-× close lifecycle is exercisable. Task 012 opens the real
  // entitlement-gated widget library instead.
  const placeholderSeq = React.useRef(0);
  const onAddWidget = React.useCallback(() => {
    placeholderSeq.current += 1;
    const n = placeholderSeq.current;
    const tab: WidgetTab = { id: `placeholder-${n}`, title: `Sample Widget ${n}`, icon: AppsRegular };
    openTab(tab);
  }, [openTab]);

  const renderWidget = React.useCallback(
    (widgetId: string) => {
      const tab = widgetTabs.find((t) => t.id === widgetId);
      return <PlaceholderWidget title={tab?.title ?? 'Widget'} />;
    },
    [widgetTabs],
  );

  return (
    <div className={s.root}>
      <PortalWorkspaceShell
        widgetTabs={widgetTabs}
        activeTabId={activeTabId}
        onSelectTab={selectTab}
        onCloseTab={closeTab}
        onAddWidget={onAddWidget}
        quickStart={<QuickStartPane />}
        assistant={<AssistantPane />}
        renderWidget={renderWidget}
        assistantCollapsed={assistantCollapsed}
        onToggleAssistant={() => setAssistantCollapsed((v) => !v)}
        assistantDock={assistantDock}
        onMoveAssistant={() => setAssistantDock((d) => (d === 'left' ? 'right' : 'left'))}
      />
    </div>
  );
};

export default WorkspaceHomePage;
