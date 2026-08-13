/**
 * RecentTab — Recent (Viewed / Edited) tab content
 * (spaarke-side-pane-navigation-history-r1 task 041 Viewed, task 042 adds the
 * Viewed/Edited segmented toggle, spec FR-03/FR-04 UI).
 *
 * **Viewed** (task 041, unchanged): renders the signed-in user's `history`
 * `sprk_navitem` rows (produced by the task-030 capture poller —
 * `navigatorCaptureService.ts`) newest-first by `sprk_lastvisited`, each with
 * a type chip (Matter / Document / <other entity> / View / Page / Link) and
 * an inline star that promotes the row to a per-user pin.
 *
 * **Edited** (task 042, spec FR-04 / OQ-5): renders the signed-in user's
 * recently-MODIFIED records across the fixed core entity set
 * (`editedByMeService.listEditedByMe` — N per-entity `modifiedby=me` queries,
 * merged + sorted `modifiedon desc` client-side; NO audit entity, NO
 * dependency on the Viewed capture history). A record edited via a flow (not
 * the UI) still appears because the derivation is purely `modifiedon`-based.
 * Edited rows are always `EntityRecord`-shaped (a direct entity-table query
 * can only ever be a record) — they get a type chip but no pin star (pinning
 * an Edited row is out of this task's scope; the Viewed row for the same
 * target already offers it once captured).
 *
 * Mounted by `NavigatorBody.tsx`'s `recent` tab panel.
 *
 * Host-context only (project constraint): reads via `Xrm.WebApi`
 * (`navItemRepository.listHistoryItems`/`listPinItems`,
 * `editedByMeService.listEditedByMe`), navigates via `Xrm.Navigation` (never
 * a raw URL for a logical target), creates the pin via
 * `navItemRepository.createPinItem`. Mirrors the `useSprkMemoRepository`-style
 * Xrm.WebApi read pattern (list on demand, no external store). The Edited
 * list is lazy-loaded on first toggle to Edited (not on mount) to avoid six
 * extra WebApi round-trips for a user who never switches off Viewed.
 *
 * Chip mapping (closed set, task 041; task 042 reuses the same
 * `KNOWN_ENTITY_CHIP_LABELS`/`formatEntityLabel` helpers for Edited rows,
 * which are always the `EntityRecord` case):
 *   - `sprk_pagetype=EntityList`   -> "View"
 *   - `sprk_pagetype=Custom`       -> "Page" (OQ-6 best-effort generic label —
 *     see escalation note below; capture (030) does not currently write
 *     Custom history rows, so this branch is defensive/future-proofing)
 *   - `sprk_pagetype=WebLink`      -> "Link"
 *   - `sprk_pagetype=EntityRecord` -> "Matter"/"Document" for the two named
 *     entities, else a generic Title-Case label derived from the target's
 *     logical name (mirrors `navigatorCaptureService.ts`'s
 *     `formatEntityFallbackLabel` — kept as a local 2-line duplicate per
 *     CLAUDE.md §11 rather than promoting a shared util for one more
 *     consumer).
 *
 * OQ-6 escalation note: the task's `<escalation>` trigger calls for a STOP
 * when a pagetype can't supply a clean label/target chip. Per the task-041
 * dispatch instructions (which supersede the raw POML trigger for THIS
 * narrow case), an unresolvable custom page renders a generic "Page" chip
 * using its stored `sprk_displayname` rather than blocking — full custom-page
 * labeling is deferred to task 051. Documented in
 * `projects/spaarke-side-pane-navigation-history-r1/notes/task-041-oq6-deviation.md`.
 *
 * Read-time trimming (FR-12/NFR-04, task 080 full implementation): each
 * Viewed row's target is re-validated via `securityTrimService.ts`'s batched
 * `classifyTargets` BEFORE `setRows` is ever called — the trimmed/final row
 * array is the ONLY thing this component ever puts in state, so a `denied`
 * row's cached name can never flash on screen even momentarily (the
 * `loading` Spinner covers the entire window between "history rows fetched"
 * and "trim resolved"; see the module's `load()` effect below and
 * `projects/spaarke-side-pane-navigation-history-r1/notes/task-080-security-trimming.md`
 * for the full anti-flash write-up). `denied` (403/404-equivalent) rows are
 * dropped entirely; `transient` (network/timeout/5xx/ambiguous) rows are
 * KEPT — a blip must never permanently hide an otherwise-accessible row.
 * Non-`EntityRecord` rows (View/Page/Link) are exempt from the re-check by
 * `securityTrimService.ts` itself (no cached record name at risk — see that
 * module's docblock). Edited rows are NOT separately trimmed here — they
 * come from a live query against the target entity itself (not a cached
 * label), so an inaccessible record simply never appears in the result set
 * (standard Dataverse row security) — this same reasoning is why
 * `PinnedTab.tsx`'s Monitored group (also a live query) is likewise exempt.
 *
 * Segmented toggle (task 042): a local two-button Griffel-styled group (NOT
 * the shared `ViewToggle` component — that component is icon-only with a
 * hardcoded `'list'|'card'` domain and no text-label support; generalizing
 * its public API for one more consumer with a different (text-labeled)
 * domain was judged higher blast-radius than a small local implementation
 * that reuses the SAME border/radius/selected-state Griffel pattern
 * (`ViewToggle.styles.ts`) without touching the shared component (CLAUDE.md
 * §11 — extension is earned by a real second consumer of the SAME contract,
 * not a sibling surface with a different one).
 *
 * ADR-021: Fluent v9 tokens only; no portal-rendered component here (no
 * Popover/Tooltip/Dialog/Menu) so the fluent-v9-portal-gotcha re-wrap does
 * not apply — light/dark flow through the ambient `FluentProvider`
 * `NavigatorBody` already resolves (same mechanism, no duplicate theme
 * resolution needed here).
 * ADR-022: only React-16/17-safe APIs used (no `createRoot`, `React.FC`
 * return types compatible with both React 17 and 19 consumers of this file
 * were it ever promoted — it is not shared-lib code today, it lives in the
 * Code Page bundle which is React 19).
 *
 * @see navItemRepository.ts (task 030/041) — Xrm.WebApi CRUD + option-set maps
 * @see navigatorCaptureService.ts (task 030) — produces the history rows this tab renders (Viewed)
 * @see editedByMeService.ts (task 042) — produces the merged modifiedby=me rows (Edited)
 * @see NavigatorBody.tsx (task 040) — mounts this component in the `recent` tab panel
 */

