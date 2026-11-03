/**
 * The invoice decision wizard (ebilling-r1 task 111; FR-32, FR-35, D-26 / ADR-050): Approve (with an optional invoice-level adjustment),
 * Reject (final) or Return to firm, in `WizardShell` (modal mode: the wizard preset). Single-field reasons that are not part of a wizard
 * (the blocked-submission Override / Return, the adjustment removal confirm) use `FormModal` elsewhere.
 *
 *   Approve:           Decision → Invoice adjustment (skippable) → Confirm
 *   Reject / Return:   Decision → Reason → [Notify: an optional slot, nothing is built in it] → Confirm
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
import { decisionFailureMessage, isDecisionIncomplete, type ApprovalState, type InvoiceDecisionApi } from '../../services/decisionApi';
import {
  INVOICE_OUTCOME_LABEL,
  invoiceDecisionRecord,
  outcomeOfApproval,
  type InvoiceOutcome,
} from '../../services/decisionModel';
import { money } from '../../services/reviewModel';
import type { InvoiceReview } from '../../services/invoiceReviewApi';
import { AdjustStep, ConfirmStep, OutcomeStep, ReasonStep } from './steps';
import { EMPTY_FORM, WizardContext, adjustmentFormDirty, type WizardCtx, type WizardForm } from './wizardContext';

const useStyles = makeStyles({
  ok: { color: tokens.colorPaletteGreenForeground1 },
});

const ORDER = ['outcome', 'adjust', 'reason', 'notify', 'confirm'];
const FIXED = new Set(['outcome', 'confirm']);

/** The optional firm-notification step: a slot only (131 / 132 fill it). Shown on the reject and return paths, skippable. */
export type NotifyStep = Omit<IWizardStepConfig, 'id' | 'isSkippable'>;

export interface InvoiceDecisionWizardProps {
  open: boolean;
  onClose: () => void;
  review: InvoiceReview;
  approval: ApprovalState;
  api: InvoiceDecisionApi;
  invoiceId: string;
  /** Re-read the review after the decision (the page shows the server's state). */
  reload: () => Promise<unknown>;
  /** Re-read the approval state after the decision. */
  refreshApproval: () => Promise<unknown>;
  notifyStep?: NotifyStep;
}

function stepIds(outcome: InvoiceOutcome | undefined, hasNotify: boolean): string[] {
  if (outcome === 'approve') return ['outcome', 'adjust', 'confirm'];
  if (outcome === 'reject' || outcome === 'return') return hasNotify ? ['outcome', 'reason', 'notify', 'confirm'] : ['outcome', 'reason', 'confirm'];
  return ['outcome', 'confirm'];
}

const STATUS_TONE: Readonly<Record<InvoiceOutcome, ConsoleStatusOverride['tone']>> = { approve: 'success', reject: 'critical', return: 'warning' };
const STATUS_LABEL: Readonly<Record<InvoiceOutcome, string>> = { approve: 'Approved', reject: 'Rejected', return: 'Returned to firm' };

export function InvoiceDecisionWizard(props: InvoiceDecisionWizardProps) {
  const { open, onClose, review, approval, api, invoiceId, reload, refreshApproval, notifyStep } = props;
  const s = useStyles();

  // Frozen at mount: the wizard that opened on a pending invoice does not turn read-only under the user when its own decision refreshes the page.
  const [readOnly] = React.useState(() => approval.status !== 'Pending' || review.header.status !== 'InReview');
  const recorded = React.useMemo(() => invoiceDecisionRecord(review), [review]);
  const [form, setForm] = React.useState<WizardForm>(() => {
    if (readOnly) return { ...EMPTY_FORM, outcome: outcomeOfApproval(approval.status), reason: recorded?.reason ?? '' };
    return { ...EMPTY_FORM, outcome: approval.canApprove ? 'approve' : undefined };
  });
  const set = React.useCallback((patch: Partial<WizardForm>) => setForm(f => ({ ...f, ...patch })), []);

  const ctx: WizardCtx = {
    form,
    set,
    readOnly,
    review,
    approval,
    api,
    invoiceId,
    reload,
    onOutcome: (outcome, handle) => syncSteps(outcome, handle),
  };
  const live = React.useRef<WizardCtx>(ctx);
  live.current = ctx;
  const hasNotify = !!notifyStep && !readOnly;

  const configFor = React.useCallback(
    (id: string): IWizardStepConfig => {
      switch (id) {
        case 'outcome':
          return {
            id,
            label: 'Decision',
            renderContent: handle => <OutcomeStep handle={handle} />,
            canAdvance: () => live.current.readOnly || (live.current.form.outcome !== undefined && (live.current.form.outcome !== 'approve' || live.current.approval.canApprove)),
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
          return { ...(notifyStep as NotifyStep), id, isSkippable: true };
        default:
          return { id: 'confirm', label: readOnly ? 'Recorded decision' : 'Confirm', renderContent: () => <ConfirmStep />, canAdvance: () => true };
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

  const onFinish = async (): Promise<IWizardSuccessConfig> => {
    const { form: f, api: a, invoiceId: id } = live.current;
    const outcome = f.outcome;
    if (!outcome) throw new Error('Choose a decision first.');
    const result =
      outcome === 'approve' ? await a.approve(id) : outcome === 'reject' ? await a.reject(id, f.reason.trim()) : await a.returnToFirm(id, f.reason.trim());
    if (!result.ok) {
      // Part of the change may be saved (503 incomplete): show the server's state, then the message tells the user to repeat. Any other
      // refusal (403, 409, a validation code) changes nothing on the page; the message carries the server's detail and its code.
      if (isDecisionIncomplete(result.failure)) await Promise.all([refreshApproval(), reload()]);
      throw new Error(decisionFailureMessage(result.failure));
    }
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
