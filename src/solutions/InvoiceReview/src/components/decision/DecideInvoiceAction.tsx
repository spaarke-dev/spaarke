/**
 * "Decide invoice…" / "View decision" (ebilling-r1 task 111; FR-35): fills 110's `invoiceActions` slot. The button shows only when the
 * server's own signals say it can do something (`decisionAccess`): an eligible approver on a pending, not-held invoice gets "Decide invoice…";
 * a decided invoice gets "View decision" (the wizard reopens read-only); everyone else sees nothing. The server stays authoritative: a
 * refused decision is shown in the wizard with its code and nothing on the page changes.
 *
 * ISS-032 (#2883): `IncompleteDecisionBanner` fills the `banners` slot when the server says the invoice's reject, return or approve stopped
 * after its status transition, before its Decision Record, and that this caller finishes it (`incompleteDecision`). It says so and offers
 * "Finish decision…", which sends the SAME repeat the open wizard would send (approve, reject or return), so the server's own repeat rules
 * apply. While it shows, the action here shows nothing: the read-only view would show a decision that is not on record.
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  Field,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import { decisionFailureMessage, isOutcomeUnknown, type FirmNoticeKind } from '../../services/decisionApi';
import {
  decisionAccess,
  finishDecisionIntro,
  incompleteDecisionText,
  incompleteOutcome,
  isRecordedStage,
  recordedReason,
  type InvoiceOutcome,
} from '../../services/decisionModel';
import { isNoteRefusal, noteArgs, noteProblem, noteRefusalMessage, noteToSend } from '../../services/noteModel';
import {
  EMPTY_NOTICES,
  isNoticeRefusal,
  noticeBlocker,
  noticeEntryOfLock,
  noticeKindOf,
  noticeLockOf,
  noticeToSend,
  settleNoticeEntries,
  type NoticeEntries,
  type NoticeEntry,
} from '../../services/noticeModel';
import { NoteField } from '../NoteField';
import { InvoiceDecisionWizard } from './InvoiceDecisionWizard';
import { NotifyStep } from './NotifyStep';
import { useNoticeDrafts } from './useNoticeDrafts';
import { useReportDirty } from '../useDirtyCheck';
import { EMPTY_FORM, WizardContext, type WizardCtx } from './wizardContext';
import { useApprovalSync } from './AwaitingApprovers';
import { useApproval } from './ApprovalProvider';
import type { WizardNoticeLock } from './decisionLocks';
import { REASON_MAX } from './steps';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM },
  muted: { color: tokens.colorNeutralForeground3 },
});

export interface DecideInvoiceActionProps {
  review: InvoiceReview;
  canWrite: boolean;
  reload: () => Promise<InvoiceReview | null>;
}

export function DecideInvoiceAction({ review, canWrite, reload }: DecideInvoiceActionProps) {
  const { state, api, invoiceId, refresh, locks } = useApproval();
  useApprovalSync(review);
  // The server's state shows the decision is done: a request kept locked from an unsure send has nothing left to repeat. ISS-032: not while
  // the server says the decision did not finish; the Finish banner repeats that same request.
  const decided = !!state && (state.status !== 'Pending' || review.header.status !== 'InReview');
  const stranded = incompleteOutcome(state) !== null;
  React.useEffect(() => {
    if (decided && !stranded) {
      locks.clear('wizard');
      locks.clear('adjust');
    }
  }, [decided, stranded, locks]);
  // The wizard is closed with `open={false}`, never unmounted while open, which would leave the page aria-hidden (#2658).
  // `seq` keys each opening and changes only while closed, so the form, the steps and the read-only choice start fresh.
  const [wizard, setWizard] = React.useState<{ open: boolean; seq: number } | null>(null);
  const access = decisionAccess(review, state, canWrite);
  // Task 195: an EDITABLE decision wizard being open is work in progress (its notes, reasons and notices are in it), so the Console modal
  // asks before it browses away from it. The read-only view ("View decision") holds nothing to lose, so it never prompts.
  useReportDirty(wizard?.open === true && access === 'decide');

  const refreshApproval = React.useCallback(() => refresh(), [refresh]);
  const openWizard = React.useCallback(() => {
    setWizard(prev => (prev?.open ? prev : { open: true, seq: (prev?.seq ?? 0) + 1 }));
  }, []);
  const closeWizard = React.useCallback(() => {
    setWizard(prev => (prev ? { ...prev, open: false } : prev));
  }, []);

  return (
    <>
      {access === 'decide' && (
        <Button appearance="primary" onClick={openWizard} data-testid="decide-invoice">
          Decide invoice…
        </Button>
      )}
      {access === 'view' && (
        <Button onClick={openWizard} data-testid="view-decision">
          View decision
        </Button>
      )}
      {/* Keyed per opening: the form, the steps and the read-only choice start fresh each time. */}
      {wizard && state && (
        <InvoiceDecisionWizard
          key={wizard.seq}
          open={wizard.open}
          onClose={closeWizard}
          review={review}
          approval={state}
          api={api}
          invoiceId={invoiceId}
          reload={reload}
          refreshApproval={refreshApproval}
          locks={locks}
        />
      )}
    </>
  );
}

