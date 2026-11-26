/**
 * The invoice decision wizard (ebilling-r1 task 111; FR-32, FR-35, D-26 / ADR-050): Approve (with an optional invoice-level adjustment),
 * Reject (final) or Return to firm, in `WizardShell` (modal mode: the wizard preset). Single-field reasons that are not part of a wizard
 * (the blocked-submission Override / Return, the adjustment removal confirm) use `FormModal` elsewhere.
 *
 *   Approve:           Decision → Invoice adjustment (skippable) → Confirm
 *   Reject / Return:   Decision → Reason → Notify (the notice to the firm, task 132ui; skippable) → Confirm
 *
 * The same shell reopens read-only on a decided invoice (the recorded decision, its Decision Record, the adjustments and the reason), with a
 * `StatusBar` carrying the eBilling words through task 109's `statusOverride`. Nothing is computed here (ADR-002, I-1): every amount shown is
 * the server's, every refusal is shown with the server's detail and its code, and a refused call changes nothing on the page.
 *
 * WizardShell keeps each dynamic step's config as registered, so the steps read the live form from `WizardContext` and the step predicates
 * read `live.current` (assigned on each render), never a captured value.
 */

import * as React from 'react';
import { Button, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircle48Filled } from '@fluentui/react-icons';
import {
  StatusBar,
  WizardShell,
  type ConsoleStatusOverride,
  type IWizardShellHandle,
  type IWizardStepConfig,
  type IWizardSuccessConfig,
} from '@spaarke/ui-components';
import {
  decisionFailureMessage,
  isOutcomeUnknown,
  newAdjustmentId,
  type ApprovalState,
  type FirmNotice,
  type FirmNoticeKind,
  type InvoiceDecisionApi,
} from '../../services/decisionApi';
import {
  INVOICE_OUTCOME_LABEL,
  invoiceDecisionRecord,
  outcomeOfApproval,
  type InvoiceOutcome,
} from '../../services/decisionModel';
import { isNoteRefusal, noteArgs, noteProblem, noteRefusalMessage, noteToSend } from '../../services/noteModel';
import {
  isNoticeRefusal,
  noticeBlocker,
  noticeEntryOfLock,
  noticeKindOf,
  noticeLockOf,
  noticeToSend,
  settleNoticeEntries,
  type NoticeEntry,
} from '../../services/noticeModel';
import { money } from '../../services/reviewModel';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import { createDecisionLocks, type DecisionLocks } from './decisionLocks';
import { NotifyStep } from './NotifyStep';
import { useNoticeDrafts } from './useNoticeDrafts';
import { AdjustStep, ConfirmStep, OutcomeStep, ReasonStep } from './steps';
import { EMPTY_FORM, WizardContext, adjustmentFormDirty, type WizardCtx, type WizardForm } from './wizardContext';

const useStyles = makeStyles({
  ok: { color: tokens.colorPaletteGreenForeground1 },
});

const ORDER = ['outcome', 'adjust', 'reason', 'notify', 'confirm'];
const FIXED = new Set(['outcome', 'confirm']);

/** The step id of the notice to the firm (Reject and Return, editable wizard only; skippable). */
const NOTIFY_STEP = 'notify';

export interface InvoiceDecisionWizardProps {
  open: boolean;
  onClose: () => void;
  review: InvoiceReview;
  approval: ApprovalState;
  api: InvoiceDecisionApi;
  invoiceId: string;
  /** Re-read the review after the decision (the page shows the server's state); null when the re-read failed. */
  reload: () => Promise<InvoiceReview | null>;
  /** Re-read the approval state after the decision. */
  refreshApproval: () => Promise<unknown>;
  /**
   * The requests locked until they finish, held at page level (the `ApprovalProvider`'s), so reopening the wizard for the same invoice
   * prefills a decision that came back unsure and keeps it locked. Without it the wizard keeps its own (lost on close).
   */
  locks?: DecisionLocks;
}

