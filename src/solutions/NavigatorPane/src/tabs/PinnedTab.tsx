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
 * This file renders the Records `<section>`, the **Bookmarks** `<section>`
 * (task 051, spec FR-08 / OQ-7), and (task 052) the Monitored `<section>`.
 * `PinnedTab`'s root container is a plain vertical stack of `<section>`
 * blocks.
 *
 * **Bookmarks group (task 051)**: two gestures, both writing a
 * `sprk_type=pin` `sprk_navitem` via `bookmarkService.ts` (which itself
 * reuses `pinService.ts`'s dedupe-before-create write path) —
 * "Pin this page" (`bookmarkService.pinCurrentPage`, `sprk_source=Captured`,
 * derived from `getPageContext()`) and "+ Add bookmark"
 * (`bookmarkService.addBookmark`, `sprk_source=Manual`, derived from
 * `urlParse.ts`'s MDA-URL-shape parse of a pasted/typed string). The
 * Bookmarks group does NOT issue a second `Xrm.WebApi` query — it reuses the
 * SAME `rows` state the Records group's `listPinItems` load populates,
 * partitioned client-side by `sprk_pagetype`: `EntityRecord` rows render in
 * Records (a star-pinned record and a "Pin this page"-captured record are
 * behaviorally identical — both ARE personal pins of a Dataverse record, so
 * they share the Records group); `EntityList`/`Custom`/`WebLink` rows (view
 * targets, weblinks — never a "record" in the Dataverse sense) render in
 * Bookmarks. Both groups reuse the SAME row template (`renderPinRow`, chip
 * mapping, unstar) — this partition is the "second real consumer" CLAUDE.md
 * §11 expects before extracting a shared local helper, so `renderPinRow` is
 * introduced now rather than duplicated a third time. A successful
 * pin/bookmark create re-fetches `listPinItems` (`loadPins`) rather than
 * optimistically constructing a row client-side, since the create call only
 * returns an id, not the full row shape.
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
 * **Read-time security trimming (task 080, spec FR-12/NFR-04)**: every
 * `sprk_navitem` pin row `loadPins` fetches is re-validated via
 * `securityTrimService.ts`'s batched `classifyTargets` BEFORE `setRows` is
 * ever called — so a `denied` (403/404-equivalent) row's cached name can
 * never flash on screen (the `loading` Spinner covers the whole async
 * window; there is no interim `setRows` call). This covers BOTH the Records
 * group AND any `EntityRecord`-pagetype Bookmarks (a "Pin this page"-
 * captured record renders in Records per the partition above, so it is
 * covered by the SAME trim pass — no second code path). `EntityList`/
 * `Custom`/`WebLink` Bookmarks are exempt (no cached RECORD name at risk —
 * see `securityTrimService.ts`'s module docblock "Exemptions", which also
 * documents why an `EntityList` view-bookmark's `targetid` must NOT be
 * retrieve-checked as a record id). The Monitored group is DELIBERATELY NOT
 * additionally re-checked here: `listMonitoredByMe` is itself a LIVE query
 * against the target entities (not a cached label) issued fresh on every
 * mount, and Dataverse row-level security means a record the user cannot
 * read is never returned by that query in the first place — identical
 * reasoning to `RecentTab.tsx`'s Edited-tab exemption (see that file's
 * module docblock). Re-checking already-live, already-security-filtered
 * rows would be a redundant extra `retrieveRecord` per row with no
 * confidentiality benefit (CLAUDE.md §11). See
 * `projects/spaarke-side-pane-navigation-history-r1/notes/task-080-security-trimming.md`
 * for the full write-up, including this as a documented, deliberate scope
 * narrowing from the task's literal file list.
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
 * @see bookmarkService.ts (task 051) — "Pin this page" + "+ Add bookmark" gesture semantics the Bookmarks group calls
 * @see monitoredService.ts (task 052) — the Monitored group's SEPARATE read-only data path
 * @see navItemRepository.ts (task 030/041/050/051) — Xrm.WebApi CRUD + option-set maps
 * @see RecentTab.tsx (task 041/042) — chip/navigate pattern this file mirrors locally
 * @see NavigatorBody.tsx (task 040/050) — mounts this component in the `pinned` tab panel
 */

import * as React from 'react';
import {
  Badge,
  Button,
  Caption1,
  Input,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
  type BadgeProps,
  type InputOnChangeData,
} from '@fluentui/react-components';
import { Add16Regular, Info16Regular, Pin16Regular, Star16Filled } from '@fluentui/react-icons';
import { getXrm, type XrmContext } from '@spaarke/ui-components';
import {
  NavItemPageType,
  listPinItems,
  type NavItemRecord,
} from '@spaarke/ui-components/services/navigator/navItemRepository';
import { unpinById } from '../services/pinService';
import { listMonitoredByMe, type MonitoredItem } from '../services/monitoredService';
import { addBookmark, pinCurrentPage, BookmarkError } from '../services/bookmarkService';
import { classifyTargets, trimTargetFromRow } from '../services/securityTrimService';
import {
  setPinnedSearchEntries,
  type SearchEntryTarget,
  type SearchIndexEntry,
} from '../services/navigatorSearchIndex';

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

/**
 * Navigate to a row's target — logical (Dataverse) targets go through
 * `Xrm.Navigation` (never a raw URL — project MUST); a `WebLink` row (task
 * 051 — a bookmarked non-Dataverse URL) is the one exception and opens via
 * `window.open(url, '_blank', 'noopener')` instead, per the task's explicit
 * instruction — `noopener` prevents the new tab from getting a `window.opener`
 * reference back to this pane (reverse-tabnabbing protection for an
 * arbitrary user-pasted URL). Mirrors RecentTab.tsx's `navigateToRow` for the
 * logical-target cases.
 */
function navigateToRow(xrm: XrmContext, row: NavItemRecord): void {
  if (row.sprk_pagetype === NavItemPageType.WebLink) {
    if (row.sprk_url) window.open(row.sprk_url, '_blank', 'noopener');
    return;
  }

  const navigation = xrm.Navigation;
  if (!navigation) return;

  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      if (row.sprk_targetlogicalname) {
        void navigation.navigateTo({
          pageType: 'entitylist',
          entityName: row.sprk_targetlogicalname,
          // `sprk_targetid` carries the saved view's `viewid` for a
          // view-kind bookmark (task 051 — `bookmarkService.addBookmark`'s
          // `view` branch stores `viewId` as `targetId`, mirroring
          // ViewsTab.tsx's `navigateToView`). Existing 041/050 EntityList
          // rows (if any) never set `sprk_targetid`, so this is additive —
          // `undefined` here is a no-op for `navigateTo`.
          viewId: row.sprk_targetid ?? undefined,
        });
      }
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

/**
 * Search-index target for a pin row (task 070) — mirrors `navigateToRow`
 * above (including its `EntityList` `viewId` handling and its `WebLink` ->
 * `weblink`-kind mapping) but returns DATA instead of a navigation side
 * effect, so `QuickSwitcher.tsx` can derive the same Enter/click destination.
 */
function targetForRow(row: NavItemRecord): SearchEntryTarget | null {
  if (row.sprk_pagetype === NavItemPageType.WebLink) {
    return row.sprk_url ? { type: 'weblink', url: row.sprk_url } : null;
  }
  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      return row.sprk_targetlogicalname
        ? { type: 'entitylist', entityLogicalName: row.sprk_targetlogicalname, viewId: row.sprk_targetid ?? undefined }
        : null;
    case NavItemPageType.Custom:
      return null; // No safe generic target — mirrors RecentTab.tsx OQ-6 note.
    case NavItemPageType.EntityRecord:
    default:
      return row.sprk_targetlogicalname && row.sprk_targetid
        ? { type: 'entityrecord', entityLogicalName: row.sprk_targetlogicalname, entityId: row.sprk_targetid }
        : null;
  }
}

/** Maps an (already-080-trimmed) pin row to a `navigatorSearchIndex.ts` entry (task 070). Covers BOTH the Records and Bookmarks groups (same `rows` source — see module docblock "Bookmarks group"). */
function rowToSearchEntry(row: NavItemRecord): SearchIndexEntry {
  const chip = chipForRow(row);
  return {
    id: `pinned-${row.sprk_navitemid}`,
    label: row.sprk_displayname,
    chipLabel: chip.label,
    target: targetForRow(row),
  };
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
  // Bookmarks group (task 051) — the two-gesture entry area above the bookmark rows.
  bookmarkActions: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  addBookmarkRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
  },
  addBookmarkInput: {
    flexGrow: 1,
    minWidth: 0,
  },
  bookmarkInlineMessage: {
    color: tokens.colorNeutralForeground3,
  },
  bookmarkErrorMessage: {
    color: tokens.colorPaletteRedForeground1,
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

  // Bookmarks group (task 051) — gesture state. Shares `rows`/`status` above
  // (see module docblock "Bookmarks group") rather than a second data slice.
  const [pinningCurrentPage, setPinningCurrentPage] = React.useState(false);
  const [pinCurrentPageMessage, setPinCurrentPageMessage] = React.useState<string | null>(null);
  const [bookmarkInput, setBookmarkInput] = React.useState('');
  const [addingBookmark, setAddingBookmark] = React.useState(false);
  const [bookmarkMessage, setBookmarkMessage] = React.useState<{ tone: 'error' | 'success'; text: string } | null>(
    null
  );

  // Monitored group (task 052) — SEPARATE load, SEPARATE state, from Records
  // above. Never merges with `rows`/personal pins.
  const [monitoredStatus, setMonitoredStatus] = React.useState<LoadStatus>('loading');
  const [monitoredErrorMessage, setMonitoredErrorMessage] = React.useState<string | null>(null);
  const [monitoredItems, setMonitoredItems] = React.useState<MonitoredItem[]>([]);

  // Guards state updates after unmount for the manual (post-gesture) reload
  // below — the mount-time load effect uses its own local `cancelled` flag,
  // same shape as before task 051.
  const mountedRef = React.useRef(true);
  React.useEffect(
    () => () => {
      mountedRef.current = false;
    },
    []
  );

  /**
   * Load (or reload) the current user's `pin` `sprk_navitem` rows. Called on
   * mount AND after a successful "Pin this page"/"+ Add bookmark" gesture
   * (task 051) — the create call only returns an id, not the full row shape,
   * so a reload is simpler and no less correct than optimistically
   * constructing a row client-side.
   */
  const loadPins = React.useCallback(async () => {
    const xrm = getXrm();
    const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
    if (!xrm || !ownerId) {
      if (mountedRef.current) {
        setRows([]);
        setStatus('ready');
        setPinnedSearchEntries([]);
      }
      return;
    }

    setStatus('loading');
    setErrorMessage(null);

    try {
      const pinRows = await listPinItems(ownerId);

      // Security trim (task 080, FR-12/NFR-04) — see module docblock
      // "Read-time security trimming". Classify BEFORE `setRows` so a
      // `denied` row's cached name is never placed in state, let alone
      // rendered.
      const classifications = await classifyTargets(xrm, pinRows.map(trimTargetFromRow));
      const trimmed = pinRows.filter(row => classifications.get(row.sprk_navitemid) !== 'denied');

      if (!mountedRef.current) return;
      setRows(trimmed);
      setStatus('ready');
      // task 070 — report the SAME already-trimmed rows (Records + Bookmarks)
      // into the shared search index; see navigatorSearchIndex.ts module docblock.
      setPinnedSearchEntries(trimmed.map(rowToSearchEntry));
    } catch (err) {
      if (!mountedRef.current) return;
      setErrorMessage(err instanceof Error ? err.message : 'Failed to load pinned items.');
      setStatus('error');
    }
  }, []);

  React.useEffect(() => {
    void loadPins();
  }, [loadPins]);

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

  // ── Bookmarks group handlers (task 051) ──────────────────────────────────

  const handlePinCurrentPageClick = React.useCallback(async () => {
    const xrm = getXrm();
    const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
    if (!xrm || !ownerId) {
      setPinCurrentPageMessage("Can't pin this page right now.");
      return;
    }

    setPinningCurrentPage(true);
    setPinCurrentPageMessage(null);
    try {
      await pinCurrentPage(ownerId);
      await loadPins();
    } catch (err) {
      setPinCurrentPageMessage(err instanceof BookmarkError ? err.message : "Couldn't pin this page. Try again.");
    } finally {
      setPinningCurrentPage(false);
    }
  }, [loadPins]);

  const handleBookmarkInputChange = React.useCallback(
    (_event: React.ChangeEvent<HTMLInputElement>, data: InputOnChangeData) => {
      setBookmarkInput(data.value);
      setBookmarkMessage(null);
    },
    []
  );

  const handleAddBookmarkSubmit = React.useCallback(async () => {
    const trimmed = bookmarkInput.trim();
    if (!trimmed || addingBookmark) return;

    const xrm = getXrm();
    const ownerId = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
    if (!xrm || !ownerId) {
      setBookmarkMessage({ tone: 'error', text: "Can't add a bookmark right now." });
      return;
    }

    setAddingBookmark(true);
    setBookmarkMessage(null);
    try {
      await addBookmark(ownerId, trimmed);
      setBookmarkInput('');
      setBookmarkMessage({ tone: 'success', text: 'Bookmark added.' });
      await loadPins();
    } catch (err) {
      setBookmarkMessage({
        tone: 'error',
        text: err instanceof BookmarkError ? err.message : "Couldn't add that bookmark. Try again.",
      });
    } finally {
      setAddingBookmark(false);
    }
  }, [bookmarkInput, addingBookmark, loadPins]);

  /**
   * Shared row template for a `sprk_type=pin` row — used by BOTH the Records
   * group (EntityRecord-pagetype rows) and the Bookmarks group (all other
   * pagetypes: EntityList/Custom/WebLink). `testIdPrefix` keeps each group's
   * `data-testid`s distinct (`pinned-tab-row-*` for Records — UNCHANGED from
   * pre-051, so the existing test suite's ids keep resolving — and
   * `pinned-tab-bookmark-row-*` for Bookmarks).
   */
  const renderPinRow = React.useCallback(
    (row: NavItemRecord, testIdPrefix: string) => {
      const chip = chipForRow(row);
      return (
        <div
          key={row.sprk_navitemid}
          className={styles.row}
          role="listitem"
          tabIndex={0}
          data-testid={`${testIdPrefix}-${row.sprk_navitemid}`}
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
              data-testid={`${testIdPrefix}-chip-${row.sprk_navitemid}`}
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
            data-testid={`${testIdPrefix}-star-${row.sprk_navitemid}`}
            onClick={event => void handleUnpinClick(row, event)}
          />
        </div>
      );
    },
    [handleRowClick, handleUnpinClick, styles, unpinningIds]
  );

  const recordRows = React.useMemo(
    () => rows.filter(row => row.sprk_pagetype === NavItemPageType.EntityRecord),
    [rows]
  );
  const bookmarkRows = React.useMemo(
    () => rows.filter(row => row.sprk_pagetype !== NavItemPageType.EntityRecord),
    [rows]
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
  } else if (recordRows.length === 0) {
    recordsContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-empty">
        <Caption1>Pinned records will appear here.</Caption1>
      </div>
    );
  } else {
    recordsContent = (
      <div data-testid="pinned-tab" role="list" aria-label="Pinned records">
        {recordRows.map(row => renderPinRow(row, 'pinned-tab-row'))}
      </div>
    );
  }

  // ── Bookmarks group content (task 051) — shares `status`/`errorMessage`
  // with Records above (same `listPinItems` load); only the filtered row
  // set + empty-state copy differ. ──
  let bookmarksContent: React.ReactElement;
  if (status === 'loading') {
    bookmarksContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-bookmarks-loading">
        <Spinner size="tiny" label="Loading bookmarks…" />
      </div>
    );
  } else if (status === 'error') {
    bookmarksContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-bookmarks-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (bookmarkRows.length === 0) {
    bookmarksContent = (
      <div className={styles.centeredState} data-testid="pinned-tab-bookmarks-empty">
        <Caption1>Bookmarked views and links will appear here.</Caption1>
      </div>
    );
  } else {
    bookmarksContent = (
      <div data-testid="pinned-tab-bookmarks" role="list" aria-label="Bookmarks">
        {bookmarkRows.map(row => renderPinRow(row, 'pinned-tab-bookmark-row'))}
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
      <section className={styles.group} aria-label="Bookmarks" data-testid="pinned-tab-group-bookmarks">
        <Text className={styles.groupHeading}>Bookmarks</Text>
        <div className={styles.bookmarkActions}>
          <Button
            appearance="secondary"
            size="small"
            icon={<Pin16Regular />}
            disabled={pinningCurrentPage}
            data-testid="pinned-tab-pin-current-page"
            onClick={() => void handlePinCurrentPageClick()}
          >
            Pin this page
          </Button>
          {pinCurrentPageMessage && (
            <Caption1 className={styles.bookmarkErrorMessage} data-testid="pinned-tab-pin-current-page-message">
              {pinCurrentPageMessage}
            </Caption1>
          )}
          <div className={styles.addBookmarkRow}>
            <Input
              className={styles.addBookmarkInput}
              size="small"
              placeholder="Paste or type a link to bookmark"
              value={bookmarkInput}
              onChange={handleBookmarkInputChange}
              onKeyDown={event => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  void handleAddBookmarkSubmit();
                }
              }}
              disabled={addingBookmark}
              aria-label="Bookmark URL"
              data-testid="pinned-tab-add-bookmark-input"
            />
            <Button
              appearance="primary"
              size="small"
              icon={<Add16Regular />}
              disabled={addingBookmark || !bookmarkInput.trim()}
              data-testid="pinned-tab-add-bookmark-submit"
              onClick={() => void handleAddBookmarkSubmit()}
            >
              Add
            </Button>
          </div>
          {bookmarkMessage && (
            <Caption1
              className={bookmarkMessage.tone === 'error' ? styles.bookmarkErrorMessage : styles.bookmarkInlineMessage}
              data-testid="pinned-tab-add-bookmark-message"
            >
              {bookmarkMessage.text}
            </Caption1>
          )}
        </div>
        {bookmarksContent}
      </section>
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
