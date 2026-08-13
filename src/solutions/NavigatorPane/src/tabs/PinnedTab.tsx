/**
 * PinnedTab — Pinned tab content, Records group
 * (spaarke-side-pane-navigation-history-r1 task 050, spec FR-07 UI).
 *
 * Renders the signed-in user's per-user `sprk_type=Pin` `sprk_navitem` rows
 * (`navItemRepository.listPinItems`, task 041) in a **Records** group, newest
 * `sprk_lastvisited` first. Each row's inline star is always filled (every
 * row here IS a current pin) and unstars via `pinService.unpinById` — a
 * DELETE on `sprk_navitem`, never a write to `sprk_monitor` (project HARD
 * MUST-NOT, see `pinService.ts` module docblock).
 *
 * Seams for 051 (NOT built here): this file renders the Records `<section>`
 * PLUS (task 052) the Monitored `<section>`. `PinnedTab`'s root container is a
 * plain vertical stack of `<section>` blocks — task 051 (Bookmarks) adds its
 * OWN `<section aria-label="Bookmarks">` sibling, with its own load/render
 * logic. No shared "group" abstraction is introduced pre-emptively (CLAUDE.md
 * §11 — extension is earned by the second/third real consumer, not
 * anticipated); if 051 turns out to need the identical row/chip/navigate
 * shape as these sections, THAT is the trigger to extract a shared local
 * helper at that point, not now.
 *
 * **Monitored group (task 052, spec FR-09 / OQ-1b, design.md §6c)**: a THIRD,
 * visually distinct `<section>` surfacing the shared record-level
 * `sprk_monitor` flag, scoped to the current user (owner-scoped for r1 — see
 * `monitoredService.ts`'s module docblock "Scoping" for the escalated
 * assigned-to-me deferral). This group is READ-ONLY and reads a COMPLETELY
 * SEPARATE data path (`monitoredService.listMonitoredByMe`, N per-entity
 * queries against `sprk_matter`/`sprk_project`/`sprk_document`/`sprk_todo`/
 * `sprk_event`/`sprk_workassignment`/`sprk_invoice`) from the Records group's
 * per-user `sprk_navitem` pins above — it is NEVER merged into Records, has
 * no star/pin affordance, and its own `useEffect` load is independent of the
 * Records load. UI copy explains the shared-flag semantics (setting/clearing
 * `sprk_monitor` affects everyone; last-writer-wins) so a user does not
 * mistake this group for a personal list they exclusively control.
 *
 * Host-context only (project constraint): reads via `Xrm.WebApi`
 * (`navItemRepository.listPinItems`, `monitoredService.listMonitoredByMe`),
 * navigates via `Xrm.Navigation` (never a raw URL for a logical target),
 * unpins via `pinService.unpinById`. Chip mapping + row navigation for
 * Records are a local duplicate of `RecentTab.tsx`'s `chipForRow`/
 * `navigateToRow` (same closed pagetype set) rather than an extracted shared
 * util — RecentTab already established this "local duplication over
 * premature shared extraction" precedent for this exact logic (see
 * RecentTab.tsx's own module docblock). The Monitored group's chip mapping
 * mirrors RecentTab's `chipForEditedItem` (always `EntityRecord`-shaped,
 * entity-name-derived label — same closed-set reasoning).
 *
 * ADR-021: Fluent v9 tokens only; no portal-rendered component here, so no
 * portal re-wrap needed (light/dark flow through the ambient `FluentProvider`
 * the same way `RecentTab.tsx` documents).
 * ADR-022: only React-16/17-safe APIs used (no `createRoot`) — not shared-lib
 * code today (lives in the Code Page bundle, React 19), consistent with
 * RecentTab.tsx's own note.
 *
 * @see pinService.ts (task 050) — pin/unpin gesture semantics this tab's star wraps; NEVER writes `sprk_monitor`
 * @see monitoredService.ts (task 052) — the Monitored group's SEPARATE read-only data path
 * @see navItemRepository.ts (task 030/041/050) — Xrm.WebApi CRUD + option-set maps
 * @see RecentTab.tsx (task 041/042) — chip/navigate pattern this file mirrors locally
 * @see NavigatorBody.tsx (task 040/050) — mounts this component in the `pinned` tab panel
 */

