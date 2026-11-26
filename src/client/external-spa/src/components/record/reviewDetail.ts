/**
 * reviewDetail — the one gate for an invoice's Review detail (`sprk_invoice.sprk_reviewdetail`, ebilling-r1 082c), shared by
 * every place the SPA shows it (spaarke-SPA-external-access-platform-r4 task 020, FR-11).
 *
 * BINDING GATE (uac-r2 condition, 2026-11-04). The value, or a line explaining it, is shown ONLY while the invoice's RAW
 * `statuscode` is 1 (In Review; the server's `InvoiceStatusCodes.InReview`). Any other status renders blank, whatever the
 * column carries. The gate reads the raw number, never the display label ("In Review" is a label an owner can rename). It is
 * defence in depth: the BFF also returns the column as null unless the row is In Review (`ExternalInvoiceReviewDetail`).
 *
 * RAW VALUE SHAPE. Rows read through the module fetch seam carry an option-set column as the SDK object
 * `{ "value": 1, "extensionData": null }`, not a bare number (the typed route DTOs and tests may pass a number or a digit
 * string). Both the status and the detail are read through `rawWholeNumberOf` (`utils/rawWholeNumber`), which accepts all
 * three forms and returns null for anything else (a label, a non-integer, another object), so the gate stays fail-closed.
 *
 * THE MAP (design §7, owner-approved 2026-11-05, verbatim; ebilling's option values are never reused):
 *   659490000  Waiting for billing terms              (+ explanation)
 *   659490001  Billing terms could not be checked     (+ explanation)
 *   659490002  Waiting for the firm to be confirmed   (+ explanation)
 *   blank, while In Review                             -> only the line "The client is reviewing this invoice."
 *   any other value                                    -> blank. The client never derives a priority or a new value.
 *
 * FOUR RENDER POINTS, ONE FUNCTION. The Invoices grid column (`InvoicesWidget`), the record's Invoices section, the Invoice
 * header's status-fact secondary line and the Information field all call {@link reviewDetailOf} / {@link reviewDetailOfRow}.
 * None re-implements the gate. "Your access" never reads this field.
 *
 * Pure: no React, no I/O, no BFF import.
 */
import { rawWholeNumberOf } from '../../utils/rawWholeNumber';
import type { DetailFieldDisplay } from '../detail/RecordDetailFields';

/** The column the gate reads (on the invoice module's `ReadableColumns`; ebilling owns that entry). */
export const REVIEW_DETAIL_COLUMN = 'sprk_reviewdetail';

/** The invoice `statuscode` for "In Review" (D-41: the existing value 1, whose old label was "Active"). */
export const IN_REVIEW_STATUSCODE = 1;

/** The line shown for an In Review invoice that carries no review detail. */
export const BLANK_IN_REVIEW_EXPLANATION = 'The client is reviewing this invoice.';

/** What a render point shows for a gated row. */
export interface ReviewDetailDisplay {
  /**
   * The short label (the option's name) for the grid cell, the Invoices list line, the header's secondary line and the
   * Information value. `null` for an In Review invoice with no review detail: there is no label, only the line below.
   */
  text: string | null;
  /** The fixed explanation line (the Information field's second line). Never empty. */
  explanation: string;
}

const OPTIONS: Readonly<Record<number, { text: string; explanation: string }>> = {
  659490000: {
    text: 'Waiting for billing terms',
    explanation:
      "This invoice has not been rejected. It is on hold for review because no billing terms are set up for some of its work dates on this record. If the record and the work dates are right, you don't need to do anything; the client will follow up.",
  },
  659490001: {
    text: 'Billing terms could not be checked',
    explanation:
      "This invoice has not been rejected. It is on hold for review because its billing terms could not be checked. You don't need to do anything; the client will follow up.",
  },
  659490002: {
    text: 'Waiting for the firm to be confirmed',
    explanation:
      "This invoice has not been rejected. It is on hold for review until the client confirms which firm sent it. You don't need to do anything; the client will follow up.",
  },
};

/**
 * The review detail to show for an invoice with this raw `statuscode` and raw `sprk_reviewdetail`, or `null` (blank).
 * Blank for: any status other than 1 (including a missing or unreadable one: fail closed), and an unknown value.
 */
export function reviewDetailOf(statuscode: unknown, value: unknown): ReviewDetailDisplay | null {
  if (rawWholeNumberOf(statuscode) !== IN_REVIEW_STATUSCODE) return null;
  if (value === null || value === undefined || value === '') {
    return { text: null, explanation: BLANK_IN_REVIEW_EXPLANATION };
  }
  const option = OPTIONS[rawWholeNumberOf(value) ?? -1];
  return option ? { text: option.text, explanation: option.explanation } : null;
}

/** {@link reviewDetailOf} for a read row (the grid's row, a list row or the record): reads the two raw columns itself. */
export function reviewDetailOfRow(row: Readonly<Record<string, unknown>>): ReviewDetailDisplay | null {
  return reviewDetailOf(row.statuscode, row[REVIEW_DETAIL_COLUMN]);
}

/** The cell text for the Invoices grid column: the label, or empty (blank) when there is none to show. */
export function reviewDetailCellText(row: Readonly<Record<string, unknown>>): string {
  return reviewDetailOfRow(row)?.text ?? '';
}

/** The header status-fact's secondary line: the label, or `null` (no line). */
export function reviewDetailSecondaryLine(row: Readonly<Record<string, unknown>>): string | null {
  return reviewDetailOfRow(row)?.text ?? null;
}

/** The Information field: the label (if any) and the explanation line under it; `null` hides the row. */
export function reviewDetailFieldDisplay(row: Readonly<Record<string, unknown>>): DetailFieldDisplay | null {
  const shown = reviewDetailOfRow(row);
  return shown ? { text: shown.text ?? undefined, note: shown.explanation } : null;
}
