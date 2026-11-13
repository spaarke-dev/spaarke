/**
 * The wizard frame both wizards share (ebilling-r1 task 084b): the shared `WizardShell` with the four steps
 *
 *   Procedure | Rule  ->  On this record  ->  Impact  ->  Confirm
 *
 * plus the explicit close, the "N of M" nav (‹ ›) through the filtered list, 089's removal preview where the change removes an override, and
 * the write itself. The two wizards supply the first two steps, the plan (`plan.ts`) and the words; the frame owns everything that touches
 * the BFF, so there is one place where "a refusal keeps the wizard open and re-reads nothing" is true.
 *
 * Authorization is the server's: the frame never inspects who the caller is. A refusal (403, 404, 409, 422 ...) is thrown out of `onFinish`,
 * which `WizardShell` shows in its error bar with the wizard still open; nothing is re-read because nothing changed. The lists are re-read
 * (`onChanged`) after a write succeeded, and also when a plan of two calls got only as far as its first (the record's override changed).
 *
 * While a save runs the frame blocks the header ‹ › and the close (the shared shell only disables its own footer buttons), so a result or a
 * partial-save message can never land under another row, and the user is never left unaware of a half-done switch. After a partial save the
 * wizard holds the row it was opened on, which is out of date: Finish stays off until the panel hands back the re-read row.
 *
 * Task 084c adds an optional fifth step, "New values", between "On this record" and Impact: the rule wizard asks for it (`valuesStep.active`)
 * while "Change for this record" is chosen, and the shell inserts and removes it as a dynamic step.
 *
 * Task 084d-4: Impact reads the dry run (`dryRunApi.ts`) of the plan, for the firm and date the lists were read for, in place of 084b's
 * interim impact. It is advisory: a refusal is shown and Confirm stays reachable, because the write route decides. 089's removal preview
 * is now only the fallback for a refused dry run of a plan that removes (see the preview section below).
 *
 * Read-only (task 084g, EB-D-221): when the wizard has no choice left that makes a call the lists' answers allow (fix round 1, B1), it
 * shows the row and its choices with every choice disabled and the reason above them, and has no Impact or Confirm step and no Finish: Back,
 * Next and Close only. With `readOnly` false the frame is exactly 084b's,
 * and a refusal at Finish is still shown with its title (the flag is a hint; the route decides).
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { CheckmarkCircle48Filled } from '@fluentui/react-icons';
import { WizardShell, type IWizardShellHandle, type IWizardShellProps, type IWizardStepConfig, type IWizardSuccessConfig } from '@spaarke/ui-components';
import type { CoreRoute, ProceduresQuery } from '../../services/recordProceduresApi';
import { failureText, type OverridesApi, type RemovalPreview, type WriteFailure, type WriteResult } from '../../services/overridesApi';
import { dryRunBody, dryRunRequestOf, requestFirmOf, type DryRunApi, type DryRunView } from '../../services/dryRunApi';
import { executePlan, removalOf, type Plan, type PlanRun } from './plan';
import { ConfirmStep, ImpactStep, O21_LINE, READ_ONLY_LINE, useWizardStyles, type DryRunState, type PreviewState } from './steps';

/**
 * Told to the panel after a write: `done` (the plan ran to its end) or `partial` (its first call was accepted and the next refused). For a
 * `partial` the panel answers (a promise) whether the open wizard now holds the re-read row of the same key, so a retry sends only what is left.
 */
export type ChangeOutcome = 'done' | 'partial';
export type OnChanged = (outcome: ChangeOutcome) => void | Promise<boolean>;

/** The "N of M" nav of the modal header, as `WizardShell` takes it. */
export interface WizardNav {
  index: number;
  total: number;
  onNavigate: (dir: 'prev' | 'next') => void;
}

/** The step order of the shell, for the dynamic "New values" step. */
const STEP_ORDER = ['review', 'mode', 'values', 'impact', 'confirm'];

/** The optional "New values" step (task 084c): its content, and whether the user may leave it. */
export interface ValuesStep {
  /** True while the choice that needs the step is selected. */
  active: boolean;
  content: React.ReactNode;
  canAdvance: boolean;
}

