import React, { useEffect, useMemo, useRef, useState } from 'react';
import { makeStyles, tokens, Button, Card, Input, Spinner, Text, mergeClasses } from '@fluentui/react-components';
import { CheckmarkRegular, SearchRegular, AddRegular, DismissRegular, InfoRegular } from '@fluentui/react-icons';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import type { RelatedCandidate } from '../services/communicationSuggestionsService';
import type { CreateRecordFormData, ReferenceListState } from '../hooks/useCreateRecordFormData';
import type { ContactOption } from './views/CreateTodoView';
import { CreateRecordForm, type CreateRecordInput } from './CreateRecordForm';

/**
 * RelatedToPicker — the add-in's "Related to" selector, modeled on the email-intelligence
 * reconciliation surface (UI feedback, owner 2026-09-02).
 *
 * Layout (UAT round 4, task 095 — no "Related to" label; round 5, task 099 — no icon in the placeholder):
 *   [ Matter ] [ Project ] [ Invoice ]                      [ + New ]  ← left pills, "+ New" right
 *   [ Look up related Matter...                          ] [ 🔍 ]     ← lookup box (text only) + search icon button
 *   ┌ recommended auto-match cards ──────────────────────────────┐
 *   │ LITG-763955 : Litigation matter · Matter · 100% match  [✓]  │  ← blue check; green ✓ + × on select
 *   └────────────────────────────────────────────────────────────┘
 *
 * Round 5 (task 099, owner 2026-10-05):
 *   - Selecting a record COLLAPSES the picker to just the selected record (green check + ×): the pills, lookup
 *     box, results and "+ New" are hidden. The × clears the selection and the picker comes back as it was (the
 *     query and results are component state, so they are still in hand — no new request).
 *   - "+ New" shows ONLY the create form (and the pills); `onCreatingChange` tells the host so it can hide the
 *     rest of its form until the record exists (or the create is cancelled).
 * Round 5 item 3 (task 100): the create form is `CreateRecordForm` — per type, the fields the owner listed
 * (Matter: Name, Description, Matter Type, Practice Area, Assigned To Internal; Project: Name, Project Type,
 * Description, Assigned To Internal; Invoice: Name, Description, Assigned To), created by the BFF quick-create.
 *   - Focus follows the user's action (ADR-021): into the name box on "+ New" / Cancel, onto the selected
 *     record on select / create, back into the lookup box on ×.
 * Single-select chips (gray except selected=blue, default Matter).
 * Host-agnostic: selecting only *chooses*; the regarding is written at save. Fluent v9.
 *
 * Task 084 (#1037, "pickable equals savable"): a record whose `canFile === false` (the caller can read
 * it but the save would refuse it — filing needs AppendTo) renders DISABLED with the reason, for search
 * rows and suggestion cards alike. It has no select control, is out of the tab order, and is never
 * selected — not by click, not by keyboard, not by any pre-selection. `canFile` true/null/absent rows
 * behave exactly as before.
 */

