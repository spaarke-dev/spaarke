/**
 * RecipientField.tsx
 *
 * THE single canonical recipient-normalization implementation (task 020
 * constraint — replaces the 5 caller-local implementations identified by the
 * R3 architecture assessment). Used for To / Cc / Bcc.
 *
 * Behavior (design §5.6.1):
 *   - Accepts `;`/`,`-separated paste input (matches the de facto contract of
 *     text pasted from Outlook).
 *   - Resolves free-text against the directory via `onSearch` (host-bound
 *     `searchUsersAndContacts`).
 *   - Renders resolved + free-text recipients as removable Fluent Tags.
 *   - Free-text email is accepted; validated by regex on commit (blur/Enter/separator).
 *
 * No caller-local parsing may be added elsewhere — this is the ONE place
 * recipient strings get split/normalized (task 020 constraint).
 */
import * as React from 'react';
import {
  Input,
  Field,
  Tag,
  TagGroup,
  Spinner,
  Text,
  makeStyles,
  shorthands,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { IRecipient } from '../EmailComposer.types';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const SEPARATOR_RE = /[;,]/;

/** Splits a raw paste/typed string on `;`/`,` into trimmed, non-empty tokens. */
function splitTokens(raw: string): string[] {
  return raw
    .split(SEPARATOR_RE)
    .map(s => s.trim())
    .filter(s => s.length > 0);
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRecipientFieldProps {
  label: string;
  required?: boolean;
  disabled?: boolean;
  placeholder?: string;
  value: IRecipient[];
  onChange: (recipients: IRecipient[]) => void;
  /** Directory search — host binds `searchUsersAndContacts(dataService, query)`. */
  onSearch?: (query: string) => Promise<ILookupItem[]>;
  /** Field-level validation error message (from `IValidationResult`), if any. */
  errorMessage?: string;
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
  tagGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
  },
  tagInvalid: {
    ...shorthands.borderColor(tokens.colorPaletteRedBorder2),
  },
  resultsList: {
    display: 'flex',
    flexDirection: 'column',
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
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
    color: tokens.colorNeutralForeground1,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
  },
  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RecipientField: React.FC<IRecipientFieldProps> = ({
  label,
  required,
  disabled,
  placeholder,
  value,
  onChange,
  onSearch,
  errorMessage,
}) => {
  const styles = useStyles();
  const [draft, setDraft] = React.useState('');
  const [results, setResults] = React.useState<ILookupItem[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [showResults, setShowResults] = React.useState(false);
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const wrapperRef = React.useRef<HTMLDivElement>(null);

  const commitTokens = React.useCallback(
    (raw: string) => {
      const tokens = splitTokens(raw);
      if (tokens.length === 0) return;
      const existingEmails = new Set(value.map(r => r.email.toLowerCase()));
      const added: IRecipient[] = [];
      for (const email of tokens) {
        const key = email.toLowerCase();
        if (existingEmails.has(key)) continue;
        existingEmails.add(key);
        added.push({ email, resolved: false });
      }
      if (added.length > 0) {
        onChange([...value, ...added]);
      }
    },
    [value, onChange]
  );

  const handleInputChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const raw = e.target.value;
      // Auto-commit whenever the user types a separator (paste-from-Outlook
      // contract) — commit everything up to and including the last separator,
      // keep the remainder as the live draft.
      if (SEPARATOR_RE.test(raw)) {
        const lastSepIndex = Math.max(raw.lastIndexOf(';'), raw.lastIndexOf(','));
        const toCommit = raw.slice(0, lastSepIndex);
        const remainder = raw.slice(lastSepIndex + 1);
        commitTokens(toCommit);
        setDraft(remainder);
        setShowResults(false);
        return;
      }
      setDraft(raw);
    },
    [commitTokens]
  );

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        if (draft.trim()) {
          commitTokens(draft);
          setDraft('');
        }
        setShowResults(false);
      } else if (e.key === 'Backspace' && draft === '' && value.length > 0) {
        // Backspace on empty draft removes the last chip (common chip-input UX).
        onChange(value.slice(0, -1));
      } else if (e.key === 'Escape') {
        setShowResults(false);
      }
    },
    [draft, commitTokens, value, onChange]
  );

  const handleBlur = React.useCallback(() => {
    if (draft.trim()) {
      commitTokens(draft);
      setDraft('');
    }
    setShowResults(false);
  }, [draft, commitTokens]);

  const handleSelectResult = React.useCallback(
    (item: ILookupItem) => {
      // ILookupItem.name is formatted "Full Name (email)" (userLookup.ts) —
      // extract the email; fall back to the whole name if no email is present.
      const match = item.name.match(/\(([^)]+)\)\s*$/);
      const email = match ? match[1] : item.name;
      const displayName = match ? item.name.slice(0, match.index).trim() : undefined;
      const existingEmails = new Set(value.map(r => r.email.toLowerCase()));
      if (!existingEmails.has(email.toLowerCase())) {
        onChange([...value, { email, displayName, resolved: true, sourceId: item.id, entityType: item.entityType }]);
      }
      setDraft('');
      setResults([]);
      setShowResults(false);
    },
    [value, onChange]
  );

  const handleRemove = React.useCallback(
    (email: string) => {
      onChange(value.filter(r => r.email.toLowerCase() !== email.toLowerCase()));
    },
    [value, onChange]
  );

  // Debounced directory search over the live draft.
  React.useEffect(() => {
    if (!onSearch) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (draft.trim().length < 2) {
      setResults([]);
      setShowResults(false);
      return;
    }
    debounceRef.current = setTimeout(async () => {
      setLoading(true);
      try {
        const items = await onSearch(draft.trim());
        setResults(items);
        setShowResults(items.length > 0);
      } catch (err) {
        console.error('[RecipientField] search error:', label, err);
        setResults([]);
        setShowResults(false);
      } finally {
        setLoading(false);
      }
    }, 300);
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [draft, onSearch, label]);

  React.useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setShowResults(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const renderLabel = (): React.ReactElement => (
    <span className={styles.labelRow}>
      {label}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
    </span>
  );

  return (
    <div className={styles.wrapper} ref={wrapperRef} role="group" aria-label={label}>
      <Field label={renderLabel()} required={required} validationState={errorMessage ? 'error' : 'none'}>
        <Input
          value={draft}
          onChange={handleInputChange}
          onKeyDown={handleKeyDown}
          onBlur={handleBlur}
          placeholder={placeholder ?? `Add ${label.toLowerCase()} — separate with ; or ,`}
          aria-label={label}
          disabled={disabled}
          autoComplete="off"
        />
      </Field>

      {value.length > 0 && (
        <TagGroup
          className={styles.tagGroup}
          aria-label={`${label} recipients`}
          onDismiss={(_, data) => handleRemove(String(data.value))}
        >
          {value.map(r => (
            <Tag
              key={r.email}
              value={r.email}
              dismissible={!disabled}
              className={mergeClasses(!EMAIL_RE.test(r.email) ? styles.tagInvalid : undefined)}
              appearance={r.resolved ? 'filled' : 'outline'}
            >
              {r.displayName ? `${r.displayName} (${r.email})` : r.email}
            </Tag>
          ))}
        </TagGroup>
      )}

      {loading && <Spinner size="tiny" label="Searching..." />}

      {showResults && (
        <div className={styles.resultsList} role="listbox" aria-label={`${label} search results`}>
          {results.map(item => (
            <div
              key={item.id}
              className={styles.resultItem}
              role="option"
              aria-selected={false}
              tabIndex={0}
              onClick={() => handleSelectResult(item)}
              onKeyDown={e => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  handleSelectResult(item);
                }
              }}
            >
              <Text size={200}>{item.name}</Text>
            </div>
          ))}
        </div>
      )}

      {errorMessage && (
        <Text size={200} className={styles.errorText} role="alert">
          {errorMessage}
        </Text>
      )}
    </div>
  );
};

RecipientField.displayName = 'RecipientField';
