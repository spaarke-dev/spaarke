/**
 * The blocked-submission view (ebilling-r1 task 111; D-101, D-73, D-76, FR-35): fills 110's `banners` slot while the invoice is held In Review
 * because its internal submission was blocked. It shows why (the blocking findings the server returns, with the clause) and offers:
 *
 *   Override…        only when the server says `canOverride` (the caller holds the eBilling Override privilege, D-76). A reason is required.
 *   Return to firm…  only when the server says `canReturn` (no privilege needed, D-73). A reason is required.
 *
 * Each is one reason on `FormModal` (a single-field form, ADR-050). A refusal (403 without the privilege, 409 already decided, 503 partly
 * saved) is shown in the form with its code and nothing changes on the page; a 503 is finished by submitting the same request again. The
 * invoice's own state is the server's: after success the page re-reads and the banner goes away with the hold.
 *
 * No navigation entry or form command is needed for this: the page opens on the invoice, the banner reads the submission the review names.
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
import { decisionFailureMessage, isBlockedOutcomeUnknown, type BlockedFinding, type BlockedSubmission } from '../../services/decisionApi';
import type { ApiFailure, InvoiceReview } from '../../services/invoiceReviewApi';
import { isNoteRefusal, noteArgs, noteProblem, noteRefusalMessage } from '../../services/noteModel';
import { NoteField } from '../NoteField';
import { useApproval } from './ApprovalProvider';

const REASON_MAX = 2000;

const useStyles = makeStyles({
  findings: { margin: 0, paddingLeft: tokens.spacingHorizontalL, display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  muted: { color: tokens.colorNeutralForeground3 },
  form: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM },
});

/** The submission the review names (its type is `unknown` on 110's contract: the id is all this reads). */
export function submissionIdOf(review: Pick<InvoiceReview, 'submission'>): string | null {
  const sub = review.submission as { submissionId?: unknown } | null | undefined;
  return sub && typeof sub.submissionId === 'string' ? sub.submissionId : null;
}

/** One finding as one line: the policy and clause, why it holds the invoice, and what to do about its detail when withheld. */
export function findingLine(f: BlockedFinding): string {
  const head = [f.policyCode, f.clause].filter(Boolean).join(' · ');
  const where = f.line !== null ? `line ${f.line}` : f.subject ?? '';
  const why = f.heldBecause ?? f.reasonCode ?? f.consequence ?? '';
  const parts = [head, where, why].filter(p => p && p.length > 0);
  const base = parts.length > 0 ? parts.join(' — ') : 'A blocking finding';
  return f.detailWithheld ? `${base} (detail withheld for your role)` : base;
}

type Action = 'override' | 'return';

export interface BlockedBannerProps {
  review: InvoiceReview;
  reload: () => Promise<InvoiceReview | null>;
}

