import React, { useMemo, useState } from 'react';
import { makeStyles, tokens, Button, Card, Input, Spinner, Text, mergeClasses } from '@fluentui/react-components';
import {
  CheckmarkRegular,
  SearchRegular,
  AddRegular,
  DismissRegular,
  PersonSearchRegular,
} from '@fluentui/react-icons';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import type { RelatedCandidate } from '../services/communicationSuggestionsService';

/**
 * RelatedToPicker — the add-in's "Related to" selector, modeled on the email-intelligence
 * reconciliation surface (UI feedback, owner 2026-09-02).
 *
 * Layout (feedback round 3):
 *   [ Related to ]                          [ Matter Project Invoice … ]  ← header + right chips
 *   [ search input ] [ Search ]                                    [ + ]  ← search row on top
 *   ┌ recommended auto-match cards ───────────────────────────────────┐
 *   │ LITG-763955 : Litigation matter · Matter · 100% match       [✓]  │  ← small blue check
 *   └─────────────────────────────────────────────────────────────────┘
 *
 * Selecting a card turns its check GREEN with a small "×" to reset; the other cards go
 * gray. Single-select chips (gray except selected=blue, default Matter). Host-agnostic:
 * selecting only *chooses*; the regarding is written at save. Fluent v9 (ADR-021).
 */

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  header: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  headerLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
  },
  chips: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalXS, marginLeft: 'auto' },
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
  cardDimmed: { opacity: 0.5 },
  cardRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  cardBody: { display: 'flex', flexDirection: 'column', gap: '2px', flexGrow: 1, minWidth: 0 },
  cardTitle: { overflow: 'hidden', textOverflow: 'ellipsis' },
  cardMeta: { color: tokens.colorNeutralForeground3 },
  selectedControls: { display: 'flex', alignItems: 'center', gap: '2px', flexShrink: 0 },
  greenCheck: {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorStatusSuccessBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    flexShrink: 0,
  },
  ctrlBtn: { flexShrink: 0 },
  emptyNote: { color: tokens.colorNeutralForeground3, padding: `${tokens.spacingVerticalXS} 0` },
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
  /** Create a new record of the given type (Slice 3 — BFF-backed). */
  onCreateNew?: (type: EntityType) => void;
  /** Types offered as chips. */
  allowedTypes: EntityType[];
  /** Default selected type. */
  defaultType?: EntityType;
  disabled?: boolean;
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
  onCreateNew,
  allowedTypes,
  defaultType = 'Matter',
  disabled = false,
}) => {
  const styles = useStyles();
  const initialType: EntityType = allowedTypes.includes(defaultType) ? defaultType : (allowedTypes[0] ?? 'Matter');
  const [selectedType, setSelectedType] = useState<EntityType>(initialType);
  const [query, setQuery] = useState('');
  const [searchResults, setSearchResults] = useState<EntitySearchResult[]>([]);
  const [searching, setSearching] = useState(false);

  const typeMatches = useMemo(() => candidates.filter(c => c.entityType === selectedType), [candidates, selectedType]);
  const isConfirmed = value !== null;
  const confirmedNotInList = value !== null && !typeMatches.some(c => sameRecord(c, value));

  const handleTypeChange = (type: EntityType) => {
    setSelectedType(type);
    setQuery('');
    setSearchResults([]);
  };

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

  // One card. `state`: 'confirm' (blue check), 'selected' (green check + reset ×), 'dimmed'.
  const renderCard = (
    rec: EntitySearchResult,
    opts: { confidence?: number; state: 'confirm' | 'selected' | 'dimmed'; keyPrefix?: string }
  ) => {
    const key = `${opts.keyPrefix ?? ''}${rec.logicalName}:${rec.id}`;
    return (
      <Card
        key={key}
        className={mergeClasses(
          styles.card,
          opts.state === 'selected' && styles.cardSelected,
          opts.state === 'dimmed' && styles.cardDimmed
        )}
      >
        <div className={styles.cardRow}>
          <div className={styles.cardBody}>
            <Text weight="semibold" className={styles.cardTitle}>
              {rec.displayInfo ? `${rec.displayInfo} : ${rec.name}` : rec.name}
            </Text>
            <Text size={200} className={styles.cardMeta}>
              {opts.confidence != null ? `${rec.entityType} · ${pct(opts.confidence)}% match` : rec.entityType}
            </Text>
          </div>
          {opts.state === 'selected' ? (
            <div className={styles.selectedControls}>
              <span className={styles.greenCheck} aria-label="Selected">
                <CheckmarkRegular />
              </span>
              <Button
                className={styles.ctrlBtn}
                size="small"
                appearance="subtle"
                icon={<DismissRegular />}
                onClick={() => onChange(null)}
                disabled={disabled}
                aria-label="Reset selection"
              />
            </div>
          ) : (
            <Button
              className={styles.ctrlBtn}
              size="small"
              appearance="primary"
              icon={<CheckmarkRegular />}
              onClick={() => onChange(rec)}
              disabled={disabled || opts.state === 'dimmed'}
              aria-label="Select this record"
            />
          )}
        </div>
      </Card>
    );
  };

  return (
    <div className={styles.root}>
      {/* Header: "Related to" (left) + type chips (right-aligned). */}
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

      {/* Search row on top — hidden once a record is selected. */}
      {!isConfirmed && (
        <>
          <div className={styles.searchRow}>
            <Input
              value={query}
              onChange={(_, d) => setQuery(d.value)}
              onKeyDown={e => {
                if (e.key === 'Enter') void runSearch();
              }}
              placeholder={`Look up another ${selectedType}…`}
              disabled={disabled}
              contentBefore={<SearchRegular />}
              style={{ flexGrow: 1 }}
              aria-label={`Search ${selectedType} records`}
            />
            <Button appearance="subtle" onClick={() => void runSearch()} disabled={disabled || searching}>
              {searching ? <Spinner size="tiny" /> : 'Search'}
            </Button>
            {onCreateNew && (
              <Button
                className={styles.ctrlBtn}
                appearance="subtle"
                icon={<AddRegular />}
                onClick={() => onCreateNew(selectedType)}
                disabled={disabled}
                aria-label={`New ${selectedType}`}
                title={`New ${selectedType}`}
              />
            )}
          </div>

          {searchResults.length > 0 && (
            <div className={styles.cards}>
              {searchResults.map(r => renderCard(r, { state: 'confirm', keyPrefix: 's:' }))}
            </div>
          )}
        </>
      )}

      {/* Recommended auto-match cards. */}
      <div className={styles.cards}>
        {confirmedNotInList && value && renderCard(value, { state: 'selected', keyPrefix: 'sel:' })}
        {candidatesLoading ? (
          <div className={styles.cardRow}>
            <Spinner size="tiny" /> <Text size={200}>Finding matches…</Text>
          </div>
        ) : typeMatches.length > 0 ? (
          typeMatches.map(c => {
            const selected = value !== null && sameRecord(c, value);
            const state: 'confirm' | 'selected' | 'dimmed' = selected ? 'selected' : isConfirmed ? 'dimmed' : 'confirm';
            return renderCard(c, { confidence: c.confidence, state });
          })
        ) : (
          !isConfirmed && (
            <Text size={200} className={styles.emptyNote}>
              No suggested {selectedType} matches — search above or create a new record.
            </Text>
          )
        )}
      </div>
    </div>
  );
};

export default RelatedToPicker;