export interface WizardFrameProps {
  title: string;
  /** Changes when the wizard is about another row: the shell starts over in place (no remount of the dialog). */
  resetKey: string;
  nav?: WizardNav;
  api: OverridesApi;
  /** Task 084d-4: the dry run the Impact step reads. */
  dryRunApi: DryRunApi;
  /** Task 084d-4: the firm and date the lists were read for; the dry run is asked for the same ones. */
  query: ProceduresQuery;
  core: CoreRoute;
  recordId: string;
  /** Task 084g: the wizard only shows (choices disabled, the reason, no Impact, no Confirm, no Finish). The wizards disable their choices. */
  readOnly: boolean;

  /** The first step ("Procedure" / "Rule"): the row as the server describes it. */
  reviewLabel: string;
  reviewStep: React.ReactNode;
  /** The second step: the choices. */
  modeStep: React.ReactNode;
  /** True when the choice differs from now and its inputs are complete: the user may go on. */
  canChoose: boolean;
  /** What the change does to the row, from the row's own data. */
  plan: Plan;
  before: string;
  after: string;
  /** Extra Impact content (the Procedure wizard's gap notes). */
  impactExtra?: React.ReactNode;
  confirmRows: ReadonlyArray<readonly [string, React.ReactNode]>;

  finishLabel: string;
  successTitle: string;
  successBody: string;
  /** Said after a plan of two calls stopped between them. */
  partialNote: string;

  /** Both lists must be read again: the write succeeded (or part of it did). */
  onChanged: OnChanged;
  onClose: () => void;
  /**
   * Words added to a refusal's text (a hint for one code), or null. `partial`: the plan's first call was accepted before this refusal, so the
   * partial note follows; a hint must not contradict it (084c fix round 1, F3-4).
   */
  refusalHint?: (failure: WriteFailure, partial: boolean) => string | null;
  /** Task 084c: the "New values" step, for the choice that needs one. */
  valuesStep?: ValuesStep;
  /** Told of every refusal of the write (before the error bar shows it), so a form can put the server's field errors on its fields. */
  onRefused?: (failure: WriteFailure) => void;
}

