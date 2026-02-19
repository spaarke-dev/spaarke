/**
 * LookupField.tsx
 * Reusable search-as-you-type lookup field for Dataverse reference tables.
 *
 * Used by CreateRecordStep for Matter Type, Practice Area, Attorney, and
 * Paralegal lookups. Follows the search pattern from AssignCounselStep.
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
import {
  Input,
  Text,
  Button,
  Spinner,
  Field,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import { AiFieldTag } from './AiFieldTag';
import type { ILookupItem } from '../../types/entities';

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
  /** Whether this field was AI-pre-filled (shows AiFieldTag). */
  isAiPrefilled?: boolean;
  /** Minimum characters before search fires. Default: 1. */
  minSearchLength?: number;
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
  isAiPrefilled,
  minSearchLength = 1,
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
      } catch {
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
        setHighlightedIndex((prev) =>
          prev < results.length - 1 ? prev + 1 : 0
        );
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setHighlightedIndex((prev) =>
          prev > 0 ? prev - 1 : results.length - 1
        );
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
    if (results.length > 0 && !value) {
      setShowResults(true);
    }
  }, [results.length, value]);

  // ── Render label ──────────────────────────────────────────────────────
  const renderLabel = (): React.ReactElement => (
    <span className={styles.labelRow}>
      {label}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
      {isAiPrefilled && <AiFieldTag />}
    </span>
  );

  const showEmpty =
    !loading &&
    !value &&
    results.length === 0 &&
    searchTerm.trim().length >= minSearchLength;

  return (
    <div className={styles.wrapper} ref={wrapperRef}>
      <Field label={renderLabel()} required={required}>
        {value ? (
          <div className={styles.selectedChip}>
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
        <div
          className={styles.resultsList}
          role="listbox"
          aria-label={`${label} search results`}
        >
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
              onKeyDown={(e) => {
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
