/**
 * RecentTab — Recent tab content (Viewed history only)
 * (spaarke-side-pane-navigation-history-r1 task 041, spec FR-03 UI; UAT-driven
 * redesign removed the task-042 Viewed/Edited segmented toggle — see below).
 *
 * Renders the signed-in user's `history` `sprk_navitem` rows (produced by the
 * capture poller, `navigatorCaptureService.ts` — now WIRED UP via
 * `NavigatorBody.tsx`'s `startNavigatorCapture()` mount effect, closing the
 * bug where no history was ever captured) newest-first by
 * `sprk_lastvisited`, each with a far-left record-type icon
 * (`rowIcon.tsx`'s `rowIconFor`) and an inline star that promotes the row to
 * a per-user pin.
 *
 * UAT redesign (owner decision, "keep only captured Viewed"): the task-042
 * Viewed/Edited segmented toggle and its Edited list are REMOVED from this
 * tab. `editedByMeService.ts` (the `modifiedby=me` derivation the Edited mode
 * used) is left in the repo — nothing else references it — but is no longer
 * imported here. The former type-chip `Badge` pill is also REMOVED (replaced
 * by the far-left icon); the star affordance is unchanged.
 *
 * Mounted by `NavigatorBody.tsx`'s `recent` tab panel.
 *
 * Host-context only (project constraint): reads via `Xrm.WebApi`
 * (`navItemRepository.listHistoryItems`/`listPinItems`), navigates via
 * `Xrm.Navigation` (never a raw URL for a logical target), creates the pin
 * via `navItemRepository.createPinItem`.
 *
 * Read-time trimming (FR-12/NFR-04, task 080): each Viewed row's target is
 * re-validated via `securityTrimService.ts`'s batched `classifyTargets`
 * BEFORE `setRows` is ever called — the trimmed/final row array is the ONLY
 * thing this component ever puts in state, so a `denied` row's cached name
 * can never flash on screen even momentarily (the `loading` Spinner covers
 * the entire window between "history rows fetched" and "trim resolved").
 * `denied` (403/404-equivalent) rows are dropped entirely; `transient`
 * (network/timeout/5xx/ambiguous) rows are KEPT — a blip must never
 * permanently hide an otherwise-accessible row.
 *
 * ADR-021: Fluent v9 tokens only; no portal-rendered component here (no
 * Popover/Tooltip/Dialog/Menu) so the fluent-v9-portal-gotcha re-wrap does
 * not apply — light/dark flow through the ambient `FluentProvider`
 * `NavigatorBody` already resolves.
 * ADR-022: only React-16/17-safe APIs used (no `createRoot`) — not shared-lib
 * code today (lives in the Code Page bundle, React 19).
 *
 * @see navItemRepository.ts — Xrm.WebApi CRUD + option-set maps
 * @see navigatorCaptureService.ts — produces the history rows this tab renders; wired up by NavigatorBody.tsx
 * @see rowIcon.tsx — the shared far-left record-type icon this tab (and BookmarksTab.tsx/MonitoredTab.tsx) renders
 * @see NavigatorBody.tsx — mounts this component in the `recent` tab panel
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { Star16Filled, Star16Regular } from '@fluentui/react-icons';
import { getXrm, type XrmContext } from '@spaarke/ui-components';
import {
  NavItemPageType,
  createPinItem,
  listHistoryItems,
  listPinItems,
  type NavItemRecord,
} from '@spaarke/ui-components/services/navigator/navItemRepository';
import { classifyTargets, trimTargetFromRow } from '../services/securityTrimService';
import { rowIconFor } from '../rowIcon';
import {
  setRecentSearchEntries,
  type SearchEntryTarget,
  type SearchIndexEntry,
} from '../services/navigatorSearchIndex';

// ─────────────────────────────────────────────────────────────────────────────
// Label mapping (search-index chipLabel only — no on-row Badge anymore)
// ─────────────────────────────────────────────────────────────────────────────

const KNOWN_ENTITY_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_document: 'Document',
};

/** "sprk_project" -> "Project". Mirrors `navigatorCaptureService.ts`'s private `formatEntityFallbackLabel`. */
function formatEntityLabel(entityLogicalName: string | null): string {
  if (!entityLogicalName) return 'Record';
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

/** Search-index `chipLabel` for a history row (the on-row Badge pill itself was removed in the UAT redesign). */
function labelForRow(row: NavItemRecord): string {
  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      return 'View';
    case NavItemPageType.Custom:
      return 'Page';
    case NavItemPageType.WebLink:
      return 'Link';
    case NavItemPageType.EntityRecord:
    default:
      return row.sprk_targetlogicalname
        ? (KNOWN_ENTITY_LABELS[row.sprk_targetlogicalname] ?? formatEntityLabel(row.sprk_targetlogicalname))
        : formatEntityLabel(row.sprk_targetlogicalname);
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Navigation + trimming
// ─────────────────────────────────────────────────────────────────────────────

/** Stable dedupe key for pinned-state lookup, matching a history row to a pin row by target. */
function pinKey(targetLogicalName: string | null, targetId: string | null): string {
  return `${targetLogicalName ?? ''}|${targetId ?? ''}`;
}

/**
 * Navigate to a row's target via `Xrm.Navigation` — never a constructed raw
 * URL for a logical target (project constraint). `Custom` rows have no
 * generic navigable target from a stored history row (OQ-6) and are
 * intentionally not clickable-to-navigate.
 */
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
      return; // No safe generic target — see OQ-6 note.
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

/**
 * Search-index target for a history row (task 070) — mirrors `navigateToRow`
 * above but returns DATA instead of a navigation side effect.
 */
function targetForRow(row: NavItemRecord): SearchEntryTarget | null {
  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      return row.sprk_targetlogicalname ? { type: 'entitylist', entityLogicalName: row.sprk_targetlogicalname } : null;
    case NavItemPageType.WebLink:
      return row.sprk_url ? { type: 'weblink', url: row.sprk_url } : null;
    case NavItemPageType.Custom:
      return null; // No safe generic target — see OQ-6 note.
    case NavItemPageType.EntityRecord:
    default:
      return row.sprk_targetlogicalname && row.sprk_targetid
        ? { type: 'entityrecord', entityLogicalName: row.sprk_targetlogicalname, entityId: row.sprk_targetid }
        : null;
  }
}

/** Maps a (already-080-trimmed) history row to a `navigatorSearchIndex.ts` entry (task 070). */
function rowToSearchEntry(row: NavItemRecord): SearchIndexEntry {
  return {
    id: `recent-${row.sprk_navitemid}`,
    label: row.sprk_displayname,
    chipLabel: labelForRow(row),
    target: targetForRow(row),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
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
  // Far-left record-type icon (UAT redesign — replaces the removed Badge pill).
  rowIcon: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
  },
  rowName: {
    // flexGrow + minWidth:0 lets the label take the available width and
    // ellipsis instead of pushing the row (and pane) wider (task 086 UAT).
    flexGrow: 1,
    flexShrink: 1,
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

type LoadStatus = 'loading' | 'ready' | 'error';

export const RecentTab: React.FC = () => {
  const styles = useStyles();

  const [status, setStatus] = React.useState<LoadStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [rows, setRows] = React.useState<NavItemRecord[]>([]);
  const [pinnedKeys, setPinnedKeys] = React.useState<Set<string>>(new Set());
  const [pinningIds, setPinningIds] = React.useState<Set<string>>(new Set());

  React.useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      const xrm = getXrm();
      const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
      if (!xrm || !ownerId) {
        if (!cancelled) {
          setRows([]);
          setStatus('ready');
          setRecentSearchEntries([]);
        }
        return;
      }

      setStatus('loading');
      setErrorMessage(null);

      try {
        const [historyRows, pinRows] = await Promise.all([
          listHistoryItems(ownerId),
          // Pin lookup is best-effort — a failure here should not block
          // rendering history rows, it only means stars start un-filled.
          listPinItems(ownerId).catch(() => [] as NavItemRecord[]),
        ]);

        // Security trim (task 080, FR-12/NFR-04) — classify EVERY row's
        // target BEFORE any of them are ever placed in `rows` state. The
        // `loading` status (and its Spinner) covers this entire async
        // window, so a `denied` row's cached name is never rendered even
        // momentarily — there is no partial/interim `setRows` call anywhere
        // in this path. `denied` rows are dropped; `transient` rows are kept.
        const classifications = await classifyTargets(xrm, historyRows.map(trimTargetFromRow));
        const accessible = historyRows.filter(row => classifications.get(row.sprk_navitemid) !== 'denied');

        if (cancelled) return;
        setRows(accessible);
        setPinnedKeys(new Set(pinRows.map(r => pinKey(r.sprk_targetlogicalname, r.sprk_targetid))));
        setStatus('ready');
        // task 070 — report the SAME already-trimmed rows into the shared
        // search index; see navigatorSearchIndex.ts module docblock.
        setRecentSearchEntries(accessible.map(rowToSearchEntry));
      } catch (err) {
        if (cancelled) return;
        setErrorMessage(err instanceof Error ? err.message : 'Failed to load recent items.');
        setStatus('error');
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleRowClick = React.useCallback((row: NavItemRecord) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToRow(xrm, row);
  }, []);

  const handlePinClick = React.useCallback(
    async (row: NavItemRecord, event: React.SyntheticEvent) => {
      event.stopPropagation();
      const key = pinKey(row.sprk_targetlogicalname, row.sprk_targetid);
      if (pinnedKeys.has(key) || pinningIds.has(row.sprk_navitemid)) return;
      if (!row.sprk_targetlogicalname || !row.sprk_targetid) return;

      setPinningIds(prev => new Set(prev).add(row.sprk_navitemid));
      try {
        await createPinItem({
          targetLogicalName: row.sprk_targetlogicalname,
          targetId: row.sprk_targetid,
          pageType: row.sprk_pagetype,
          displayName: row.sprk_displayname,
        });
        setPinnedKeys(prev => new Set(prev).add(key));
      } catch {
        // Non-fatal — the star simply stays un-filled; user can retry.
      } finally {
        setPinningIds(prev => {
          const next = new Set(prev);
          next.delete(row.sprk_navitemid);
          return next;
        });
      }
    },
    [pinnedKeys, pinningIds]
  );

  let content: React.ReactElement;
  if (status === 'loading') {
    content = (
      <div className={styles.centeredState} data-testid="recent-tab-loading">
        <Spinner size="tiny" label="Loading recent items…" />
      </div>
    );
  } else if (status === 'error') {
    content = (
      <div className={styles.centeredState} data-testid="recent-tab-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (rows.length === 0) {
    content = (
      <div className={styles.centeredState} data-testid="recent-tab-empty">
        <Caption1>Recently viewed records will appear here.</Caption1>
      </div>
    );
  } else {
    content = (
      <div className={styles.root} data-testid="recent-tab" role="list" aria-label="Recently viewed records">
        {rows.map(row => {
          const key = pinKey(row.sprk_targetlogicalname, row.sprk_targetid);
          const pinned = pinnedKeys.has(key);
          return (
            <div
              key={row.sprk_navitemid}
              className={styles.row}
              role="listitem"
              tabIndex={0}
              data-testid={`recent-tab-row-${row.sprk_navitemid}`}
              onClick={() => handleRowClick(row)}
              onKeyDown={event => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  handleRowClick(row);
                }
              }}
            >
              <div className={styles.rowMain}>
                <span className={styles.rowIcon} data-testid={`recent-tab-row-icon-${row.sprk_navitemid}`}>
                  {rowIconFor({ pageType: row.sprk_pagetype, logicalName: row.sprk_targetlogicalname })}
                </span>
                <Text className={styles.rowName} title={row.sprk_displayname}>
                  {row.sprk_displayname}
                </Text>
              </div>
              <Button
                appearance="transparent"
                size="small"
                icon={pinned ? <Star16Filled /> : <Star16Regular />}
                aria-label={pinned ? `${row.sprk_displayname} is pinned` : `Pin ${row.sprk_displayname}`}
                aria-pressed={pinned}
                disabled={pinningIds.has(row.sprk_navitemid)}
                data-testid={`recent-tab-row-star-${row.sprk_navitemid}`}
                onClick={event => void handlePinClick(row, event)}
              />
            </div>
          );
        })}
      </div>
    );
  }

  return (
    <div data-testid="recent-tab-container">
      {content}
    </div>
  );
};

export default RecentTab;
