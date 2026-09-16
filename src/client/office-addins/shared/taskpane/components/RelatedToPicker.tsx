import React, { useMemo, useState } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  Dropdown,
  Input,
  Label,
  Option,
  Spinner,
  Text,
  mergeClasses,
} from '@fluentui/react-components';
import {
  CheckmarkRegular,
  SearchRegular,
  AddRegular,
  DismissRegular,
  PersonSearchRegular,
} from '@fluentui/react-icons';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import type { RelatedCandidate } from '../services/communicationSuggestionsService';
import type { MatterTypeChoice } from '../services/matterTypeLookupService';

/**
 * RelatedToPicker — the add-in's "Related to" selector, modeled on the email-intelligence
 * reconciliation surface (UI feedback, owner 2026-09-02).
 *
 * Layout (feedback round 4):
 *   [ Related to  Matter Project Invoice … ]                         ← header + left chips
 *   [ search input ] [ Search ] [ + New ]                            ← search row (always visible)
 *   ┌ recommended auto-match cards ──────────────────────────────┐
 *   │ LITG-763955 : Litigation matter · Matter · 100% match  [✓]  │  ← blue check; green ✓ + × on select
 *   └────────────────────────────────────────────────────────────┘
 *
 * Selecting only turns the card's check GREEN (with a small × to clear) — the search row
 * and other cards stay. Single-select chips (gray except selected=blue, default Matter).
 * Host-agnostic: selecting only *chooses*; the regarding is written at save. Fluent v9.
 */

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalM,
  },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  headerLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
  },
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
  matterTypeField: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  matterTypeErrorRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  fieldError: { color: tokens.colorPaletteRedForeground1, fontSize: tokens.fontSizeBase200 },
  fieldWarning: { color: tokens.colorPaletteDarkOrangeForeground1, fontSize: tokens.fontSizeBase200 },
});

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
   * Create a new record of the given type + name (BFF-backed). For Matter, also carries the chosen
   * Matter Type id (task 038 — required in this UI, always sent). Resolves to the created record
   * (auto-selected as the Related-to) plus any non-fatal server warnings, or null on failure. Absent →
   * no "New" button.
   */
  onCreateRecord?: (type: EntityType, name: string, matterTypeId?: string) => Promise<CreateRecordResult | null>;
  /** Types offered as chips. */
  allowedTypes: EntityType[];
  /** Default selected type. */
  defaultType?: EntityType;
  /**
   * Active Matter Type reference options for the required field shown when creating a Matter (task
   * 038). Empty while loading, or if the reference list failed to load / has no active rows — either
   * way the Matter create form stays blocked (the type is required, never optional in this UI).
   */
  matterTypeOptions?: MatterTypeChoice[];
  /** Whether `matterTypeOptions` is still loading (disables the dropdown, shows a loading placeholder). */
  matterTypesLoading?: boolean;
  /**
   * A readable message when the matter-type list failed to load — distinct from "loaded, zero active
   * rows" (coordinator fix, 2026-09-13). Renders a non-blocking notice with a Retry action; the field
   * stays required (and Create stays disabled) either way.
   */
  matterTypesError?: string | null;
  /** Re-fetches the matter-type list (the Retry action). Absent → no Retry button is rendered. */
  onRetryMatterTypes?: () => void;
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
  matterTypeOptions = [],
  matterTypesLoading = false,
  matterTypesError = null,
  onRetryMatterTypes,
  disabled = false,
}) => {
  const styles = useStyles();
  const initialType: EntityType = allowedTypes.includes(defaultType) ? defaultType : (allowedTypes[0] ?? 'Matter');
  const [selectedType, setSelectedType] = useState<EntityType>(initialType);
  const [query, setQuery] = useState('');
  const [searchResults, setSearchResults] = useState<EntitySearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createWarning, setCreateWarning] = useState<string | null>(null);
  // Matter Type (task 038) — required only when creating a Matter; owner decision 2026-09-11.
  const [selectedMatterTypeId, setSelectedMatterTypeId] = useState('');
  const [matterTypeError, setMatterTypeError] = useState<string | null>(null);

  const typeMatches = useMemo(() => candidates.filter(c => c.entityType === selectedType), [candidates, selectedType]);

  // Prepend the selected record as a card only when it isn't already shown in either list.
  const selectedShown =
    value !== null && (typeMatches.some(c => sameRecord(c, value)) || searchResults.some(r => sameRecord(r, value)));

  const handleTypeChange = (type: EntityType) => {
    setSelectedType(type);
    setQuery('');
    setSearchResults([]);
    setShowCreate(false);
    setNewName('');
    setCreateError(null);
    setCreateWarning(null);
    setSelectedMatterTypeId('');
    setMatterTypeError(null);
  };

  const handleCreate = async () => {
    if (!onCreateRecord) return;
    const n = newName.trim();
    if (n.length === 0) return;

    // Matter Type is required for a Matter — the create action is blocked until one is chosen
    // (owner decision 2026-09-11; the server never rejects a missing one, but this UI always sends
    // it). The error uses role="alert" below, so it is announced (NFR-11) the moment it renders.
    if (selectedType === 'Matter' && !selectedMatterTypeId) {
      setMatterTypeError('Choose a Matter Type before creating a Matter.');
      return;
    }

    setCreating(true);
    setCreateError(null);
    setCreateWarning(null);
    try {
      const result = await onCreateRecord(
        selectedType,
        n,
        selectedType === 'Matter' ? selectedMatterTypeId : undefined
      );
      if (result) {
        onChange(result.record);
        setShowCreate(false);
        setNewName('');
        setSelectedMatterTypeId('');
        setMatterTypeError(null);
        // Non-blocking — the record is created and selected regardless (task 038: never swallow a
        // server warning, e.g. an unresolvable matterTypeId).
        if (result.warnings && result.warnings.length > 0) {
          setCreateWarning(result.warnings.join(' '));
        }
      } else {
        setCreateError(`Couldn't create the ${selectedType}.`);
      }
    } catch {
      setCreateError(`Couldn't create the ${selectedType}.`);
    } finally {
      setCreating(false);
    }
  };

  const selectedMatterTypeName = matterTypeOptions.find(mt => mt.id === selectedMatterTypeId)?.name;

  const runSearch = async () => {
    const q = query.trim();
    if (q.length === 0) {
      setSearchResults([]);
      return;
    }
    setSearching(true);
    try {
      setSearchResults(await onSearch(q, selectedType));
    } catch {
      setSearchResults([]);
    } finally {
      setSearching(false);
    }
  };

  // One card. Selected → green check + a small × to clear; else a blue check to select.
  const renderCard = (rec: EntitySearchResult, opts: { confidence?: number; keyPrefix?: string }) => {
    const selected = value !== null && sameRecord(rec, value);
    const key = `${opts.keyPrefix ?? ''}${rec.logicalName}:${rec.id}`;
    return (
      <Card key={key} className={mergeClasses(styles.card, selected && styles.cardSelected)}>
        <div className={styles.cardRow}>
          <div className={styles.cardBody}>
            <Text weight="semibold" className={styles.cardTitle}>
              {rec.displayInfo ? `${rec.displayInfo} : ${rec.name}` : rec.name}
            </Text>
            <Text size={200} className={styles.cardMeta}>
              {opts.confidence != null ? `${rec.entityType} · ${pct(opts.confidence)}% match` : rec.entityType}
            </Text>
          </div>
          {selected ? (
            <div className={styles.checkWrap}>
              <Button
                className={styles.greenCheckBtn}
                appearance="primary"
                icon={<CheckmarkRegular />}
                onClick={() => onChange(null)}
                disabled={disabled}
                aria-label="Selected — click to clear"
              />
              <button
                type="button"
                className={styles.clearX}
                onClick={() => onChange(null)}
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
              onClick={() => onChange(rec)}
              disabled={disabled}
              aria-label="Select this record"
            />
          )}
        </div>
      </Card>
    );
  };

  return (
    <div className={styles.root}>
      {/* Header: "Related to" + type chips, left-aligned next to the label. */}
      <div className={styles.header}>
        <div className={styles.headerLabel}>
          <PersonSearchRegular aria-hidden="true" />
          <Text weight="semibold">Related to</Text>
        </div>
        <div className={styles.chips} role="radiogroup" aria-label="Record type">
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
      </div>

      {/* Search row. Clicking "New" turns this SAME row into the create form:
          the input becomes the new-record name, Search→Create, New→Cancel. */}
      <div className={styles.searchRow}>
        <Input
          value={showCreate ? newName : query}
          onChange={(_, d) => (showCreate ? setNewName(d.value) : setQuery(d.value))}
          onKeyDown={e => {
            if (e.key === 'Enter') void (showCreate ? handleCreate() : runSearch());
          }}
          placeholder={showCreate ? `New ${selectedType} name` : `Look up another ${selectedType}…`}
          disabled={disabled || (showCreate && creating)}
          {...(showCreate ? {} : { contentBefore: <SearchRegular /> })}
          style={{ flexGrow: 1 }}
          aria-label={showCreate ? `New ${selectedType} name` : `Search ${selectedType} records`}
        />
        {showCreate ? (
          <>
            <Button
              appearance="primary"
              onClick={() => void handleCreate()}
              disabled={
                disabled ||
                creating ||
                newName.trim().length === 0 ||
                (selectedType === 'Matter' && !selectedMatterTypeId)
              }
            >
              {creating ? <Spinner size="tiny" /> : 'Create'}
            </Button>
            <Button
              appearance="subtle"
              onClick={() => {
                setShowCreate(false);
                setNewName('');
                setCreateError(null);
                setCreateWarning(null);
                setSelectedMatterTypeId('');
                setMatterTypeError(null);
              }}
              disabled={creating}
            >
              Cancel
            </Button>
          </>
        ) : (
          <>
            <Button appearance="subtle" onClick={() => void runSearch()} disabled={disabled || searching}>
              {searching ? <Spinner size="tiny" /> : 'Search'}
            </Button>
            {onCreateRecord && (
              <Button
                appearance="subtle"
                icon={<AddRegular />}
                onClick={() => {
                  setShowCreate(true);
                  setCreateError(null);
                  setCreateWarning(null);
                }}
                disabled={disabled}
              >
                New
              </Button>
            )}
          </>
        )}
      </div>

      {/* Matter Type — required only for a new Matter (task 038; owner decision 2026-09-11). A small,
          load-once reference list (see matterTypeLookupService.ts); the Create button above stays
          disabled until one is chosen. */}
      {showCreate && selectedType === 'Matter' && (
        <div className={styles.matterTypeField}>
          <Label htmlFor="new-matter-type" required>
            Matter Type
          </Label>
          <Dropdown
            id="new-matter-type"
            placeholder={matterTypesLoading ? 'Loading matter types…' : 'Select a matter type'}
            value={selectedMatterTypeName ?? ''}
            selectedOptions={selectedMatterTypeId ? [selectedMatterTypeId] : []}
            onOptionSelect={(_, data) => {
              setSelectedMatterTypeId(data.optionValue ?? '');
              setMatterTypeError(null);
            }}
            disabled={disabled || creating || matterTypesLoading || !!matterTypesError}
            aria-label="Matter Type"
            aria-required="true"
            aria-invalid={!!matterTypeError || !!matterTypesError}
          >
            {matterTypeOptions.map(mt => (
              <Option key={mt.id} value={mt.id} text={mt.name}>
                {mt.name}
              </Option>
            ))}
          </Dropdown>
          {/* A failed load and "zero active rows, loaded fine" are different states (coordinator fix,
              2026-09-13): only the genuine empty-table case gets the quiet note; a failure gets its
              own message + Retry below, and the two never show at once. */}
          {!matterTypesLoading && !matterTypesError && matterTypeOptions.length === 0 && (
            <Text size={200} className={styles.emptyNote}>
              No matter types are available right now.
            </Text>
          )}
          {!matterTypesLoading && matterTypesError && (
            <div className={styles.matterTypeErrorRow}>
              <Text size={200} className={styles.fieldError} role="alert">
                {matterTypesError}
              </Text>
              {onRetryMatterTypes && (
                <Button appearance="outline" size="small" onClick={() => onRetryMatterTypes()}>
                  Retry
                </Button>
              )}
            </div>
          )}
          {matterTypeError && (
            <Text size={200} className={styles.fieldError} role="alert">
              {matterTypeError}
            </Text>
          )}
        </div>
      )}

      {createError && (
        <Text size={200} className={styles.emptyNote} role="alert">
          {createError}
        </Text>
      )}
      {createWarning && (
        <Text size={200} className={styles.fieldWarning} role="status">
          {createWarning}
        </Text>
      )}

      {searchResults.length > 0 && (
        <div className={styles.cards}>{searchResults.map(r => renderCard(r, { keyPrefix: 's:' }))}</div>
      )}

      {/* Recommended auto-match cards. */}
      <div className={styles.cards}>
        {value && !selectedShown && renderCard(value, { keyPrefix: 'sel:' })}
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
    </div>
  );
};

export default RelatedToPicker;
