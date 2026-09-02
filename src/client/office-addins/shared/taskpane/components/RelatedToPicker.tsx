import React, { useMemo, useState } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  Input,
  Spinner,
  Text,
  Badge,
  mergeClasses,
} from '@fluentui/react-components';
import {
  CheckmarkRegular,
  CheckmarkCircleFilled,
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
 * Layout (UI feedback round 2):
 *   [ Related to ]                         [ Matter  Project  Invoice … ]   ← header + chips
 *   [ search input ] [ Search ]            [ + New Matter ]                 ← search row on top
 *   ┌ recommended auto-match cards ────────────────────────────────────┐
 *   │ LITG-763955 : Litigation matter · Matter · 100% match   [Confirm] │
 *   └──────────────────────────────────────────────────────────────────┘
 *
 * On Confirm the chosen card turns GREEN with a check + a small "x" (change); the other
 * cards go gray. Single-select chips (gray except selected=blue, default Matter) scope
 * the cards + search. Host-agnostic: Confirm only *selects*; the regarding is written at
 * save. Fluent v9 (ADR-021).
 */

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalXXL,
    flexWrap: 'wrap',
  },
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
  newBtn: { flexShrink: 0 },
  cards: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  card: { padding: tokens.spacingVerticalS, position: 'relative' },
  cardConfirmed: {
    backgroundColor: tokens.colorStatusSuccessBackground1,
    border: `1px solid ${tokens.colorStatusSuccessBorder1}`,
  },
  cardDimmed: { opacity: 0.5 },
  cardRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  cardBody: { display: 'flex', flexDirection: 'column', gap: '2px', flexGrow: 1, minWidth: 0 },
  cardTitle: { overflow: 'hidden', textOverflow: 'ellipsis' },
  cardMeta: { color: tokens.colorNeutralForeground3 },
  changeX: { position: 'absolute', top: '2px', right: '2px' },
  confirmedBadge: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorStatusSuccessForeground1,
    flexShrink: 0,
  },
  emptyNote: { color: tokens.colorNeutralForeground3, padding: `${tokens.spacingVerticalXS} 0` },
  confirmBtn: { flexShrink: 0 },
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

  // Confirmed record shown as a green card. If the selection came from search (not in the
  // candidate list), prepend it so it still renders as the confirmed green card.
  const confirmedNotInList = value && !typeMatches.some(c => sameRecord(c, value));

  const renderChips = () => (
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
  );

  const renderConfirmedCard = (record: EntitySearchResult, key: string) => (
    <Card key={key} className={mergeClasses(styles.card, styles.cardConfirmed)}>
      <Button
        className={styles.changeX}
        size="small"
        appearance="subtle"
        icon={<DismissRegular />}
        onClick={() => onChange(null)}
        disabled={disabled}
        aria-label="Change related record"
      />
      <div className={styles.cardRow}>
        <div className={styles.cardBody}>
          <div className={styles.confirmedBadge}>
            <CheckmarkCircleFilled aria-hidden="true" />
            <Badge appearance="tint" color="success">
              {record.entityType}
            </Badge>
          </div>
          <Text weight="semibold" className={styles.cardTitle}>
            {record.displayInfo ? `${record.displayInfo} : ${record.name}` : record.name}
          </Text>
        </div>
      </div>
    </Card>
  );

  const renderCandidateCard = (c: RelatedCandidate) => {
    const confirmed = value !== null && sameRecord(c, value);
    if (confirmed) {
      return renderConfirmedCard(c, `${c.logicalName}:${c.id}`);
    }
    return (
      <Card key={`${c.logicalName}:${c.id}`} className={mergeClasses(styles.card, isConfirmed && styles.cardDimmed)}>
        <div className={styles.cardRow}>
          <div className={styles.cardBody}>
            <Text weight="semibold" className={styles.cardTitle}>
              {c.displayInfo ? `${c.displayInfo} : ${c.name}` : c.name}
            </Text>
            <Text size={200} className={styles.cardMeta}>
              {c.entityType} · {pct(c.confidence)}% match
            </Text>
          </div>
          {!isConfirmed && (
            <Button
              className={styles.confirmBtn}
              appearance="primary"
              icon={<CheckmarkRegular />}
              onClick={() => onChange(c)}
              disabled={disabled}
            >
              Confirm
            </Button>
          )}
        </div>
      </Card>
    );
  };

  return (
    <div className={styles.root}>
      {/* Header: "Related to" (left) + type chips (right). */}
      <div className={styles.header}>
        <div className={styles.headerLabel}>
          <PersonSearchRegular aria-hidden="true" />
          <Text weight="semibold">Related to</Text>
        </div>
        {renderChips()}
      </div>

      {/* Search row on top of the recommendations — hidden once a record is confirmed. */}
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
                className={styles.newBtn}
                appearance="primary"
                icon={<AddRegular />}
                onClick={() => onCreateNew(selectedType)}
                disabled={disabled}
              >
                New {selectedType}
              </Button>
            )}
          </div>

          {searchResults.length > 0 && (
            <div className={styles.cards}>
              {searchResults.map(r => (
                <Card key={`s:${r.logicalName}:${r.id}`} className={styles.card}>
                  <div className={styles.cardRow}>
                    <div className={styles.cardBody}>
                      <Text weight="semibold" className={styles.cardTitle}>
                        {r.displayInfo ? `${r.displayInfo} : ${r.name}` : r.name}
                      </Text>
                      <Text size={200} className={styles.cardMeta}>
                        {r.entityType}
                      </Text>
                    </div>
                    <Button
                      className={styles.confirmBtn}
                      appearance="outline"
                      icon={<CheckmarkRegular />}
                      onClick={() => onChange(r)}
                      disabled={disabled}
                    >
                      Confirm
                    </Button>
                  </div>
                </Card>
              ))}
            </div>
          )}
        </>
      )}

      {/* Recommended auto-match cards. */}
      <div className={styles.cards}>
        {confirmedNotInList && value && renderConfirmedCard(value, `sel:${value.logicalName}:${value.id}`)}
        {candidatesLoading ? (
          <div className={styles.cardRow}>
            <Spinner size="tiny" /> <Text size={200}>Finding matches…</Text>
          </div>
        ) : typeMatches.length > 0 ? (
          typeMatches.map(renderCandidateCard)
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
