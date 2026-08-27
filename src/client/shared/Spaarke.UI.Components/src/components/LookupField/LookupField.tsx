/**
 * LookupField.tsx
 * Reusable search-as-you-type lookup field for entity reference searches.
 *
 * Layout (OOB inline-lookup parity, 2026-08-27):
 *   ┌───────────────────────────────────────────────┐
 *   │ Look for Practice Area                    [🔍] │  ← icon is a BUTTON
 *   ├───────────────────────────────────────────────┤
 *   │  Commercial Transactions                     ▲ │
 *   │  Intellectual Property Patents               ░ │  ← thin overlay
 *   │  Intellectual Property Trademarks            ░ │     scrollbar
 *   │  Mergers & Acquisitions                      ▼ │
 *   ├───────────────────────────────────────────────┤
 *   │                                  🔍 Advanced  │  ← pinned, right-aligned
 *   └───────────────────────────────────────────────┘
 *   — OR —
 *   Selected: [Litigation] [x]
 *
 * ── Why this shape ─────────────────────────────────────────────────────────
 * Model-driven forms render lookups with a first-party control that Microsoft
 * exposes NO API to instantiate: `Xrm.Utility.lookupObjects` is a callable
 * function (it opens the *advanced* dialog), but the INLINE control is a class
 * the form runtime owns. `ComponentFramework.Factory` carries exactly two
 * members — `getPopupService` and `requestRender` — so there is no supported
 * host for it inside a PCF or a Code Page.
 *
 * This component therefore REPRODUCES the OOB inline shape with supported
 * primitives, and escalates to the real OOB dialog via `onAdvanced`. That is
 * the "proprietary browse + OOB escalation" pattern in
 * `docs/standards/MODAL-DECISION-CRITERIA.md`.
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors (ADR-021)
 *   - Full keyboard support (arrow keys, Enter, Escape)
 *   - NO "+ New" in the footer — deliberate; see `onAdvanced` prop docs
 */

