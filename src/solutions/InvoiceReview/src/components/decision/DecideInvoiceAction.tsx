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
import { decisionFailureMessage, isOutcomeUnknown } from '../../services/decisionApi';
import {
  decisionAccess,
  finishDecisionIntro,
  incompleteDecisionText,
  incompleteOutcome,
  isRecordedStage,
  recordedReason,
  type InvoiceOutcome,
} from '../../services/decisionModel';
import { isNoteRefusal, noteArgs, noteProblem, noteRefusalMessage } from '../../services/noteModel';
import { NoteField } from '../NoteField';
import { InvoiceDecisionWizard } from './InvoiceDecisionWizard';
import { useApprovalSync } from './AwaitingApprovers';
import { useApproval } from './ApprovalProvider';
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
  const [dialog, setDialog] = React.useState<{ open: boolean; seq: number; outcome: InvoiceOutcome; recorded: boolean } | null>(null);
  const closeDialog = () => setDialog(prev => (prev ? { ...prev, open: false } : prev));
  const [reason, setReason] = React.useState('');
  const [note, setNote] = React.useState('');
  const [noteError, setNoteError] = React.useState<string | null>(null);
  const [locked, setLocked] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
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
    if (recordedStage) {
      // The record exists: its reason (or, failing that, the one this page sent), read-only; no note, which the server would drop.
      setReason(stranded === 'approve' ? '' : (recordedReason(review, stranded) ?? same?.reason ?? ''));
      setNote('');
      setLocked(true);
    } else {
      setReason(same ? same.reason : '');
      setNote(same ? same.note : '');
      setLocked(!!same);
    }
    setNoteError(null);
    setError(null);
    setDialog(prev => (prev?.open ? prev : { open: true, seq: (prev?.seq ?? 0) + 1, outcome: stranded, recorded: recordedStage }));
  };

  const outcome = dialog?.outcome ?? null;
  const recorded = dialog?.recorded ?? false;
  const needsReason = outcome === 'reject' || outcome === 'return';
  const reasonMissing = needsReason && reason.trim() === '';
  const incomplete = reasonMissing || noteProblem(note) !== null;

  const submit = async () => {
    if (!dialog?.open || !outcome || sending.current || incomplete) return;
    sending.current = true;
    setBusy(true);
    setError(null);
    setNoteError(null);
    const n = noteArgs(note);
    const r =
      outcome === 'approve'
        ? await api.approve(invoiceId, ...n)
        : outcome === 'reject'
          ? await api.reject(invoiceId, reason.trim(), ...n)
          : await api.returnToFirm(invoiceId, reason.trim(), ...n);
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
    // Saved in part, or no answer: the same request is the repeat, so what was sent is locked, here and at page level (as the wizard does).
    const unknown = isOutcomeUnknown(r.failure);
    if (unknown) {
      setLocked(true);
      locks.set('wizard', { outcome, reason: needsReason ? reason.trim() : '', note });
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
          size="sm"
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
            <Text data-testid="finish-decision-intro">{finishDecisionIntro(outcome, locked, recorded)}</Text>
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
