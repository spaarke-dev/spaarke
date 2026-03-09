/**
 * RecipientField.tsx
 * Hybrid contact-lookup + freeform email entry field with chip list.
 *
 * Used by DraftSummaryStep for "Distribute to" and "CC" fields.
 *
 * Behavior:
 *   - Shows chip list of current recipients (each with dismiss button)
 *   - Input with debounced contact search (300ms)
 *   - Selecting a contact adds as chip (displayName + email from name parse)
 *   - Typing a valid email + Enter adds as manual entry chip
 *   - Duplicate prevention by key (contactId or email)
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner — ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 */

import * as React from 'react';
import {
  Input,
  Text,
  Button,
  Spinner,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import {
  DismissRegular,
  SearchRegular,
  PersonRegular,
  MailRegular,
} from '@fluentui/react-icons';
import type { ILookupItem } from '../../types/entities';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IRecipientItem {
  /** Unique key — contactId for lookup entries, email for manual entries. */
  key: string;
  /** Display name (contact name or email). */
  displayName: string;
  /** Email address. */
  email: string;
  /** Whether this was manually entered (true) or from contact lookup (false). */
  isManual: boolean;
}

export interface IRecipientFieldProps {
  /** Field label. */
  label: string;
  /** Placeholder for the search/input field. */
  placeholder?: string;
  /** Current recipient list (controlled). */
  recipients: IRecipientItem[];
  /** Called when recipients change. */
  onRecipientsChange: (recipients: IRecipientItem[]) => void;
  /** Async search function for contact lookup. */
  onSearch: (query: string) => Promise<ILookupItem[]>;
  /** Minimum characters before search fires. Default: 2. */
  minSearchLength?: number;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Extract email from a lookup item name like "John Doe (john@example.com)".
 */
function extractEmailFromName(name: string): string {
  const match = name.match(/\(([^)]+@[^)]+)\)/);
  return match ? match[1] : '';
}

/**
 * Extract display name (without email) from lookup item name.
 */
function extractDisplayName(name: string): string {
  return name.replace(/\s*\([^)]*@[^)]*\)\s*$/, '').trim();
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  label: {
    color: tokens.colorNeutralForeground1,
  },
  chipList: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
  chip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: '2px',
    paddingBottom: '2px',
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: '2px',
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
  },
  chipManual: {
    backgroundColor: tokens.colorNeutralBackground3,
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
  },
  chipIcon: {
    display: 'flex',
    alignItems: 'center',
    color: tokens.colorBrandForeground2,
    fontSize: '14px',
  },
  chipIconManual: {
    color: tokens.colorNeutralForeground3,
  },
  chipText: {
    color: tokens.colorBrandForeground2,
    maxWidth: '200px',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  chipTextManual: {
    color: tokens.colorNeutralForeground1,
  },
  inputRow: {
    position: 'relative',
  },
  resultsList: {
    position: 'absolute',
    top: '100%',
    left: '0',
    right: '0',
    zIndex: 100,
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
    marginTop: '2px',
    boxShadow: tokens.shadow8,
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
    gap: tokens.spacingHorizontalS,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  resultItemHighlighted: {
    backgroundColor: tokens.colorNeutralBackground1Hover,
  },
  resultIcon: {
    color: tokens.colorBrandForeground1,
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
  },
  spinnerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  hintText: {
    color: tokens.colorNeutralForeground4,
  },
});

// ---------------------------------------------------------------------------
// RecipientField (exported)
// ---------------------------------------------------------------------------

