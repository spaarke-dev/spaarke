/**
 * NavigatorBody — the Navigator contributor's content (spaarke-side-pane-
 * navigation-history-r1 task 040, spec FR-01 + FR-11).
 *
 * A SHARED component (NFR-08): authored so a phase-2 dashboard widget can
 * mount it directly (its own top-level app, own FluentProvider) without
 * rework — no dependency on SprkSidePaneHost internals beyond the
 * `SidePaneContributorProps` shape it is registered under in `main.tsx`.
 *
 * This task ships the SCAFFOLD ONLY: a 3-tab layout (Recent / Pinned /
 * Views) + a persistent top-of-pane search-bar placeholder. No data wiring —
 * later tasks (041/042 Recent, 050/051/052 Pinned, 060 Views, 070 search)
 * fill each tab and wire the search box.
 *
 * ADR-021 compliance:
 *  - Fluent v9 tokens only (no hardcoded colors) — light/dark handled by
 *    whichever ambient `FluentProvider` wraps this component (normally
 *    `SprkSidePaneHost`'s root provider).
 *  - `--sprk-ui-scale` (NFR-07): this component resolves its OWN theme via
 *    `resolveCodePageTheme()` + `useUiScale()`/`scaleTheme()` — the SAME
 *    mechanism `SprkSidePaneHost` uses — because `SidePaneContributorProps`
 *    carries only `paneId`, not a theme value, and this component must stay
 *    self-sufficient for standalone (non-SprkSidePaneHost) reuse (NFR-08).
 *  - Portal re-wrap: the search-bar info Tooltip renders through a Portal;
 *    per the fluent-v9-portal-gotcha pattern (Option A, matching
 *    `SprkSidePaneHost`'s rail-icon Tooltip precedent) its content is
 *    explicitly re-wrapped in its own `FluentProvider` rather than relying
 *    on the ambient root provider's `applyStylesToPortals` default alone.
 *
 * No-Xrm case: `getXrm()` (3-frame walk, never throws) is checked at render;
 * when undefined (non-MDA host, e.g. a bare preview) this renders a benign
 * empty state instead of the tab scaffold.
 *
 * @see main.tsx — registers this component as the sole SprkSidePaneHost
 *      contributor for the NavigatorPane webresource bundle.
 * @see sidePaneRegistry (SidePaneContributorProps contract)
 * @see ADR-006 (Vite code-page-as-webresource-pane)
 * @see ADR-021 (Fluent v9 tokens, portal re-wrap, light/dark, --sprk-ui-scale)
 * @see ADR-022 (React-16/17-safe shared-lib code the theme/scale hooks come from)
 */

import * as React from 'react';
import {
  Button,
  FluentProvider,
  Input,
  Tab,
  TabList,
  Tooltip,
  makeStyles,
  shorthands,
  tokens,
  type SelectTabData,
  type SelectTabEvent,
} from '@fluentui/react-components';
import { Info16Regular, Search20Regular } from '@fluentui/react-icons';
import {
  getXrm,
  resolveCodePageTheme,
  scaleTheme,
  setupCodePageThemeListener,
  useUiScale,
} from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props NavigatorBody accepts. `paneId` mirrors `SidePaneContributorProps`
 * but is OPTIONAL here (not just required-as-registered) so a future
 * standalone/dashboard-widget consumer can mount this component without a
 * pane id — a component typed to accept an optional prop remains structurally
 * assignable to `SidePaneContributorComponent`'s `React.ComponentType<{ paneId: string }>`.
 */
export interface NavigatorBodyProps {
  /** The hosting pane's id, when rendered as an `SprkSidePaneHost` contributor. */
  paneId?: string;
}

const NAVIGATOR_TABS = ['recent', 'pinned', 'views'] as const;
type NavigatorTabValue = (typeof NAVIGATOR_TABS)[number];

const TAB_LABELS: Record<NavigatorTabValue, string> = {
  recent: 'Recent',
  pinned: 'Pinned',
  views: 'Views',
};

