/**
 * SearchIndexSelector — Fluent UI v9 Dropdown bound to active `sprk_aisearchindex`
 * rows.
 *
 * Phase G (Lookup-driven multi-index) — see spec §6.
 *
 * Replaces the 4-domain ToggleButton grid (`SearchDomainTabs`) at the top of
 * `SearchFilterPane`. A single dropdown drives BOTH the Azure index name AND
 * the entity scope filter via the row's `sprk_targetentitytype` Choice label
 * (see `targetEntityNormalize.buildSearchRequestFragment`).
 *
 * Empty-list behavior: when no active rows exist, the dropdown renders with
 * a disabled "no indexes available" message rather than silently falling
 * back to the BFF tenant default (per spec §6 Authorization — operator scopes
 * indexes by security role; the user has no way to recover from a silent
 * fallback).
 *
 * @see ADR-021 — Fluent UI v9 design system requirements
 * @see SearchFilterPane.tsx — host component
 * @see services/aiSearchIndexService.ts — row source
 */

import { useCallback } from 'react';
import { makeStyles, tokens, Dropdown, Option, Label, Spinner, Text } from '@fluentui/react-components';
import type { AiSearchIndexRow } from '../services/aiSearchIndexService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SearchIndexSelectorProps {
  /** Active `sprk_aisearchindex` rows, ordered by displayorder + displayname. */
  indexes: AiSearchIndexRow[];
  /** ID of the currently-selected row (PK). Empty string if none. */
  selectedIndexId: string;
  /** Whether the index list is still loading from Dataverse. */
  isLoading: boolean;
  /** Called when the user picks a different row. */
  onSelectIndex: (row: AiSearchIndexRow) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  label: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  dropdown: {
    width: '100%',
    minWidth: 0,
  },
  loadingRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  emptyHint: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SearchIndexSelector: React.FC<SearchIndexSelectorProps> = ({
  indexes,
  selectedIndexId,
  isLoading,
  onSelectIndex,
}) => {
  const styles = useStyles();

  const handleOptionSelect = useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const row = indexes.find(r => r.sprk_aisearchindexid === data.optionValue);
      if (row) {
        onSelectIndex(row);
      }
    },
    [indexes, onSelectIndex]
  );

  // ---------------- Loading state ----------------
  if (isLoading) {
    return (
      <div className={styles.section}>
        <Label className={styles.label}>Search Index</Label>
        <div className={styles.loadingRow}>
          <Spinner size="extra-tiny" />
          <Text size={200}>Loading indexes...</Text>
        </div>
      </div>
    );
  }

  // ---------------- Empty state ----------------
  if (indexes.length === 0) {
    return (
      <div className={styles.section}>
        <Label className={styles.label}>Search Index</Label>
        <Dropdown
          className={styles.dropdown}
          size="small"
          disabled
          placeholder="no indexes available"
          aria-label="Search index (none available)"
        />
        <Text className={styles.emptyHint}>No indexes configured for your role.</Text>
      </div>
    );
  }

  // ---------------- Populated state ----------------
  const selectedRow = indexes.find(r => r.sprk_aisearchindexid === selectedIndexId);
  const selectedLabel = selectedRow?.sprk_displayname ?? '';

  return (
    <div className={styles.section}>
      <Label className={styles.label}>Search Index</Label>
      <Dropdown
        className={styles.dropdown}
        size="small"
        value={selectedLabel}
        selectedOptions={selectedIndexId ? [selectedIndexId] : []}
        onOptionSelect={handleOptionSelect}
        aria-label="Search index"
      >
        {indexes.map(row => (
          <Option key={row.sprk_aisearchindexid} value={row.sprk_aisearchindexid}>
            {row.sprk_displayname}
          </Option>
        ))}
      </Dropdown>
    </div>
  );
};

export default SearchIndexSelector;