import * as React from 'react';
import { Input, Text, Button, Spinner, Field, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import type { InputProps } from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import type { ILookupItem } from '../../types/LookupTypes';
import { thinScrollbarStyle } from '../../theme/scrollbar';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ILookupFieldProps {
  /** Field label displayed above the input. */
  label: string;
  /** Whether the field is required. */
  required?: boolean;
  /** Placeholder text for the search input. */
  placeholder?: string;
  /** Currently selected lookup item (or null). */
  value: ILookupItem | null;
  /** Called when the user selects or clears an item. */
  onChange: (item: ILookupItem | null) => void;
  /** Async search function — called with the query string, returns results. */
  onSearch: (query: string) => Promise<ILookupItem[]>;
  /** Optional content rendered after the label (e.g., AI badge). */
  labelExtra?: React.ReactNode;
  /** Minimum characters before search fires. Default: 1. */
  minSearchLength?: number;
  /**
   * Optional chip icon rendered before the selected item's name. Matches
   * OOB Dataverse lookup chip presentation (e.g., chain-link icon for
   * cross-entity references). Ignored when no value is selected. Added
   * v1.0.4 for record-header lookup parity with OOB form fields.
   */
  chipIcon?: React.ReactElement;
  /**
   * When `true`, focusing the empty input triggers a search with the current
   * (possibly empty) term and opens the results dropdown so the user can
   * browse values without having to guess. Consumers using this MUST have an
   * `onSearch` implementation that returns a useful default set for an empty
   * query (e.g., top 10 unfiltered records) and MUST pass `minSearchLength={0}`.
   *
   * Added v1.0.5 for record-header lookup parity with OOB Dataverse pickers,
   * which show the option list immediately on focus (no keystroke required).
   */
  openOnFocus?: boolean;
  /**
   * Opens the OOB **advanced lookup** dialog (`Xrm.Utility.lookupObjects`).
   *
   * When supplied, an **Advanced** action renders right-aligned in the results
   * footer — the same affordance the OOB inline lookup offers, and the escape
   * hatch to the full record browser with views, filters and search.
   *
   * OMITTED BY DEFAULT ON PURPOSE. The wizard consumers run in Code Pages,
   * where `lookupObjects` may be unavailable (the BFF navigation adapter
   * implements `openLookup` as a no-op — see `ui-create-wizard-enhancements-r1`
   * task 010), so the footer must be opt-in rather than assumed.
   *
   * ── Why there is deliberately NO "+ New" ──────────────────────────────
   * The OOB footer also offers **+ New**. We do not, by owner decision
   * (2026-08-27): the lookup targets here are taxonomy tables
   * (`sprk_projecttype_ref`, `sprk_practicearea_ref`, …) that users are not
   * permitted to add to, and record creation does not belong on this surface.
   * Do NOT "restore parity" by adding it.
   */
  onAdvanced?: () => void | Promise<void>;
  /**
   * Number of `FieldGrid` columns this cell should occupy (1..3), applied as
   * an inline `gridColumn: span N` on this component's own wrapper.
   *
   * `FieldGrid` is renderer-agnostic — it never touches `gridColumn` on its
   * children, so each cell owns its span (record-header FR-03). Without this
   * prop a consumer has to hand-roll a wrapper `<div style={{ gridColumn }}>`
   * around the field, which is what `MatterHeaderView` does today.
   *
   * OMIT outside a CSS grid. When undefined no `gridColumn` is emitted at all,
   * so every pre-existing consumer (the twelve `Create*Wizard` steps, which
   * lay out with flex) is byte-identical.
   */
  span?: 1 | 2 | 3;
  /**
   * Fluent `Input` appearance for the search box. Defaults to `'outline'` —
   * the boxed look this component has always had.
   *
   * ── Use `'filled-darker'` to match an OOB Dataverse FORM field ────────────
   * Verified against the shipped Fluent v9 source rather than inferred:
   *   - `filled-darker` sets `backgroundColor: colorNeutralBackground3` (the
   *     same gray the record-header read cells already use) and `filled` sets
   *     `borderColor: colorTransparentStroke`, so there is NO border box; and
   *   - the 2px brand focus underline is an `::after` on the input's BASE
   *     style — not on the `outline`/`underline` variants — so it renders for
   *     every appearance, animating in on `:focus-within`.
   * Together that is exactly OOB's "no border, gray fill, blue line on focus".
   *
   * The default is deliberately NOT changed: the twelve `Create*Wizard`
   * consumers sit beside plain `outline` inputs in Code Page forms, where a
   * form-field look would make the lookup the odd one out.
   */
  appearance?: InputProps['appearance'];
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    // Positioning context for the three transient panels below the field.
    // Without it they anchor to the nearest positioned ancestor — which in a
    // form is unpredictable — instead of to this cell.
    position: 'relative',
  },

  /**
   * Shared geometry for everything that appears BELOW the field: the results
   * list, the loading spinner and the empty-state message.
   *
   * ══════════════════════════════════════════════════════════════════════════
   * THESE MUST OVERLAY, NOT DISPLACE. This is a layout-shift fix, not styling.
   * ══════════════════════════════════════════════════════════════════════════
   * All three were previously in NORMAL FLOW, so opening the dropdown pushed
   * every field below it down the form, and committing a value let the whole
   * form snap back up — reported as "the screen jumps" (UAT, v1.1.10).
   *
   * An earlier comment here claimed `shadow8` made the list "elevate over the
   * following field instead of pushing it down". That was wrong: a box-shadow
   * paints over neighbours but does not remove the element from flow. Only
   * `position: absolute` does.
   */
  overlayBelowField: {
    position: 'absolute',
    top: '100%',
    left: 0,
    right: 0,
    // Above sibling form fields, far below Fluent's portal layers (Dialog,
    // Popover and Tooltip all sit at ~1000000), so this can never cover a
    // modal. Fluent v9 ships no z-index token.
    zIndex: 100,
    marginTop: tokens.spacingVerticalXXS,
  },

  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
  },

  resultsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '1px',
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow8,
  },
  /**
   * The scrollable region — separated from the container so the Advanced
   * footer stays PINNED while the options scroll under it (OOB behaviour).
   *
   * ~5.5 rows at the current row height, so the cut-off row signals "more
   * below" rather than the list ending flush.
   */
  resultsScroll: {
    display: 'flex',
    flexDirection: 'column',
    gap: '1px',
    maxHeight: '240px',
    overflowY: 'auto',
    // The Spaarke thin scrollbar — the SHARED one, not a local copy. An earlier
    // revision hand-rolled an almost-identical block here before noticing this
    // helper existed (CLAUDE.md §11: extend, don't re-derive). It is also the
    // reason the `::-webkit-scrollbar` rules live on THIS element rather than a
    // parent: those pseudo-elements do not cascade. See `thin-scrollbar.md`.
    ...thinScrollbarStyle,
  },
  /** Pinned footer — Advanced only, right-aligned. No "+ New" (see props). */
  resultsFooter: {
    display: 'flex',
    justifyContent: 'flex-end',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  resultItem: {
    display: 'flex',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    cursor: 'pointer',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '-2px',
    },
  },
  resultItemHighlighted: {
    backgroundColor: tokens.colorNeutralBackground1Hover,
  },

  selectedChip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalXXS,
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground2,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorBrandStroke2,
    borderRightColor: tokens.colorBrandStroke2,
    borderBottomColor: tokens.colorBrandStroke2,
    borderLeftColor: tokens.colorBrandStroke2,
    alignSelf: 'flex-start',
    // Match the Fluent medium field height (32px, `fieldHeights.medium` in
    // @fluentui/react-input) EXACTLY. Committing a value swaps the Input for
    // this chip, and any height difference between them reflows the grid row —
    // the second half of the "screen jumps" report. No marginTop, for the same
    // reason: the Input has none.
    minHeight: '32px',
    boxSizing: 'border-box',
  },
  selectedChipName: {
    color: tokens.colorBrandForeground2,
  },
  chipIcon: {
    display: 'inline-flex',
    alignItems: 'center',
    color: tokens.colorBrandForeground2,
    // Ensure the icon size is fixed even when the parent flex layout shrinks.
    flexShrink: 0,
  },

  // Spinner + empty state also overlay (see `overlayBelowField`), so they need
  // the same card chrome as the results list to stay legible over content.
  panelSurface: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    boxShadow: tokens.shadow8,
  },
  spinnerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    display: 'block',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    textAlign: 'center',
  },
});

