/**
 * "Submit corrected invoice" (ebilling-r1 task 095f-2e2b; EB-D-245, design 095f-part2 section 16, option B'): fills one more part of 110's
 * `invoiceActions` slot. For an invoice that is In Review it opens the Create Invoice wizard in Correction mode (the code page, with the
 * invoice id), where the reviewer chooses the corrected PDF, sees the firm invoice number, record and firm of the invoice it replaces
 * (read from the server's correction prefill), and gives the required note. The page refreshes when the wizard closes.
 *
 * The page decides nothing the server owns (ADR-002): it shows the action for an In Review invoice and asks the server's correction
 * prefill first, so a refusal the server already knows (the invoice moved on, is still processing, is gone for this caller, could not be
 * read) is a plain sentence here instead of an empty dialog. The server checks everything again when the corrected PDF is sent
 * (409 `correction_not_allowed`, the uniform 404, the note rules), and the wizard shows those refusals itself.
 *
 * One request at a time: the button is off while the prefill is read and while the wizard is open, so a double click opens one wizard.
 *
 * The words of every refusal are the shared library's (`correctionTargetFromPrefill`, `correctionPrefillErrorMessage`): one copy, the one
 * the wizard itself shows.
 */

import * as React from 'react';
import { Button } from '@fluentui/react-components';
import {
  CORRECTION_UNAVAILABLE_ERROR_CODE,
  InvoiceSubmissionError,
  correctionPrefillErrorMessage,
  correctionTargetFromPrefill,
} from '@spaarke/ui-components';
import type { InvoiceDecisionApi } from '../../services/decisionApi';
import type { ApiFailure, InvoiceReview } from '../../services/invoiceReviewApi';
import { decisionFailureMessage } from '../../services/decisionApi';
import { useReportDirty } from '../useDirtyCheck';

/** What the host's dialog resolves with. `confirmed: false` means the user backed out of the dialog without finishing (nothing to refresh). */
export type CorrectionWizardResult = { confirmed?: boolean } | void | undefined;

/** Opens the Create Invoice wizard code page in Correction mode for one invoice; resolves when the dialog closes. */
export type OpenCorrectionWizard = (invoiceId: string) => Promise<CorrectionWizardResult>;

export interface SubmitCorrectedInvoiceActionProps {
  review: InvoiceReview;
  /** The page's write access (as DecideInvoiceAction): without it the action is not shown. */
  canWrite: boolean;
  invoiceId: string;
  api: InvoiceDecisionApi;
  openCorrectionWizard: OpenCorrectionWizard;
  reload: () => Promise<InvoiceReview | null>;
  /** The plain sentence of a refusal shown by the page (null clears it). */
  onNotice: (message: string | null) => void;
}

/** The sentence for a failed read of the correction prefill, in the shared wizard's words. */
export function correctionReadFailureMessage(f: ApiFailure): string {
  if (f.signInFailed) return decisionFailureMessage(f);
  const err = new InvoiceSubmissionError({
    statusCode: f.status === 0 ? undefined : f.status,
    // The uniform 404 carries a `reasonCode` (which `toFailure` reads as the code), never an `errorCode` of its own: every 404 here is
    // "not available to you, or gone", in the correction's own words (as the wizard's adapter gives it).
    errorCode: f.status === 404 ? CORRECTION_UNAVAILABLE_ERROR_CODE : (f.errorCode ?? undefined),
    // A 404 carries the server's generic "not found" detail, which would win over the correction's own sentence: leave it out for a 404.
    // A 2xx whose body was not a prefill (this client's own "unexpected body" failure) is "could not be read" in the wizard's words too.
    detail: f.status === 404 || (f.status >= 200 && f.status < 300) ? undefined : (f.detail ?? undefined),
  });
  return correctionPrefillErrorMessage(err);
}

export function SubmitCorrectedInvoiceAction({ review, canWrite, invoiceId, api, openCorrectionWizard, reload, onNotice }: SubmitCorrectedInvoiceActionProps) {
  const [busy, setBusy] = React.useState(false);
  useReportDirty(busy); // task 195: the correction in flight (its prefill read, its dialog) is a write in progress for the Console modal's browse guard
  // A ref as well as state: two clicks before React re-renders the disabled button are still one request.
  const busyRef = React.useRef(false);

  const run = React.useCallback(async () => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    onNotice(null);
    try {
      const prefill = await api.getCorrectionPrefill(invoiceId);
      if (!prefill.ok) {
        onNotice(correctionReadFailureMessage(prefill.failure));
        return;
      }
      const launch = correctionTargetFromPrefill(prefill.value);
      if (!launch.ok) {
        onNotice(launch.message);
        return;
      }
      let result: CorrectionWizardResult;
      try {
        result = await openCorrectionWizard(invoiceId);
      } catch {
        onNotice('The corrected-invoice form could not be opened. Try again. Nothing was saved.');
        return;
      }
      // The wizard closed. A submitted correction replaces this invoice once the server has processed it, so the page reads it again;
      // a user who backed out of the dialog (`confirmed: false`) changed nothing.
      if (!result || result.confirmed !== false) await reload();
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }, [api, invoiceId, onNotice, openCorrectionWizard, reload]);

  if (!canWrite || review.header.status !== 'InReview') return null;
  return (
    <Button onClick={() => void run()} disabled={busy} data-testid="submit-corrected-invoice">
      Submit corrected invoice…
    </Button>
  );
}