import * as React from 'react';
import {
  Badge,
  Button,
  Caption1,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  type BadgeProps,
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
import { listEditedByMe, type EditedByMeItem } from '../services/editedByMeService';
import { classifyTargets, trimTargetFromRow } from '../services/securityTrimService';
import {
  setRecentSearchEntries,
  type SearchEntryTarget,
  type SearchIndexEntry,
} from '../services/navigatorSearchIndex';

// ─────────────────────────────────────────────────────────────────────────────
// Chip mapping
// ─────────────────────────────────────────────────────────────────────────────

const KNOWN_ENTITY_CHIP_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_document: 'Document',
};

/**
 * "sprk_project" -> "Project". Mirrors `navigatorCaptureService.ts`'s private
 * `formatEntityFallbackLabel` — duplicated here as a 2-line pure function
 * rather than exported/shared (CLAUDE.md §11: extension is earned by a real
 * second consumer needing the SAME module, not a sibling render surface).
 */
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

/** Maps a history row to its type chip. See module docblock "Chip mapping". */
function chipForRow(row: NavItemRecord): RowChip {
  switch (row.sprk_pagetype) {
    case NavItemPageType.EntityList:
      return { label: 'View', appearance: 'tint', color: 'informative' };
    case NavItemPageType.Custom:
      // OQ-6 best-effort — see module docblock escalation note.
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
 * Maps an Edited row to its type chip (task 042). Edited rows are always the
 * `EntityRecord` case (a direct entity-table query) — reuses the same
 * `KNOWN_ENTITY_CHIP_LABELS`/`formatEntityLabel` closed set as `chipForRow`'s
 * `EntityRecord` branch rather than duplicating the label logic.
 */
function chipForEditedItem(item: EditedByMeItem): RowChip {
  const known = KNOWN_ENTITY_CHIP_LABELS[item.targetLogicalName];
  return {
    label: known ?? formatEntityLabel(item.targetLogicalName),
    appearance: 'tint',
    color: 'brand',
  };
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
 * intentionally not clickable-to-navigate; they still render with a "Page"
 * chip.
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
 * above but returns DATA instead of a navigation side effect, so
 * `QuickSwitcher.tsx` can derive the same Enter/click destination without
 * this tab needing to expose `navigateToRow` itself. `EntityList` rows here
 * never carry a real `viewId` (see `navigateToRow`'s own `EntityList` case —
 * unchanged from task 041) — a view-kind BOOKMARK's `viewId` only exists on
 * `PinnedTab.tsx`'s rows (task 051).
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
  const chip = chipForRow(row);
  return {
    id: `recent-${row.sprk_navitemid}`,
    label: row.sprk_displayname,
    chipLabel: chip.label,
    target: targetForRow(row),
  };
}

/** Navigate to an Edited row's target — always `entityrecord` (task 042; see module docblock). */
function navigateToEditedItem(xrm: XrmContext, item: EditedByMeItem): void {
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
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  // Segmented Viewed/Edited toggle (task 042) — same border/radius/selected
  // Griffel pattern as `ViewToggle.styles.ts`, kept local (see module
  // docblock "Segmented toggle" note).
  modeToggleGroup: {
    display: 'inline-flex',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: '0',
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.overflow('hidden'),
  },
  modeToggleSegment: {
    minWidth: 'auto',
    ...shorthands.border('0'),
    ...shorthands.borderRadius('0'),
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  modeToggleSegmentSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    color: tokens.colorNeutralForeground1Selected,
  },
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
  rowName: {
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

/** Viewed/Edited segmented toggle selection (task 042). */
type RecentMode = 'viewed' | 'edited';

/** Edited load status. `idle` = never toggled to Edited yet (lazy-load — see module docblock). */
type EditedLoadStatus = 'idle' | 'loading' | 'ready' | 'error';

export const RecentTab: React.FC = () => {
  const styles = useStyles();

  const [status, setStatus] = React.useState<LoadStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [rows, setRows] = React.useState<NavItemRecord[]>([]);
  const [pinnedKeys, setPinnedKeys] = React.useState<Set<string>>(new Set());
  const [pinningIds, setPinningIds] = React.useState<Set<string>>(new Set());

  const [mode, setMode] = React.useState<RecentMode>('viewed');
  const [editedStatus, setEditedStatus] = React.useState<EditedLoadStatus>('idle');
  const [editedErrorMessage, setEditedErrorMessage] = React.useState<string | null>(null);
  const [editedItems, setEditedItems] = React.useState<EditedByMeItem[]>([]);

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

  // ── Edited (task 042) — lazy-loaded on first toggle to Edited. ──
  const loadEdited = React.useCallback(async () => {
    setEditedStatus('loading');
    setEditedErrorMessage(null);
    try {
      const items = await listEditedByMe();
      setEditedItems(items);
      setEditedStatus('ready');
    } catch (err) {
      // listEditedByMe() itself never rejects (each per-entity fetch is
      // independently try/caught) — this branch is defensive only.
      setEditedErrorMessage(err instanceof Error ? err.message : 'Failed to load edited items.');
      setEditedStatus('error');
    }
  }, []);

  const handleModeChange = React.useCallback(
    (next: RecentMode) => {
      setMode(next);
      if (next === 'edited' && editedStatus === 'idle') {
        void loadEdited();
      }
    },
    [editedStatus, loadEdited]
  );

  const handleEditedRowClick = React.useCallback((item: EditedByMeItem) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToEditedItem(xrm, item);
  }, []);

  // ── Segmented Viewed/Edited toggle — always rendered, above whichever
  // mode's content follows (see module docblock "Segmented toggle"). ──
  const modeToggle = (
    <div role="group" aria-label="Recent tab mode" className={styles.modeToggleGroup} data-testid="recent-tab-mode-toggle">
      <Button
        appearance="subtle"
        aria-pressed={mode === 'viewed'}
        onClick={() => handleModeChange('viewed')}
        className={mergeClasses(styles.modeToggleSegment, mode === 'viewed' && styles.modeToggleSegmentSelected)}
        data-testid="recent-tab-mode-viewed"
      >
        Viewed
      </Button>
      <Button
        appearance="subtle"
        aria-pressed={mode === 'edited'}
        onClick={() => handleModeChange('edited')}
        className={mergeClasses(styles.modeToggleSegment, mode === 'edited' && styles.modeToggleSegmentSelected)}
        data-testid="recent-tab-mode-edited"
      >
        Edited
      </Button>
    </div>
  );

  // ── Viewed content (task 041, unchanged — same testids as before the
  // task-042 toggle was added). ──
  let viewedContent: React.ReactElement;
  if (status === 'loading') {
    viewedContent = (
      <div className={styles.centeredState} data-testid="recent-tab-loading">
        <Spinner size="tiny" label="Loading recent items…" />
      </div>
    );
  } else if (status === 'error') {
    viewedContent = (
      <div className={styles.centeredState} data-testid="recent-tab-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (rows.length === 0) {
    viewedContent = (
      <div className={styles.centeredState} data-testid="recent-tab-empty">
        <Caption1>Recently viewed records will appear here.</Caption1>
      </div>
    );
  } else {
    viewedContent = (
      <div className={styles.root} data-testid="recent-tab" role="list" aria-label="Recently viewed records">
        {rows.map(row => {
          const key = pinKey(row.sprk_targetlogicalname, row.sprk_targetid);
          const pinned = pinnedKeys.has(key);
          const chip = chipForRow(row);
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
                <Text className={styles.rowName} title={row.sprk_displayname}>
                  {row.sprk_displayname}
                </Text>
                <Badge
                  appearance={chip.appearance}
                  color={chip.color}
                  size="small"
                  data-testid={`recent-tab-row-chip-${row.sprk_navitemid}`}
                >
                  {chip.label}
                </Badge>
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

  // ── Edited content (task 042). `idle` renders the same loading affordance
  // as `loading` — the first toggle-to-Edited kicks off the fetch, so there
  // is no meaningful "not yet started" state to show the user. ──
  let editedContent: React.ReactElement;
  if (editedStatus === 'idle' || editedStatus === 'loading') {
    editedContent = (
      <div className={styles.centeredState} data-testid="recent-tab-edited-loading">
        <Spinner size="tiny" label="Loading edited items…" />
      </div>
    );
  } else if (editedStatus === 'error') {
    editedContent = (
      <div className={styles.centeredState} data-testid="recent-tab-edited-error">
        <Caption1>{editedErrorMessage}</Caption1>
      </div>
    );
  } else if (editedItems.length === 0) {
    editedContent = (
      <div className={styles.centeredState} data-testid="recent-tab-edited-empty">
        <Caption1>Recently edited records will appear here.</Caption1>
      </div>
    );
  } else {
    editedContent = (
      <div className={styles.root} data-testid="recent-tab-edited" role="list" aria-label="Recently edited records">
        {editedItems.map(item => {
          const chip = chipForEditedItem(item);
          const rowKey = `${item.targetLogicalName}-${item.targetId}`;
          return (
            <div
              key={rowKey}
              className={styles.row}
              role="listitem"
              tabIndex={0}
              data-testid={`recent-tab-edited-row-${rowKey}`}
              onClick={() => handleEditedRowClick(item)}
              onKeyDown={event => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  handleEditedRowClick(item);
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
                  data-testid={`recent-tab-edited-row-chip-${rowKey}`}
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
    <div className={styles.container} data-testid="recent-tab-container">
      {modeToggle}
      {mode === 'viewed' ? viewedContent : editedContent}
    </div>
  );
};

export default RecentTab;