export function BlockedBanner({ review, reload }: BlockedBannerProps) {
  const s = useStyles();
  const { api, refresh, locks } = useApproval();
  const held = review.header.held;
  const submissionId = submissionIdOf(review);

  const [blocked, setBlocked] = React.useState<BlockedSubmission | null>(null);
  const [failure, setFailure] = React.useState<ApiFailure | null>(null);
  // The action last shown, kept after the form closes: the FormModal is closed with `open={false}`, never unmounted while
  // open, which would leave the page aria-hidden (#2658). `seq` keys each opening, so every opening starts fresh.
  const [dialog, setDialog] = React.useState<{ action: Action; open: boolean; seq: number } | null>(null);
  const action = dialog?.action ?? null;
  const closeDialog = () => setDialog(prev => (prev ? { ...prev, open: false } : prev));
  const [reason, setReason] = React.useState('');
  // Task 095f-2ui: the optional note. Like the reason, it is kept across a refusal (a 409 or 503) so the reviewer can resend. A decision saved
  // in part (503), or one that got no answer (status 0), is repeated with the same reason and note, so both are locked from then on, and the
  // locked request is kept at page level (`locks`) so closing and reopening the form does not lose it.
  const [note, setNote] = React.useState('');
  const [noteError, setNoteError] = React.useState<string | null>(null);
  const [locked, setLocked] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const alive = React.useRef(true);
  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  const load = React.useCallback(async () => {
    if (!submissionId) return;
    const r = await api.getBlocked(submissionId);
    if (!alive.current) return;
    if (r.ok) {
      setBlocked(r.value);
      setFailure(null);
    } else {
      setFailure(r.failure);
    }
  }, [api, submissionId]);

  React.useEffect(() => {
    if (held) void load();
  }, [held, load]);

  // The hold is gone (the decision is done): nothing is left to repeat, so a lock kept from an unsure send is dropped.
  React.useEffect(() => {
    if (!held) locks.clear('blocked');
  }, [held, locks]);

  if (!held || !submissionId) return null;

  const open = (a: Action) => {
    // An Override or Return that came back unsure is reopened as it was sent, locked, so the repeat is the same request. Review round 2: the
    // lock is KEPT when the other action is opened (the first may already be recorded, and the server finishes a repeat from what it
    // stored). The other action opens with an empty, editable form that warns and cannot be submitted until the lock clears.
    const kept = locks.get('blocked');
    const same = kept && kept.action === a ? kept : undefined;
    setReason(same ? same.reason : '');
    setNote(same ? same.note : '');
    setNoteError(null);
    setLocked(!!same);
    setError(null);
    setDialog(prev => (prev?.open ? { ...prev, action: a } : { action: a, open: true, seq: (prev?.seq ?? 0) + 1 }));
  };

  // An Override or Return of the OTHER action that came back unsure and is not finished: this form must not start a second decision.
  const lock = locks.get('blocked');
  const otherLock = action && lock && lock.action !== action ? lock : null;

  const submit = async () => {
    if (!dialog?.open || !action || otherLock || reason.trim() === '' || noteProblem(note) !== null) return;
    setBusy(true);
    setError(null);
    setNoteError(null);
    const n = noteArgs(note);
    const r =
      action === 'override' ? await api.overrideBlocked(submissionId, reason.trim(), ...n) : await api.returnBlocked(submissionId, reason.trim(), ...n);
    if (!alive.current) return;
    if (r.ok) {
      locks.clear('blocked');
      setBusy(false);
      closeDialog();
      await Promise.all([reload(), refresh(), load()]);
      return;
    }
    // The server refused the note itself (400): the field message goes under the Note box, and the form keeps the reason and the note.
    if (isNoteRefusal(r.failure)) {
      setBusy(false);
      setNoteError(noteRefusalMessage(r.failure));
      return;
    }
    // A 503 saved part of the decision, no answer came back, or a 409 `concurrent_change` (the server answers it AFTER the Decision Record is
    // written): the same request, repeated, finishes it, so the reason and the note are held as they were sent, here and at page level.
    const unknown = isBlockedOutcomeUnknown(r.failure);
    if (unknown) {
      setLocked(true);
      locks.set('blocked', { action, reason: reason.trim(), note });
    }
    // A 503 `ebilling.blocked.unavailable` can come after the decision stood (the Signals were not all closed), and a send with no answer may
    // have been recorded: show the server's state (the hold may already be gone) before the error.
    if (unknown) {
      await Promise.all([reload(), refresh(), load()]);
      if (!alive.current) return;
    }
    // Shown in the form with the server's detail and code. A 503 is repeated by submitting again.
    setBusy(false);
    setError(decisionFailureMessage(r.failure));
  };

  const blocking = (blocked?.findings ?? []).filter(f => f.blocking && !f.overridden);
  const shown = blocking.slice(0, 5);
  const title = action === 'override' ? 'Override the block' : 'Return to firm';

  return (
    <>
      <MessageBar intent="warning" layout="multiline" data-testid="blocked-banner">
        <MessageBarBody>
          <MessageBarTitle>This invoice is on hold</MessageBarTitle>
          <Text as="p">Its submission was blocked by a rule. It stays In Review and cannot be approved until the block is overridden or the invoice is returned to the firm.</Text>
          {failure && (
            <Caption1 className={s.muted} data-testid="blocked-unavailable">
              The reasons could not be loaded: {decisionFailureMessage(failure)}{' '}
              <Button size="small" appearance="transparent" onClick={() => void load()} data-testid="blocked-retry">
                Retry
              </Button>
            </Caption1>
          )}
          {shown.length > 0 && (
            <ul className={s.findings} data-testid="blocked-findings" aria-label="Why this invoice is held">
              {shown.map(f => (
                <li key={f.index}>
                  <Caption1>{findingLine(f)}</Caption1>
                </li>
              ))}
              {blocking.length > shown.length && <li><Caption1>and {blocking.length - shown.length} more</Caption1></li>}
            </ul>
          )}
          {blocked && !blocked.canOverride && (
            <Caption1 className={s.muted} data-testid="no-override">
              Overriding a block needs the eBilling Override privilege, which you do not hold.
            </Caption1>
          )}
        </MessageBarBody>
        {blocked && (blocked.canOverride || blocked.canReturn) && (
          <MessageBarActions>
            {blocked.canOverride && (
              <Button size="small" onClick={() => open('override')} data-testid="override-block">
                Override…
              </Button>
            )}
            {blocked.canReturn && (
              <Button size="small" onClick={() => open('return')} data-testid="return-blocked">
                Return to firm…
              </Button>
            )}
          </MessageBarActions>
        )}
      </MessageBar>

      {dialog && (
        <FormModal
          key={dialog.seq}
          open={dialog.open}
          size="sm"
          title={title}
          submitLabel={action === 'override' ? 'Override' : 'Return'}
          submitDisabled={otherLock !== null || reason.trim() === '' || noteProblem(note) !== null}
          busy={busy}
          onClose={() => {
            if (!busy) closeDialog();
          }}
          onSubmit={() => void submit()}
        >
          <div className={s.form}>
            <Text>
              {action === 'override'
                ? 'The invoice is released into the normal review. A reason is required and is recorded with your name.'
                : 'The invoice goes back to the firm, which can correct and resubmit it. A reason is required and the firm sees it.'}
            </Text>
            {otherLock && (
              <MessageBar intent="warning" layout="multiline" data-testid="blocked-other-lock">
                <MessageBarBody>
                  {otherLock.action === 'override'
                    ? 'An Override was saved in part. Submit it again to finish it before returning.'
                    : 'A Return was saved in part. Submit it again to finish it before overriding.'}
                </MessageBarBody>
              </MessageBar>
            )}
            <Field label="Reason" required>
              <Textarea
                value={reason}
                onChange={(_, d) => setReason(d.value)}
                resize="vertical"
                rows={3}
                maxLength={REASON_MAX}
                readOnly={locked}
                aria-label="Reason"
                data-testid="blocked-reason"
              />
            </Field>
            <NoteField
              value={note}
              onChange={v => {
                setNote(v);
                setNoteError(null);
              }}
              serverError={noteError}
              readOnly={locked}
              ariaLabel="Note"
              testId="blocked-note"
            />
            {locked && (
              <Caption1 className={s.muted} data-testid="blocked-note-locked">
                This decision was saved in part, or no answer came back. Submitting again finishes it with the same reason and note, so they cannot be
                changed now.
              </Caption1>
            )}
            {error && (
              <MessageBar intent="error" layout="multiline" data-testid="blocked-error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}
          </div>
        </FormModal>
      )}
    </>
  );
}
