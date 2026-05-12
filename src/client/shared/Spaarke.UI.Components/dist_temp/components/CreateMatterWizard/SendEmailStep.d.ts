/**
 * SendEmailStep.tsx
 * Follow-on step for "Send Notification Email" in the Create New Matter wizard.
 *
 * "To" field uses LookupField searching the systemuser table.
 * Subject is pre-filled: "New Matter: {matterName}"
 * Body uses a default template including matter type + practice area.
 *
 * Constraints:
 *   - Fluent v9: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import type { ICreateMatterFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
export interface ISendEmailStepProps {
    /** Form values from Step 2 -- used to pre-fill subject/body. */
    formValues: ICreateMatterFormState;
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
}
export declare function buildDefaultEmailSubject(matterName: string): string;
export declare function buildDefaultEmailBody(form: ICreateMatterFormState): string;
export declare const SendEmailStep: React.FC<ISendEmailStepProps>;
//# sourceMappingURL=SendEmailStep.d.ts.map