export const RecipientField: React.FC<IRecipientFieldProps> = ({
  label,
  placeholder,
  recipients,
  onRecipientsChange,
  onSearch,
  minSearchLength = 2,
}) => {
  const styles = useStyles();

  const [inputValue, setInputValue] = React.useState('');
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

    if (inputValue.trim().length < minSearchLength) {
      setResults([]);
      setShowResults(false);
      return;
    }

    // Don't search if input looks like an email being typed
    if (inputValue.includes('@') && !EMAIL_REGEX.test(inputValue.trim())) {
      setResults([]);
      setShowResults(false);
      return;
    }

    debounceRef.current = setTimeout(async () => {
      setLoading(true);
      try {
        const items = await onSearch(inputValue.trim());
        // Filter out already-added recipients
        const existingKeys = new Set(recipients.map((r) => r.key));
        const filtered = items.filter((item) => !existingKeys.has(item.id));
        setResults(filtered);
        setShowResults(filtered.length > 0);
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
  }, [inputValue, onSearch, minSearchLength, recipients]);

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

  // ── Add recipient from lookup ─────────────────────────────────────────
  const handleSelectContact = React.useCallback(
    (item: ILookupItem) => {
      const email = extractEmailFromName(item.name);
      const displayName = extractDisplayName(item.name);
      const newRecipient: IRecipientItem = {
        key: item.id,
        displayName: displayName || item.name,
        email,
        isManual: false,
      };
      onRecipientsChange([...recipients, newRecipient]);
      setInputValue('');
      setResults([]);
      setShowResults(false);
    },
    [recipients, onRecipientsChange]
  );

  // ── Add manual email ──────────────────────────────────────────────────
  const handleAddManualEmail = React.useCallback(() => {
    const email = inputValue.trim();
    if (!EMAIL_REGEX.test(email)) return;
    if (recipients.some((r) => r.email === email || r.key === email)) return;

    const newRecipient: IRecipientItem = {
      key: email,
      displayName: email,
      email,
      isManual: true,
    };
    onRecipientsChange([...recipients, newRecipient]);
    setInputValue('');
    setResults([]);
    setShowResults(false);
  }, [inputValue, recipients, onRecipientsChange]);

  // ── Remove recipient ──────────────────────────────────────────────────
  const handleRemove = React.useCallback(
    (key: string) => {
      onRecipientsChange(recipients.filter((r) => r.key !== key));
    },
    [recipients, onRecipientsChange]
  );

  // ── Keyboard navigation ───────────────────────────────────────────────
  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (showResults && highlightedIndex >= 0) {
          handleSelectContact(results[highlightedIndex]);
        } else {
          handleAddManualEmail();
        }
      } else if (e.key === 'ArrowDown' && showResults) {
        e.preventDefault();
        setHighlightedIndex((prev) =>
          prev < results.length - 1 ? prev + 1 : 0
        );
      } else if (e.key === 'ArrowUp' && showResults) {
        e.preventDefault();
        setHighlightedIndex((prev) =>
          prev > 0 ? prev - 1 : results.length - 1
        );
      } else if (e.key === 'Escape') {
        setShowResults(false);
      }
    },
    [showResults, highlightedIndex, results, handleSelectContact, handleAddManualEmail]
  );

  const handleFocus = React.useCallback(() => {
    if (results.length > 0) {
      setShowResults(true);
    }
  }, [results.length]);

  // ── Render ────────────────────────────────────────────────────────────
  return (
    <div className={styles.wrapper} ref={wrapperRef}>
      <Text size={300} weight="semibold" className={styles.label}>
        {label}
      </Text>

      {/* Chip list */}
      {recipients.length > 0 && (
        <div className={styles.chipList}>
          {recipients.map((r) => (
            <span
              key={r.key}
              className={mergeClasses(styles.chip, r.isManual && styles.chipManual)}
            >
              <span className={mergeClasses(styles.chipIcon, r.isManual && styles.chipIconManual)}>
                {r.isManual ? <MailRegular fontSize={14} /> : <PersonRegular fontSize={14} />}
              </span>
              <Text
                size={200}
                className={mergeClasses(styles.chipText, r.isManual && styles.chipTextManual)}
                title={r.email ? `${r.displayName} (${r.email})` : r.displayName}
              >
                {r.displayName}
              </Text>
              <Button
                appearance="subtle"
                size="small"
                icon={<DismissRegular fontSize={12} />}
                onClick={() => handleRemove(r.key)}
                aria-label={`Remove ${r.displayName}`}
              />
            </span>
          ))}
        </div>
      )}

      {/* Search / freeform input */}
      <div className={styles.inputRow}>
        <Input
          value={inputValue}
          onChange={(e) => setInputValue(e.target.value)}
          onKeyDown={handleKeyDown}
          onFocus={handleFocus}
          placeholder={placeholder ?? `Search contacts or type email...`}
          contentBefore={<SearchRegular aria-hidden="true" />}
          aria-label={label}
          autoComplete="off"
          style={{ width: '100%' }}
        />

        {/* Loading */}
        {loading && (
          <div className={styles.spinnerRow}>
            <Spinner size="tiny" label="Searching..." />
          </div>
        )}

        {/* Results dropdown */}
        {showResults && (
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
                onClick={() => handleSelectContact(item)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handleSelectContact(item);
                  }
                }}
                tabIndex={0}
              >
                <span className={styles.resultIcon}>
                  <PersonRegular fontSize={16} />
                </span>
                <Text size={200}>{item.name}</Text>
              </div>
            ))}
          </div>
        )}
      </div>

      <Text size={100} className={styles.hintText}>
        Search contacts by name, or type an email address and press Enter.
      </Text>
    </div>
  );
};

RecipientField.displayName = 'RecipientField';
