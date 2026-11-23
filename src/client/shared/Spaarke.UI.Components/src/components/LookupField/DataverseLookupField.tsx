/**
 * DataverseLookupField.tsx
 * Lookup field that opens the standard Dataverse lookup side pane
 * via INavigationService.openLookup() (Xrm.Utility.lookupObjects).
 *
 * When a value is selected it renders as a dismissible chip (same visual as
 * the inline LookupField). When no value is selected a "Select" button is shown.
 *
 * With a navigationService the field never falls back to inline search: an empty
 * result or a failed pane is a cancel and the current value stays. Without a
 * navigationService it renders the inline LookupField when onSearch is given
 * (otherwise a read-only chip).
 *
 * A pane is opened at most once at a time (single flight). A rejection from
 * openLookup (for example no Xrm host) is logged and treated as a cancel.
 *
 * Usage:
 * ```tsx
 * <DataverseLookupField
 *   label="Matter Type"
 *   required
 *   entityType="sprk_mattertype_ref"
 *   value={matterTypeValue}
 *   onChange={handleMatterTypeChange}
 *   navigationService={navigationService}
 *   // Fallback: used only when navigationService is absent
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
import { Button, Field, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import type { ILookupItem } from '../../types/LookupTypes';
import type { INavigationService, LookupOptions } from '../../types/serviceInterfaces';
import { LookupField } from './LookupField';
import { cleanGuid } from '../../services/PolymorphicResolverService';

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
  /**
   * Row filters for the side pane, passed unchanged to INavigationService.openLookup
   * (e.g. limit contacts to active ones). Omitted from the options when not set.
   */
  filters?: LookupOptions['filters'];
  /** Optional hint text rendered under the field (Fluent Field hint). */
  hint?: string;
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

  // ── Empty state: search-input–styled button ─────────────────────────────
  selectRow: {
    display: 'flex',
    alignItems: 'center',
    width: '100%',
  },
  selectButton: {
    width: '100%',
    justifyContent: 'flex-start',
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground4,
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    minHeight: '32px',
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
  filters,
  hint,
}) => {
  const styles = useStyles();
  const [isOpening, setIsOpening] = React.useState(false);
  // Single flight: a second activation while a pane is open opens nothing.
  const opening = React.useRef(false);
  const mounted = React.useRef(true);
  React.useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  // ── Open Dataverse lookup side pane ──────────────────────────────────────
  const handleOpenLookup = React.useCallback(async () => {
    if (!navigationService || opening.current) return;

    opening.current = true;
    setIsOpening(true);
    try {
      const results = await navigationService.openLookup(filters ? { entityType, filters } : { entityType });
      if (results.length > 0) {
        // Use first result (allowMultiSelect defaults to false).
        // cleanGuid defensively (the adapter already normalizes at the platform
        // boundary; belt-and-suspenders for any INavigationService impl that doesn't).
        onChange({ id: cleanGuid(results[0].id), name: results[0].name });
      }
      // If results is empty the user cancelled — preserve existing value
    } catch {
      // A failed pane is a cancel: the value is unchanged and the user can retry.
      // Log the entity only, never the error object (it can carry host/session detail).
      // eslint-disable-next-line no-restricted-syntax -- a failed pane is a cancel: the value is unchanged and the user can retry; the warning is the diagnostic
      console.warn(`[DataverseLookupField] openLookup failed for "${entityType}"; treated as cancel`);
    } finally {
      opening.current = false;
      if (mounted.current) setIsOpening(false);
    }
  }, [navigationService, entityType, filters, onChange]);

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
          hint={hint}
        />
      );
    }
    // No navigationService and no onSearch — render read-only chip or empty
    return (
      <div className={styles.wrapper}>
        <Field
          label={<LabelContent label={label} required={required} labelExtra={labelExtra} styles={styles} />}
          required={required}
          hint={hint}
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
        label={<LabelContent label={label} required={required} labelExtra={labelExtra} styles={styles} />}
        required={required}
        hint={hint}
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
              className={styles.selectButton}
              appearance="outline"
              size="medium"
              icon={isOpening ? <Spinner size="extra-tiny" /> : <SearchRegular />}
              onClick={handleOpenLookup}
              disabled={isOpening}
              aria-label={`Select ${label}`}
            >
              {isOpening ? 'Opening\u2026' : (placeholder ?? `Search ${label.toLowerCase()}...`)}
            </Button>
          </div>
        )}
      </Field>
    </div>
  );
};

DataverseLookupField.displayName = 'DataverseLookupField';
