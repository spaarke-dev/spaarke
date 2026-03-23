/**
 * DataverseLookupField.tsx
 * Lookup field that opens the standard Dataverse lookup side pane
 * via INavigationService.openLookup() (Xrm.Utility.lookupObjects).
 *
 * When a value is selected it renders as a dismissible chip (same visual as
 * the inline LookupField). When no value is selected a "Select" button is shown.
 *
 * Falls back to the inline LookupField when no navigationService is provided
 * or when the caller passes an explicit onSearch function and the lookup returns
 * an empty result (graceful no-op in non-Dataverse contexts such as the BFF SPA).
 *
 * Usage:
 * ```tsx
 * <DataverseLookupField
 *   label="Matter Type"
 *   required
 *   entityType="sprk_mattertype"
 *   value={matterTypeValue}
 *   onChange={handleMatterTypeChange}
 *   navigationService={navigationService}
 *   // Fallback: used when navigationService is absent or returns empty
 *   onSearch={handleSearchMatterTypes}
 *   isAiPrefilled={isAiField('matterTypeId')}
 * />
 * ```
 *
 * Constraints:
 *   - Fluent v9 only: Button, Text, Field, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 *
 * @see INavigationService.openLookup
 * @see ADR-012 — Shared Component Library
 * @see ADR-021 — Fluent v9 Design System
 */

import * as React from 'react';
import {
  Button,
  Field,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import type { ILookupItem } from '../../types/LookupTypes';
import type { INavigationService } from '../../types/serviceInterfaces';
import { LookupField } from './LookupField';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDataverseLookupFieldProps {
  /** Field label displayed above the control. */
  label: string;
  /** Whether the field is required. */
  required?: boolean;
  /**
   * Dataverse entity logical name for the lookup (e.g., "sprk_mattertype").
   * Passed directly to INavigationService.openLookup({ entityType }).
   */
  entityType: string;
  /** Currently selected lookup item (or null if nothing selected). */
  value: ILookupItem | null;
  /** Called when the user selects or clears an item. */
  onChange: (item: ILookupItem | null) => void;
  /**
   * Navigation service — when provided the field opens the Dataverse lookup
   * side pane. When absent the component falls back to inline search.
   */
  navigationService?: INavigationService;
  /**
   * Fallback inline search function — used when navigationService is absent.
   * Also used as the search implementation if the environment is non-Dataverse
   * (e.g., the BFF SPA adapter returns an empty array from openLookup).
   */
  onSearch?: (query: string) => Promise<ILookupItem[]>;
  /** Placeholder text for the fallback inline search input. */
  placeholder?: string;
  /** Optional content rendered after the label (e.g., AI badge). */
  labelExtra?: React.ReactNode;
  /** Minimum characters before fallback search fires. Default: 1. */
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

  // ── Label row ─────────────────────────────────────────────────────────────
  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
  },

  // ── Empty state: "Select" button ──────────────────────────────────────────
  selectRow: {
    display: 'flex',
    alignItems: 'center',
  },

  // ── Selected chip ─────────────────────────────────────────────────────────
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
});

// ---------------------------------------------------------------------------
// DataverseLookupField
// ---------------------------------------------------------------------------

/**
 * Renders a label with an optional required mark and optional extra content.
 */
function LabelContent({
  label,
  required,
  labelExtra,
  styles,
}: {
  label: string;
  required?: boolean;
  labelExtra?: React.ReactNode;
  styles: ReturnType<typeof useStyles>;
}): React.ReactElement {
  return (
    <span className={styles.labelRow}>
      {label}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
      {labelExtra}
    </span>
  );
}

export const DataverseLookupField: React.FC<IDataverseLookupFieldProps> = ({
  label,
  required,
  entityType,
  value,
  onChange,
  navigationService,
  onSearch,
  placeholder,
  labelExtra,
  minSearchLength = 1,
}) => {
  const styles = useStyles();
  const [isOpening, setIsOpening] = React.useState(false);

  // ── Open Dataverse lookup side pane ──────────────────────────────────────
  const handleOpenLookup = React.useCallback(async () => {
    if (!navigationService) return;

    setIsOpening(true);
    try {
      const results = await navigationService.openLookup({ entityType });
      if (results.length > 0) {
        // Use first result (allowMultiSelect defaults to false)
        onChange({ id: results[0].id, name: results[0].name });
      }
      // If results is empty the user cancelled — preserve existing value
    } finally {
      setIsOpening(false);
    }
  }, [navigationService, entityType, onChange]);

  // ── Clear selection ───────────────────────────────────────────────────────
  const handleClear = React.useCallback(() => {
    onChange(null);
  }, [onChange]);

  // ── Fallback: no navigation service (or not a Dataverse context) ──────────
  if (!navigationService) {
    if (onSearch) {
      return (
        <LookupField
          label={label}
          required={required}
          placeholder={placeholder ?? `Search ${label.toLowerCase()}...`}
          value={value}
          onChange={onChange}
          onSearch={onSearch}
          labelExtra={labelExtra}
          minSearchLength={minSearchLength}
        />
      );
    }
    // No navigationService and no onSearch — render read-only chip or empty
    return (
      <div className={styles.wrapper}>
        <Field
          label={
            <LabelContent
              label={label}
              required={required}
              labelExtra={labelExtra}
              styles={styles}
            />
          }
          required={required}
        >
          {value ? (
            <div className={styles.selectedChip}>
              <Text size={200} weight="semibold" className={styles.selectedChipName}>
                {value.name}
              </Text>
            </div>
          ) : (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              No selection
            </Text>
          )}
        </Field>
      </div>
    );
  }

  // ── Dataverse side-pane lookup mode ─────────────────────────────────────
  return (
    <div className={styles.wrapper}>
      <Field
        label={
          <LabelContent
            label={label}
            required={required}
            labelExtra={labelExtra}
            styles={styles}
          />
        }
        required={required}
      >
        {value ? (
          // Selected: show chip with dismiss button
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
          // Empty: show "Select" button that opens the Dataverse lookup pane
          <div className={styles.selectRow}>
            <Button
              appearance="outline"
              size="small"
              icon={isOpening ? <Spinner size="extra-tiny" /> : <SearchRegular />}
              onClick={handleOpenLookup}
              disabled={isOpening}
              aria-label={`Select ${label}`}
            >
              {isOpening ? 'Opening\u2026' : `Select ${label}`}
            </Button>
          </div>
        )}
      </Field>
    </div>
  );
};

DataverseLookupField.displayName = 'DataverseLookupField';
