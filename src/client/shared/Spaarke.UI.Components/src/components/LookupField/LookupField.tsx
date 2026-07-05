/**
 * LookupField.tsx
 * Reusable search-as-you-type lookup field for entity reference searches.
 *
 * Layout:
 *   ┌───────────────────────────────────────────────┐
 *   │ [Search input: "lit..."]                   [x] │
 *   ├───────────────────────────────────────────────┤
 *   │  Litigation                                    │
 *   │  Licensing                                     │
 *   │  Litigation Support                            │
 *   └───────────────────────────────────────────────┘
 *   — OR —
 *   Selected: [Litigation] [x]
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 *   - Full keyboard support (arrow keys, Enter, Escape)
 */

import * as React from 'react';
import { Input, Text, Button, Spinner, Field, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import type { ILookupItem } from '../../types/LookupTypes';

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
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
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
    maxHeight: '200px',
    overflowY: 'auto',
    marginTop: tokens.spacingVerticalXXS,
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
    marginTop: tokens.spacingVerticalXXS,
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

  spinnerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
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
}) => {
  const styles = useStyles();

  const [searchTerm, setSearchTerm] = React.useState('');
  const [results, setResults] = React.useState<ILookupItem[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [showResults, setShowResults] = React.useState(false);
  const [highlightedIndex, setHighlightedIndex] = React.useState(-1);
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const wrapperRef = React.useRef<HTMLDivElement>(null);

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
    <div className={styles.wrapper} ref={wrapperRef}>
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
            value={searchTerm}
            onChange={handleSearchChange}
            onKeyDown={handleKeyDown}
            onFocus={handleFocus}
            placeholder={placeholder ?? `Search ${label.toLowerCase()}...`}
            contentBefore={<SearchRegular aria-hidden="true" />}
            aria-label={label}
            autoComplete="off"
          />
        )}
      </Field>

      {/* Loading spinner */}
      {loading && (
        <div className={styles.spinnerRow}>
          <Spinner size="tiny" label="Searching..." />
        </div>
      )}

      {/* Results list */}
      {showResults && !value && (
        <div className={styles.resultsList} role="listbox" aria-label={`${label} search results`}>
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
      )}

      {/* Empty results */}
      {showEmpty && (
        <Text size={100} className={styles.emptyText}>
          No results found
        </Text>
      )}
    </div>
  );
};

LookupField.displayName = 'LookupField';
