/**
 * SummarizeSendEmailStep.tsx
 * Follow-on step for "Send Email" in the Summarize Files wizard.
 *
 * Adapts the CreateMatter/SendEmailStep pattern:
 *   - To (LookupField -> searchUsersAsLookup)
 *   - Subject (Input)
 *   - Body (Textarea, 15 rows, pre-filled with summary)
 *   - "Include only short summary" checkbox at top
 */
import * as React from 'react';
import type { ILookupItem } from '../../types/LookupTypes';
export interface ISummarizeSendEmailStepProps {
    /** Controlled "To" value (email address string). */
    emailTo: string;
    /** Called when "To" changes. */
    onEmailToChange: (value: string) => void;
    /** Controlled subject value. */
    emailSubject: string;
    /** Called when subject changes. */
    onEmailSubjectChange: (value: string) => void;
    /** Controlled body value. */
    emailBody: string;
    /** Called when body changes. */
    onEmailBodyChange: (value: string) => void;
    /** Search function for user lookup. */
    onSearchUsers: (query: string) => Promise<ILookupItem[]>;
    /** Whether to include only the short summary. */
    includeShortSummary: boolean;
    /** Toggle for short summary. */
    onIncludeShortSummaryChange: (checked: boolean) => void;
}
export declare function buildSummaryEmailSubject(): string;
export declare function buildSummaryEmailBody(summary: string, shortSummary: string, useShort: boolean): string;
export declare const SummarizeSendEmailStep: React.FC<ISummarizeSendEmailStepProps>;
//# sourceMappingURL=SummarizeSendEmailStep.d.ts.map