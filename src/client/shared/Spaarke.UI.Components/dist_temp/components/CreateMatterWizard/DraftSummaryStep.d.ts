/**
 * DraftSummaryStep.tsx
 * Follow-on step for "Draft Summary" in the Create New Matter wizard.
 *
 * On mount: calls streamAiDraftSummary from matterService (stub / BFF).
 * Uses RecipientField for "Distribute to" and "CC" with contact lookup
 * and freeform email entry.
 *
 * Constraints:
 *   - Fluent v9: Card, Textarea, Text, Spinner, Badge
 *   - SparkleRegular for AI indicator
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import { IRecipientItem } from './RecipientField';
import type { ICreateMatterFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
export interface IDraftSummaryStepProps {
    /** Form values from Step 2 -- used to personalise the AI prompt. */
    formValues: ICreateMatterFormState;
    /** Current AI-generated summary text (controlled). */
    summaryText: string;
    /** Called when summary text changes. */
    onSummaryChange: (text: string) => void;
    /** Current "Distribute to" recipients (controlled). */
    recipients: IRecipientItem[];
    /** Called when "Distribute to" recipients change. */
    onRecipientsChange: (recipients: IRecipientItem[]) => void;
    /** Current CC recipients (controlled). */
    ccRecipients: IRecipientItem[];
    /** Called when CC recipients change. */
    onCcRecipientsChange: (recipients: IRecipientItem[]) => void;
    /** Search function for contact lookup. */
    onSearchContacts: (query: string) => Promise<ILookupItem[]>;
    /**
     * Authenticated fetch function for BFF API calls.
     * Required for AI summary streaming. Injected by the host application.
     */
    authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>;
    /**
     * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
     * Required for AI summary streaming. Injected by the host application.
     */
    bffBaseUrl?: string;
}
export declare const DraftSummaryStep: React.FC<IDraftSummaryStepProps>;
//# sourceMappingURL=DraftSummaryStep.d.ts.map