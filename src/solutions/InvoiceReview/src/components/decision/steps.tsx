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
  decisionFailureMessage,
  newAdjustmentId,
  type AdjustmentMethod,
  type AdjustmentRule,
} from '../../services/decisionApi';
import { approveBlocker, INVOICE_OUTCOME_LABEL, outcomeOfApproval, type InvoiceOutcome } from '../../services/decisionModel';
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
        {option('reject', 'Not payable. The firm cannot resubmit this invoice.', readOnly)}
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

export function AdjustStep() {
  const s = useStyles();
  const { form, set, readOnly, review, api, invoiceId, reload } = useWiz();
  const [rules, setRules] = React.useState<AdjustmentRule[] | null>(null);
  const [rulesUnavailable, setRulesUnavailable] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [repeatable, setRepeatable] = React.useState(false);
  const [removing, setRemoving] = React.useState<ReviewLine | null>(null);
  const [removeBusy, setRemoveBusy] = React.useState(false);
  const [removeError, setRemoveError] = React.useState<string | null>(null);
  const adjustmentId = React.useRef(newAdjustmentId());
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
  const canAdd = !readOnly && !busy && form.adjValue.trim() !== '' && !valueError && form.adjReason.trim() !== '';

  const add = async () => {
    setBusy(true);
    setError(null);
    const r = await api.addAdjustment(invoiceId, {
      adjustmentId: adjustmentId.current,
      method: form.adjMethod,
      value: Number(form.adjValue),
      reason: form.adjReason.trim(),
      ruleId: form.adjRule || null,
    });
    if (!alive.current) return;
    if (r.ok) {
      // The server computed the amount and stored the totals; the page re-reads them.
      adjustmentId.current = newAdjustmentId();
      setRepeatable(false);
      set({ adjValue: '', adjReason: '', adjRule: '' });
      await reload();
      if (!alive.current) return;
    } else {
      setError(decisionFailureMessage(r.failure));
      // A 503 where the line was written but the totals were not: the same request, with the same id, finishes it.
      setRepeatable(r.failure.status === 503);
    }
    setBusy(false);
  };

  const confirmRemove = async () => {
    if (!removing) return;
    setRemoveBusy(true);
    setRemoveError(null);
    const r = await api.removeAdjustment(invoiceId, removing.id);
    if (!alive.current) return;
    setRemoveBusy(false);
    if (r.ok) {
      setRemoving(null);
      await reload();
    } else {
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
                <Button size="small" appearance="subtle" onClick={() => { setRemoveError(null); setRemoving(a); }} data-testid={`remove-adjustment-${a.id}`}>
                  Remove…
                </Button>
              )}
            </div>
          ))}
        </div>
      )}

      {!readOnly && (
        <>
          <RadioGroup layout="horizontal" value={form.adjMethod} onChange={(_, d) => set({ adjMethod: d.value as AdjustmentMethod })} aria-label="Adjustment type">
            <Radio value="percent" label="Percent of fees" />
            <Radio value="fixed" label="Fixed amount" />
          </RadioGroup>
          <div className={s.row}>
            <Field label={form.adjMethod === 'percent' ? 'Percent' : 'Amount'} validationMessage={valueError ?? undefined} validationState={valueError ? 'error' : 'none'}>
              <Input
                type="number"
                value={form.adjValue}
                onChange={(_, d) => set({ adjValue: d.value })}
                contentAfter={form.adjMethod === 'percent' ? '%' : undefined}
                aria-label="Adjustment value"
              />
            </Field>
            <Field label="Rule (optional)" hint={rulesUnavailable ? 'The rule list is not available; you can still add the adjustment without a rule.' : undefined}>
              <Dropdown
                aria-label="Adjustment rule"
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
            <Textarea value={form.adjReason} onChange={(_, d) => set({ adjReason: d.value })} resize="vertical" aria-label="Adjustment reason" maxLength={REASON_MAX} />
          </Field>
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
              <Button appearance="subtle" onClick={() => { set({ adjValue: '', adjReason: '', adjRule: '' }); setError(null); setRepeatable(false); adjustmentId.current = newAdjustmentId(); }}>
                Clear
              </Button>
            )}
          </div>
        </>
      )}

      <Totals />

      {removing && (
        <FormModal
          open
          size="sm"
          title="Remove adjustment"
          submitLabel="Remove"
          busy={removeBusy}
          onClose={() => {
            if (!removeBusy) setRemoving(null);
          }}
          onSubmit={() => void confirmRemove()}
        >
          <Text>
            Remove the adjustment of {signedMoney(removing.amount)}
            {removing.reason ? ` (${removing.reason})` : ''}? The approved total is recalculated by the server.
          </Text>
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
          readOnly={readOnly}
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
  const { form, readOnly, review, approval } = useWiz();
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
