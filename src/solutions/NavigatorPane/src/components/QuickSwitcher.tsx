/**
 * QuickSwitcher — the Navigator's persistent top-of-pane search box
 * (spaarke-side-pane-navigation-history-r1 task 070, spec FR-11).
 *
 * Replaces `NavigatorBody.tsx`'s task-040 placeholder `Input`
 * (`data-testid="navigator-search-placeholder"`) with real, local-first,
 * escalate-on-miss search over the Recent/Pinned/Views entries those three
 * tabs (task 041/050/060) have already loaded and — for Recent/Pinned —
 * already security-trimmed (task 080). See `../services/navigatorSearchIndex.ts`
 * for the shared-index design (why it is a module store, not lifted React
 * state) and its "Progressive availability" note.
 *
 * ── Local-first / escalate-on-miss (FR-11 HARD constraint) ──
 * As the user types, `filterLocalEntries` fuzzy-matches synchronously over
 * the CURRENT `useNavigatorSearchIndex()` snapshot — instant, in-memory, no
 * network call. Only when that produces ZERO matches for a non-empty query
 * does this component (after a short debounce, so a fast typist does not
 * fire one live query per keystroke) call `liveSearchService.ts`'s
 * `liveSearch()` — a live `Xrm.WebApi` record lookup (`CORE_ENTITY_SET`,
 * `contains()` filter) + `ViewService.getAllUserQueries()` view lookup —
 * and renders those results with a "Live" badge so the user can tell them
 * apart from the always-available local index.
 *
 * ── Navigation (Enter or click) ──
 * Logical (Dataverse) targets ALWAYS navigate via `Xrm.Navigation`
 * (`navigateTo`, `entityrecord`/`entitylist` — project HARD constraint,
 * never a constructed raw URL). Only a `weblink` target opens via
 * `window.open(url, '_blank', 'noopener')` — `noopener` prevents the new tab
 * from getting a `window.opener` back-reference (reverse-tabnabbing
 * protection), mirroring `PinnedTab.tsx`'s identical Bookmarks-group choice.
 *
 * ── Keyboard accelerator (NFR-07 / WCAG 2.1 AA) ──
 * Ctrl+K (Cmd+K on macOS), registered at `document` level. NavigatorPane is
 * a single `webresource` iframe occupying the WHOLE side pane (ADR-006) —
 * there is no nested iframe boundary inside the pane itself — so a
 * document-level `keydown` listener inside that one iframe's document
 * reaches every element the pane ever renders, "from anywhere in the pane"
 * per the task's acceptance criterion. "/" was considered and rejected: it
 * would fire while the user is typing inside ANY other Navigator text input
 * (e.g. `PinnedTab.tsx`'s "Paste or type a link to bookmark" field),
 * stealing keystrokes rather than being a safe global accelerator.
 * RUNTIME ASSUMPTION (per task 070's own dispatch note): this is verified by
 * unit test only in this task — not yet confirmed against the live UCI's
 * actual iframe/focus boundary at runtime. See
 * `projects/spaarke-side-pane-navigation-history-r1/notes/task-070-search.md`.
 *
 * ── Keyboard-navigable results (WCAG 2.1 AA) ──
 * The input is `role="combobox"` (`aria-expanded`, `aria-controls`,
 * `aria-autocomplete="list"`); the result list is `role="listbox"`; each row
 * is `role="option"` with `aria-selected`. ArrowUp/ArrowDown move the
 * highlighted option (`activeIndex`, reset to 0 — the top result — whenever
 * the visible result set changes); Enter activates the highlighted option
 * (defaulting to the top result if the user has not navigated); Escape
 * clears the query.
 *
 * ── No portal here (ADR-021) ──
 * The results dropdown is a normal in-tree absolutely-positioned `<div>`,
 * NOT a React `Portal`/Fluent `Popover`/`Menu` — so it inherits the ambient
 * `FluentProvider` the same way `RecentTab.tsx`/`PinnedTab.tsx`/
 * `ViewsTab.tsx` already document for their own non-portalled content; no
 * portal re-wrap is needed here. `NavigatorBody.tsx` keeps its existing
 * info-icon `Tooltip` (which DOES portal-render and IS re-wrapped) alongside
 * this component — unchanged responsibility split.
 *
 * ADR-022: React-16/17-safe APIs only (no `createRoot`) — this file is not
 * shared-lib code (lives in the NavigatorPane Code Page bundle, React 19),
 * consistent with the sibling tab files' own notes.
 *
 * @see ../services/navigatorSearchIndex.ts — the local index this searches
 * @see ../services/liveSearchService.ts — the live escalation
 * @see ../NavigatorBody.tsx — mounts this component above the tab scaffold
 */

