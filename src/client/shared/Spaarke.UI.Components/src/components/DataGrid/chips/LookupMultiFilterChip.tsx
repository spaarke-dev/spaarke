/**
 * LookupMultiFilterChip — Fluent v9 multi-select filter chip with debounced
 * async type-to-search against an `IDataverseClient`.
 *
 * Renders a compact trigger button labelled "{first-record-name} +{N more}"
 * (or `label` when no selections). Clicking opens a Fluent v9 `<Popover>`
 * containing a `<Combobox>` (freeform) above a `<TagGroup>` of currently
 * selected records. Typing in the Combobox debounces 300ms and then issues a
 * FetchXML `contains` query to `IDataverseClient.retrieveMultipleRecords`,
 * caching results 60s in component state to avoid repeated network calls.
 * When the user clears the search box, the chip shows the top 50 most-recent
 * records (`<order attribute="createdon" descending="true" />`).
 *
 * Designed for any Dataverse lookup target — `systemuser`, `sprk_matter`,
 * `sprk_vendor`, etc. — via the `lookupTargetEntity` + `primaryNameAttribute`
 * props. The chip is fully controlled: it holds NO selection state, emits
 * `onChange(Set<string>)` to the parent.
 *
 * **Spec**: projects/spaarke-datagrid-framework-r1/spec.md FR-DG-07 + FR-DG-13
 * **Design**: design.md §6.4 (filter chip primitives), §8.1 (FetchXML contains)
 * **ADRs**: ADR-021 (Fluent v9 + dark mode), ADR-022 (React-16-safe)
 *
 * **NFR-03 compliance**: the Popover content is re-wrapped in
 * `<FluentProvider applyStylesToPortals>` (theme inherited via React context)
 * so dark-mode + portal theme inheritance work across PCF iframe contexts
 * where the root FluentProvider's default `applyStylesToPortals` may not
 * propagate through the portaled DOM subtree.
 *
 * **NFR-02 compliance**: zero raw hex; all colors via `tokens.*` and
 * `dataGridTokens` (themselves token-derived).
 *
 * **React-16-safe**: no `useId`, no `useSyncExternalStore`, no `createRoot`.
 *
 * @see TagFilter — sibling multi-select chip for static option sets (no async lookup)
 * @see ../tokens.ts — `dataGridTokens.filterChip` for surface tokens
 */

import * as React from 'react';
import {
  Button,
  Combobox,
  Option,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  FluentProvider,
  Tag,
  TagGroup,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  type ComboboxProps,
} from '@fluentui/react-components';
import { ChevronDownRegular, DismissRegular } from '@fluentui/react-icons';

import type { IDataverseClient } from '../../../services/IDataverseClient';
import { dataGridTokens } from '../tokens';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Minimal projection of a Dataverse record used by the chip — just the id +
 * display name. The chip extracts these from the rows returned by
 * `IDataverseClient.retrieveMultipleRecords` using the `primaryNameAttribute`
 * prop + the canonical `{entityName}id` primary-id attribute pattern.
 */
export interface LookupRecord {
  id: string;
  name: string;
}

/**
 * Props for {@link LookupMultiFilterChip}.
 *
 * Fully controlled: `value` + `onChange` form the selection contract. Pass
 * an empty `Set<string>()` for "no selection". The chip does NOT hydrate
 * selected-record display names automatically — pass `selectedRecords` if
 * the parent already knows them (avoids one extra round-trip per render);
 * otherwise the chip shows raw IDs until the user reopens the popover and
 * the cache resolves them.
 */
export interface LookupMultiFilterChipProps {
  /** Logical name of the lookup target (e.g. `"systemuser"`, `"sprk_matter"`). */
  lookupTargetEntity: string;

  /**
   * Logical name of the primary-name attribute on the lookup target
   * (e.g. `"fullname"` for systemuser, `"sprk_mattername"` for sprk_matter).
   * Used in the FetchXML `<order>` clause and to extract the display label
   * from each row.
   */
  primaryNameAttribute: string;

  /** Currently selected record IDs. Controlled. */
  value: Set<string>;

