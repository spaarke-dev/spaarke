/**
 * MonitoredTab — Monitored tab content
 * (spaarke-side-pane-navigation-history-r1 task 052, spec FR-09 / OQ-1b,
 * design.md §6c; promoted to its OWN top-level tab by the UAT-driven
 * redesign — previously a nested `<section>` inside `PinnedTab.tsx`).
 *
 * Renders the shared record-level `sprk_monitor` flag, scoped to the current
 * user (owner-scoped for r1 — see `monitoredService.ts`'s module docblock
 * "Scoping" for the escalated assigned-to-me deferral). READ-ONLY: reads via
 * `monitoredService.listMonitoredByMe` (N per-entity queries against
 * `sprk_matter`/`sprk_project`/`sprk_document`/`sprk_todo`/`sprk_event`/
 * `sprk_workassignment`/`sprk_invoice`), no star/pin affordance, no write of
 * any kind. UI copy explains the shared-flag semantics (setting/clearing
 * `sprk_monitor` affects everyone; last-writer-wins) so a user does not
 * mistake this list for a personal one they exclusively control.
 *
 * UAT redesign: this file is the extracted former "Monitored" `<section>`
 * from `PinnedTab.tsx` (task 052) — same data path
 * (`monitoredService.listMonitoredByMe`), same semantics-note copy, same
 * read-only/no-navigate-affordance-beyond-click contract. The only render
 * changes are: (1) it is now its own tab rather than a nested section, and
 * (2) each row's type-chip `Badge` pill is REMOVED, replaced by the shared
 * far-left `rowIconFor` icon (mirrors `RecentTab.tsx`/`BookmarksTab.tsx`).
 *
 * **Read-time security trimming (task 080) — DELIBERATELY NOT applied here**:
 * `listMonitoredByMe` is itself a LIVE query against the target entities (not
 * a cached label) issued fresh on every mount, and Dataverse row-level
 * security means a record the user cannot read is never returned by that
 * query in the first place — identical reasoning to `RecentTab.tsx`'s former
 * Edited-mode exemption. Re-checking already-live, already-security-filtered
 * rows would be a redundant extra `retrieveRecord` per row with no
 * confidentiality benefit (CLAUDE.md §11).
 *
 * Host-context only (project constraint): reads via `Xrm.WebApi`
 * (`monitoredService.listMonitoredByMe`), navigates via `Xrm.Navigation`.
 * NEVER writes `sprk_monitor` — toggling it happens on the record's own form
 * (`TrackingFieldTrio`), never from this Navigator surface.
 *
 * ADR-021: Fluent v9 tokens only; no portal-rendered component here.
 * ADR-022: only React-16/17-safe APIs used (no `createRoot`) — not shared-lib
 * code today (lives in the Code Page bundle, React 19).
 *
 * @see monitoredService.ts — the SEPARATE, read-only data path this tab renders
 * @see rowIcon.tsx — the shared far-left record-type icon this tab (and RecentTab.tsx/BookmarksTab.tsx) renders
 * @see NavigatorBody.tsx — mounts this component in the `monitored` tab panel
 */

import * as React from 'react';
import { Caption1, Spinner, Text, makeStyles, shorthands, tokens } from '@fluentui/react-components';
import { Info16Regular } from '@fluentui/react-icons';
import { getXrm, type XrmContext } from '@spaarke/ui-components';
import { NavItemPageType } from '@spaarke/ui-components/services/navigator/navItemRepository';
import { listMonitoredByMe, type MonitoredItem } from '../services/monitoredService';
import { rowIconFor } from '../rowIcon';

// ─────────────────────────────────────────────────────────────────────────────
// Navigation
// ─────────────────────────────────────────────────────────────────────────────

/** Navigate to a Monitored row's target — always `entityrecord` (a direct entity-table query). */
function navigateToMonitoredItem(xrm: XrmContext, item: MonitoredItem): void {
  const navigation = xrm.Navigation;
  if (!navigation) return;
  void navigation.navigateTo({
    pageType: 'entityrecord',
    entityName: item.targetLogicalName,
    entityId: item.targetId,
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    // Clear vertical whitespace between the semantics note and the list (UAT polish).
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  centeredState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    minHeight: '80px',
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalL),
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalSNudge, tokens.spacingHorizontalS),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      ...shorthands.outline('2px', 'solid', tokens.colorStrokeFocus2),
    },
  },
  rowIcon: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },
  rowName: {
    flexGrow: 1,
    flexShrink: 1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  semanticsNote: {
    display: 'flex',
    alignItems: 'flex-start',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    color: tokens.colorNeutralForeground3,
  },
  semanticsNoteIcon: {
    flexShrink: 0,
    marginTop: '2px',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

type LoadStatus = 'loading' | 'ready' | 'error';

export const MonitoredTab: React.FC = () => {
  const styles = useStyles();

  const [status, setStatus] = React.useState<LoadStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [items, setItems] = React.useState<MonitoredItem[]>([]);

  React.useEffect(() => {
    let cancelled = false;

    // `listMonitoredByMe` never throws — this handler is defensive only.
    async function load(): Promise<void> {
      setStatus('loading');
      setErrorMessage(null);
      try {
        const result = await listMonitoredByMe();
        if (cancelled) return;
        setItems(result);
        setStatus('ready');
      } catch (err) {
        if (cancelled) return;
        setErrorMessage(err instanceof Error ? err.message : 'Failed to load monitored items.');
        setStatus('error');
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleRowClick = React.useCallback((item: MonitoredItem) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToMonitoredItem(xrm, item);
  }, []);

  let listContent: React.ReactElement;
  if (status === 'loading') {
    listContent = (
      <div className={styles.centeredState} data-testid="monitored-tab-loading">
        <Spinner size="tiny" label="Loading monitored items…" />
      </div>
    );
  } else if (status === 'error') {
    listContent = (
      <div className={styles.centeredState} data-testid="monitored-tab-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (items.length === 0) {
    listContent = (
      <div className={styles.centeredState} data-testid="monitored-tab-empty">
        <Caption1>Records you're monitoring will appear here.</Caption1>
      </div>
    );
  } else {
    listContent = (
      <div className={styles.list} data-testid="monitored-tab" role="list" aria-label="Monitored records">
        {items.map(item => {
          const rowKey = `${item.targetLogicalName}-${item.targetId}`;
          return (
            <div
              key={rowKey}
              className={styles.row}
              role="listitem"
              tabIndex={0}
              data-testid={`monitored-tab-row-${rowKey}`}
              onClick={() => handleRowClick(item)}
              onKeyDown={event => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  handleRowClick(item);
                }
              }}
            >
              <span className={styles.rowIcon} data-testid={`monitored-tab-row-icon-${rowKey}`}>
                {rowIconFor({ pageType: NavItemPageType.EntityRecord, logicalName: item.targetLogicalName })}
              </span>
              <Text className={styles.rowName} title={item.displayName}>
                {item.displayName}
              </Text>
            </div>
          );
        })}
      </div>
    );
  }

  return (
    <div className={styles.container} data-testid="monitored-tab-container">
      <div className={styles.semanticsNote} data-testid="monitored-tab-semantics-note">
        <Info16Regular className={styles.semanticsNoteIcon} aria-hidden="true" />
        <Caption1>
          Shared with everyone who can see the record — setting or clearing Monitor
          affects everyone, and the last change wins.
        </Caption1>
      </div>
      {listContent}
    </div>
  );
};

export default MonitoredTab;