import * as React from 'react';
import {
  Badge,
  Input,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  type InputOnChangeData,
} from '@fluentui/react-components';
import { Search20Regular } from '@fluentui/react-icons';
import { getXrm, type XrmContext } from '@spaarke/ui-components';
import {
  useNavigatorSearchIndex,
  type SearchEntryTarget,
  type SearchIndexEntry,
} from '../services/navigatorSearchIndex';
import { liveSearch } from '../services/liveSearchService';

// ─────────────────────────────────────────────────────────────────────────────
// Config
// ─────────────────────────────────────────────────────────────────────────────

/** Debounce before firing the live escalation, so a fast typist doesn't fire one query per keystroke. */
const LIVE_SEARCH_DEBOUNCE_MS = 300;
/** Cap on rendered local results (the local index can grow large over a long session). */
const MAX_LOCAL_RESULTS = 8;

// ─────────────────────────────────────────────────────────────────────────────
// Fuzzy match (local-first — instant, in-memory, no network)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Scores `label` against `query` (case-insensitive). Returns `null` for no
 * match. A contiguous substring match scores highest (earlier position,
 * tighter length ratio = better); otherwise an in-order subsequence match
 * (every query character appears, in order, somewhere in the label) scores
 * lower but still counts as a "fuzzy" hit. Exported for direct unit testing.
 */
export function fuzzyScore(query: string, label: string): number | null {
  const q = query.trim().toLowerCase();
  if (!q) return null;
  const l = label.toLowerCase();

  const idx = l.indexOf(q);
  if (idx !== -1) {
    return 1000 - idx * 2 - (l.length - q.length);
  }

  let qi = 0;
  let firstMatch = -1;
  let lastMatch = -1;
  for (let li = 0; li < l.length && qi < q.length; li++) {
    if (l[li] === q[qi]) {
      if (firstMatch === -1) firstMatch = li;
      lastMatch = li;
      qi++;
    }
  }
  if (qi < q.length) return null; // not every query character matched, in order

  return 100 - (lastMatch - firstMatch);
}

function filterLocalEntries(entries: SearchIndexEntry[], query: string): SearchIndexEntry[] {
  if (!query.trim()) return [];
  return entries
    .map(entry => ({ entry, score: fuzzyScore(query, entry.label) }))
    .filter((scored): scored is { entry: SearchIndexEntry; score: number } => scored.score !== null)
    .sort((a, b) => b.score - a.score)
    .slice(0, MAX_LOCAL_RESULTS)
    .map(scored => scored.entry);
}

// ─────────────────────────────────────────────────────────────────────────────
// Navigation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Navigate to a result's target. Logical (Dataverse) targets go through
 * `Xrm.Navigation` (never a raw URL — project MUST); `weblink` is the sole
 * exception (`window.open(url, '_blank', 'noopener')`). Mirrors
 * `PinnedTab.tsx`'s `navigateToRow`.
 */