export interface IncompleteDecisionBannerProps {
  /** The review the page shows: a recorded reject or return's reason comes from its invoice-level Decision Record. */
  review: InvoiceReview;
  reload: () => Promise<InvoiceReview | null>;
}

/**
 * ISS-032: the stranded-decision banner and its Finish form (`FormModal`, a single form as BlockedBanner's Finish as operator, ADR-050).
 *
 * Shown only on the server's word (`incompleteDecision`, which it gives only to the caller whose repeat finishes the decision). Finish sends
 * the repeat once (the button is disabled while it is out) and re-reads the approval and the review: the banner goes with the hint and
 * "View decision" comes back. A reject or return asks for its reason again (the server keeps it only in the record that was not written);
 * when this page still holds the request it sent (an unsure send in this session, `locks`), that request is repeated as it was. A refusal or
 * a 503 is shown in the form with its code, and the banner keeps its Finish button while the server still reports the decision. The form
 * is not tied to the banner: when a re-read shows the hint gone, an open form stays open with its error.
 *
 * ISS-041 (#2930): the notice to the firm (task 132ui) lives only in the page's memory (`locks`). When this form finishes a reject or return and no
 * notice is kept there (the page was reloaded, or the first attempt came from another tab), the repeat would decide the invoice and the server
 * would send the firm no email, so the form offers the wizard's own Notify step (`NotifyStep`, with the same draft call, limits and screening).
 * Its switch sends no notice. The repeat then carries the confirmed notice exactly as the wizard's first call would. With a kept notice, and for
 * an approve, the form is as before.
 *
 * Review round 1 F4: when the record exists and only its follow-up steps did not finish (`failedStep` `link`), nothing is asked. The server
 * finishes from the stored record and ignores the request's reason and note, but a reject or return still needs a non-empty reason, so the
 * form sends the reason the record holds (`recordedReason`, from the review's invoice-level Decision Record), read-only, and no note. When the
 * review does not carry it, Finish is disabled and the form says to reload; the user is never asked to type it again.
 */