import * as React from 'react';
import {
  Badge,
  Button,
  Caption1,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
  type BadgeProps,
} from '@fluentui/react-components';
import { Info16Regular, Star16Filled } from '@fluentui/react-icons';
import { getXrm, type XrmContext } from '@spaarke/ui-components';
import {
  NavItemPageType,
  listPinItems,
  type NavItemRecord,
} from '@spaarke/ui-components/services/navigator/navItemRepository';
import { unpinById } from '../services/pinService';
import { listMonitoredByMe, type MonitoredItem } from '../services/monitoredService';

// ─────────────────────────────────────────────────────────────────────────────
// Chip mapping (local duplicate of RecentTab.tsx's chipForRow — see module docblock)
// ─────────────────────────────────────────────────────────────────────────────

const KNOWN_ENTITY_CHIP_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_document: 'Document',
};

function formatEntityLabel(entityLogicalName: string | null): string {
  if (!entityLogicalName) return 'Record';
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

interface RowChip {
  label: string;
  appearance: NonNullable<BadgeProps['appearance']>;
  color: NonNullable<BadgeProps['color']>;
}

function chipForRow(row: NavItemRecord): RowChip {
  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      return { label: 'View', appearance: 'tint', color: 'informative' };
    case NavItemPageType.Custom:
      return { label: 'Page', appearance: 'tint', color: 'subtle' };
    case NavItemPageType.WebLink:
      return { label: 'Link', appearance: 'tint', color: 'subtle' };
    case NavItemPageType.EntityRecord:
    default: {
      const known = row.sprk_targetlogicalname
        ? KNOWN_ENTITY_CHIP_LABELS[row.sprk_targetlogicalname]
        : undefined;
      return {
        label: known ?? formatEntityLabel(row.sprk_targetlogicalname),
        appearance: 'tint',
        color: 'brand',
      };
    }
  }
}

/**
 * Chip for a Monitored-group row (task 052). Monitored rows are always the
 * `EntityRecord` case (each is a direct entity-table query) — mirrors
 * RecentTab.tsx's `chipForEditedItem` (same closed entity-label set + fallback).
 */
function chipForMonitoredItem(item: MonitoredItem): RowChip {
  const known = KNOWN_ENTITY_CHIP_LABELS[item.targetLogicalName];
  return {
    label: known ?? formatEntityLabel(item.targetLogicalName),
    appearance: 'tint',
    color: 'brand',
  };
}

/** Navigate to a Monitored row's target — always `entityrecord` (task 052; mirrors RecentTab.tsx's `navigateToEditedItem`). */
function navigateToMonitoredItem(xrm: XrmContext, item: MonitoredItem): void {
  const navigation = xrm.Navigation;
  if (!navigation) return;
  void navigation.navigateTo({
    pageType: 'entityrecord',
    entityName: item.targetLogicalName,
    entityId: item.targetId,
  });
}