function navigateToTarget(xrm: XrmContext, target: SearchEntryTarget | null): void {
  if (!target) return;

  if (target.type === 'weblink') {
    window.open(target.url, '_blank', 'noopener');
    return;
  }

  const navigation = xrm.Navigation;
  if (!navigation) return;

  if (target.type === 'entityrecord') {
    void navigation.navigateTo({
      pageType: 'entityrecord',
      entityName: target.entityLogicalName,
      entityId: target.entityId,
    });
    return;
  }

  // entitylist
  void navigation.navigateTo({
    pageType: 'entitylist',
    entityName: target.entityLogicalName,
    viewId: target.viewId,
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    position: 'relative',
    flexGrow: 1,
    minWidth: 0,
  },
  input: {
    width: '100%',
  },
  results: {
    position: 'absolute',
    top: 'calc(100% + 4px)',
    left: 0,
    right: 0,
    zIndex: 10,
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.padding(tokens.spacingVerticalXS, '0'),
    boxShadow: tokens.shadow16,
    maxHeight: '320px',
    overflowY: 'auto',
  },
  statusRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  resultRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalSNudge, tokens.spacingHorizontalM),
    cursor: 'pointer',
  },
  resultRowActive: {
    backgroundColor: tokens.colorNeutralBackground1Hover,
  },
  resultLabel: {
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

export const QuickSwitcher: React.FC = () => {
  const styles = useStyles();
  const allEntries = useNavigatorSearchIndex();
  const inputRef = React.useRef<HTMLInputElement | null>(null);

  const [query, setQuery] = React.useState('');
  const [liveResults, setLiveResults] = React.useState<SearchIndexEntry[]>([]);
  const [liveLoading, setLiveLoading] = React.useState(false);
  const [activeIndex, setActiveIndex] = React.useState(0);

  const localResults = React.useMemo(() => filterLocalEntries(allEntries, query), [allEntries, query]);
  const hasQuery = query.trim().length > 0;
  // Escalate ONLY when there is a query AND the local (instant) pass found
  // nothing — FR-11 local-first / escalate-on-miss (HARD constraint).
  const shouldEscalate = hasQuery && localResults.length === 0;

  React.useEffect(() => {
    if (!shouldEscalate) {
      setLiveResults([]);
      setLiveLoading(false);
      return;
    }

    let cancelled = false;
    setLiveLoading(true);
    const handle = window.setTimeout(() => {
      void liveSearch(query).then(found => {
        if (cancelled) return;
        setLiveResults(found);
        setLiveLoading(false);
      });
    }, LIVE_SEARCH_DEBOUNCE_MS);

    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [shouldEscalate, query]);

  const results = shouldEscalate ? liveResults : localResults;
  const showResults = hasQuery;

  // The highlighted option always tracks "top result" for the CURRENT result
  // set unless the user explicitly arrows away — reset on every result-set change.
  React.useEffect(() => {
    setActiveIndex(0);
  }, [results]);

  // ── Keyboard accelerator — see module docblock. ──
  React.useEffect(() => {
    function handleWindowKeyDown(event: KeyboardEvent): void {
      const isAccelerator = (event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k';
      if (!isAccelerator) return;
      event.preventDefault();
      inputRef.current?.focus();
    }
    document.addEventListener('keydown', handleWindowKeyDown);
    return () => document.removeEventListener('keydown', handleWindowKeyDown);
  }, []);

  const handleChange = React.useCallback((_event: React.ChangeEvent<HTMLInputElement>, data: InputOnChangeData) => {
    setQuery(data.value);
  }, []);

  const handleSelect = React.useCallback((entry: SearchIndexEntry) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToTarget(xrm, entry.target);
    setQuery('');
  }, []);

  const handleInputKeyDown = React.useCallback(
    (event: React.KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Escape') {
        setQuery('');
        return;
      }
      if (results.length === 0) return;

      if (event.key === 'ArrowDown') {
        event.preventDefault();
        setActiveIndex(i => Math.min(i + 1, results.length - 1));
      } else if (event.key === 'ArrowUp') {
        event.preventDefault();
        setActiveIndex(i => Math.max(i - 1, 0));
      } else if (event.key === 'Enter') {
        event.preventDefault();
        const top = results[activeIndex] ?? results[0];
        if (top) handleSelect(top);
      }
    },
    [results, activeIndex, handleSelect]
  );

  return (
    <div className={styles.root} data-testid="navigator-quickswitcher">
      <Input
        ref={inputRef}
        className={styles.input}
        contentBefore={<Search20Regular />}
        placeholder="Search Recent, Pinned, Views… (Ctrl+K)"
        aria-label="Search Navigator"
        value={query}
        onChange={handleChange}
        onKeyDown={handleInputKeyDown}
        role="combobox"
        aria-expanded={showResults}
        aria-controls="navigator-quickswitcher-listbox"
        aria-autocomplete="list"
        // Code-review fix (task 070): completes the ARIA 1.2 combobox
        // pattern — without this, ArrowUp/ArrowDown moves the VISUAL
        // highlight (`activeIndex`) but a screen reader user is never told
        // which option is active, since DOM focus stays on the input.
        aria-activedescendant={
          showResults && results.length > 0 ? `navigator-quickswitcher-option-${activeIndex}` : undefined
        }
        data-testid="navigator-quickswitcher-input"
      />
      {showResults && (
        <div
          id="navigator-quickswitcher-listbox"
          role="listbox"
          aria-label="Search results"
          className={styles.results}
          data-testid="navigator-quickswitcher-results"
        >
          {shouldEscalate && liveLoading ? (
            <div className={styles.statusRow} data-testid="navigator-quickswitcher-live-loading">
              <Spinner size="tiny" label="Searching Dataverse…" />
            </div>
          ) : results.length === 0 ? (
            <div className={styles.statusRow} data-testid="navigator-quickswitcher-empty">
              <Text>No matches.</Text>
            </div>
          ) : (
            results.map((entry, index) => (
              <div
                key={entry.id}
                id={`navigator-quickswitcher-option-${index}`}
                role="option"
                aria-selected={index === activeIndex}
                tabIndex={-1}
                className={mergeClasses(styles.resultRow, index === activeIndex && styles.resultRowActive)}
                data-testid={`navigator-quickswitcher-result-${entry.id}`}
                onMouseEnter={() => setActiveIndex(index)}
                onClick={() => handleSelect(entry)}
              >
                <Text className={styles.resultLabel} title={entry.label}>
                  {entry.label}
                </Text>
                <Badge appearance="tint" size="small" color={shouldEscalate ? 'informative' : 'brand'}>
                  {shouldEscalate ? `Live · ${entry.chipLabel}` : entry.chipLabel}
                </Badge>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
};

export default QuickSwitcher;
