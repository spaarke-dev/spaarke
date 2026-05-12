/**
 * SendEmailStep.tsx
 * Generic email composition step for use in any wizard or multi-step form.
 *
 * Accepts configurable title, subtitle, default subject/body, and an optional
 * regarding entity reference. All domain-specific values are passed via props
 * rather than hardcoded.
 *
 * Layout:
 *   +----------------------------------------------------------------------+
 *   |  {title}                                                              |
 *   |  {subtitle}                                                           |
 *   |                                                                       |
 *   |  {headerContent}  (optional slot for extra controls above the form)   |
 *   |                                                                       |
 *   |  To *      [Search users...                             ]             |
 *   |                                                                       |
 *   |  Subject * [Pre-filled subject                          ]             |
 *   |                                                                       |
 *   |  Message * [Pre-filled body                             ]             |
 *   |                                                                       |
 *   |  {infoNote}                                                           |
 *   +----------------------------------------------------------------------+
 *
 * Constraints:
 *   - Fluent v9: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import type { ILookupItem } from './LookupField';
export interface ISendEmailStepProps {
    /** Title displayed at the top of the step. */
    title: string;
    /** Subtitle / description displayed below the title. */
    subtitle: string;
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
    /** Search function for user lookup (searches systemuser table). */
    onSearchUsers: (query: string) => Promise<ILookupItem[]>;
    /**
     * Logical name of the Dataverse entity this email relates to
     * (e.g. "sprk_matter", "sprk_document"). Stored for reference by the caller.
     */
    regardingEntityType?: string;
    /**
     * GUID of the regarding record. Stored for reference by the caller.
     */
    regardingId?: string;
    /**
     * Optional content rendered between the header and the email form.
     * Useful for domain-specific controls like a "short summary" checkbox.
     */
    headerContent?: React.ReactNode;
    /**
     * Info note displayed below the message field.
     * Defaults to: "This email will be saved as a draft activity."
     */
    infoNote?: string;
    /** Number of rows for the message textarea. Default: 15. */
    messageRows?: number;
}
export declare const SendEmailStep: React.FC<ISendEmailStepProps>;
//# sourceMappingURL=SendEmailStep.d.ts.map