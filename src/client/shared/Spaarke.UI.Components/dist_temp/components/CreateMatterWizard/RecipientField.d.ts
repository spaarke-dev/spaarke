/**
 * RecipientField.tsx
 * Hybrid contact-lookup + freeform email entry field with chip list.
 *
 * Used by DraftSummaryStep for "Distribute to" and "CC" fields.
 *
 * Behavior:
 *   - Shows chip list of current recipients (each with dismiss button)
 *   - Input with debounced contact search (300ms)
 *   - Selecting a contact adds as chip (displayName + email from name parse)
 *   - Typing a valid email + Enter adds as manual entry chip
 *   - Duplicate prevention by key (contactId or email)
 *
 * Constraints:
 *   - Fluent v9: Input, Text, Button, Spinner -- ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 */
import * as React from 'react';
import type { ILookupItem } from '../../types/LookupTypes';
export interface IRecipientItem {
    /** Unique key -- contactId for lookup entries, email for manual entries. */
    key: string;
    /** Display name (contact name or email). */
    displayName: string;
    /** Email address. */
    email: string;
    /** Whether this was manually entered (true) or from contact lookup (false). */
    isManual: boolean;
}
export interface IRecipientFieldProps {
    /** Field label. */
    label: string;
    /** Placeholder for the search/input field. */
    placeholder?: string;
    /** Current recipient list (controlled). */
    recipients: IRecipientItem[];
    /** Called when recipients change. */
    onRecipientsChange: (recipients: IRecipientItem[]) => void;
    /** Async search function for contact lookup. */
    onSearch: (query: string) => Promise<ILookupItem[]>;
    /** Minimum characters before search fires. Default: 2. */
    minSearchLength?: number;
}
export declare const RecipientField: React.FC<IRecipientFieldProps>;
//# sourceMappingURL=RecipientField.d.ts.map