  /**
   * OPTIONAL — pre-resolved display info for selected IDs. When provided, the
   * chip uses these names for the "{first} +{N more}" label and the TagGroup
   * pills, avoiding ID-fallback rendering. Parent may omit if it doesn't
   * have the names cached.
   */
  selectedRecords?: ReadonlyArray<LookupRecord>;

  /** Fires when the user toggles a selection. Emits the new selection set. */
  onChange: (next: Set<string>) => void;

  /**
   * Chip trigger label when `value.size === 0` (e.g. `"Owner"`, `"Assigned to"`).
   * When at least one record is selected, the label becomes
   * `"{first-record-name} +{N more}"` (or just the first name when N=0).
   */
  label: string;

  /**
   * Dataverse access — defaulting to a prop (not constructed internally) keeps
   * the chip host-agnostic per ADR-012. Storybook passes a mock; PCF passes
   * `XrmDataverseClient`; Code Pages pass `BffDataverseClient`.
   */
  dataverseClient: IDataverseClient;

  /** OPTIONAL — additional class merged AFTER component classes (Spaarke convention). */
  className?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Idle delay before issuing the async lookup. design.md §8.1 acceptance. */
const DEBOUNCE_MS = 300;

/** Per-cache-entry TTL. Spec: "Cache results 60s in component state". */
const CACHE_TTL_MS = 60_000;

/** Top-N rows returned on empty-search (initial popover open). */
const EMPTY_SEARCH_TOP_N = 50;

/** FetchXML `contains` page size when the user is typing. Mirrors empty-search top-N. */
const SEARCH_PAGE_SIZE = 50;

// ─────────────────────────────────────────────────────────────────────────────
// Pure helpers (module scope so each render reuses the reference)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Build the FetchXML for either an empty-state (top-50 most recent) OR a
 * `contains`-style typed search. Kept as a single function so the FetchXML
 * shape is colocated and the cache key construction stays consistent.
 *
 * The primary-id attribute is derived from the conventional `{entity}id`
 * pattern — this matches every standard Dataverse table (systemuser →
 * systemuserid, sprk_matter → sprk_matterid). Custom-id attributes (rare)
 * would need a future `primaryIdAttribute` prop; out of scope for R1.
 */
function buildFetchXml(lookupTargetEntity: string, primaryNameAttribute: string, search: string): string {
  const primaryId = `${lookupTargetEntity}id`;
  // XML-encode user input — even though Dataverse will tolerate most characters
  // inside an attribute value, `&`, `<`, `"` MUST be escaped to keep the
  // resulting FetchXML well-formed.
  const escaped = search.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

  const isEmpty = search.length === 0;
  const top = isEmpty ? EMPTY_SEARCH_TOP_N : SEARCH_PAGE_SIZE;

  // Note: for empty search we order by `createdon desc` to surface most-recent
  // records; for a typed search we order by the primary-name attribute so
  // the user sees alphabetical matches. Both modes return at most `top` rows.
  const orderXml = isEmpty
    ? '<order attribute="createdon" descending="true" />'
    : `<order attribute="${primaryNameAttribute}" descending="false" />`;

  const filterXml = isEmpty
    ? ''
    : `<filter type="and"><condition attribute="${primaryNameAttribute}" operator="like" value="%${escaped}%" /></filter>`;

  return (
    `<fetch top="${top}">` +
    `<entity name="${lookupTargetEntity}">` +
    `<attribute name="${primaryId}" />` +
    `<attribute name="${primaryNameAttribute}" />` +
    filterXml +
    orderXml +
    '</entity>' +
    '</fetch>'
  );
}

/** Project a raw Dataverse row to the chip's narrower {@link LookupRecord}. */
function projectRow(row: Record<string, unknown>, primaryId: string, primaryNameAttribute: string): LookupRecord {
  const id = String(row[primaryId] ?? '');
  const rawName = row[primaryNameAttribute];
  const name = typeof rawName === 'string' && rawName !== '' ? rawName : id;
  return { id, name };
}

/** Cache key — entity + lowercased search keeps `"Acme"` and `"acme"` aligned. */
function cacheKey(lookupTargetEntity: string, search: string): string {
  return `${lookupTargetEntity}${search.toLowerCase()}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Debounce hook — React-16-safe (no useSyncExternalStore, no useId).
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Returns `value` debounced by `delayMs`. Pure implementation using
 * `useEffect` + `setTimeout`. Idiomatic React-16-safe pattern: when `value`
 * changes, schedule a state update `delayMs` later; the cleanup function
 * cancels the prior pending timer so only the last-typed value resolves.
 *
 * Exported (named) for unit-testability in case future tasks add Jest tests
 * for the chip; not part of the `index.ts` barrel.
 *
 * @internal
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = React.useState<T>(value);

  React.useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);

  return debounced;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (module scope per ADR-021)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Trigger button wrapper — `inline-flex` so the chip sits alongside other
   * filter chips in a horizontal strip with `dataGridTokens.filterChip.gap`
   * between them (gap is owned by the parent chip-strip container).
   */
  root: {
    display: 'inline-flex',
    alignItems: 'center',
  },
  /**
   * The trigger button itself. We rely on Fluent's `appearance="subtle"`
   * default chrome — no custom borders / backgrounds — so light + dark
   * + Windows HC all resolve through Fluent tokens automatically.
   */
  trigger: {
    fontSize: dataGridTokens.filterChip.fontSize,
  },
  /**
   * Popover surface — sized to comfortably show ~50 single-line options
   * without becoming a screen-filler. `maxHeight` + the Combobox's own
   * internal scrolling keep long result sets ergonomic.
   */
  surface: {
    minWidth: '320px',
    maxWidth: '420px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    ...shorthands.padding(tokens.spacingVerticalS),
  },
  combobox: {
    // Combobox auto-sizes; we let it.
  },
  /** Loading row inside the Combobox header — spinner + caption. */
  loadingRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    gap: tokens.spacingHorizontalS,
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    color: tokens.colorNeutralForeground3,
  },
  /** Empty / no-results caption. */
  emptyRow: {
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    color: tokens.colorNeutralForeground3,
  },
  /** Selected-record pills row below the Combobox. */
  selectedRow: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    rowGap: tokens.spacingVerticalXS,
  },
  /** "Clear all" subtle button at the end of the pills row. */
  clearAll: {
    color: tokens.colorBrandForegroundLink,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

interface CacheEntry {
  at: number;
  records: LookupRecord[];
}

/**
 * Fluent v9 multi-select lookup filter chip with debounced async search.
 * See module-level JSDoc for the full design rationale and ADR references.
 */
export const LookupMultiFilterChip: React.FC<LookupMultiFilterChipProps> = ({
  lookupTargetEntity,
  primaryNameAttribute,
  value,
  selectedRecords,
  onChange,
  label,
  dataverseClient,
  className,
}) => {
  const styles = useStyles();

  // ── Search state ───────────────────────────────────────────────────────────
  // `searchInput` mirrors the Combobox value (controlled). `debouncedSearch`
  // lags by 300ms — the effect that fires the network call depends on
  // `debouncedSearch`, NOT `searchInput`, which is the debouncing mechanism.
  const [open, setOpen] = React.useState<boolean>(false);
  const [searchInput, setSearchInput] = React.useState<string>('');
  const debouncedSearch = useDebouncedValue(searchInput, DEBOUNCE_MS);

  // ── Result state ───────────────────────────────────────────────────────────
  const [results, setResults] = React.useState<LookupRecord[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);

  // ── 60s cache (component-scoped, no global leak) ───────────────────────────
  // `useRef<Map>` keeps the cache stable across renders without re-creating
  // it. Entries expire by timestamp check in `loadResults` below; we do not
  // prune proactively because the chip's lifetime is the parent's lifetime
  // and the Map size is bounded by the user's distinct searches.
  const cacheRef = React.useRef<Map<string, CacheEntry>>(new Map());

  // ── Track in-flight request to cancel-on-new-input ─────────────────────────
  // We don't use AbortController because IDataverseClient does not take a
  // signal — instead we record the latest request key and ignore stale
  // responses by comparing on resolve.
  const latestRequestKeyRef = React.useRef<string>('');
  const isMountedRef = React.useRef<boolean>(true);

  React.useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  // ── Lookup effect — fires when popover opens OR debouncedSearch changes ───
  // We skip the network when the popover is closed (nothing to display) AND
  // skip when a fresh cache entry exists (TTL not yet exceeded).
  React.useEffect(() => {
    if (!open) {
      return;
    }

    const key = cacheKey(lookupTargetEntity, debouncedSearch);
    latestRequestKeyRef.current = key;

    // Cache hit?
    const cached = cacheRef.current.get(key);
    if (cached && Date.now() - cached.at < CACHE_TTL_MS) {
      setResults(cached.records);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    const fetchXml = buildFetchXml(lookupTargetEntity, primaryNameAttribute, debouncedSearch);
    const primaryId = `${lookupTargetEntity}id`;

    (async () => {
      try {
        const result = await dataverseClient.retrieveMultipleRecords<Record<string, unknown>>(
          lookupTargetEntity,
          fetchXml
        );
        // Ignore stale responses (user typed more before this one returned).
        if (cancelled || !isMountedRef.current) return;
        if (latestRequestKeyRef.current !== key) return;

        const projected = (result.entities ?? []).map(row => projectRow(row, primaryId, primaryNameAttribute));
        cacheRef.current.set(key, { at: Date.now(), records: projected });
        setResults(projected);
        setIsLoading(false);
      } catch (err) {
        if (cancelled || !isMountedRef.current) return;
        if (latestRequestKeyRef.current !== key) return;
        setError(err instanceof Error ? err : new Error(String(err)));
        setResults([]);
        setIsLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [open, debouncedSearch, lookupTargetEntity, primaryNameAttribute, dataverseClient]);

  // ── Selection helpers ──────────────────────────────────────────────────────
  // Fluent v9 Combobox emits the full selection set on each toggle via
  // `onOptionSelect` (selectedOptions: string[]). We translate that to our
  // Set<string> contract for the parent.
  const handleOptionSelect = React.useCallback<NonNullable<ComboboxProps['onOptionSelect']>>(
    (_event, data) => {
      const next = new Set<string>(data.selectedOptions);
      onChange(next);
    },
    [onChange]
  );

  // Removing a single tag from the selected-pills row.
  const handleRemoveTag = React.useCallback(
    (idToRemove: string) => {
      const next = new Set(value);
      next.delete(idToRemove);
      onChange(next);
    },
    [onChange, value]
  );

  const handleClearAll = React.useCallback(() => {
    onChange(new Set<string>());
  }, [onChange]);

  const handleOpenChange = React.useCallback((_event: unknown, data: { open: boolean }) => {
    setOpen(data.open);
    // Reset the search box when closing so re-opening starts fresh.
    if (!data.open) {
      setSearchInput('');
    }
  }, []);

  // Combobox `onInput` provides raw text typed by the user (freeform mode).
  const handleInput: React.FormEventHandler<HTMLInputElement> = React.useCallback(ev => {
    setSearchInput((ev.target as HTMLInputElement).value ?? '');
  }, []);

  // ── Compute the displayed chip label ───────────────────────────────────────
  // Priority for resolving each selected ID to a name:
  //   1. `selectedRecords` prop (parent-supplied — most authoritative)
  //   2. current `results` (filled when the popover has been opened in this session)
  //   3. fallback to the raw ID
  // Computed once per render rather than memoized because Set iteration +
  // small joins are cheap and a `useMemo` here would add more bookkeeping
  // than it saves.
  const selectedIds: string[] = Array.from(value);
  const lookupName = (id: string): string => {
    const fromProp = selectedRecords?.find(r => r.id === id);
    if (fromProp) return fromProp.name;
    const fromResults = results.find(r => r.id === id);
    if (fromResults) return fromResults.name;
    return id;
  };

  const triggerLabel = (() => {
    if (selectedIds.length === 0) return label;
    const firstName = lookupName(selectedIds[0]);
    if (selectedIds.length === 1) return firstName;
    return `${firstName} +${selectedIds.length - 1} more`;
  })();

  // ── Build the option list for the Combobox ─────────────────────────────────
  // We always include every currently-selected record at the top of the list
  // (even if it's not in the current search results), so unchecking still
  // works. De-duplicate by ID. The search results sort already; we preserve
  // that order for the non-selected tail.
  const optionRecords: LookupRecord[] = (() => {
    const seen = new Set<string>();
    const merged: LookupRecord[] = [];
    // Selected first (so they always appear in the dropdown).
    for (const id of selectedIds) {
      if (seen.has(id)) continue;
      seen.add(id);
      merged.push({ id, name: lookupName(id) });
    }
    for (const rec of results) {
      if (seen.has(rec.id)) continue;
      seen.add(rec.id);
      merged.push(rec);
    }
    return merged;
  })();

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <div className={mergeClasses(styles.root, className)} data-testid="lookup-multi-filter-chip">
      <Popover open={open} onOpenChange={handleOpenChange} trapFocus positioning="below-start">
        <PopoverTrigger disableButtonEnhancement>
          <Button
            className={styles.trigger}
            appearance="subtle"
            iconPosition="after"
            icon={<ChevronDownRegular aria-hidden="true" />}
            aria-label={selectedIds.length > 0 ? `${label} (${selectedIds.length} selected): ${triggerLabel}` : label}
            data-testid="lookup-multi-filter-chip-trigger"
          >
            {triggerLabel}
          </Button>
        </PopoverTrigger>

        <PopoverSurface>
          {/*
            NFR-03 portal-theme re-wrap. The Popover surface renders into a
            React Portal which escapes the root FluentProvider's DOM subtree;
            in PCF iframe contexts the root provider's applyStylesToPortals
            default does not always propagate. The nested FluentProvider here
            inherits its theme via React context (which DOES flow through the
            React tree even though the DOM is portaled), and re-asserts
            applyStylesToPortals so any nested portal (e.g., Combobox listbox)
            stays themed. This is the canonical Option A from
            .claude/patterns/ui/fluent-v9-portal-gotcha.md, deliberately NOT
            passing an explicit `theme` prop so the customer-tenant theme is
            never accidentally shadowed.
          */}
          <FluentProvider applyStylesToPortals>
            <div className={styles.surface}>
              <Combobox
                className={styles.combobox}
                freeform
                multiselect
                placeholder={`Search ${lookupTargetEntity}…`}
                value={searchInput}
                onInput={handleInput}
                selectedOptions={selectedIds}
                onOptionSelect={handleOptionSelect}
                aria-label={`Search ${label}`}
                data-testid="lookup-multi-filter-chip-combobox"
              >
                {isLoading ? (
                  <div className={styles.loadingRow} role="status" aria-live="polite">
                    <Spinner size="tiny" />
                    <Text size={200}>Searching…</Text>
                  </div>
                ) : null}

                {error && !isLoading ? (
                  <div className={styles.emptyRow} role="alert">
                    <Text size={200}>Could not load options.</Text>
                  </div>
                ) : null}

                {!isLoading && !error && optionRecords.length === 0 ? (
                  <div className={styles.emptyRow}>
                    <Text size={200}>No matches</Text>
                  </div>
                ) : null}

                {optionRecords.map(rec => (
                  <Option key={rec.id} value={rec.id} text={rec.name}>
                    {rec.name}
                  </Option>
                ))}
              </Combobox>

              {selectedIds.length > 0 ? (
                <TagGroup
                  className={styles.selectedRow}
                  role="list"
                  aria-label={`Selected ${label} filters`}
                  data-testid="lookup-multi-filter-chip-selected"
                >
                  {selectedIds.map(id => {
                    const name = lookupName(id);
                    return (
                      <Tag
                        key={id}
                        shape="rounded"
                        size="small"
                        appearance="brand"
                        dismissible
                        dismissIcon={<DismissRegular aria-label={`Remove ${name}`} />}
                        value={id}
                        onClick={() => handleRemoveTag(id)}
                        data-testid={`lookup-multi-filter-chip-tag-${id}`}
                      >
                        {name}
                      </Tag>
                    );
                  })}
                  <Button
                    className={styles.clearAll}
                    appearance="subtle"
                    size="small"
                    onClick={handleClearAll}
                    aria-label={`Clear all ${label} filters`}
                    data-testid="lookup-multi-filter-chip-clear-all"
                  >
                    Clear all
                  </Button>
                </TagGroup>
              ) : null}
            </div>
          </FluentProvider>
        </PopoverSurface>
      </Popover>
    </div>
  );
};

export default LookupMultiFilterChip;
