/**
 * LookupField.tsx
 * Reusable search-as-you-type lookup field for entity reference searches.
 *
 * Layout:
 *   ┌───────────────────────────────────────────────┐
 *   │ [Search input: "lit..."]                   [x] │
 *   ├───────────────────────────────────────────────┤
 *   │  Litigation                                    │
 *   │  Licensing                                     │
 *   │  Litigation Support                            │
 *   └───────────────────────────────────────────────┘
 *   — OR —
 *   Selected: [Litigation] [x]
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 *   - Full keyboard support (arrow keys, Enter, Escape)
 */
import * as React from 'react';
import type { ILookupItem } from '../../types/LookupTypes';
export interface ILookupFieldProps {
    /** Field label displayed above the input. */
    label: string;
    /** Whether the field is required. */
    required?: boolean;
    /** Placeholder text for the search input. */
    placeholder?: string;
    /** Currently selected lookup item (or null). */
    value: ILookupItem | null;
    /** Called when the user selects or clears an item. */
    onChange: (item: ILookupItem | null) => void;
    /** Async search function — called with the query string, returns results. */
    onSearch: (query: string) => Promise<ILookupItem[]>;
    /** Optional content rendered after the label (e.g., AI badge). */
    labelExtra?: React.ReactNode;
    /** Minimum characters before search fires. Default: 1. */
    minSearchLength?: number;
}
export declare const LookupField: React.FC<ILookupFieldProps>;
//# sourceMappingURL=LookupField.d.ts.map