/** Navigate to a row's target via `Xrm.Navigation` — mirrors RecentTab.tsx's `navigateToRow`. */
function navigateToRow(xrm: XrmContext, row: NavItemRecord): void {
  const navigation = xrm.Navigation;
  if (!navigation) return;

  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      if (row.sprk_targetlogicalname) {
        void navigation.navigateTo({ pageType: 'entitylist', entityName: row.sprk_targetlogicalname });
      }
      return;
    case NavItemPageType.WebLink:
      if (row.sprk_url) navigation.openUrl(row.sprk_url);
      return;
    case NavItemPageType.Custom:
      return; // No safe generic target — mirrors RecentTab.tsx OQ-6 note.
    case NavItemPageType.EntityRecord:
    default:
      if (row.sprk_targetlogicalname && row.sprk_targetid) {
        void navigation.navigateTo({
          pageType: 'entityrecord',
          entityName: row.sprk_targetlogicalname,
          entityId: row.sprk_targetid,
        });
      }
      return;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  group: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  groupHeading: {
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.02em',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
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
    justifyContent: 'space-between',
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
  rowMain: {
    display: 'flex',
    alignItems: 'center',
    minWidth: 0,
    flexGrow: 1,
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  rowName: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  // Monitored group (task 052) — shared-flag semantics messaging.
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

export const PinnedTab: React.FC = () => {
  const styles = useStyles();

  const [status, setStatus] = React.useState<LoadStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [rows, setRows] = React.useState<NavItemRecord[]>([]);
  const [unpinningIds, setUnpinningIds] = React.useState<Set<string>>(new Set());

  // Monitored group (task 052) — SEPARATE load, SEPARATE state, from Records
  // above. Never merges with `rows`/personal pins.
  const [monitoredStatus, setMonitoredStatus] = React.useState<LoadStatus>('loading');
  const [monitoredErrorMessage, setMonitoredErrorMessage] = React.useState<string | null>(null);
  const [monitoredItems, setMonitoredItems] = React.useState<MonitoredItem[]>([]);

  React.useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      const xrm = getXrm();
      const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
      if (!xrm || !ownerId) {
        if (!cancelled) {
          setRows([]);
          setStatus('ready');
        }
        return;
      }

      setStatus('loading');
      setErrorMessage(null);

      try {
        const pinRows = await listPinItems(ownerId);
        if (cancelled) return;
        setRows(pinRows);
        setStatus('ready');
      } catch (err) {
        if (cancelled) return;
        setErrorMessage(err instanceof Error ? err.message : 'Failed to load pinned items.');
        setStatus('error');
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  // Monitored group (task 052) — its own `useEffect`, independent of the
  // Records load above. `listMonitoredByMe` never throws; this handler is
  // defensive only (mirrors RecentTab.tsx's `loadEdited`).
  React.useEffect(() => {
    let cancelled = false;

    async function loadMonitored(): Promise<void> {
      setMonitoredStatus('loading');
      setMonitoredErrorMessage(null);
      try {
        const items = await listMonitoredByMe();
        if (cancelled) return;
        setMonitoredItems(items);
        setMonitoredStatus('ready');
      } catch (err) {
        if (cancelled) return;
        setMonitoredErrorMessage(err instanceof Error ? err.message : 'Failed to load monitored items.');
        setMonitoredStatus('error');
      }
    }

    void loadMonitored();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleRowClick = React.useCallback((row: NavItemRecord) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToRow(xrm, row);
  }, []);

  const handleMonitoredRowClick = React.useCallback((item: MonitoredItem) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToMonitoredItem(xrm, item);
  }, []);

  const handleUnpinClick = React.useCallback(
    async (row: NavItemRecord, event: React.SyntheticEvent) => {
      event.stopPropagation();
      if (unpinningIds.has(row.sprk_navitemid)) return;

      setUnpinningIds(prev => new Set(prev).add(row.sprk_navitemid));
      try {
        await unpinById(row.sprk_navitemid);
        setRows(prev => prev.filter(r => r.sprk_navitemid !== row.sprk_navitemid));
      } catch {
        // Non-fatal — the row stays pinned; user can retry.
      } finally {
        setUnpinningIds(prev => {
          const next = new Set(prev);
          next.delete(row.sprk_navitemid);
          return next;
        });
      }
    },
    [unpinningIds]
  );

  let recordsContent: React.ReactElement;
  if (status === 'loading') {
    recordsContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-loading">
        <Spinner size="tiny" label="Loading pinned items…" />
      </div>
    );
  } else if (status === 'error') {
    recordsContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (rows.length === 0) {
    recordsContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-empty">
        <Caption1>Pinned records will appear here.</Caption1>
      </div>
    );
  } else {
    recordsContent = (
      <div data-testid="pinned-tab" role="list" aria-label="Pinned records">
        {rows.map(row => {
          const chip = chipForRow(row);
          return (
            <div
              key={row.sprk_navitemid}
              className={styles.row}
              role="listitem"
              tabIndex={0}
              data-testid={`pinned-tab-row-${row.sprk_navitemid}`}
              onClick={() => handleRowClick(row)}
              onKeyDown={event => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  handleRowClick(row);
                }
              }}
            >
              <div className={styles.rowMain}>
                <Text className={styles.rowName} title={row.sprk_displayname}>
                  {row.sprk_displayname}
                </Text>
                <Badge
                  appearance={chip.appearance}
                  color={chip.color}
                  size="small"
                  data-testid={`pinned-tab-row-chip-${row.sprk_navitemid}`}
                >
                  {chip.label}
                </Badge>
              </div>
              <Button
                appearance="transparent"
                size="small"
                icon={<Star16Filled />}
                aria-label={`Unpin ${row.sprk_displayname}`}
                aria-pressed={true}
                disabled={unpinningIds.has(row.sprk_navitemid)}
                data-testid={`pinned-tab-row-star-${row.sprk_navitemid}`}
                onClick={event => void handleUnpinClick(row, event)}
              />
            </div>
          );
        })}
      </div>
    );
  }

  // ── Monitored group content (task 052) — SEPARATE render path from Records
  // above; never merged. ──
  let monitoredContent: React.ReactElement;
  if (monitoredStatus === 'loading') {
    monitoredContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-monitored-loading">
        <Spinner size="tiny" label="Loading monitored items…" />
      </div>
    );
  } else if (monitoredStatus === 'error') {
    monitoredContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-monitored-error">
        <Caption1>{monitoredErrorMessage}</Caption1>
      </div>
    );
  } else if (monitoredItems.length === 0) {
    monitoredContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-monitored-empty">
        <Caption1>Records you're monitoring will appear here.</Caption1>
      </div>
    );
  } else {
    monitoredContent = (
      <div data-testid="pinned-tab-monitored" role="list" aria-label="Monitored records">
        {monitoredItems.map(item => {
          const chip = chipForMonitoredItem(item);
          const rowKey = `${item.targetLogicalName}-${item.targetId}`;
          return (
            <div
              key={rowKey}
              className={styles.row}
              role="listitem"
              tabIndex={0}
              data-testid={`pinned-tab-monitored-row-${rowKey}`}
              onClick={() => handleMonitoredRowClick(item)}
              onKeyDown={event => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  handleMonitoredRowClick(item);
                }
              }}
            >
              <div className={styles.rowMain}>
                <Text className={styles.rowName} title={item.displayName}>
                  {item.displayName}
                </Text>
                <Badge
                  appearance={chip.appearance}
                  color={chip.color}
                  size="small"
                  data-testid={`pinned-tab-monitored-row-chip-${rowKey}`}
                >
                  {chip.label}
                </Badge>
              </div>
            </div>
          );
        })}
      </div>
    );
  }

  return (
    <div className={styles.container} data-testid="pinned-tab-container">
      <section className={styles.group} aria-label="Records" data-testid="pinned-tab-group-records">
        <Text className={styles.groupHeading}>Records</Text>
        {recordsContent}
      </section>
      {/* Bookmarks group (task 051) mounts as an additional <section> sibling
          here — see module docblock "Seams". */}
      <section className={styles.group} aria-label="Monitored" data-testid="pinned-tab-group-monitored">
        <Text className={styles.groupHeading}>Monitored</Text>
        <div className={styles.semanticsNote} data-testid="pinned-tab-monitored-semantics-note">
          <Info16Regular className={styles.semanticsNoteIcon} aria-hidden="true" />
          <Caption1>
            Shared with everyone who can see the record — setting or clearing Monitor
            affects everyone, and the last change wins.
          </Caption1>
        </div>
        {monitoredContent}
      </section>
    </div>
  );
};

export default PinnedTab;