export function IncompleteDecisionBanner({ review, reload }: IncompleteDecisionBannerProps) {
  const s = useStyles();
  const { state, api, invoiceId, refresh, locks } = useApproval();
  const stranded = incompleteOutcome(state);
  const recordedStage = isRecordedStage(state);

  // The form, kept after it closes with `open={false}` (never unmounted while open, #2658). `seq` keys each opening; `outcome` and
  // `recorded` are what the hint said when it opened, so a re-read under an open form does not change what it sends.
  // `offerNotice` (ISS-041): a reject or return with no notice kept in this page's memory (the page was reloaded, or another tab sent the first
  // attempt) offers the wizard's Notify step here, because the repeat decides the invoice and the server sends the firm email only when the
  // repeat itself carries the notice. Fixed when the form opens, so the form does not change size under the reviewer.
  const [dialog, setDialog] = React.useState<{ open: boolean; seq: number; outcome: InvoiceOutcome; recorded: boolean; offerNotice: boolean } | null>(
    null
  );
  const closeDialog = () => setDialog(prev => (prev ? { ...prev, open: false } : prev));
  const [reason, setReason] = React.useState('');
  const [note, setNote] = React.useState('');
  const [noteError, setNoteError] = React.useState<string | null>(null);
  // Review round 1 F1-a: the firm notice of the request this page sent and that came back unsure (reject or return), as that request carried it.
  const [noticeLock, setNoticeLock] = React.useState<WizardNoticeLock | undefined>(undefined);
  const [noticeError, setNoticeError] = React.useState<string | null>(null);
  // ISS-041: the notice the reviewer composes in this form (the Notify step's own entries, one per kind), when none is kept.
  const [notices, setNotices] = React.useState<NoticeEntries>(EMPTY_NOTICES);
  const patchNotice = React.useCallback((kind: FirmNoticeKind, patch: Partial<NoticeEntry>, edited = false) => {
    setNotices(n => ({ ...n, [kind]: { ...n[kind], ...patch } }));
    // A reviewer's edit drops the server's refusal of the old text.
    if (edited) setNoticeError(null);
  }, []);
  const noticeWriter = React.useMemo(
    () => ({
      patch: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>) => patchNotice(kind, patch),
      settle: (kind: FirmNoticeKind, patch: Partial<NoticeEntry>) => setNotices(n => settleNoticeEntries(n, kind, patch)),
    }),
    [patchNotice]
  );
  const { loadNotice, reset: resetNoticeDrafts } = useNoticeDrafts(api, invoiceId, noticeWriter);
  const [locked, setLocked] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  // Task 195: a repeat in flight, or the open form with a reason or note typed, is unsaved work for the Console modal's browse guard.
  useReportDirty(busy || (dialog?.open === true && (reason.trim() !== '' || note.trim() !== '')));
  const [error, setError] = React.useState<string | null>(null);
  // One repeat at a time: a second click before the busy state renders sends nothing.
  const sending = React.useRef(false);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const open = () => {
    if (!stranded) return;
    // A decision this page sent and that came back unsure (the wizard's or this form's) is repeated as it was sent, locked.
    const kept = locks.get('wizard');
    const same = kept && kept.outcome === stranded ? kept : undefined;
    // Fix round 1 F2: a recorded reject or return whose reason the page cannot read cannot be finished from here (Finish is off and the form says
    // to reload), so no notice is offered and no draft is asked for.
    let reasonUnreadable = false;
    if (recordedStage) {
      // The record exists: its reason (or, failing that, the one this page sent), read-only; no note, which the server would drop.
      const recordedText = stranded === 'approve' ? '' : (recordedReason(review, stranded) ?? same?.reason ?? '');
      reasonUnreadable = stranded !== 'approve' && recordedText.trim() === '';
      setReason(recordedText);
      setNote('');
      setLocked(true);
    } else {
      setReason(same ? same.reason : '');
      setNote(same ? same.note : '');
      setLocked(!!same);
    }
    setNoteError(null);
    setNoticeError(null);
    const keptNotice = stranded === 'approve' ? undefined : same?.notice;
    setNoticeLock(keptNotice);
    // A fresh form asks for a fresh draft (never one that was drafted for an earlier opening).
    setNotices(EMPTY_NOTICES);
    resetNoticeDrafts();
    setError(null);
    setDialog(prev =>
      prev?.open
        ? prev
        : {
            open: true,
            seq: (prev?.seq ?? 0) + 1,
            outcome: stranded,
            recorded: recordedStage,
            offerNotice: stranded !== 'approve' && !keptNotice && !reasonUnreadable,
          }
    );
  };

  const outcome = dialog?.outcome ?? null;
  const recorded = dialog?.recorded ?? false;
  const needsReason = outcome === 'reject' || outcome === 'return';
  const reasonMissing = needsReason && reason.trim() === '';
  // ISS-041: the Notify step is offered while no notice is kept (an unsure send from this form keeps what it sent, and the form then shows it).
  const noticeKind = needsReason ? noticeKindOf(outcome ?? undefined) : undefined;
  const noticeEntry = noticeKind ? notices[noticeKind] : undefined;
  const offered = !!dialog?.offerNotice && !noticeLock && !!noticeEntry;
  // Held while the draft loads, and for a blank or over-limit notice with sending on; a failed draft, or sending off, holds nothing.
  const noticeHeld = offered && !!noticeEntry && noticeBlocker(noticeEntry) !== null;
  const incomplete = reasonMissing || noteProblem(note) !== null || noticeHeld;
  // What the repeat carries for the firm: the kept notice with sending on (the wizard's own rule), else the notice composed here, else none.
  const sentNotice = !needsReason
    ? null
    : noticeLock
      ? noticeToSend(noticeEntryOfLock(noticeLock), { skipped: false, locked: true })
      : offered
        ? noticeToSend(noticeEntry, { skipped: false, locked: false })
        : null;
  // What the Notify step reads (it is the wizard's step, so it reads the wizard's context): only these members are used there.
  const notifyCtx: WizardCtx | null =
    offered && state && outcome
      ? {
          form: { ...EMPTY_FORM, outcome, reason, note, notices, noticeError, locked: false },
          set: () => undefined,
          readOnly: false,
          review,
          approval: state,
          api,
          invoiceId,
          reload,
          onOutcome: () => undefined,
          loadNotice,
          patchNotice,
          locks,
        }
      : null;

  const submit = async () => {
    if (!dialog?.open || !outcome || sending.current || incomplete) return;
    sending.current = true;
    setBusy(true);
    setError(null);
    setNoteError(null);
    setNoticeError(null);
    const n = noteArgs(note);
    // The same request the wizard sent, notice included: with the notice on, the argument shape is the wizard's (note, then notice); with it
    // off, or none kept, the old call exactly (no notice fields).
    const notice = needsReason ? sentNotice : null;
    const r =
      outcome === 'approve'
        ? await api.approve(invoiceId, ...n)
        : outcome === 'reject'
          ? await (notice ? api.reject(invoiceId, reason.trim(), noteToSend(note), notice) : api.reject(invoiceId, reason.trim(), ...n))
          : await (notice ? api.returnToFirm(invoiceId, reason.trim(), noteToSend(note), notice) : api.returnToFirm(invoiceId, reason.trim(), ...n));
    if (!alive.current) return;
    if (r.ok) {
      locks.clear('wizard');
      sending.current = false;
      setBusy(false);
      closeDialog();
      await Promise.all([refresh(), reload()]);
      return;
    }
    // The server refused the note itself (400): under the Note box; the form keeps the reason and the note.
    if (isNoteRefusal(r.failure)) {
      sending.current = false;
      setBusy(false);
      setNoteError(noteRefusalMessage(r.failure));
      return;
    }
    // The server refused the notice itself (400, before it decided anything): shown in the form; nothing was decided.
    if (isNoticeRefusal(r.failure)) {
      // Fix round 1 F1 (as the wizard's review round 2): the route answers this before the service is called, so it proves this request decided
      // nothing. A notice kept from an unsure send is therefore not a request to repeat any more: drop the lock (page level and form), so the
      // Notify step is offered again with the text as sent (editable, or turn it off) and the reason and note, as typed, can be edited. In the
      // recorded stage the reason is the record's and stays read-only.
      if (noticeLock && needsReason) {
        locks.clear('wizard');
        setNoticeLock(undefined);
        if (!recorded) setLocked(false);
        // A notice kept from before (not composed in this form) seeds the Notify entry, so its text is what the reviewer edits.
        if (noticeKind && !dialog.offerNotice) patchNotice(noticeKind, noticeEntryOfLock(noticeLock));
        setDialog(prev => (prev ? { ...prev, offerNotice: true } : prev));
      }
      sending.current = false;
      setBusy(false);
      setNoticeError(decisionFailureMessage(r.failure));
      return;
    }
    // Saved in part, or no answer: the same request is the repeat, so what was sent is locked, here and at page level (as the wizard does).
    const unknown = isOutcomeUnknown(r.failure);
    if (unknown) {
      setLocked(true);
      // The notice stays in the lock, so an unsure send here does not erase it. One composed in this form (ISS-041) is what this request carried
      // (or no notice, when sending was off): the repeat is the same request, and the form then shows it read-only like a kept one.
      const keptNotice = !needsReason ? undefined : noticeLock ?? (offered && noticeEntry ? noticeLockOf(noticeEntry, sentNotice) : undefined);
      if (keptNotice && !noticeLock) setNoticeLock(keptNotice);
      locks.set('wizard', { outcome, reason: needsReason ? reason.trim() : '', note, ...(keptNotice ? { notice: keptNotice } : {}) });
    }
    // An unsure outcome or a 409 means the page may be out of date: re-read the approval and the review, as the wizard does.
    if (unknown || r.failure.status === 409) {
      await Promise.all([refresh(), reload()]);
      if (!alive.current) return;
    }
    sending.current = false;
    setBusy(false);
    setError(decisionFailureMessage(r.failure));
  };

  return (
    <>
      {stranded && (
        <MessageBar intent="warning" layout="multiline" data-testid="incomplete-decision-banner">
          <MessageBarBody>
            <MessageBarTitle>This decision did not finish</MessageBarTitle>
            <Text as="p" data-testid="incomplete-decision-text">
              {incompleteDecisionText(stranded, recordedStage)}
            </Text>
          </MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={open} data-testid="finish-decision">
              Finish decision…
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {dialog && outcome && (
        <FormModal
          key={dialog.seq}
          open={dialog.open}
          size={dialog.offerNotice ? 'md' : 'sm'}
          title="Finish decision"
          submitLabel="Finish"
          submitDisabled={incomplete}
          busy={busy}
          onClose={() => {
            if (!busy) closeDialog();
          }}
          onSubmit={() => void submit()}
        >
          <div className={s.form}>
            <Text data-testid="finish-decision-intro">{finishDecisionIntro(outcome, locked, recorded, offered)}</Text>
            {needsReason && recorded && reasonMissing && (
              <MessageBar intent="warning" layout="multiline" data-testid="finish-decision-no-reason">
                <MessageBarBody>
                  The reason on its Decision Record could not be read on this page. Reload the page, then finish the decision.
                </MessageBarBody>
              </MessageBar>
            )}
            {needsReason && !(recorded && reasonMissing) && (
              <Field label={recorded ? 'Recorded reason' : 'Reason'} required={!recorded}>
                <Textarea
                  value={reason}
                  onChange={(_, d) => setReason(d.value)}
                  resize="vertical"
                  rows={3}
                  maxLength={REASON_MAX}
                  readOnly={locked}
                  aria-label="Reason"
                  data-testid="finish-decision-reason"
                />
              </Field>
            )}
            {/* ISS-041: no notice is kept, so the same Notify step the wizard offers (its own draft call and limits; Send is on once a draft loads,
                and the switch sends none). The step shows the server's refusal under its fields. */}
            {notifyCtx && (
              <WizardContext.Provider value={notifyCtx}>
                <NotifyStep />
              </WizardContext.Provider>
            )}
            {needsReason && !offered && !(recorded && reasonMissing) && (
              <Text className={s.muted} data-testid="finish-decision-notice">
                {sentNotice
                  ? `The firm will be emailed this notice, with the findings table: “${sentNotice.subject}”.`
                  : 'No email is sent to the firm. The decision is still recorded.'}
              </Text>
            )}
            {noticeError && !offered && (
              <MessageBar intent="error" layout="multiline" data-testid="finish-decision-notice-refused">
                <MessageBarBody>{noticeError}</MessageBarBody>
              </MessageBar>
            )}
            {!recorded && (
              <NoteField
                value={note}
                onChange={v => {
                  setNote(v);
                  setNoteError(null);
                }}
                serverError={noteError}
                readOnly={locked}
                ariaLabel="Note"
                testId="finish-decision-note"
              />
            )}
            {locked && !recorded && (
              <Caption1 className={s.muted} data-testid="finish-decision-locked">
                This decision was saved in part, or no answer came back. Submitting again finishes it with the same{' '}
                {needsReason ? 'reason and note, so they cannot' : 'note, so it cannot'} be changed now.
              </Caption1>
            )}
            {error && (
              <MessageBar intent="error" layout="multiline" data-testid="finish-decision-error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}
          </div>
        </FormModal>
      )}
    </>
  );
}