// ---------------------------------------------------------------------------
// LookupField (exported)
// ---------------------------------------------------------------------------

export const LookupField: React.FC<ILookupFieldProps> = ({
  label,
  required,
  placeholder,
  value,
  onChange,
  onSearch,
  labelExtra,
  minSearchLength = 1,
  chipIcon,
  openOnFocus = false,
  onAdvanced,
  span,
  appearance = 'outline',
}) => {
  const styles = useStyles();

  // Undefined `span` emits NO inline style at all — see the prop docs. This is
  // what keeps the flex-laid-out wizard consumers unchanged.
  const gridColumnStyle: React.CSSProperties | undefined =
    span === undefined ? undefined : { gridColumn: `span ${span}` };

  const [searchTerm, setSearchTerm] = React.useState('');
  const [results, setResults] = React.useState<ILookupItem[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [showResults, setShowResults] = React.useState(false);
  const [highlightedIndex, setHighlightedIndex] = React.useState(-1);
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const wrapperRef = React.useRef<HTMLDivElement>(null);
  /**
   * Needed to keep the brand focus underline lit while browsing.
   *
   * Fluent draws that 2px line from `:focus-within` on the Input root, so it
   * only shows while the real `<input>` holds focus. Clicking the magnifier
   * from a cold field previously left focus on `<body>` — the field opened its
   * list with no underline, which is the "there is no blue line" report. The
   * results list is a SIBLING of the Input, so focus must stay in the input
   * rather than move into the list.
   */
  const inputRef = React.useRef<HTMLInputElement>(null);

  // ── Debounced search ──────────────────────────────────────────────────
  React.useEffect(() => {
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    if (searchTerm.trim().length < minSearchLength) {
      setResults([]);
      setShowResults(false);
      return;
    }

    debounceRef.current = setTimeout(async () => {
      setLoading(true);
      try {
        const items = await onSearch(searchTerm.trim());
        setResults(items);
        setShowResults(items.length > 0);
        setHighlightedIndex(-1);
      } catch (err) {
        console.error('[LookupField] Search error:', label, err);
        setResults([]);
        setShowResults(false);
      } finally {
        setLoading(false);
      }
    }, 300);

    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, [searchTerm, onSearch, minSearchLength]);

  // ── Close results on outside click ────────────────────────────────────
  React.useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setShowResults(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // ── Handlers ──────────────────────────────────────────────────────────
  const handleSearchChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setSearchTerm(e.target.value);
      if (value) {
        onChange(null);
      }
    },
    [value, onChange]
  );

  const handleSelect = React.useCallback(
    (item: ILookupItem) => {
      onChange(item);
      setSearchTerm(item.name);
      setResults([]);
      setShowResults(false);
    },
    [onChange]
  );

  const handleClear = React.useCallback(() => {
    onChange(null);
    setSearchTerm('');
    setResults([]);
    setShowResults(false);
  }, [onChange]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (!showResults || results.length === 0) return;

      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setHighlightedIndex(prev => (prev < results.length - 1 ? prev + 1 : 0));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setHighlightedIndex(prev => (prev > 0 ? prev - 1 : results.length - 1));
      } else if (e.key === 'Enter' && highlightedIndex >= 0) {
        e.preventDefault();
        handleSelect(results[highlightedIndex]);
      } else if (e.key === 'Escape') {
        setShowResults(false);
      }
    },
    [showResults, results, highlightedIndex, handleSelect]
  );

  /**
   * Fetch and open the option list for the CURRENT term (usually empty).
   *
   * Shared by `openOnFocus` and by the search-icon button, so "browse the
   * values" behaves identically however the user got there.
   *
   * NOTE for consumers: showing the FULL list on an empty query requires
   * `onSearch('')` to return a useful default set (e.g. top N unfiltered).
   * A consumer that cannot do that still works — it simply renders the
   * "No results found" state until the user types.
   */
  const runBrowse = React.useCallback(() => {
    const query = searchTerm.trim();
    setLoading(true);
    onSearch(query)
      .then(items => {
        setResults(items);
        setShowResults(items.length > 0);
        setHighlightedIndex(-1);
      })
      .catch(err => {
        // eslint-disable-next-line no-console
        console.error('[LookupField] browse search error:', label, err);
        setResults([]);
        setShowResults(false);
      })
      .finally(() => setLoading(false));
  }, [searchTerm, onSearch, label]);

  /**
   * The search icon is a real button (OOB parity): clicking it drops the full
   * list down, so a user who does not know what to type can still browse.
   * Toggles, so a second click dismisses.
   */
  const handleSearchIconClick = React.useCallback(() => {
    // Focus FIRST — `onMouseDown` preventDefault stops the button stealing
    // focus, but it cannot grant focus the input never had.
    inputRef.current?.focus();
    if (value) {
      // A committed value occupies the input slot with its chip; the icon is
      // not rendered in that state, so this is defensive only.
      return;
    }
    if (showResults) {
      setShowResults(false);
      return;
    }
    if (results.length > 0) {
      setShowResults(true);
      return;
    }
    runBrowse();
  }, [value, showResults, results.length, runBrowse]);

  const handleFocus = React.useCallback(() => {
    if (value) return;
    if (results.length > 0) {
      setShowResults(true);
      return;
    }
    // v1.0.5: when `openOnFocus` is enabled and no results are cached yet,
    // trigger an immediate search with the current (possibly empty) term so
    // the user can browse values without having to guess. Requires the
    // consumer's `onSearch` to return a sensible default set for empty input
    // (e.g. top N unfiltered rows). Failures are surfaced to the console —
    // the empty-state message covers the UX so there's no thrown error.
    if (openOnFocus) {
      const query = searchTerm.trim();
      setLoading(true);
      onSearch(query)
        .then(items => {
          setResults(items);
          setShowResults(items.length > 0);
          setHighlightedIndex(-1);
        })
        .catch(err => {
          // eslint-disable-next-line no-console
          console.error('[LookupField] openOnFocus search error:', label, err);
          setResults([]);
          setShowResults(false);
        })
        .finally(() => setLoading(false));
    }
  }, [value, results.length, openOnFocus, searchTerm, onSearch, label]);

  // ── Render label ──────────────────────────────────────────────────────
  const renderLabel = (): React.ReactElement => (
    <span className={styles.labelRow}>
      {label}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
      {labelExtra}
    </span>
  );

  const showEmpty = !loading && !value && results.length === 0 && searchTerm.trim().length >= minSearchLength;

  return (
    <div className={styles.wrapper} ref={wrapperRef} style={gridColumnStyle}>
      <Field label={renderLabel()} required={required}>
        {value ? (
          <div className={styles.selectedChip}>
            {chipIcon ? (
              <span className={styles.chipIcon} aria-hidden="true">
                {chipIcon}
              </span>
            ) : null}
            <Text size={200} weight="semibold" className={styles.selectedChipName}>
              {value.name}
            </Text>
            <Button
              appearance="subtle"
              size="small"
              icon={<DismissRegular fontSize={14} />}
              onClick={handleClear}
              aria-label={`Clear ${label}`}
            />
          </div>
        ) : (
          <Input
            appearance={appearance}
            input={{ ref: inputRef }}
            value={searchTerm}
            onChange={handleSearchChange}
            onKeyDown={handleKeyDown}
            onFocus={handleFocus}
            placeholder={placeholder ?? `Search ${label.toLowerCase()}...`}
            // RIGHT-hand side, and a real button — matching the OOB inline
            // lookup, where the magnifier both signals "this is a lookup" and
            // opens the full list on click. It was previously a decorative
            // `contentBefore` glyph.
            contentAfter={
              <Button
                appearance="transparent"
                size="small"
                icon={<SearchRegular />}
                onClick={handleSearchIconClick}
                aria-label={`Browse ${label}`}
                aria-expanded={showResults}
                // Keep focus in the text input so typing continues to work
                // straight after a browse click.
                onMouseDown={e => e.preventDefault()}
              />
            }
            aria-label={label}
            autoComplete="off"
          />
        )}
      </Field>

      {/*
        Loading spinner — overlays, and only while the list is CLOSED. With the
        list open the rows update in place (what OOB does); rendering a spinner
        over an open list would just cover the results the user is reading.
      */}
      {loading && !showResults && (
        <div className={mergeClasses(styles.overlayBelowField, styles.panelSurface, styles.spinnerRow)}>
          <Spinner size="tiny" label="Searching..." />
        </div>
      )}

      {/* Results list — scrollable options + pinned footer */}
      {showResults && !value && (
        <div className={mergeClasses(styles.overlayBelowField, styles.resultsList)}>
          <div className={styles.resultsScroll} role="listbox" aria-label={`${label} search results`}>
            {results.map((item, index) => (
              <div
                key={item.id}
                className={mergeClasses(
                  styles.resultItem,
                  index === highlightedIndex ? styles.resultItemHighlighted : undefined
                )}
                role="option"
                aria-selected={index === highlightedIndex}
                tabIndex={0}
                // Do not let mousedown pull focus out of the input: that would
                // drop `:focus-within` and blink the underline off mid-click.
                // Keyboard selection already routes through the input, so this
                // also makes mouse and keyboard behave identically.
                onMouseDown={e => e.preventDefault()}
                onClick={() => handleSelect(item)}
                onKeyDown={e => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handleSelect(item);
                  }
                }}
              >
                <Text size={200}>{item.name}</Text>
              </div>
            ))}
          </div>

          {/*
            Advanced — right-aligned, pinned below the scroll region, exactly
            where the OOB inline lookup puts it. Opt-in via `onAdvanced`.
            There is deliberately no "+ New" beside it; see the prop docs.
          */}
          {onAdvanced ? (
            <div className={styles.resultsFooter}>
              <Button
                appearance="subtle"
                size="small"
                icon={<SearchRegular />}
                onClick={() => {
                  setShowResults(false);
                  void onAdvanced();
                }}
                // Same reason as the browse icon — do not steal focus before
                // the click handler runs.
                onMouseDown={e => e.preventDefault()}
              >
                Advanced
              </Button>
            </div>
          ) : null}
        </div>
      )}

      {/* Empty results */}
      {showEmpty && (
        <Text
          size={100}
          className={mergeClasses(styles.overlayBelowField, styles.panelSurface, styles.emptyText)}
        >
          No results found
        </Text>
      )}
    </div>
  );
};

LookupField.displayName = 'LookupField';
