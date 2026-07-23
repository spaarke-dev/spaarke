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

/**
 * Resolve a directory result into the email address + display name to commit
 * as a recipient (task 123 / UAT R4 C12-1).
 *
 * Order of resolution:
 *   1. `item.email` — the FIRST-CLASS email field (`userLookup.ts` now populates
 *      it from `internalemailaddress` / `emailaddress1`). Authoritative.
 *   2. Fallback: parse the email out of the `"Full Name (email)"` display
 *      string, for callers that don't set `item.email`.
 *
 * Returns `null` when NO valid email can be resolved (e.g. a contact with no
 * `emailaddress1`). A `null` result MUST NOT be turned into a recipient — the
 * previous code fell back to using the bare display name (e.g. "ralph") as the
 * recipient value, which then failed validation. Such rows are rendered
 * non-selectable instead.
 */
function resolveRecipientEmail(item: ILookupItem): { email: string; displayName?: string } | null {
  const parenMatch = item.name.match(/\(([^)]+)\)\s*$/);
  const displayFromName = parenMatch ? item.name.slice(0, parenMatch.index).trim() || undefined : undefined;

  const explicit = item.email?.trim();
  if (explicit && EMAIL_RE.test(explicit)) {
    // Prefer the display-name segment of "Name (email)"; else the whole name.
    return { email: explicit, displayName: displayFromName ?? (item.name.trim() || undefined) };
  }

  if (parenMatch) {
    const parsed = parenMatch[1].trim();
    if (EMAIL_RE.test(parsed)) {
      return { email: parsed, displayName: displayFromName };
    }
  }

  return null;
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
  // Inline "To [ input ]" row (owner UAT mockup 2026-07-22): a boxed label on the
  // left, an underline input filling the rest — the Outlook-style recipient row.
  inlineRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  labelBox: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    minWidth: '44px',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
  },
  // The right-hand cell: a bordered box holding the resolved chips AND the text
  // input, wrapping together (owner UAT 2026-07-22 #2 — a selected recipient enters
  // the field as a chip, not a list below it).
  fieldBox: {
    flexGrow: 1,
    minWidth: 0,
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingBottom: tokens.spacingVerticalXXS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke1,
  },
  chipInput: {
    flexGrow: 1,
    minWidth: '140px',
    backgroundColor: 'transparent',
    // Strip the Fluent Input's own border/focus-underline so it reads as a bare text
    // field inside `fieldBox` (which owns the underline).
    ...shorthands.border('0'),
    '::after': {
      borderBottomWidth: 0,
    },
  },
  // `display: contents` so the chips flow as direct flex children of `fieldBox`
  // and wrap inline with the text input, rather than as a block below it.
  tagGroup: {
    display: 'contents',
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
  resultItemDisabled: {
    cursor: 'default',
    color: tokens.colorNeutralForegroundDisabled,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1,
    },
  },
  noEmailHint: {
    marginLeft: 'auto',
    paddingLeft: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
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
      // Resolve to the contact's actual email (first-class `item.email`, else
      // parsed from the "Full Name (email)" display string). A result with no
      // usable email is NON-SELECTABLE — never commit the display name as the
      // recipient value (task 123 / UAT R4 C12-1 + parallel R6-5, both 2026-07-21:
      // the "Invalid email address: {name}" bug). This variant is stricter than
      // the R6-5 inline fix — it refuses to commit an unusable recipient rather
      // than falling back to `item.name`.
      const resolved = resolveRecipientEmail(item);
      if (!resolved) return;
      const { email, displayName } = resolved;
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

  return (
    <div className={styles.wrapper} ref={wrapperRef} role="group" aria-label={label}>
      <div className={styles.inlineRow}>
        <span className={styles.labelBox} aria-hidden="true">
          {label}
          {required && <span className={styles.requiredMark}>{' *'}</span>}
        </span>
        <div className={styles.fieldBox}>
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
                  size="small"
                  className={mergeClasses(!EMAIL_RE.test(r.email) ? styles.tagInvalid : undefined)}
                  appearance={r.resolved ? 'filled' : 'outline'}
                >
                  {r.displayName ? `${r.displayName} (${r.email})` : r.email}
                </Tag>
              ))}
            </TagGroup>
          )}
          <Input
            className={styles.chipInput}
            value={draft}
            onChange={handleInputChange}
            onKeyDown={handleKeyDown}
            onBlur={handleBlur}
            placeholder={value.length > 0 ? '' : (placeholder ?? `Add ${label.toLowerCase()} — separate with ; or ,`)}
            aria-label={label}
            disabled={disabled}
            autoComplete="off"
          />
        </div>
      </div>

      {loading && <Spinner size="tiny" label="Searching..." />}

      {showResults && (
        <div className={styles.resultsList} role="listbox" aria-label={`${label} search results`}>
          {results.map(item => {
            // A result with no resolvable email cannot become a valid recipient —
            // render it clearly non-selectable rather than letting a click commit
            // the display name as an invalid recipient (task 123 / UAT R4 C12-1).
            const selectable = resolveRecipientEmail(item) !== null;
            return (
              <div
                key={item.id}
                className={mergeClasses(styles.resultItem, !selectable && styles.resultItemDisabled)}
                role="option"
                aria-selected={false}
                aria-disabled={!selectable}
                tabIndex={selectable ? 0 : -1}
                // CRITICAL: prevent the input from blurring when a suggestion is
                // clicked. Without this, mousedown blurs the <Input> → `handleBlur`
                // commits the half-typed draft (e.g. "ralp") as a free-text recipient
                // AND hides this list, so the `onClick` below never fires and the
                // selection is lost (UAT round: "selecting a name only takes 'ralp'").
                // preventDefault on mousedown keeps focus, so onClick → handleSelectResult runs.
                onMouseDown={e => e.preventDefault()}
                onClick={selectable ? () => handleSelectResult(item) : undefined}
                onKeyDown={
                  selectable
                    ? e => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          handleSelectResult(item);
                        }
                      }
                    : undefined
                }
              >
                <Text size={200}>{item.name}</Text>
                {!selectable && (
                  <Text size={200} className={styles.noEmailHint}>
                    no email address
                  </Text>
                )}
              </div>
            );
          })}
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
