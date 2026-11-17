/**
 * The invoice decision wizard's step contents (ebilling-r1 task 111; prototype InvoiceDecisionWizard steps, FR-32, FR-35, D-72).
 *
 *   Approve:           Decision → Invoice adjustment (skippable; 102) → Confirm → POST approve (103)
 *   Reject / Return:   Decision → Reason → (firm notice: a slot for 131 / 132, not built here) → Confirm → POST reject | return (104)
 *
 * Nothing is decided here: the adjustment amount and the approved total are the server's (102 / 016); every refusal is shown with the
 * server's detail and its stable code, and nothing changes on the page.
 */

import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  Caption1,
  Dropdown,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Option,
  Radio,
  RadioGroup,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FormModal, type IWizardShellHandle } from '@spaarke/ui-components';
import {
  CODE_ADJUSTMENT_ID_REUSED,
  decisionFailureMessage,
  isDecisionIncomplete,
  newAdjustmentId,
  type AdjustmentMethod,
  type AdjustmentRule,
} from '../../services/decisionApi';
import { isNoteRefusal, noteArgs, noteMember, noteProblem, noteRefusalMessage } from '../../services/noteModel';
import { NoteField } from '../NoteField';
import { approveBlocker, firmNeededBlocker, INVOICE_OUTCOME_LABEL, outcomeOfApproval, type InvoiceOutcome } from '../../services/decisionModel';
import { isAdjustment, isDecided, money, shortDate, signedMoney } from '../../services/reviewModel';
import type { ReviewLine } from '../../services/invoiceReviewApi';
import { adjustmentFormDirty, useWiz } from './wizardContext';

export const REASON_MAX = 2000;

const useStyles = makeStyles({
  step: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL, maxWidth: '640px' },
  kv: { display: 'grid', gridTemplateColumns: '180px minmax(0, 1fr)', rowGap: tokens.spacingVerticalS, columnGap: tokens.spacingHorizontalL },
  muted: { color: tokens.colorNeutralForeground3 },
  row: { display: 'flex', columnGap: tokens.spacingHorizontalM, alignItems: 'flex-end', flexWrap: 'wrap' },
  option: { display: 'flex', flexDirection: 'column' },
  adjList: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXS },
  adjRow: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalM },
  adjText: { flex: 1, minWidth: 0 },
  changed: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
});

/** Counts of the real lines (an Adjustment line is computed, not decided). */
export function lineCounts(lines: readonly ReviewLine[]) {
  const real = lines.filter(l => !isAdjustment(l));
  return {
    total: real.length,
    decided: real.filter(isDecided).length,
    clean: real.filter(l => l.status === 'Clean').length,
    flagged: real.filter(l => l.status === 'Flagged').length,
    pending: real.filter(l => l.status === 'Pending').length,
  };
}

