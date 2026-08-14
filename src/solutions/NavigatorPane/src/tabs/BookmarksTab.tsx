/**
 * BookmarksTab — Bookmarks tab content
 * (spaarke-side-pane-navigation-history-r1 task 050/051, spec FR-07/FR-08;
 * renamed + restructured from `PinnedTab.tsx` by the UAT-driven redesign —
 * see "UAT redesign" below).
 *
 * Renders the signed-in user's per-user `sprk_navitem` PIN rows
 * (`navItemRepository.listPinItems`) — both star-pinned Dataverse records AND
 * bookmarked views/links, created via the two gestures at the TOP of this
 * tab: "Pin this page" (`bookmarkService.pinCurrentPage`) and "+ Add
 * bookmark" (`bookmarkService.addBookmark`). Unstarring a row deletes it
 * (`pinService.unpinById`) — a DELETE on `sprk_navitem`, never a write to
 * `sprk_monitor` (project HARD MUST-NOT, see `pinService.ts`).
 *
 * UAT fix #4 (inline rename): each row shows a hover-revealed (and
 * keyboard-focusable) pencil (`Edit16Regular`) that turns the row's name
 * into an inline `Input` — confirm via Enter or the checkmark button, cancel
 * via Escape or the dismiss button. Confirming calls
 * `navItemRepository.renameNavItem` (an `UPDATE sprk_displayname` on
 * `sprk_navitem`) with an optimistic local update that reverts on failure.
 * Applies to every row (record pins AND weblink bookmarks) — the same flat
 * list, same row template.
 *
 * UAT redesign (owner decision): this file supersedes `PinnedTab.tsx`.
 * Changes from the former Pinned tab:
 *   - Renamed tab label "Pinned" -> "Bookmarks" (`NavigatorBody.tsx`).
 *   - The "Pin this page" button + "Paste or type a link to bookmark" input
 *     moved to the TOP of the tab, above the list (previously they sat
 *     inside the Bookmarks `<section>`, below Records).
 *   - The former Records + Bookmarks `<section>`s (with headings + a
 *     `EntityRecord`-vs-other-pagetype partition) are now ONE flat,
 *     unheaded list — `rows` sorted newest-`sprk_lastvisited`-first
 *     (`listPinItems`'s own `$orderby`, unchanged) — since the tab's own
 *     name ("Bookmarks") already conveys what the list is; a record pin and
 *     a bookmarked view/link render with the SAME row template, distinguished
 *     only by their far-left `rowIconFor` icon (record-type icon vs.
 *     `GlobeRegular` for weblinks).
 *   - The former Monitored `<section>` MOVED OUT to its own tab,
 *     `MonitoredTab.tsx` — this file no longer reads `monitoredService.ts`
 *     at all.
 *   - The on-row type-chip `Badge` pill is REMOVED (replaced by the far-left
 *     icon, mirroring `RecentTab.tsx`'s identical change).
 *
 * **Read-time security trimming (task 080, spec FR-12/NFR-04)**: every
 * `sprk_navitem` pin row `loadPins` fetches is re-validated via
 * `securityTrimService.ts`'s batched `classifyTargets` BEFORE `setRows` is
 * ever called — so a `denied` (403/404-equivalent) row's cached name can
 * never flash on screen (the `loading` Spinner covers the whole async
 * window; there is no interim `setRows` call). `EntityList`/`Custom`/
 * `WebLink` bookmarks are exempt (no cached RECORD name at risk — see
 * `securityTrimService.ts`'s module docblock "Exemptions").
 *
 * Host-context only (project constraint): reads via `Xrm.WebApi`
 * (`navItemRepository.listPinItems`), navigates via `Xrm.Navigation` (never
 * a raw URL for a logical target; `window.open(..., 'noopener')` for a
 * weblink row), unpins via `pinService.unpinById`.
 *
 * ADR-021: Fluent v9 tokens only; no portal-rendered component here, so no
 * portal re-wrap needed.
 * ADR-022: only React-16/17-safe APIs used (no `createRoot`) — not shared-lib
 * code today (lives in the Code Page bundle, React 19).
 *
 * @see pinService.ts — pin/unpin gesture semantics this tab's star wraps; NEVER writes `sprk_monitor`
 * @see bookmarkService.ts — "Pin this page" + "+ Add bookmark" gesture semantics
 * @see navItemRepository.ts — Xrm.WebApi CRUD + option-set maps
 * @see rowIcon.tsx — the shared far-left record-type icon this tab (and RecentTab.tsx/MonitoredTab.tsx) renders
 * @see MonitoredTab.tsx — the SEPARATE, formerly-nested-here Monitored lens
 * @see NavigatorBody.tsx — mounts this component in the `bookmarks` tab panel
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Input,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  type InputOnChangeData,
} from '@fluentui/react-components';
import {
  Add16Regular,
  Checkmark16Regular,
  Dismiss16Regular,
  Edit16Regular,
  Pin16Regular,
  Star16Filled,
} from '@fluentui/react-icons';
import { getXrm, type XrmContext } from '@spaarke/ui-components';
import {
  NavItemPageType,
  listPinItems,
  renameNavItem,
  type NavItemRecord,
} from '@spaarke/ui-components/services/navigator/navItemRepository';
import { unpinById } from '../services/pinService';
import { addBookmark, pinCurrentPage, BookmarkError } from '../services/bookmarkService';
import { classifyTargets, trimTargetFromRow } from '../services/securityTrimService';
import { rowIconFor } from '../rowIcon';
import {
  setPinnedSearchEntries,
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

function formatEntityLabel(entityLogicalName: string | null): string {
  if (!entityLogicalName) return 'Record';
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

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

/**
 * Navigate to a row's target — logical (Dataverse) targets go through
 * `Xrm.Navigation` (never a raw URL — project MUST); a `WebLink` row is the
 * one exception and opens via `window.open(url, '_blank', 'noopener')` —
 * `noopener` prevents the new tab from getting a `window.opener` reference
 * back to this pane (reverse-tabnabbing protection for an arbitrary
 * user-pasted URL).
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
          // view-kind bookmark (`bookmarkService.addBookmark`'s `view`
          // branch stores `viewId` as `targetId`). `viewType` is left unset
          // here — an arbitrary pasted MDA view URL does not carry enough
          // information at parse time to know whether it names a personal
          // (`userquery`) or system (`savedquery`) view (unlike
          // `ViewsTab.tsx`, whose views are ALWAYS `userquery` — see that
          // file's `USERQUERY_VIEW_TYPE` fix).
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
 * effect.
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

/** Maps an (already-080-trimmed) pin row to a `navigatorSearchIndex.ts` entry (task 070). */
function rowToSearchEntry(row: NavItemRecord): SearchIndexEntry {
  return {
    id: `pinned-${row.sprk_navitemid}`,
    label: row.sprk_displayname,
    chipLabel: labelForRow(row),
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
    // Clear vertical whitespace between the gesture area and the list (UAT polish).
    ...shorthands.gap(tokens.spacingVerticalL),
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
    // Hover-reveal for the rename pencil (UAT fix #4) — the pencil's own
    // `:focus-visible` (see `rowPencil` below) reveals it independently for
    // keyboard users tabbing directly to it.
    ':hover .navRowPencil': {
      opacity: 1,
    },
  },
  rowMain: {
    display: 'flex',
    alignItems: 'center',
    minWidth: 0,
    flexGrow: 1,
    ...shorthands.gap(tokens.spacingHorizontalS),
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
  // Inline rename input (UAT fix #4) — replaces `rowName` in edit mode.
  rowNameInput: {
    flexGrow: 1,
    flexShrink: 1,
    minWidth: 0,
  },
  rowActions: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
    ...shorthands.gap(tokens.spacingHorizontalXXS),
  },
  // Hidden until the row is hovered (`row`'s `:hover .navRowPencil` above) OR
  // the pencil itself receives keyboard focus — always accessible via Tab
  // even though it is visually hidden at rest.
  rowPencil: {
    opacity: 0,
    ':focus-visible': {
      opacity: 1,
    },
  },
  // Gesture area — "Pin this page" + "+ Add bookmark", now at the TOP of the tab.
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

export const BookmarksTab: React.FC = () => {
  const styles = useStyles();

  const [status, setStatus] = React.useState<LoadStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [rows, setRows] = React.useState<NavItemRecord[]>([]);
  const [unpinningIds, setUnpinningIds] = React.useState<Set<string>>(new Set());

  // Inline rename state (UAT fix #4) — at most one row edits at a time.
  const [editingId, setEditingId] = React.useState<string | null>(null);
  const [editValue, setEditValue] = React.useState('');
  const [renamingIds, setRenamingIds] = React.useState<Set<string>>(new Set());

  // Gesture state.
  const [pinningCurrentPage, setPinningCurrentPage] = React.useState(false);
  const [pinCurrentPageMessage, setPinCurrentPageMessage] = React.useState<string | null>(null);
  const [bookmarkInput, setBookmarkInput] = React.useState('');
  const [addingBookmark, setAddingBookmark] = React.useState(false);
  const [bookmarkMessage, setBookmarkMessage] = React.useState<{ tone: 'error' | 'success'; text: string } | null>(
    null
  );

  // Guards state updates after unmount for the manual (post-gesture) reload
  // below — the mount-time load effect uses its own local `cancelled` flag.
  const mountedRef = React.useRef(true);
  React.useEffect(
    () => () => {
      mountedRef.current = false;
    },
    []
  );

  /**
   * Load (or reload) the current user's `pin` `sprk_navitem` rows. Called on
   * mount AND after a successful "Pin this page"/"+ Add bookmark" gesture —
   * the create call only returns an id, not the full row shape, so a reload
   * is simpler and no less correct than optimistically constructing a row
   * client-side.
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

      // Security trim (task 080, FR-12/NFR-04) — classify BEFORE `setRows`
      // so a `denied` row's cached name is never placed in state, let alone
      // rendered.
      const classifications = await classifyTargets(xrm, pinRows.map(trimTargetFromRow));
      const trimmed = pinRows.filter(row => classifications.get(row.sprk_navitemid) !== 'denied');

      if (!mountedRef.current) return;
      setRows(trimmed);
      setStatus('ready');
      // task 070 — report the SAME already-trimmed rows into the shared search index.
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

  const handleRowClick = React.useCallback((row: NavItemRecord) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToRow(xrm, row);
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

  // ── Inline rename (UAT fix #4 — pencil on hover) ────────────────────────

  const handleStartRename = React.useCallback((row: NavItemRecord, event: React.SyntheticEvent) => {
    event.stopPropagation();
    setEditingId(row.sprk_navitemid);
    setEditValue(row.sprk_displayname);
  }, []);

  const handleCancelRename = React.useCallback((event?: React.SyntheticEvent) => {
    event?.stopPropagation();
    setEditingId(null);
    setEditValue('');
  }, []);

  const handleConfirmRename = React.useCallback(
    async (row: NavItemRecord, event?: React.SyntheticEvent) => {
      event?.stopPropagation();
      const trimmed = editValue.trim();
      setEditingId(null);

      if (!trimmed || trimmed === row.sprk_displayname) {
        return; // blank/unchanged — no-op, keep the old name.
      }

      const previousName = row.sprk_displayname;
      // Optimistic — the row shows the new name immediately.
      setRows(prev =>
        prev.map(r => (r.sprk_navitemid === row.sprk_navitemid ? { ...r, sprk_displayname: trimmed } : r))
      );
      setRenamingIds(prev => new Set(prev).add(row.sprk_navitemid));
      try {
        await renameNavItem(row.sprk_navitemid, trimmed);
      } catch {
        // Non-fatal — revert to the old name; user can retry.
        setRows(prev =>
          prev.map(r => (r.sprk_navitemid === row.sprk_navitemid ? { ...r, sprk_displayname: previousName } : r))
        );
      } finally {
        setRenamingIds(prev => {
          const next = new Set(prev);
          next.delete(row.sprk_navitemid);
          return next;
        });
      }
    },
    [editValue]
  );

  // ── Gesture handlers ─────────────────────────────────────────────────────

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

  const renderRow = React.useCallback(
    (row: NavItemRecord) => {
      const isEditing = editingId === row.sprk_navitemid;

      return (
        <div
          key={row.sprk_navitemid}
          className={styles.row}
          role="listitem"
          tabIndex={0}
          data-testid={`bookmarks-tab-row-${row.sprk_navitemid}`}
          onClick={() => {
            if (isEditing) return;
            handleRowClick(row);
          }}
          onKeyDown={event => {
            if (isEditing) return;
            if (event.key === 'Enter' || event.key === ' ') {
              event.preventDefault();
              handleRowClick(row);
            }
          }}
        >
          <div className={styles.rowMain}>
            <span className={styles.rowIcon} data-testid={`bookmarks-tab-row-icon-${row.sprk_navitemid}`}>
              {rowIconFor({ pageType: row.sprk_pagetype, logicalName: row.sprk_targetlogicalname })}
            </span>
            {isEditing ? (
              <Input
                className={styles.rowNameInput}
                size="small"
                value={editValue}
                onClick={event => event.stopPropagation()}
                onChange={(_event, data) => setEditValue(data.value)}
                onKeyDown={event => {
                  event.stopPropagation();
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    void handleConfirmRename(row);
                  } else if (event.key === 'Escape') {
                    event.preventDefault();
                    handleCancelRename();
                  }
                }}
                autoFocus
                aria-label={`Rename ${row.sprk_displayname}`}
                data-testid={`bookmarks-tab-row-rename-input-${row.sprk_navitemid}`}
              />
            ) : (
              <Text className={styles.rowName} title={row.sprk_displayname}>
                {row.sprk_displayname}
              </Text>
            )}
          </div>
          {isEditing ? (
            <div className={styles.rowActions}>
              <Button
                appearance="transparent"
                size="small"
                icon={<Checkmark16Regular />}
                aria-label={`Save name for ${row.sprk_displayname}`}
                disabled={renamingIds.has(row.sprk_navitemid)}
                data-testid={`bookmarks-tab-row-rename-confirm-${row.sprk_navitemid}`}
                onClick={event => void handleConfirmRename(row, event)}
              />
              <Button
                appearance="transparent"
                size="small"
                icon={<Dismiss16Regular />}
                aria-label="Cancel rename"
                data-testid={`bookmarks-tab-row-rename-cancel-${row.sprk_navitemid}`}
                onClick={event => handleCancelRename(event)}
              />
            </div>
          ) : (
            <div className={styles.rowActions}>
              <Button
                appearance="transparent"
                size="small"
                icon={<Edit16Regular />}
                aria-label={`Rename ${row.sprk_displayname}`}
                className={mergeClasses(styles.rowPencil, 'navRowPencil')}
                data-testid={`bookmarks-tab-row-rename-${row.sprk_navitemid}`}
                onClick={event => handleStartRename(row, event)}
              />
              <Button
                appearance="transparent"
                size="small"
                icon={<Star16Filled />}
                aria-label={`Unpin ${row.sprk_displayname}`}
                aria-pressed={true}
                disabled={unpinningIds.has(row.sprk_navitemid)}
                data-testid={`bookmarks-tab-row-star-${row.sprk_navitemid}`}
                onClick={event => void handleUnpinClick(row, event)}
              />
            </div>
          )}
        </div>
      );
    },
    [
      editingId,
      editValue,
      handleCancelRename,
      handleConfirmRename,
      handleRowClick,
      handleStartRename,
      handleUnpinClick,
      renamingIds,
      styles,
      unpinningIds,
    ]
  );

  let listContent: React.ReactElement;
  if (status === 'loading') {
    listContent = (
      <div className={styles.centeredState} data-testid="bookmarks-tab-loading">
        <Spinner size="tiny" label="Loading bookmarks…" />
      </div>
    );
  } else if (status === 'error') {
    listContent = (
      <div className={styles.centeredState} data-testid="bookmarks-tab-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (rows.length === 0) {
    listContent = (
      <div className={styles.centeredState} data-testid="bookmarks-tab-empty">
        <Caption1>Pinned records, views, and links will appear here.</Caption1>
      </div>
    );
  } else {
    listContent = (
      <div className={styles.list} data-testid="bookmarks-tab" role="list" aria-label="Bookmarks">
        {rows.map(renderRow)}
      </div>
    );
  }

  return (
    <div className={styles.container} data-testid="bookmarks-tab-container">
      <div className={styles.bookmarkActions}>
        <Button
          appearance="secondary"
          size="small"
          icon={<Pin16Regular />}
          disabled={pinningCurrentPage}
          data-testid="bookmarks-tab-pin-current-page"
          onClick={() => void handlePinCurrentPageClick()}
        >
          Pin this page
        </Button>
        {pinCurrentPageMessage && (
          <Caption1 className={styles.bookmarkErrorMessage} data-testid="bookmarks-tab-pin-current-page-message">
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
            data-testid="bookmarks-tab-add-bookmark-input"
          />
          <Button
            appearance="primary"
            size="small"
            icon={<Add16Regular />}
            disabled={addingBookmark || !bookmarkInput.trim()}
            data-testid="bookmarks-tab-add-bookmark-submit"
            onClick={() => void handleAddBookmarkSubmit()}
          >
            Add
          </Button>
        </div>
        {bookmarkMessage && (
          <Caption1
            className={bookmarkMessage.tone === 'error' ? styles.bookmarkErrorMessage : styles.bookmarkInlineMessage}
            data-testid="bookmarks-tab-add-bookmark-message"
          >
            {bookmarkMessage.text}
          </Caption1>
        )}
      </div>
      {listContent}
    </div>
  );
};

export default BookmarksTab;
