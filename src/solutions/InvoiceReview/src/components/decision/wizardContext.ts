/**
 * What the decision wizard's steps share (ebilling-r1 task 111). WizardShell captures its step configs once on mount, so the steps read the
 * live form, review and approval state from this context instead of closing over props.
 */

import * as React from 'react';
import type { IWizardShellHandle } from '@spaarke/ui-components';
import type { AdjustmentMethod, ApprovalState, InvoiceDecisionApi } from '../../services/decisionApi';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import type { InvoiceOutcome } from '../../services/decisionModel';

export interface WizardForm {
  outcome?: InvoiceOutcome;
  /** The reason of a reject or return (required by the server). */
  reason: string;
  /** The adjustment being typed, not yet added. */
  adjMethod: AdjustmentMethod;
  adjValue: string;
  adjReason: string;
  /** '' = no rule. */
  adjRule: string;
}

export const EMPTY_FORM: WizardForm = { reason: '', adjMethod: 'percent', adjValue: '', adjReason: '', adjRule: '' };

export interface WizardCtx {
  form: WizardForm;
  set: (patch: Partial<WizardForm>) => void;
  /** The invoice was decided: the steps show what was recorded and change nothing. */
  readOnly: boolean;
  review: InvoiceReview;
  approval: ApprovalState;
  api: InvoiceDecisionApi;
  invoiceId: string;
  /** Re-read the review (after an adjustment is added or removed); resolves with the review now shown, or null when the re-read failed. */
  reload: () => Promise<InvoiceReview | null>;
  onOutcome: (outcome: InvoiceOutcome, handle: IWizardShellHandle) => void;
}

export const WizardContext = React.createContext<WizardCtx | null>(null);

export function useWiz(): WizardCtx {
  const v = React.useContext(WizardContext);
  if (!v) throw new Error('useWiz needs the decision wizard.');
  return v;
}

/** Unsaved text in the adjustment form: it must be added or cleared before the step is left with Next (Skip discards it). */
export function adjustmentFormDirty(f: WizardForm): boolean {
  return f.adjValue.trim() !== '' || f.adjReason.trim() !== '';
}
