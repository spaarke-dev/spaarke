/**
 * RecipientField.tsx
 * Hybrid contact-lookup + freeform email entry field with chip list.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library since
 * this component is entity-agnostic — it uses search callbacks.
 *
 * @see DraftSummaryStep — primary consumer
 */
import * as React from 'react';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { IRecipientItem } from '../types';
export interface IRecipientFieldProps {
    label: string;
    placeholder?: string;
    recipients: IRecipientItem[];
    onRecipientsChange: (recipients: IRecipientItem[]) => void;
    onSearch: (query: string) => Promise<ILookupItem[]>;
    minSearchLength?: number;
}
export declare const RecipientField: React.FC<IRecipientFieldProps>;
//# sourceMappingURL=RecipientField.d.ts.map