/**
 * DraftSummaryStep.tsx
 * Follow-on step for AI-generated summary with recipient distribution.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library.
 * The AI fetch function is provided via props (no entity-specific imports).
 *
 * @see CreateRecordWizard — wires the fetchAiSummary callback from config
 */
import * as React from 'react';
import type { IRecipientItem } from '../types';
import type { ILookupItem } from '../../../types/LookupTypes';
export interface IDraftSummaryStepProps {
    /** Current AI-generated or user-edited summary text (controlled). */
    summaryText: string;
    /** Called when summary text changes. */
    onSummaryChange: (text: string) => void;
    /** "Distribute to" recipients (controlled). */
    recipients: IRecipientItem[];
    /** Called when "Distribute to" recipients change. */
    onRecipientsChange: (recipients: IRecipientItem[]) => void;
    /** CC recipients (controlled). */
    ccRecipients: IRecipientItem[];
    /** Called when CC recipients change. */
    onCcRecipientsChange: (recipients: IRecipientItem[]) => void;
    /** Search function for contact lookup. */
    onSearchContacts: (query: string) => Promise<ILookupItem[]>;
    /**
     * Optional async function to fetch AI draft summary.
     * If not provided, the step shows a manual-entry textarea immediately.
     */
    fetchAiSummary?: () => Promise<{
        summary: string;
    }>;
}
export declare const DraftSummaryStep: React.FC<IDraftSummaryStepProps>;
//# sourceMappingURL=DraftSummaryStep.d.ts.map