function stepIds(outcome: InvoiceOutcome | undefined, hasNotify: boolean): string[] {
  if (outcome === 'approve') return ['outcome', 'adjust', 'confirm'];
  if (outcome === 'reject' || outcome === 'return') return hasNotify ? ['outcome', 'reason', 'notify', 'confirm'] : ['outcome', 'reason', 'confirm'];
  return ['outcome', 'confirm'];
}

const STATUS_TONE: Readonly<Record<InvoiceOutcome, ConsoleStatusOverride['tone']>> = { approve: 'success', reject: 'critical', return: 'warning' };
const STATUS_LABEL: Readonly<Record<InvoiceOutcome, string>> = { approve: 'Approved', reject: 'Rejected', return: 'Returned to firm' };

export function InvoiceDecisionWizard(props: InvoiceDecisionWizardProps) {
  const { open, onClose, review, approval, api, invoiceId, reload, refreshApproval } = props;
  const s = useStyles();
  const ownLocks = React.useRef<DecisionLocks | null>(null);
  if (!ownLocks.current) ownLocks.current = createDecisionLocks();
  const locks = props.locks ?? ownLocks.current;

  // Frozen at mount: the wizard that opened on a pending invoice does not turn read-only under the user when its own decision refreshes the page.
  const [readOnly] = React.useState(() => approval.status !== 'Pending' || review.header.status !== 'InReview');
  const recorded = React.useMemo(() => invoiceDecisionRecord(review), [review]);
  const [form, setForm] = React.useState<WizardForm>(() => {
    if (readOnly) return { ...EMPTY_FORM, outcome: outcomeOfApproval(approval.status), reason: recorded?.reason ?? '' };
    // Review round 1: a decision or an adjustment add that came back unsure is reopened as it was sent, and locked, so the repeat is the same request.
    const w = locks.get('wizard');
    const a = locks.get('adjust');
    const base: WizardForm = { ...EMPTY_FORM, adjId: a?.adjustmentId ?? newAdjustmentId() };
    if (a) {
      Object.assign(base, { adjMethod: a.method, adjValue: a.value, adjReason: a.reason, adjRule: a.rule, adjNote: a.note, adjLocked: true });
    }
    if (w) {
      Object.assign(base, { reason: w.reason, note: w.note, locked: true });
      // Task 132ui: the notice and the send choice as they were sent (no new draft is asked for; it would differ from what the server may have kept).
      const lockKind = noticeKindOf(w.outcome);
      if (lockKind) base.notices = { ...base.notices, [lockKind]: noticeEntryOfLock(w.notice) };
    }
    return { ...base, outcome: w?.outcome ?? (a ? 'approve' : approval.canApprove ? 'approve' : undefined) };
  });
  const set = React.useCallback((patch: Partial<WizardForm>) => setForm(f => ({ ...f, ...patch })), []);

  // Task 132ui: the notice to the firm, one entry per kind in the form. It is written through `setForm` (a functional update), so a draft that
  // arrives after the Notify step was left, or after the other outcome was chosen, lands in its own kind's entry and never in another's.
  const patchNotice = React.useCallback(
    (kind: FirmNoticeKind, patch: Partial<NoticeEntry>, edited = false) =>
      setForm(f => ({
        ...f,
        notices: { ...f.notices, [kind]: { ...f.notices[kind], ...patch } },
        noticeError: edited ? null : f.noticeError,
      })),
    []
  );
  // The draft call, its once-per-kind rule and the failure wording are shared with the Finish form (`useNoticeDrafts`); here the entries live in
  // the form, and an answer lands only on an entry still waiting for it.
  const noticeWriter = React.useMemo(
    () => ({
      patch: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>) => patchNotice(kind, patch),
      settle: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>) =>
        setForm(f => {
          const notices = settleNoticeEntries(f.notices, kind, patch);
          return notices === f.notices ? f : { ...f, notices };
        }),
    }),
    [patchNotice]
  );
  const { loadNotice } = useNoticeDrafts(api, invoiceId, noticeWriter);
  // The shell's handle as the Confirm step last rendered it: Skip is the shell's own, so whether the Notify step was skipped is read from its state.
  const shell = React.useRef<IWizardShellHandle | null>(null);
  const notifySkipped = () => shell.current?.state.steps.some(x => x.id === NOTIFY_STEP && x.status === 'skipped') === true;

  const ctx: WizardCtx = {
    form,
    set,
    loadNotice,
    patchNotice,
    readOnly,
    review,
    approval,
    api,
    invoiceId,
    reload,
    onOutcome: (outcome, handle) => {
      // Review round 2: a decision that came back unsure may already be recorded, and the server finishes a repeat from what it STORED, so the
      // lock entry outlives a change of outcome. The form is editable only while the selected outcome differs from the locked one (the text
      // typed so far stays); coming back to the locked outcome restores what was sent and locks the form again. Only a success, or a reload
      // that shows the decision is done, clears the entry.
      // Review round 1 F1-b: a refusal of one outcome's notice is not shown under another's (nor on Approve's Confirm).
      if (outcome !== live.current.form.outcome) set({ noticeError: null });
      const lock = locks.get('wizard');
      if (lock) {
        if (outcome === lock.outcome) {
          set({ locked: true, reason: lock.reason, note: lock.note });
          const lockKind = noticeKindOf(lock.outcome);
          if (lockKind) patchNotice(lockKind, noticeEntryOfLock(lock.notice));
        } else if (live.current.form.locked) set({ locked: false });
      }
      syncSteps(outcome, handle);
    },
    locks,
  };
  const live = React.useRef<WizardCtx>(ctx);
  live.current = ctx;
  // The Notify step is on the reject and return paths of an editable wizard (not on Approve, never on a decided invoice's read-only view).
  const hasNotify = !readOnly;

  const configFor = React.useCallback(
    (id: string): IWizardStepConfig => {
      switch (id) {
        case 'outcome':
          return {
            id,
            label: 'Decision',
            renderContent: handle => <OutcomeStep handle={handle} />,
            canAdvance: () => live.current.readOnly || (live.current.form.outcome !== undefined && (live.current.form.outcome !== 'approve' || live.current.approval.canApprove) && (live.current.form.outcome !== 'reject' || live.current.approval.firmNeeded !== true)),
          };
        case 'adjust':
          return {
            id,
            label: 'Invoice adjustment',
            renderContent: () => <AdjustStep />,
            // Unsaved text in the adjustment form must be added or cleared; Skip leaves it behind.
            canAdvance: () => live.current.readOnly || !adjustmentFormDirty(live.current.form),
            isSkippable: !readOnly,
          };
        case 'reason':
          return {
            id,
            label: 'Reason',
            renderContent: () => <ReasonStep />,
            canAdvance: () => live.current.readOnly || live.current.form.reason.trim() !== '',
          };
        case 'notify':
          // Next is held while the draft loads and for a blank or over-limit notice with sending on; a failed draft, or sending off, holds nothing.
          return {
            id,
            label: 'Notify firm',
            renderContent: () => <NotifyStep />,
            canAdvance: () => {
              const { form: f, readOnly: ro } = live.current;
              const kind = noticeKindOf(f.outcome);
              return ro || f.locked || !kind || noticeBlocker(f.notices[kind]) === null;
            },
            isSkippable: true,
          };
        default:
          // A note over the limit holds Finish off (the Note box says why); everything else about the note is the server's to refuse.
          return {
            id: 'confirm',
            label: readOnly ? 'Recorded decision' : 'Confirm',
            renderContent: handle => {
              shell.current = handle;
              return <ConfirmStep notifySkipped={notifySkipped()} />;
            },
            canAdvance: () => live.current.readOnly || noteProblem(live.current.form.note) === null,
          };
      }
    },
    // The notify slot and read-only state are fixed for the life of one opened wizard.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  const syncSteps = (outcome: InvoiceOutcome, handle: IWizardShellHandle) => {
    set({ outcome });
    const want = stepIds(outcome, hasNotify);
    const have = handle.state.steps.map(x => x.id);
    for (const id of have) if (!FIXED.has(id) && !want.includes(id)) handle.removeDynamicStep(id);
    for (const id of want) if (!have.includes(id)) handle.addDynamicStep(configFor(id), ORDER);
  };

  // The steps the shell starts with (read once on mount by the shell): the whole recorded path when read-only, else for the preselected outcome.
  const initialSteps = React.useMemo(
    () => stepIds(readOnly ? outcomeOfApproval(approval.status) : form.outcome, hasNotify).map(configFor),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  const onFinish = async (): Promise<IWizardSuccessConfig | void> => {
    const { form: f, api: a, invoiceId: id } = live.current;
    const outcome = f.outcome;
    if (!outcome) throw new Error('Choose a decision first.');
    // The note is optional and the same text on every send of this decision (the form keeps it across a failure, and a decision saved in part
    // locks it): blank or whitespace-only sends no key, and a note over the limit never leaves the page (Confirm holds Finish off for it).
    const note = noteArgs(f.note);
    // Task 132ui: the notice to the firm goes with a reject or return only when the reviewer sent it on purpose: a draft loaded, "Send this
    // notice" on, the step not skipped. Otherwise neither notice field is in the request (no empty strings) and the firm gets no email.
    const noticeKind = noticeKindOf(outcome);
    const entry = noticeKind ? f.notices[noticeKind] : undefined;
    const skipped = notifySkipped();
    if (entry && !skipped && !f.locked && noticeBlocker(entry) === 'invalid') {
      throw new Error('The notice to the firm needs a subject and a message within the limits. Go Back to the Notify step to fix it, or turn it off.');
    }
    const notice: FirmNotice | null = noticeKind ? noticeToSend(entry, { skipped, locked: f.locked }) : null;
    const reason = f.reason.trim();
    // With no notice the call is exactly the old one (no trailing undefined arguments).
    const result =
      outcome === 'approve'
        ? await a.approve(id, ...note)
        : outcome === 'reject'
          ? await (notice ? a.reject(id, reason, noteToSend(f.note), notice) : a.reject(id, reason, ...note))
          : await (notice ? a.returnToFirm(id, reason, noteToSend(f.note), notice) : a.returnToFirm(id, reason, ...note));
    if (!result.ok) {
      // The server refused the note itself (400): say so under the Note box, and only there (review round 1: the shell's error bar did not
      // repeat it well). The shell stays open on this step because it was given `stayOpenOnFinish` and nothing is returned.
      if (isNoteRefusal(result.failure)) {
        set({ noteError: noteRefusalMessage(result.failure) });
        return;
      }
      // Task 132ui: the server refused the notice (400 `ebilling.notice_refused`, before it decided anything): shown under the notice fields,
      // and on this step, which stays open. The reviewer edits the notice (or turns it off) and finishes again; nothing was decided.
      if (isNoticeRefusal(result.failure)) {
        // Review round 2: the route answers this before the service is called, so it proves this request decided nothing. A repeat that was
        // locked after an unsure answer is therefore not a request to repeat any more: drop the lock (page level and form) so the reviewer can
        // edit the notice or turn it off. The reason and the note stay as typed, now editable.
        if (f.locked) locks.clear('wizard');
        set({ noticeError: decisionFailureMessage(result.failure), ...(f.locked ? { locked: false } : {}) });
        return;
      }
      // Saved in part, or no answer at all: the repeat must be the same request, so the reason and the note are locked until it finishes.
      // The lock is also kept at page level, so closing and reopening the wizard does not lose it.
      if (isOutcomeUnknown(result.failure)) {
        set({ locked: true });
        const noticeLock = noticeKind ? noticeLockOf(entry, notice) : undefined;
        locks.set('wizard', {
          outcome,
          reason: outcome === 'approve' ? '' : f.reason.trim(),
          note: f.note,
          // The notice and the send choice as this request carried them, so the repeat is the same request.
          ...(noticeLock ? { notice: noticeLock } : {}),
        });
        // The form's own entry becomes the same: what was sent (or not, after Skip or a draft that never loaded), whatever Skip says on the repeat.
        if (noticeKind) patchNotice(noticeKind, noticeEntryOfLock(noticeLock));
      }
      // Part of the change may be saved (503 incomplete, or no answer): show the server's state, then the message tells the user to repeat. A 409
      // (already decided, approval changed, corrections pending) means the page and the approval are out of date: re-read both so
      // the page shows what the server holds. Any other refusal (403, a validation code) changes nothing; the message carries the
      // server's detail and its code.
      if (isOutcomeUnknown(result.failure) || result.failure.status === 409) await Promise.all([refreshApproval(), reload()]);
      throw new Error(decisionFailureMessage(result.failure));
    }
    locks.clear('wizard');
    await Promise.all([refreshApproval(), reload()]);
    const value = result.value as { outcome: string; state?: ApprovalState | null };
    const already = value.outcome === 'alreadyApproved' || value.outcome === 'alreadyDecided';
    const amount = outcome === 'approve' ? (value.state?.approvedAmount ?? null) : null;
    const title = already ? `Already ${STATUS_LABEL[outcome].toLowerCase()}` : STATUS_LABEL[outcome];
    return {
      icon: <CheckmarkCircle48Filled className={s.ok} />,
      title,
      body: already
        ? 'This invoice already had this decision; no second Decision Record was written.'
        : outcome === 'approve'
          ? `${amount !== null ? `Approved total ${money(amount)}. ` : ''}One Decision Record names you and lists the line decisions. The invoice is now read-only.`
          : 'One Decision Record names you. The invoice is now closed and read-only.',
      actions: (
        <Button appearance="primary" onClick={onClose} data-testid="wizard-done">
          Close
        </Button>
      ),
    };
  };

  const finishLabel =
    form.outcome === 'approve'
      ? `Approve ${money(review.totals.approved)} & record`
      : form.outcome === 'return'
        ? 'Return to firm & record'
        : form.outcome === 'reject'
          ? 'Reject & record'
          : 'Finish';

  const outcome = form.outcome;
  const statusBar = readOnly && outcome ? (
    <StatusBar
      state="Decided"
      statusOverride={{ label: STATUS_LABEL[outcome], tone: STATUS_TONE[outcome], description: `${INVOICE_OUTCOME_LABEL[outcome]} is recorded on this invoice.` }}
      recordId={recorded?.number ?? undefined}
      decidedBy={approval.decidedBy ?? recorded?.decidedBy?.name ?? null}
      decidedOn={approval.decidedOn ?? recorded?.decidedOn ?? null}
      reason={outcome === 'approve' ? null : (recorded?.reason ?? null)}
    />
  ) : undefined;

  // The provider sits outside the shell: the shell renders the steps itself (inside a portal, which keeps React context).
  return (
    <WizardContext.Provider value={ctx}>
      <WizardShell
        open={open}
        title={readOnly ? 'Invoice decision' : 'Decide invoice'}
        steps={initialSteps}
        onClose={onClose}
        onFinish={onFinish}
        // A refused note is shown under the Note box and nothing is returned: the wizard stays on the Confirm step.
        stayOpenOnFinish
        finishLabel={finishLabel}
        finishingLabel="Recording…"
        showSkippedSteps
        statusBar={statusBar}
        footer={
          readOnly
            ? c => ({
                start: (
                  <Button appearance="secondary" onClick={c.close} data-testid="wizard-close">
                    Close
                  </Button>
                ),
                end: (
                  <>
                    {!c.isFirstStep && (
                      <Button appearance="secondary" onClick={c.goBack}>
                        Back
                      </Button>
                    )}
                    {!c.isLastStep && (
                      <Button appearance="primary" onClick={c.goNext}>
                        Next
                      </Button>
                    )}
                  </>
                ),
              })
            : undefined
        }
      />
    </WizardContext.Provider>
  );
}
