/**
 * SendEmailDialog.tsx
 * Reusable email composition dialog with user lookup, subject, and body fields.
 *
 * Fully callback-based — consumer provides search and send implementations.
 * No service dependencies; works in both PCF controls and Code Page solutions.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  Email Document                                              [X]    │
 *   │                                                                     │
 *   │  To *      [Search users...                             ]           │
 *   │                                                                     │
 *   │  Subject * [Document: Contract Agreement                ]           │
 *   │                                                                     │
 *   │  Message * [Dear Colleague,                              ]          │
 *   │            [Please find the following document...]                  │
 *   │                                                                     │
 *   │                                     [Cancel]  [Send]               │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Constraints:
 *   - Fluent v9: Dialog, Input, Textarea, Field, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 */
import * as React from 'react';
import type { ILookupItem } from '../../types/LookupTypes';
/** Payload delivered to the onSend callback. */
export interface ISendEmailPayload {
    /** Selected recipient. */
    to: ILookupItem;
    /** Email subject line. */
    subject: string;
    /** Email body text. */
    body: string;
}
/** Props for SendEmailDialog. */
export interface ISendEmailDialogProps {
    /** Whether the dialog is open. */
    open: boolean;
    /** Called when the dialog should close. */
    onClose: () => void;
    /** Pre-populated subject line. */
    defaultSubject?: string;
    /** Pre-populated email body. */
    defaultBody?: string;
    /** Async user search for the To field. */
    onSearchUsers: (query: string) => Promise<ILookupItem[]>;
    /** Called when user clicks Send. Consumer handles the BFF call. */
    onSend: (payload: ISendEmailPayload) => Promise<void>;
}
export declare const SendEmailDialog: React.FC<ISendEmailDialogProps>;
//# sourceMappingURL=SendEmailDialog.d.ts.map