function Totals() {
  const s = useStyles();
  const { review } = useWiz();
  const t = review.totals;
  const c = lineCounts(review.lines);
  return (
    <div className={s.kv} data-testid="wizard-totals">
      <Caption1 className={s.muted}>Billed</Caption1>
      <Text>{money(t.billed)}</Text>
      <Caption1 className={s.muted}>Line adjustments</Caption1>
      <Text>{signedMoney(t.lineAdjustments)}</Text>
      <Caption1 className={s.muted}>Invoice adjustments</Caption1>
      <Text data-testid="wizard-invoice-adj">{signedMoney(t.invoiceAdjustments)}</Text>
      <Caption1 className={s.muted}>Approved total</Caption1>
      <Text weight="semibold" data-testid="wizard-approved">
        {money(t.approved)}
      </Text>
      <Caption1 className={s.muted}>Lines</Caption1>
      <Text>
        {c.total} · {c.decided} decided · {c.clean} clean · {c.flagged} to decide{c.pending ? ` · ${c.pending} processing` : ''}
      </Text>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Decision
// ─────────────────────────────────────────────────────────────────────────────

export function OutcomeStep({ handle }: { handle: IWizardShellHandle }) {
  const s = useStyles();
  const { form, approval, readOnly, review, onOutcome } = useWiz();
  const blocker = readOnly ? null : approveBlocker(approval);
  // EB-D-190: Reject is refused (409) while the invoice needs its firm, exactly like Approve, so it is disabled the same way.
  const rejectBlocker = readOnly ? null : firmNeededBlocker(approval);
  const approveDisabled = readOnly || !approval.canApprove;
  const option = (value: InvoiceOutcome, caption: string, disabled: boolean) => (
    <Radio
      key={value}
      value={value}
      disabled={disabled}
      label={
        <span className={s.option}>
          <Text>{value === 'approve' && approval.canApprove ? `Approve ${money(review.totals.approved)}` : INVOICE_OUTCOME_LABEL[value]}</Text>
          <Caption1 className={s.muted}>{caption}</Caption1>
        </span>
      }
    />
  );
  return (
    <div className={s.step}>
      <Totals />
      <RadioGroup
        aria-label="Invoice decision"
        value={form.outcome ?? ''}
        onChange={(_, d) => onOutcome(d.value as InvoiceOutcome, handle)}
        disabled={readOnly}
      >
        {option('approve', blocker ?? 'Every line has a decision.', approveDisabled)}
        {option('return', 'The firm corrects and resubmits. The resubmission becomes a new invoice linked to this one.', readOnly)}
        {option('reject', rejectBlocker ?? 'Not payable. The firm cannot resubmit this invoice.', readOnly || rejectBlocker !== null)}
      </RadioGroup>
      {!readOnly && approval.undecidedCleanLines > 0 && approval.canApprove && (
        <Caption1 className={s.muted}>
          {approval.undecidedCleanLines} clean {approval.undecidedCleanLines === 1 ? 'line has' : 'lines have'} no decision yet; approving accepts{' '}
          {approval.undecidedCleanLines === 1 ? 'it' : 'them'} as billed.
        </Caption1>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Invoice adjustment (102; skippable)
// ─────────────────────────────────────────────────────────────────────────────

/** The client-side shape check of the form; the amount, the cap and the rule are the server's (102 refuses with a stable code). */
export function adjustmentValueError(method: AdjustmentMethod, valueText: string): string | null {
  if (valueText.trim() === '') return null;
  const v = Number(valueText);
  if (!Number.isFinite(v) || v <= 0) return 'Enter a positive number.';
  if (method === 'percent' && v > 100) return 'A percentage can be at most 100.';
  return null;
}

function ruleLabel(r: AdjustmentRule): string {
  const where = [r.ruleSetName, `v${r.version}`, r.clauseReference].filter(Boolean).join(', ');
  return `${r.headline ?? r.policyCode}${where ? ` (${where})` : ''}`;
}

const REREAD_FAILED = 'The totals could not be read back from the server, so the page may show old figures.';

export function AdjustStep() {
  const s = useStyles();
  const { form, set, readOnly, review, api, invoiceId, reload, approval, locks } = useWiz();
  // EB-D-197: an adjustment (add or remove) is refused (409) while the invoice needs its firm, so it is disabled the same way Approve is.
  const firmBlocker = readOnly ? null : firmNeededBlocker(approval);
  const [rules, setRules] = React.useState<AdjustmentRule[] | null>(null);
  const [rulesUnavailable, setRulesUnavailable] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  // Review round 1: the add's id and its "outcome unknown" lock live in the wizard form (and, for a reopened wizard, in the page-level locks),
  // not in this step, which is unmounted whenever the reviewer leaves it: a new mount must not mint a new id over an add that may be saved.
  const repeatable = form.adjLocked;
  const [clearedNote, setClearedNote] = React.useState(false);
  // The adjustment last asked about, kept after the confirm closes: the FormModal is closed with `open={false}`, never
  // unmounted while open, which would leave the wizard behind it aria-hidden (#2658). `seq` keys each opening.
  const [removeDialog, setRemoveDialog] = React.useState<{ line: ReviewLine; open: boolean; seq: number } | null>(null);
  const removing = removeDialog?.open ? removeDialog.line : null;
  const askRemove = (line: ReviewLine) =>
    setRemoveDialog(prev => (prev?.open ? { ...prev, line } : { line, open: true, seq: (prev?.seq ?? 0) + 1 }));
  const closeRemove = () => setRemoveDialog(prev => (prev ? { ...prev, open: false } : prev));
  const [removeBusy, setRemoveBusy] = React.useState(false);
  const [removeError, setRemoveError] = React.useState<string | null>(null);
  // Task 095f-2ui: the optional notes. The add form's note lives in the wizard form (with the rest of it); the removal's is the confirm's own.
  const [removeNote, setRemoveNote] = React.useState('');
  const [removeNoteError, setRemoveNoteError] = React.useState<string | null>(null);
  // The removal was saved but the re-read failed: Remove again is the same request, so the note cannot change now.
  const [removeLocked, setRemoveLocked] = React.useState(false);
  // The adjustment was already gone when the list was re-read after a 409 `id_reused`: the form is closed and the server's sentence is kept here.
  const [removeNotice, setRemoveNotice] = React.useState<string | null>(null);
  const [addNoteError, setAddNoteError] = React.useState<string | null>(null);
  const alive = React.useRef(true);

  React.useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
    };
  }, []);

  React.useEffect(() => {
    if (readOnly) return;
    let cancelled = false;
    void api.listAdjustmentRules(invoiceId).then(r => {
      if (cancelled) return;
      if (r.ok) setRules(r.value.rules);
      else setRulesUnavailable(true);
    });
    return () => {
      cancelled = true;
    };
  }, [api, invoiceId, readOnly]);

  const adjustments = review.lines.filter(isAdjustment);
  const valueError = adjustmentValueError(form.adjMethod, form.adjValue);
  const canAdd =
    !readOnly && !busy && firmBlocker === null && form.adjValue.trim() !== '' && !valueError && form.adjReason.trim() !== '' && noteProblem(form.adjNote) === null;

  const add = async () => {
    setBusy(true);
    setError(null);
    setAddNoteError(null);
    setClearedNote(false);
    // What this send carries. The lock below keeps exactly this, with the id, so a repeat (even after the wizard was closed) is the same request.
    const sent = { adjustmentId: form.adjId, method: form.adjMethod, value: form.adjValue, reason: form.adjReason.trim(), rule: form.adjRule, note: form.adjNote };
    const lockForm = () => {
      set({ adjLocked: true });
      locks.set('adjust', sent);
    };
    const r = await api.addAdjustment(invoiceId, {
      adjustmentId: sent.adjustmentId,
      method: sent.method,
      value: Number(sent.value),
      reason: sent.reason,
      ruleId: sent.rule || null,
      ...noteMember(sent.note),
    });
    // The lock is wizard-level state, so it is kept even if this step was left while the send was out.
    if (!r.ok && (r.failure.status === 503 || r.failure.status === 0)) lockForm();
    // Review round 2: a SUCCESS is finished even when this step was left while the send was out. The re-read and the reset (or the lock) go
    // through the wizard's form state and the page-level locks, which outlive the step; otherwise the form would keep the saved add's id
    // unlocked and a second send of the same text would file a second adjustment.
    if (!alive.current && !r.ok) return;
    if (r.ok) {
      // The server computed the amount and stored the totals; the page re-reads them and shows what the re-read returns.
      const fresh = await reload();
      if (!fresh) lockForm();
      if (fresh) {
        locks.clear('adjust');
        set({ adjId: newAdjustmentId(), adjLocked: false, adjValue: '', adjReason: '', adjRule: '', adjNote: '' });
      } else if (alive.current) {
        // Saved, but the page is not showing the server's totals. The form stays, locked, with the same id: Repeat answers "already applied"
        // and re-reads, so nothing is added twice and a new id is never minted over a saved adjustment.
        setError(`${REREAD_FAILED} Repeat to read them again; nothing is added twice.`);
      }
      if (!alive.current) return;
    } else if (isNoteRefusal(r.failure)) {
      // The server refused the note itself (400, before any read): the field message goes under the Note box, and the form keeps everything.
      setAddNoteError(noteRefusalMessage(r.failure));
      set({ adjLocked: false });
      locks.clear('adjust');
    } else {
      setError(decisionFailureMessage(r.failure));
      // A 503 where the line was written but the totals were not: the same request, with the same id, finishes it. Show the server's state
      // first (the page still shows the old totals), as 110's writes do. No answer at all (status 0) may also have been recorded: read the list too.
      // 409 `id_reused`: this id was already used for other content or by another reviewer, so the page is out of date: re-read the list (the
      // form keeps its text; Clear starts a new adjustment, with a new id, once the list has been seen).
      const reused = r.failure.status === 409 && r.failure.errorCode === CODE_ADJUSTMENT_ID_REUSED;
      if (isDecisionIncomplete(r.failure) || r.failure.status === 0 || reused) {
        await reload();
        if (!alive.current) return;
      }
      if (!(r.failure.status === 503 || r.failure.status === 0)) {
        set({ adjLocked: false });
        locks.clear('adjust');
      }
    }
    setBusy(false);
  };

  /** Clear: a form locked after an unsure send reads the list back first, so a new adjustment is never typed over one that may be saved. */
  const clearForm = async () => {
    if (form.adjLocked) {
      setBusy(true);
      const fresh = await reload();
      if (!alive.current) return;
      setBusy(false);
      if (!fresh) {
        setError(`${REREAD_FAILED} The earlier add may have been saved, so the form stays as it was. Try Clear again.`);
        return;
      }
      setClearedNote(true);
    }
    locks.clear('adjust');
    set({ adjValue: '', adjReason: '', adjRule: '', adjNote: '', adjId: newAdjustmentId(), adjLocked: false });
    setError(null);
    setAddNoteError(null);
  };

  const confirmRemove = async () => {
    if (!removing) return;
    const target = removing;
    setRemoveBusy(true);
    setRemoveError(null);
    setRemoveNoteError(null);
    const r = await api.removeAdjustment(invoiceId, target.id, ...noteArgs(removeNote));
    if (!alive.current) return;
    if (r.ok) {
      const fresh = await reload();
      if (!alive.current) return;
      setRemoveBusy(false);
      // A failed re-read leaves the list as it was: the form stays open and says so; Remove again answers "already applied" and re-reads.
      // The note is locked then: Remove again is the same request (an edited note would be refused as a reused id).
      if (fresh) closeRemove();
      else {
        setRemoveLocked(true);
        setRemoveError(`The adjustment was removed. ${REREAD_FAILED} Remove again to read them again; nothing is removed twice.`);
      }
    } else if (isNoteRefusal(r.failure)) {
      setRemoveBusy(false);
      setRemoveNoteError(noteRefusalMessage(r.failure));
    } else {
      // 409 `id_reused` on a removal: another reviewer already removed it, or a repeat carried another note. The page is out of date, so the
      // list is re-read. When that shows the adjustment is gone, there is nothing left to remove: the form closes and the server's sentence stays
      // on the step. Otherwise (still listed, or the re-read failed) the form stays open with the sentence and the note, kept for a resend.
      if (r.failure.status === 409 && r.failure.errorCode === CODE_ADJUSTMENT_ID_REUSED) {
        const fresh = await reload();
        if (!alive.current) return;
        if (fresh && !fresh.lines.some(l => l.id === target.id)) {
          setRemoveBusy(false);
          setRemoveNotice(decisionFailureMessage(r.failure));
          closeRemove();
          return;
        }
      }
      setRemoveBusy(false);
      setRemoveError(decisionFailureMessage(r.failure));
    }
  };

  return (
    <div className={s.step}>
      <Text>
        Optional. An invoice adjustment appears as an Adjustment line. The server works out the amount: a percentage applies to fees after line
        adjustments.
      </Text>

      {adjustments.length > 0 && (
        <div className={s.adjList} data-testid="adjustment-list" role="list" aria-label="Invoice adjustments">
          {adjustments.map(a => (
            <div key={a.id} className={s.adjRow} role="listitem" data-testid={`adjustment-${a.id}`}>
              <Text className={s.adjText}>
                {signedMoney(a.amount)}
                {a.reason ? ` · ${a.reason}` : ''}
              </Text>
              {!readOnly && (
                <Button size="small" appearance="subtle" disabled={firmBlocker !== null} onClick={() => { setRemoveError(null); setRemoveNote(''); setRemoveNoteError(null); setRemoveLocked(false); setRemoveNotice(null); askRemove(a); }} data-testid={`remove-adjustment-${a.id}`}>
                  Remove…
                </Button>
              )}
            </div>
          ))}
        </div>
      )}

      {removeNotice && (
        <MessageBar intent="info" layout="multiline" data-testid="remove-notice">
          <MessageBarBody>{removeNotice}</MessageBarBody>
        </MessageBar>
      )}

      {firmBlocker !== null && (
        <MessageBar intent="warning" layout="multiline" data-testid="adjustment-firm-first">
          <MessageBarBody>{firmBlocker}</MessageBarBody>
        </MessageBar>
      )}

      {!readOnly && (
        <>
          <RadioGroup layout="horizontal" disabled={repeatable} value={form.adjMethod} onChange={(_, d) => set({ adjMethod: d.value as AdjustmentMethod })} aria-label="Adjustment type">
            <Radio value="percent" label="Percent of fees" />
            <Radio value="fixed" label="Fixed amount" />
          </RadioGroup>
          <div className={s.row}>
            <Field label={form.adjMethod === 'percent' ? 'Percent' : 'Amount'} validationMessage={valueError ?? undefined} validationState={valueError ? 'error' : 'none'}>
              <Input
                type="number"
                disabled={repeatable}
                value={form.adjValue}
                onChange={(_, d) => set({ adjValue: d.value })}
                contentAfter={form.adjMethod === 'percent' ? '%' : undefined}
                aria-label="Adjustment value"
              />
            </Field>
            <Field label="Rule (optional)" hint={rulesUnavailable ? 'The rule list is not available; you can still add the adjustment without a rule.' : undefined}>
              <Dropdown
                aria-label="Adjustment rule"
                disabled={repeatable}
                value={rules?.find(r => r.ruleId === form.adjRule) ? ruleLabel(rules.find(r => r.ruleId === form.adjRule)!) : 'No rule'}
                selectedOptions={[form.adjRule]}
                onOptionSelect={(_, d) => set({ adjRule: d.optionValue ?? '' })}
              >
                <Option value="">No rule</Option>
                {(rules ?? []).map(r => (
                  <Option key={r.ruleId} value={r.ruleId} text={ruleLabel(r)}>
                    {ruleLabel(r)}
                  </Option>
                ))}
              </Dropdown>
            </Field>
          </div>
          <Field label="Reason" required={form.adjValue.trim() !== ''}>
            <Textarea value={form.adjReason} disabled={repeatable} onChange={(_, d) => set({ adjReason: d.value })} resize="vertical" aria-label="Adjustment reason" maxLength={REASON_MAX} />
          </Field>
          <NoteField
            value={form.adjNote}
            onChange={v => {
              set({ adjNote: v });
              setAddNoteError(null);
            }}
            serverError={addNoteError}
            disabled={repeatable}
            ariaLabel="Adjustment note"
            testId="adjustment-note"
            rows={2}
          />
          {error && (
            <MessageBar intent="error" layout="multiline" data-testid="adjustment-error">
              <MessageBarBody>{error}</MessageBarBody>
            </MessageBar>
          )}
          <div className={s.row}>
            <Button appearance="primary" disabled={!canAdd} onClick={() => void add()} data-testid="add-adjustment">
              {repeatable ? 'Repeat' : 'Add adjustment'}
            </Button>
            {adjustmentFormDirty(form) && !busy && (
              <Button appearance="subtle" onClick={() => void clearForm()} data-testid="clear-adjustment">
                Clear
              </Button>
            )}
          </div>
          {clearedNote && (
            <Caption1 className={s.muted} data-testid="adjustment-cleared">
              The list above was read again before the form was cleared. If the earlier adjustment was saved, it is listed there.
            </Caption1>
          )}
        </>
      )}

      <Totals />

      {removeDialog && (
        <FormModal
          key={removeDialog.seq}
          open={removeDialog.open}
          size="sm"
          title="Remove adjustment"
          submitLabel="Remove"
          busy={removeBusy}
          submitDisabled={noteProblem(removeNote) !== null}
          onClose={() => {
            if (!removeBusy) closeRemove();
          }}
          onSubmit={() => void confirmRemove()}
        >
          <Text>
            Remove the adjustment of {signedMoney(removeDialog.line.amount)}
            {removeDialog.line.reason ? ` (${removeDialog.line.reason})` : ''}? The approved total is recalculated by the server.
          </Text>
          <NoteField
            value={removeNote}
            onChange={v => {
              setRemoveNote(v);
              setRemoveNoteError(null);
            }}
            serverError={removeNoteError}
            readOnly={removeLocked}
            ariaLabel="Removal note"
            testId="remove-note"
            rows={2}
          />
          {removeError && (
            <MessageBar intent="error" layout="multiline" data-testid="remove-error">
              <MessageBarBody>{removeError}</MessageBarBody>
            </MessageBar>
          )}
        </FormModal>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Reason (reject / return)
// ─────────────────────────────────────────────────────────────────────────────

export function ReasonStep() {
  const s = useStyles();
  const { form, set, readOnly } = useWiz();
  return (
    <div className={s.step}>
      {form.outcome === 'reject' ? (
        <MessageBar intent="warning">
          <MessageBarBody>Rejection is final. The invoice will not be paid and the firm cannot resubmit it.</MessageBarBody>
        </MessageBar>
      ) : (
        <Text>The firm sees this reason in the portal and in the notification.</Text>
      )}
      <Field label="Reason" required hint={`${form.reason.length} / ${REASON_MAX}`}>
        <Textarea
          value={form.reason}
          readOnly={readOnly || form.locked}
          onChange={(_, d) => set({ reason: d.value })}
          resize="vertical"
          rows={4}
          maxLength={REASON_MAX}
          aria-label="Reason for the decision"
        />
      </Field>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Confirm / Summary
// ─────────────────────────────────────────────────────────────────────────────

export function ConfirmStep() {
  const s = useStyles();
  const { form, set, readOnly, review, approval } = useWiz();
  const outcome = form.outcome ?? outcomeOfApproval(approval.status);
  const adjustments = review.lines.filter(isAdjustment);
  const changed = review.lines.filter(l => l.status === 'Reduced' || l.status === 'Disallowed').sort((a, b) => a.sequence - b.sequence);
  const c = lineCounts(review.lines);
  return (
    <div className={s.step}>
      <div className={s.kv}>
        <Caption1 className={s.muted}>Decision</Caption1>
        <Text weight="semibold" data-testid="confirm-decision">
          {outcome ? INVOICE_OUTCOME_LABEL[outcome] : '—'}
        </Text>
        {outcome === 'approve' && (
          <>
            <Caption1 className={s.muted}>Billed</Caption1>
            <Text>{money(review.totals.billed)}</Text>
            <Caption1 className={s.muted}>Line adjustments</Caption1>
            <Text>{signedMoney(review.totals.lineAdjustments)}</Text>
            <Caption1 className={s.muted}>Invoice adjustments</Caption1>
            <Text data-testid="confirm-invoice-adj">
              {adjustments.length === 0 ? 'None' : `${signedMoney(review.totals.invoiceAdjustments)} · ${adjustments.map(a => a.reason ?? 'no reason').join('; ')}`}
            </Text>
            <Caption1 className={s.muted}>Approved total</Caption1>
            <Text weight="semibold" data-testid="confirm-approved">
              {money(readOnly && approval.approvedAmount !== null ? approval.approvedAmount : review.totals.approved)}
            </Text>
          </>
        )}
        {outcome !== 'approve' && (
          <>
            <Caption1 className={s.muted}>Reason</Caption1>
            <Text data-testid="confirm-reason">{form.reason || '—'}</Text>
          </>
        )}
        <Caption1 className={s.muted}>Line decisions</Caption1>
        <Text>
          {c.decided} decided · {c.clean} clean · {c.flagged} to decide
          {c.pending ? ` · ${c.pending} processing` : ''}
        </Text>
        <Caption1 className={s.muted}>Record</Caption1>
        <Text>
          {readOnly
            ? `Recorded${approval.decidedBy ? ` by ${approval.decidedBy}` : ''}${approval.decidedOn ? ` on ${shortDate(approval.decidedOn.slice(0, 10))}` : ''}.`
            : 'One Decision Record names you and lists the line decisions. Totals are frozen and the invoice becomes read-only.'}
        </Text>
        {!readOnly && outcome === 'approve' && approval.undecidedCleanLines > 0 && (
          <>
            <Caption1 className={s.muted}>Clean lines</Caption1>
            <Text data-testid="confirm-clean-note">
              {approval.undecidedCleanLines} clean {approval.undecidedCleanLines === 1 ? 'line has' : 'lines have'} no decision: approving accepts{' '}
              {approval.undecidedCleanLines === 1 ? 'it' : 'them'} as billed (accepted in bulk, not counted as reviewer labels).
            </Text>
          </>
        )}
      </div>
      {!readOnly && (
        <NoteField
          value={form.note}
          onChange={v => set({ note: v, noteError: null })}
          serverError={form.noteError}
          readOnly={form.locked}
          ariaLabel="Note for the decision"
          testId="decision-note"
        />
      )}
      {!readOnly && form.locked && (
        <Caption1 className={s.muted} data-testid="decision-note-locked">
          This decision was saved in part, or no answer came back. Repeating it sends the same reason and note, so they cannot be changed now. To decide
          differently, go Back and choose another decision.
        </Caption1>
      )}
      {changed.length > 0 && (
        <Accordion collapsible>
          <AccordionItem value="changed">
            <AccordionHeader size="small">{`Changed lines (${changed.length})`}</AccordionHeader>
            <AccordionPanel>
              <div className={s.changed}>
                {changed.map(l => (
                  <Caption1 key={l.id}>
                    Line {l.sequence} · {l.status} {money(l.amount)} → {money(l.approvedAmount)}
                    {l.reason ? ` · ${l.reason}` : ''}
                  </Caption1>
                ))}
              </div>
            </AccordionPanel>
          </AccordionItem>
        </Accordion>
      )}
    </div>
  );
}
