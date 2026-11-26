/**
 * What the decision wizard's steps share (ebilling-r1 task 111). WizardShell captures its step configs once on mount, so the steps read the
 * live form, review and approval state from this context instead of closing over props.
 */

import * as React from 'react';
import type { IWizardShellHandle } from '@spaarke/ui-components';
import type { AdjustmentMethod, ApprovalState, FirmNoticeKind, InvoiceDecisionApi } from '../../services/decisionApi';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import type { InvoiceOutcome } from '../../services/decisionModel';
import { EMPTY_NOTICES, type NoticeEntries, type NoticeEntry } from '../../services/noticeModel';
import type { DecisionLocks } from './decisionLocks';

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
  /** Task 095f-2ui: the optional note of the adjustment being typed. Blank is left out of the request. */
  adjNote: string;
  /** Task 095f-2ui: the optional note of the approve, reject or return decision (the Confirm step). Kept across a failed send. */
  note: string;
  /** The server refused the note (400 `ebilling.note_too_long` / `note_invalid_characters`): its sentence, shown under the Note box until the note is edited. */
  noteError: string | null;
  /** Task 132ui: the notice to the firm for each kind (Reject asks `InvoiceRejected`, Return `ReturnedToFirm`); each keeps its own draft, edits and send choice. */
  notices: NoticeEntries;
  /** Task 132ui: the server refused the notice (400 `ebilling.notice_refused`): its sentence, shown under the notice fields until the notice is edited. */
  noticeError: string | null;
  /**
   * A decision that was saved in part (503 incomplete), or that got no answer at all (status 0), is repeated with the SAME request: the server
   * keeps the first note (reject and return drop an edited one on a repeat without a word), so the reason and the note are read-only from then
   * on, until the decision finishes or the outcome is changed (a different outcome is a new request). Review round 1: the reason is locked too.
   */
  locked: boolean;
  /** Review round 1: the id of the adjustment being typed; kept across steps and openings so a repeat after an unsure add files it once. */
  adjId: string;
  /** Review round 1: the adjustment add came back unsure (503, or no answer): its form is read-only and the button says Repeat. */
  adjLocked: boolean;
}

export const EMPTY_FORM: WizardForm = {
  reason: '',
  adjMethod: 'percent',
  adjValue: '',
  adjReason: '',
  adjRule: '',
  adjNote: '',
  note: '',
  noteError: null,
  notices: EMPTY_NOTICES,
  noticeError: null,
  locked: false,
  // The wizard gives each opening its own id (or the locked one); an id here would be shared by every opening.
  adjId: '',
  adjLocked: false,
};

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
  /** Task 132ui: ask for the draft of a kind. Once per kind per opened wizard; `retry` asks again after a failure. */
  loadNotice: (kind: FirmNoticeKind, retry?: boolean) => void;
  /** Task 132ui: change one kind's notice. A reviewer's edit (`edited`) also drops the server's refusal of the old text. */
  patchNotice: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>, edited?: boolean) => void;
  /** The requests locked until they finish (page level: they survive closing and reopening the wizard). */
  locks: DecisionLocks;
}

export const WizardContext = React.createContext<WizardCtx | null>(null);

export function useWiz(): WizardCtx {
  const v = React.useContext(WizardContext);
  if (!v) throw new Error('useWiz needs the decision wizard.');
  return v;
}

/** Unsaved text in the adjustment form: it must be added or cleared before the step is left with Next (Skip discards it). */
export function adjustmentFormDirty(f: WizardForm): boolean {
  return f.adjValue.trim() !== '' || f.adjReason.trim() !== '' || f.adjNote.trim() !== '';
}