const TAB_PLACEHOLDER_TEXT: Record<NavigatorTabValue, string> = {
  recent: 'Recently viewed and edited records will appear here.',
  pinned: 'Pinned records, bookmarks, and monitored items will appear here.',
  views: 'Your saved views will appear here.',
};

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  searchRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },
  searchInput: {
    flexGrow: 1,
    minWidth: 0,
  },
  searchInfoIcon: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
  },
  tabPanel: {
    flexGrow: 1,
    minHeight: 0,
    overflowY: 'auto',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    ...shorthands.padding(tokens.spacingVerticalM, '0'),
  },
  emptyState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalL),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    textAlign: 'center',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const NavigatorBody: React.FC<NavigatorBodyProps> = ({ paneId }) => {
  const styles = useStyles();

  const [selectedTab, setSelectedTab] = React.useState<NavigatorTabValue>('recent');

  // ── Theme + --sprk-ui-scale (ADR-021 / NFR-07) — resolved independently of
  // any parent so this component stays reusable standalone (NFR-08). ──
  const [baseTheme, setBaseTheme] = React.useState(() => resolveCodePageTheme());
  const { uiScale } = useUiScale();
  const resolvedTheme = React.useMemo(() => scaleTheme(baseTheme, uiScale), [baseTheme, uiScale]);

  React.useEffect(() => setupCodePageThemeListener(setBaseTheme), []);

  const handleTabSelect = React.useCallback((_event: SelectTabEvent, data: SelectTabData) => {
    setSelectedTab(data.value as NavigatorTabValue);
  }, []);

  // ── No-Xrm case: render a safe empty state, never throw. ──
  const xrm = getXrm();
  if (!xrm) {
    return (
      <div className={styles.emptyState} data-testid="navigator-body-no-xrm">
        Navigator is unavailable outside a Dataverse session.
      </div>
    );
  }

  return (
    <div className={styles.root} data-testid="navigator-body" data-pane-id={paneId}>
      <div className={styles.searchRow}>
        <Input
          className={styles.searchInput}
          contentBefore={<Search20Regular />}
          placeholder="Search Recent, Pinned, Views…"
          aria-label="Search Navigator"
          data-testid="navigator-search-placeholder"
        />
        <Tooltip
          // "description" (not "label"): this trigger already has its own
          // aria-label ("Search availability") — relationship="label" would
          // REPLACE that accessible name with the tooltip's (different) text
          // instead of adding an aria-describedby. Code-review fix (task 040).
          relationship="description"
          withArrow={false}
          content={
            // ADR-021 portal re-wrap (Option A) — explicit, matching the
            // SprkSidePaneHost rail-icon Tooltip precedent rather than
            // relying on an ambient applyStylesToPortals default alone.
            <FluentProvider theme={resolvedTheme} applyStylesToPortals={true}>
              <span>Search wiring lands in a later task.</span>
            </FluentProvider>
          }
        >
          {/* A `Button` (not a bare icon) so the tooltip trigger is keyboard-
              focusable — WCAG 2.1 AA (ADR-021 MUST). Code-review fix (task 040). */}
          <Button
            appearance="transparent"
            size="small"
            icon={<Info16Regular />}
            aria-label="Search availability"
            className={styles.searchInfoIcon}
            data-testid="navigator-search-info-icon"
          />
        </Tooltip>
      </div>

      <TabList
        selectedValue={selectedTab}
        onTabSelect={handleTabSelect}
        data-testid="navigator-tabs"
      >
        {NAVIGATOR_TABS.map(tab => (
          <Tab key={tab} value={tab} data-testid={`navigator-tab-${tab}`}>
            {TAB_LABELS[tab]}
          </Tab>
        ))}
      </TabList>

      <div
        className={styles.tabPanel}
        role="tabpanel"
        data-testid={`navigator-tab-panel-${selectedTab}`}
      >
        {TAB_PLACEHOLDER_TEXT[selectedTab]}
      </div>
    </div>
  );
};

export default NavigatorBody;
