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
 *   entityType="sprk_mattertype_ref"
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
import type { ILookupItem } from '../../types/LookupTypes';
import type { INavigationService } from '../../types/serviceInterfaces';
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
export declare const DataverseLookupField: React.FC<IDataverseLookupFieldProps>;
//# sourceMappingURL=DataverseLookupField.d.ts.map