/** Task 084 — the disabled row's reason. Verbatim owner copy; do not reword (the tests pin it literally). */
const FILING_BLOCKED_REASON =
  "You can view this record but can't file to it. Filing needs Append To permission on the record.";

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalM,
  },
  // Pills left, "+ New" pushed to the right end of the same row; wraps (never clips) at narrow widths.
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  newBtn: { marginLeft: 'auto', flexShrink: 0 },
  chips: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalXS },
  chip: {
    borderRadius: '999px',
    minWidth: 'auto',
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  chipUnselected: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground3,
    border: 'none',
  },
  searchRow: { display: 'flex', gap: tokens.spacingHorizontalXS, alignItems: 'center' },
  cards: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  card: { padding: tokens.spacingVerticalS },
  cardSelected: { borderLeft: `3px solid ${tokens.colorStatusSuccessBorder2}` },
  cardRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  cardBody: { display: 'flex', flexDirection: 'column', gap: '2px', flexGrow: 1, minWidth: 0 },
  cardTitle: { overflow: 'hidden', textOverflow: 'ellipsis' },
  cardMeta: { color: tokens.colorNeutralForeground3 },
  // Task 084: the disabled row's inline reason. The Card's own `disabled` styling dims the record (Fluent
  // disabled tokens); the reason keeps a readable foreground token so it stays legible in both themes.
  blockedReason: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalXS,
    marginTop: tokens.spacingVerticalXXS,
    color: tokens.colorNeutralForeground2,
  },
  blockedReasonIcon: { flexShrink: 0, marginTop: tokens.spacingVerticalXXS },
  checkWrap: { position: 'relative', flexShrink: 0 },
  greenCheckBtn: {
    backgroundColor: tokens.colorStatusSuccessBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': { backgroundColor: tokens.colorStatusSuccessBackground3, color: tokens.colorNeutralForegroundOnBrand },
    ':hover:active': {
      backgroundColor: tokens.colorStatusSuccessBackground3,
      color: tokens.colorNeutralForegroundOnBrand,
    },
  },
  clearX: {
    position: 'absolute',
    top: '-6px',
    right: '-6px',
    width: '16px',
    height: '16px',
    minWidth: '16px',
    padding: 0,
    margin: 0,
    borderRadius: '50%',
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    cursor: 'pointer',
    fontSize: '12px',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  ctrlBtn: { flexShrink: 0 },
  emptyNote: { color: tokens.colorNeutralForeground3, padding: `${tokens.spacingVerticalXS} 0` },
  errorRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  fieldError: { color: tokens.colorPaletteRedForeground1, fontSize: tokens.fontSizeBase200 },
  fieldWarning: { color: tokens.colorPaletteDarkOrangeForeground1, fontSize: tokens.fontSizeBase200 },
});

/** A list that has not been provided: not loading, no error, no rows (the form then says none are available). */
const EMPTY_LIST: ReferenceListState = { options: [], loading: false, error: null, retry: () => undefined };

const NO_CONTACTS = async (): Promise<ContactOption[]> => [];

export interface RelatedToPickerProps {
  /** The currently selected Related-to record (null = none selected yet). */
  value: EntitySearchResult | null;
  onChange: (entity: EntitySearchResult | null) => void;
  /** Auto-match candidates from the engine, ranked highest confidence first. */
  candidates: RelatedCandidate[];
  candidatesLoading?: boolean;
  /** "Look up another record" search — scoped to the selected chip type. */
  onSearch: (query: string, type: EntityType) => Promise<EntitySearchResult[]>;
  /**
   * Create a new record of the given type from the "+ New" form's values (BFF-backed; task 100). Resolves to the
   * created record (auto-selected as the Related-to) plus any non-fatal server warnings. THROWS with a readable
   * message on failure (task 053). Absent → no "New" button.
   */
  onCreateRecord?: (type: EntityType, input: CreateRecordInput) => Promise<CreateRecordResult | null>;
  /** Types offered as chips. */
  allowedTypes: EntityType[];
  /** Default selected type. */
  defaultType?: EntityType;
  /**
   * The "+ New" form's data (task 100): the matter-type, practice-area and project-type lists (each with its
   * loading / failed-with-Retry state) and the Assigned To prefill. Absent → empty lists and no prefill (a Matter's
   * required fields then stay unsatisfiable and the form says no values are available).
   */
  createForm?: CreateRecordFormData;
  /** Contact search for the form's Assigned To field (the pane's one contact search). Absent → no results. */
  onSearchContacts?: (query: string) => Promise<ContactOption[]>;
  /**
   * Task 099: reports whether the "+ New" create form is open. The host hides the sections that only make sense
   * once a record exists (document name, profile, Save) while it is. Called with `false` on unmount.
   */
  onCreatingChange?: (creating: boolean) => void;
  disabled?: boolean;
}

/** Result of a successful {@link RelatedToPickerProps.onCreateRecord} call. */
export interface CreateRecordResult {
  record: EntitySearchResult;
  /**
   * Non-fatal, human-readable notices from server-side creation (task 038 / owner decision — a
   * request missing or carrying an unresolvable `matterTypeId` still creates the record; the pane
   * must show the warning, never swallow it). Rendered as a non-blocking notice, not an error.
   */
  warnings?: string[];
}

function pct(confidence: number): number {
  return Math.round(Math.max(0, Math.min(1, confidence)) * 100);
}

function sameRecord(a: EntitySearchResult, b: EntitySearchResult): boolean {
  return a.id === b.id && a.logicalName === b.logicalName;
}

