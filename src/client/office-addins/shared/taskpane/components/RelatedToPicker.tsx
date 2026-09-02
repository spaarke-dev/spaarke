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
} from '@fluentui/react-icons';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import type { RelatedCandidate } from '../services/communicationSuggestionsService';

/**
 * RelatedToPicker — the add-in's "Related to" selector, modeled on the email-intelligence
 * reconciliation surface (UI feedback, owner 2026-09-02).
 *
 * Instead of the recent-list dropdown, it shows the Association Engine's **auto-matched
 * candidate cards** (record + type + "% match" + Confirm), defaulting to matches rather
 * than Recent (#6/#7). Single-select **type chips** (gray except the selected one, default
 * Matter — #8) scope the cards + the "Look up another record" search. Confirming a card
 * (or selecting a search result) shows the **full selected card** (#9). "New record" is
 * wired to a host-supplied create callback (#10, BFF-backed — Slice 3).
 *
 * Host-agnostic: the confirm just *selects* the record; the regarding is written when the
 * email is saved (the existing save path), so no Xrm host is required. Fluent v9 (ADR-021).
 */

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
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
  cards: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  card: { padding: tokens.spacingVerticalS },
  cardRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  cardBody: { display: 'flex', flexDirection: 'column', gap: '2px', flexGrow: 1, minWidth: 0 },
  cardTitle: { overflow: 'hidden', textOverflow: 'ellipsis' },
  cardMeta: { color: tokens.colorNeutralForeground3 },
  selectedCard: {
    padding: tokens.spacingVerticalM,
    border: `1px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  selectedHeader: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  searchRow: { display: 'flex', gap: tokens.spacingHorizontalXS, marginTop: tokens.spacingVerticalXS },
  emptyNote: { color: tokens.colorNeutralForeground3, padding: `${tokens.spacingVerticalXS} 0` },
  footerRow: { display: 'flex', justifyContent: 'flex-start', marginTop: tokens.spacingVerticalXS },
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

  // Auto-match cards for the selected type (ranked highest-first from the service).
  const typeMatches = useMemo(() => candidates.filter(c => c.entityType === selectedType), [candidates, selectedType]);

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
      const results = await onSearch(q, selectedType);
      setSearchResults(results);
    } catch {
      setSearchResults([]);
    } finally {
      setSearching(false);
    }
  };

  // ---- Selected state (#9: full card) --------------------------------------
  if (value) {
    const meta = value.displayInfo;
    return (
      <div className={styles.root}>
        <Card className={styles.selectedCard} appearance="filled-alternative">
          <div className={styles.cardRow}>
            <div className={styles.cardBody}>
              <div className={styles.selectedHeader}>
                <CheckmarkCircleFilled style={{ color: tokens.colorBrandForeground1 }} aria-hidden="true" />
                <Badge appearance="tint" color="brand">
                  {value.entityType}
                </Badge>
              </div>
              <Text weight="semibold">{value.name}</Text>
              {meta && (
                <Text size={200} className={styles.cardMeta}>
                  {meta}
                </Text>
              )}
            </div>
            <Button
              appearance="subtle"
              icon={<DismissRegular />}
              onClick={() => onChange(null)}
              disabled={disabled}
              aria-label="Change related record"
            >
              Change
            </Button>
          </div>
        </Card>
      </div>
    );
  }

  // ---- Unselected: chips + auto-match cards + search + new -----------------
  return (
    <div className={styles.root}>
      {/* Type chips — single-select, gray except the selected one (blue). */}
      <div className={styles.chips} role="radiogroup" aria-label="Record type">
        {allowedTypes.map(type => {
          const isSelected = type === selectedType;
          return (
            <Button
              key={type}
              size="small"
              shape="circular"
              appearance={isSelected ? 'primary' : 'subtle'}
              className={mergeClasses(styles.chip, !isSelected && styles.chipUnselected)}
              onClick={() => handleTypeChange(type)}
              disabled={disabled}
              role="radio"
              aria-checked={isSelected}
            >
              {type}
            </Button>
          );
        })}
      </div>

      {/* Auto-match candidate cards for the selected type. */}
      <div className={styles.cards}>
        {candidatesLoading ? (
          <div className={styles.cardRow}>
            <Spinner size="tiny" /> <Text size={200}>Finding matches…</Text>
          </div>
        ) : typeMatches.length > 0 ? (
          typeMatches.map(c => (
            <Card key={`${c.logicalName}:${c.id}`} className={styles.card}>
              <div className={styles.cardRow}>
                <div className={styles.cardBody}>
                  <Text weight="semibold" className={styles.cardTitle}>
                    {c.displayInfo ? `${c.displayInfo} : ${c.name}` : c.name}
                  </Text>
                  <Text size={200} className={styles.cardMeta}>
                    {c.entityType} · {pct(c.confidence)}% match
                  </Text>
                </div>
                <Button
                  className={styles.confirmBtn}
                  appearance="primary"
                  icon={<CheckmarkRegular />}
                  onClick={() => onChange(c)}
                  disabled={disabled}
                >
                  Confirm
                </Button>
              </div>
            </Card>
          ))
        ) : (
          <Text size={200} className={styles.emptyNote}>
            No suggested {selectedType} matches — search below or create a new record.
          </Text>
        )}
      </div>

      {/* Look up another record (scoped to the selected type). */}
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
      </div>

      {searchResults.length > 0 && (
        <div className={styles.cards}>
          {searchResults.map(r => (
            <Card key={`${r.logicalName}:${r.id}`} className={styles.card}>
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
                  onClick={() => onChange(r)}
                  disabled={disabled}
                >
                  Select
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* New record (#10 — BFF-backed create wired in Slice 3). */}
      {onCreateNew && (
        <div className={styles.footerRow}>
          <Button
            appearance="subtle"
            icon={<AddRegular />}
            onClick={() => onCreateNew(selectedType)}
            disabled={disabled}
          >
            New {selectedType}
          </Button>
        </div>
      )}
    </div>
  );
};

export default RelatedToPicker;