export const WizardFrame: React.FC<WizardFrameProps> = props => {
  const { title, resetKey, nav, api, core, recordId, plan, readOnly } = props;
  const s = useWizardStyles();

  // Live values for the step predicates and `onFinish`: the shell keeps calling the closures it was handed on an earlier render.
  const live = React.useRef(props);
  live.current = props;

  // ── The dry run of the proposal (task 084d-4) ──────────────────────────
  // Keyed by the exact body it sends, so a change of the proposal (Back, another choice or reason) asks again. Answers are tagged with the
  // sequence number of their ask and only the latest ask's answer is kept: an earlier answer arriving late is dropped (the race of two asks).
  const mapping = dryRunRequestOf(plan);
  const dryRunKey = mapping.ok ? `${props.query.firmId ?? ''}|${props.query.date}|${dryRunBody(mapping.request)}` : null;
  const [dryRunFetched, setDryRunFetched] = React.useState<{ key: string; result: WriteResult<DryRunView> } | null>(null);
  const dryRunSeq = React.useRef(0);
  const dryRunInFlight = React.useRef<string | null>(null);

  const dryRun: DryRunState = !mapping.ok
    ? { state: 'none' }
    : dryRunFetched?.key === dryRunKey
      ? dryRunFetched.result.ok
        ? { state: 'ready', value: dryRunFetched.result.value }
        : { state: 'failed', failure: dryRunFetched.result.failure, requestNamesFirm: requestFirmOf(mapping.request) !== null }
      : { state: 'loading' };
  const dryRunRef = React.useRef(dryRun);
  dryRunRef.current = dryRun;

  const loadDryRun = React.useCallback(() => {
    const p = live.current;
    const m = dryRunRequestOf(p.plan);
    if (!m.ok) return;
    const key = `${p.query.firmId ?? ''}|${p.query.date}|${dryRunBody(m.request)}`;
    if (dryRunInFlight.current === key) return;
    dryRunInFlight.current = key;
    const seq = ++dryRunSeq.current;
    void p.dryRunApi.dryRun(p.core, p.recordId, p.query, m.request).then(result => {
      // Stale: a later ask (another proposal, or another row) was made meanwhile; its answer, not this one, is shown.
      if (seq !== dryRunSeq.current) return;
      dryRunInFlight.current = null;
      setDryRunFetched({ key, result });
    });
  }, []);

  // ── The removal preview of the override the plan removes ────────────────
  // Superseded by the dry run, which carries the same fields (`closes.d170` / `kinds` for `closesRule`, `closes.warning`,
  // `affectedOpenInvoices.count` and `.text`) from the same selection (084d-2b). It is kept as a FALLBACK, asked only when the dry run of a plan
  // that removes was refused: the dry run asks more of the caller than the preview does (finance.read on the record, the remove route's
  // privileges, one Read check over every row of both lists, so one unreadable row is a 503: O-K1a), and without the fallback the section 15
  // warning would not be shown at all before a changed rule's removal in that case.
  const removeId = dryRun.state === 'failed' ? removalOf(plan) : null;
  const [fetched, setFetched] = React.useState<{ id: string; result: { ok: true; value: RemovalPreview } | { ok: false; failure: WriteFailure } } | null>(null);
  const inFlight = React.useRef<string | null>(null);

  const preview: PreviewState = !removeId
    ? { state: 'none' }
    : fetched?.id === removeId
      ? fetched.result.ok
        ? { state: 'ready', value: fetched.result.value }
        : { state: 'failed', failure: fetched.result.failure }
      : { state: 'loading' };
  const previewRef = React.useRef(preview);
  previewRef.current = preview;

  const loadPreview = React.useCallback(() => {
    const id = removalOf(live.current.plan);
    if (!id || inFlight.current === id) return;
    inFlight.current = id;
    void api.removalPreview(core, recordId, id).then(result => {
      if (inFlight.current === id) inFlight.current = null;
      setFetched({ id, result });
    });
  }, [api, core, recordId]);

  const retryPreview = React.useCallback(() => setFetched(null), []);

  // A partial save leaves the wizard on a row that is out of date; it is held (Finish off) until the panel hands back the re-read row.
  const [stale, setStale] = React.useState(false);
  const staleRef = React.useRef(false);
  staleRef.current = stale;
  // True from the first call of a save to its answer: nothing may move the wizard to another row or close it meanwhile.
  const saving = React.useRef(false);

  // The wizard moved to another row: forget the previous row's answer.
  const [seenKey, setSeenKey] = React.useState(resetKey);
  if (seenKey !== resetKey) {
    setSeenKey(resetKey);
    setFetched(null);
    setDryRunFetched(null);
    dryRunSeq.current += 1; // an answer still in flight for the previous row is stale
    dryRunInFlight.current = null;
    setStale(false);
    inFlight.current = null;
  }

  // ── The write ────────────────────────────────────────────────────────────
  const onFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const p = live.current;
    // A read-only wizard has no Finish; should one ever be reached, it writes nothing.
    if (p.readOnly) throw new Error(READ_ONLY_LINE);
    saving.current = true;
    let run: PlanRun;
    try {
      run = await executePlan(p.api, p.core, p.recordId, p.plan);
    } finally {
      saving.current = false;
    }
    if (!run.ok) {
      const failure = run.failure as WriteFailure;
      p.onRefused?.(failure);
      const text = [failureText(failure), p.refusalHint?.(failure, run.completed > 0)].filter((x): x is string => !!x).join(' ');
      if (run.completed > 0) {
        // The first call of the plan was accepted and the second was refused: the record's override changed, so the lists are read again,
        // and the wizard (still on the row it was opened with, whose override is now gone) waits for the re-read row.
        setStale(true);
        void Promise.resolve(p.onChanged('partial')).then(adopted => {
          if (adopted === true) setStale(false);
        });
        throw new Error(`${text} ${p.partialNote}`);
      }
      // Nothing changed: the wizard stays open and nothing is re-read.
      throw new Error(text);
    }
    void p.onChanged('done');
    const notes = [...run.removed, ...run.changed].flatMap(r => [r.message, r.affectedText]).filter((x): x is string => !!x);
    return {
      icon: <CheckmarkCircle48Filled />,
      title: p.successTitle,
      body: (
        <div data-testid="success-body">
          <p>{p.successBody}</p>
          {notes.map(n => (
            <p key={n}>{n}</p>
          ))}
          <p data-testid="success-o21">{O21_LINE}</p>
        </div>
      ),
      actions: (
        <Button appearance="primary" onClick={p.onClose} data-testid="wizard-done">
          Done
        </Button>
      ),
    };
  }, []);

  // ── Steps ────────────────────────────────────────────────────────────────
  const review: IWizardStepConfig = { id: 'review', label: props.reviewLabel, renderContent: () => live.current.reviewStep, canAdvance: () => true };
  const readOnlySteps: IWizardStepConfig[] = [
    review,
    {
      id: 'mode',
      label: 'On this record',
      renderContent: () => (
        <div className={s.step}>
          <MessageBar intent="info" data-testid="read-only-reason">
            <MessageBarBody>{READ_ONLY_LINE}</MessageBarBody>
          </MessageBar>
          {live.current.modeStep}
        </div>
      ),
      canAdvance: () => false,
    },
  ];
  // The "New values" step is added and removed through the shell's handle, which the "On this record" step hands over when it renders (the
  // user can only choose there). Its config reads the live props like every other step.
  const handle = React.useRef<IWizardShellHandle | null>(null);
  const valuesConfig = React.useRef<IWizardStepConfig>({
    id: 'values',
    label: 'New values',
    renderContent: () => live.current.valuesStep?.content,
    canAdvance: () => live.current.valuesStep?.canAdvance === true,
  });
  const valuesActive = !readOnly && props.valuesStep?.active === true;
  React.useEffect(() => {
    if (!handle.current) return;
    if (valuesActive) handle.current.addDynamicStep(valuesConfig.current, STEP_ORDER);
    else handle.current.removeDynamicStep('values');
  }, [valuesActive]);

  const steps: IWizardStepConfig[] = readOnly ? readOnlySteps : [
    review,
    {
      id: 'mode',
      label: 'On this record',
      renderContent: h => {
        handle.current = h;
        return live.current.modeStep;
      },
      canAdvance: () => live.current.canChoose,
    },
    {
      id: 'impact',
      label: 'Impact',
      renderContent: () => (
        <ImpactStep
          before={live.current.before}
          after={live.current.after}
          dryRun={dryRunRef.current}
          onLoadDryRun={loadDryRun}
          preview={previewRef.current}
          onLoadPreview={loadPreview}
          onRetryPreview={retryPreview}
          extra={live.current.impactExtra}
        />
      ),
      // Next waits while a read is in flight, so an answer (a warning above all) is shown before Confirm. A refused or failed answer never
      // blocks: the dry run and the removal preview are advisory, and the write route decides the save (084d-4; the design note sets no
      // blocking case). 084b blocked a removal whose preview was refused; that is no longer so.
      canAdvance: () => dryRunRef.current.state !== 'loading' && previewRef.current.state !== 'loading',
    },
    { id: 'confirm', label: 'Confirm', renderContent: () => <ConfirmStep rows={live.current.confirmRows} />, canAdvance: () => !staleRef.current },
  ];

  // The shell disables only its own footer buttons while a save runs; the header ‹ › and × are guarded here.
  const guardedNav = nav ? { ...nav, onBeforeNavigate: () => !saving.current } : undefined;
  const guardedClose = React.useCallback(() => {
    if (!saving.current) live.current.onClose();
  }, []);

  // Read-only: Back · Next through the two steps, and Close on the last; no Cancel, no Finish. The shell takes its steps at open and at a
  // reset, so a change of mode is a reset (its own key).
  const readOnlyFooter = React.useCallback<NonNullable<IWizardShellProps['footer']>>(
    ctx => ({
      start: null,
      end: (
        <>
          {!ctx.isFirstStep && (
            <Button appearance="secondary" onClick={ctx.goBack}>
              Back
            </Button>
          )}
          {ctx.isLastStep ? (
            <Button appearance="primary" onClick={ctx.close}>
              Close
            </Button>
          ) : (
            <Button appearance="primary" onClick={ctx.goNext}>
              Next
            </Button>
          )}
        </>
      ),
    }),
    []
  );

  return (
    <WizardShell
      open
      title={title}
      steps={steps}
      nav={guardedNav}
      resetKey={readOnly ? `${resetKey}:read-only` : resetKey}
      onClose={guardedClose}
      onFinish={onFinish}
      finishLabel={props.finishLabel}
      finishingLabel="Saving…"
      footer={readOnly ? readOnlyFooter : undefined}
    />
  );
};