export const RelatedToPicker: React.FC<RelatedToPickerProps> = ({
  value,
  onChange,
  candidates,
  candidatesLoading = false,
  onSearch,
  onCreateRecord,
  allowedTypes,
  defaultType = 'Matter',
  createForm,
  onSearchContacts = NO_CONTACTS,
  onCreatingChange,
  disabled = false,
}) => {
  const styles = useStyles();
  const initialType: EntityType = allowedTypes.includes(defaultType) ? defaultType : (allowedTypes[0] ?? 'Matter');
  const [selectedType, setSelectedType] = useState<EntityType>(initialType);
  const [query, setQuery] = useState('');
  const [searchResults, setSearchResults] = useState<EntitySearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  // A non-blocking server notice about the record just created (task 038: never swallowed). Outlives the form: it
  // shows under the selected record once the picker collapses.
  const [createWarning, setCreateWarning] = useState<string | null>(null);
  // Task 053: a failed "Look up another record" search, distinct from a genuine zero-result search —
  // set from the Error `onSearch` (SaveFlow's `relatedSearch`) now throws instead of silently resolving
  // `[]`. Cleared at the start of every new search attempt and on a type-chip change.
  const [searchError, setSearchError] = useState<string | null>(null);

  // Task 099: focus follows the user's action. The flag is set by the picker's own handlers (never by a
  // selection the host restores), and consumed by the first render in which the target control exists.
  // Callback refs (Fluent v9's ref typing in this package resolves to `Ref<never>`; a callback ref is accepted).
  const inputRef = useRef<HTMLInputElement | null>(null);
  const selectedBtnRef = useRef<HTMLButtonElement | null>(null);
  const focusAfterRef = useRef<'input' | 'selected' | null>(null);
  useEffect(() => {
    const target = focusAfterRef.current;
    if (!target) return;
    const el = target === 'selected' ? selectedBtnRef.current : inputRef.current;
    if (el) {
      focusAfterRef.current = null;
      el.focus();
    }
  });

  useEffect(() => {
    onCreatingChange?.(showCreate);
    return () => onCreatingChange?.(false);
  }, [showCreate, onCreatingChange]);

  const selectRecord = (rec: EntitySearchResult) => {
    focusAfterRef.current = 'selected';
    onChange(rec);
  };
  const clearSelection = () => {
    focusAfterRef.current = 'input';
    onChange(null);
  };

  const typeMatches = useMemo(() => candidates.filter(c => c.entityType === selectedType), [candidates, selectedType]);

  // Task 084: a selection the save would refuse never stands. The picker itself never selects a
  // `canFile === false` record, but a selection can arrive from elsewhere — e.g. SaveFlow restoring the
  // last association, made before the caller's rights changed. If the server now reports that record as
  // not fileable (here, or in a search row / suggestion card on screen), clear it; the disabled row says why.
  const selectedNotFileable =
    value !== null &&
    (value.canFile === false ||
      searchResults.some(r => r.canFile === false && sameRecord(r, value)) ||
      candidates.some(c => c.canFile === false && sameRecord(c, value)));
  useEffect(() => {
    if (selectedNotFileable) onChange(null);
  }, [selectedNotFileable, onChange]);

  const handleTypeChange = (type: EntityType) => {
    setSelectedType(type);
    setQuery('');
    setSearchResults([]);
    setSearchError(null);
    setShowCreate(false);
    setCreateWarning(null);
  };

  // Task 100: the form validates and builds the input; this creates, selects and closes. A failure THROWS back into
  // the form, which shows the server's own message (task 053).
  const handleCreate = async (input: CreateRecordInput) => {
    if (!onCreateRecord) return;
    setCreateWarning(null);
    const result = await onCreateRecord(selectedType, input);
    if (!result) {
      // Defensive only: `onCreateRecord` implementations THROW on failure (task 053) rather than resolving null —
      // kept for any caller of this prop that still follows the older null-on-failure contract.
      throw new Error(`Couldn't create the ${selectedType}.`);
    }
    focusAfterRef.current = 'selected';
    onChange(result.record);
    setShowCreate(false);
    // Non-blocking — the record is created and selected regardless (task 038: never swallow a server warning, e.g. an
    // unresolvable matter type or practice area).
    if (result.warnings && result.warnings.length > 0) {
      setCreateWarning(result.warnings.join(' '));
    }
  };

  const cancelCreate = () => {
    focusAfterRef.current = 'input';
    setShowCreate(false);
    setCreateWarning(null);
  };

  const runSearch = async () => {
    const q = query.trim();
    if (q.length === 0) {
      setSearchResults([]);
      setSearchError(null);
      return;
    }
    setSearching(true);
    setSearchError(null);
    try {
      setSearchResults(await onSearch(q, selectedType));
    } catch (err) {
      // Task 053: a failed search must not render identically to "nothing matched" — `onSearch`
      // (SaveFlow's `relatedSearch`) throws a descriptive Error on failure. Clear any stale results
      // from a prior, different query (so they don't linger under this query's error) and show the
      // error + a Retry instead of a silent empty list.
      setSearchResults([]);
      setSearchError(err instanceof Error ? err.message : `Couldn't search for ${selectedType} records.`);
    } finally {
      setSearching(false);
    }
  };

  // One card. Selected → green check + a small × to clear; else a blue check to select.
  // Task 084: `canFile === false` → a DISABLED card with the reason and NO select control (modeled on
  // AttachmentSelector's invalid-attachment row). Fluent's Card `disabled` sets aria-disabled="true",
  // dims it with disabled tokens and drops onClick; tabIndex -1 keeps it out of the tab order. There is
  // no handler of any kind on it, so neither a click nor Enter/Space can select it.
  const renderCard = (rec: EntitySearchResult, opts: { confidence?: number; keyPrefix?: string }) => {
    const selected = value !== null && sameRecord(rec, value);
    const key = `${opts.keyPrefix ?? ''}${rec.logicalName}:${rec.id}`;
    const meta = opts.confidence != null ? `${rec.entityType} · ${pct(opts.confidence)}% match` : rec.entityType;
    if (rec.canFile === false) {
      return (
        <Card key={key} className={styles.card} disabled aria-disabled="true" tabIndex={-1}>
          <div className={styles.cardRow}>
            <div className={styles.cardBody}>
              <Text weight="semibold" className={styles.cardTitle}>
                {rec.displayInfo ? `${rec.displayInfo} : ${rec.name}` : rec.name}
              </Text>
              <Text size={200} className={styles.cardMeta}>
                {meta}
              </Text>
              <Text size={200} className={styles.blockedReason}>
                <InfoRegular className={styles.blockedReasonIcon} aria-hidden="true" />
                <span>{FILING_BLOCKED_REASON}</span>
              </Text>
            </div>
          </div>
        </Card>
      );
    }
    return (
      <Card key={key} className={mergeClasses(styles.card, selected && styles.cardSelected)}>
        <div className={styles.cardRow}>
          <div className={styles.cardBody}>
            <Text weight="semibold" className={styles.cardTitle}>
              {rec.displayInfo ? `${rec.displayInfo} : ${rec.name}` : rec.name}
            </Text>
            <Text size={200} className={styles.cardMeta}>
              {meta}
            </Text>
          </div>
          {selected ? (
            <div className={styles.checkWrap}>
              <Button
                ref={el => {
                  selectedBtnRef.current = el;
                }}
                className={styles.greenCheckBtn}
                appearance="primary"
                icon={<CheckmarkRegular />}
                onClick={clearSelection}
                disabled={disabled}
                aria-label="Selected — click to clear"
              />
              <button
                type="button"
                className={styles.clearX}
                onClick={clearSelection}
                disabled={disabled}
                aria-label="Clear selection"
              >
                <DismissRegular />
              </button>
            </div>
          ) : (
            <Button
              className={styles.ctrlBtn}
              appearance="primary"
              icon={<CheckmarkRegular />}
              onClick={() => selectRecord(rec)}
              disabled={disabled}
              aria-label="Select this record"
            />
          )}
        </div>
      </Card>
    );
  };

  // Task 099 (owner item 4): once a record is selected the picker collapses to JUST that record (with its ×) —
  // no pills, lookup box, results or "+ New". The × (clearSelection) brings it all back, with the previous
  // query and results still in state. A non-blocking create warning still shows (the record was just created).
  if (value !== null && !showCreate) {
    return (
      <div className={styles.root}>
        <div className={styles.cards}>{renderCard(value, { keyPrefix: 'sel:' })}</div>
        {createWarning && (
          <Text size={200} className={styles.fieldWarning} role="status">
            {createWarning}
          </Text>
        )}
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {/* Pill row (task 095, owner 2026-10-04): no "Related to" label — the type pills are left-aligned and
          "+ New" sits at the right end of the same row. */}
      <div className={styles.header}>
        <div className={styles.chips} role="radiogroup" aria-label="Related to record type">
          {allowedTypes.map(type => {
            const selected = type === selectedType;
            return (
              <Button
                key={type}
                size="small"
                shape="circular"
                appearance={selected ? 'primary' : 'subtle'}
                className={mergeClasses(styles.chip, !selected && styles.chipUnselected)}
                onClick={() => handleTypeChange(type)}
                disabled={disabled}
                role="radio"
                aria-checked={selected}
              >
                {type}
              </Button>
            );
          })}
        </div>
        {onCreateRecord && !showCreate && (
          <Button
            appearance="subtle"
            size="small"
            className={styles.newBtn}
            icon={<AddRegular />}
            onClick={() => {
              focusAfterRef.current = 'input';
              setShowCreate(true);
              setCreateWarning(null);
            }}
            disabled={disabled}
          >
            New
          </Button>
        )}
      </div>

      {showCreate ? (
        // Task 100: the "+ New" form — the fields the owner listed for this type (CreateRecordForm). Keyed by type so
        // a type switch never carries one type's values into another's form.
        <CreateRecordForm
          key={selectedType}
          type={selectedType}
          matterTypes={createForm?.matterTypes ?? EMPTY_LIST}
          practiceAreas={createForm?.practiceAreas ?? EMPTY_LIST}
          projectTypes={createForm?.projectTypes ?? EMPTY_LIST}
          defaultAssignee={createForm?.defaultAssignee ?? null}
          onSearchContacts={onSearchContacts}
          onSubmit={handleCreate}
          onCancel={cancelCreate}
          disabled={disabled}
          nameInputRef={el => {
            inputRef.current = el;
          }}
        />
      ) : (
        <div className={styles.searchRow}>
          <Input
            ref={el => {
              inputRef.current = el;
            }}
            value={query}
            onChange={(_, d) => setQuery(d.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') void runSearch();
            }}
            placeholder={`Look up related ${selectedType}...`}
            disabled={disabled}
            style={{ flexGrow: 1 }}
            aria-label={`Search ${selectedType} records`}
          />
          <Button
            appearance="subtle"
            icon={searching ? <Spinner size="tiny" /> : <SearchRegular />}
            onClick={() => void runSearch()}
            disabled={disabled || searching}
            aria-label="Search"
            title="Search"
          />
        </div>
      )}

      {createWarning && (
        <Text size={200} className={styles.fieldWarning} role="status">
          {createWarning}
        </Text>
      )}

      {/* Task 053: a failed search rendered identically to "nothing matched" — now shown distinctly,
          with a Retry that re-runs the same query (the same message+Retry pattern the create form's
          reference lists use). */}
      {!showCreate && searchError && (
        <div className={styles.errorRow}>
          <Text size={200} className={styles.fieldError} role="alert">
            {searchError}
          </Text>
          <Button appearance="outline" size="small" onClick={() => void runSearch()} disabled={disabled}>
            Retry
          </Button>
        </div>
      )}

      {!showCreate && searchResults.length > 0 && (
        <div className={styles.cards}>{searchResults.map(r => renderCard(r, { keyPrefix: 's:' }))}</div>
      )}

      {/* Recommended auto-match cards — hidden while the create form is open (task 099: only the form shows). */}
      {!showCreate && (
        <div className={styles.cards}>
          {candidatesLoading ? (
            <div className={styles.cardRow}>
              <Spinner size="tiny" /> <Text size={200}>Finding matches…</Text>
            </div>
          ) : typeMatches.length > 0 ? (
            typeMatches.map(c => renderCard(c, { confidence: c.confidence }))
          ) : (
            <Text size={200} className={styles.emptyNote}>
              No suggested {selectedType} matches — search above or create a new record.
            </Text>
          )}
        </div>
      )}
    </div>
  );
};

export default RelatedToPicker;
