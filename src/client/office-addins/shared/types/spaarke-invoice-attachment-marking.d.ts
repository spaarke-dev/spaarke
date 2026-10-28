/**
 * Ambient TYPE shim for `@spaarke/communication-components/invoice-attachment-marking` (spaarkeai-word-add-in-r1
 * task 133).
 *
 * RUNTIME: the specifier resolves to the REAL shared source — ebilling-r1 task 118a's `InvoiceAttachmentMarking`
 * (#2342), `src/client/shared/Spaarke.Communication.Components/src/components/InvoiceAttachmentMarking/index.ts`, the
 * package's own `./invoice-attachment-marking` export (the component plus its pure rules; Fluent v9 + React, no Xrm) —
 * through exact aliases in `webpack.config.js` (bundle) and `jest.config.js` (tests). Never the
 * `@spaarke/communication-components` barrel.
 *
 * TYPES ONLY, for the same reason as `spaarke-send-email-pane.d.ts`: this package's `tsc` cannot type-check the shared
 * source in place — the shared package has no node_modules of its own (in CI or here), so `react` and Fluent do not
 * resolve from its folder, and CI's production typecheck would count every resulting error as this package's debt.
 * `InvoiceView.test.tsx` renders the REAL component and its rules through the alias, so a declaration here that the
 * shared code no longer honours fails a test.
 *
 * SCOPE: the folder's `index.ts` exports, copied verbatim from `invoiceMarking.ts` and `InvoiceAttachmentMarking.tsx`.
 */
declare module '@spaarke/communication-components/invoice-attachment-marking' {
  import type * as React from 'react';

  export type InvoiceAttachmentRole = 'invoice' | 'supporting' | 'skip';

  export interface MarkableAttachment {
    /** Opaque: sprk_document id in the email UI, Office attachment id in the add-in. */
    id: string;
    fileName: string;
    contentType?: string;
    sizeBytes?: number;
    suggestedInvoice?: boolean;
    disabledReason?: string;
  }

  type Mode = 'new' | 'correction' | 'supporting-only';

  /** Pre-select an invoice only when exactly one candidate exists; everything else is 'supporting' (or 'skip' when disabled). */
  export function defaultInvoiceMarking(
    attachments: readonly MarkableAttachment[],
    mode: Mode
  ): Record<string, InvoiceAttachmentRole>;

  /**
   * Validate a marking. Counts and mode are checked from `value` alone; pass `attachments` (optional third argument)
   * to also refuse a disabled attachment marked anything but 'skip' ('disabled-attachment-marked') and a non-PDF
   * marked 'invoice' ('invoice-not-allowed').
   *
   * `value` must hold every attachment id: `defaultInvoiceMarking` does.
   */
  export function validateInvoiceMarking(
    value: Record<string, InvoiceAttachmentRole>,
    mode: Mode,
    attachments?: readonly MarkableAttachment[]
  ):
    | { ok: true }
    | {
        ok: false;
        reason: 'no-invoice' | 'more-than-one-invoice' | 'invoice-not-allowed' | 'disabled-attachment-marked';
      };

  export interface InvoiceAttachmentMarkingProps {
    attachments: MarkableAttachment[];
    value: Record<string, InvoiceAttachmentRole>;
    onChange: (next: Record<string, InvoiceAttachmentRole>) => void;
    mode: 'new' | 'correction' | 'supporting-only';
    disabled?: boolean;
  }

  export const InvoiceAttachmentMarking: React.FC<InvoiceAttachmentMarkingProps>;
}
