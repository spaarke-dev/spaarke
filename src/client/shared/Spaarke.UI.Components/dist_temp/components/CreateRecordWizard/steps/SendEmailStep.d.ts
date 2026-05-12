/**
 * SendEmailStep.tsx
 * Follow-on step for composing an email to client.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library.
 * Entity-specific form values are no longer referenced — email pre-fill
 * is handled by the CreateRecordWizard via config callbacks.
 *
 * @see CreateRecordWizard — pre-fills emailSubject/emailBody from config
 */
import * as React from 'react';
import type { ILookupItem } from '../../../types/LookupTypes';
export interface ISendEmailStepProps {
    /** Optional step title override (default: "Send Notification Email"). */
    title?: string;
    /** Controlled "To" value (email address string). */
    emailTo: string;
    /** Called when "To" changes. */
    onEmailToChange: (value: string) => void;
    /** Controlled "CC" value (email address string). */
    emailCc?: string;
    /** Called when "CC" changes. */
    onEmailCcChange?: (value: string) => void;
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
export declare const SendEmailStep: React.FC<ISendEmailStepProps>;
//# sourceMappingURL=SendEmailStep.d.ts.map