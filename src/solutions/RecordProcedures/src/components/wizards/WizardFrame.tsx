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
import { failureText, toWriteFailure, type OverridesApi, type RemovalPreview, type WriteFailure, type WriteResult } from '../../services/overridesApi';
import { dryRunBody, dryRunRequestOf, requestFirmOf, type DryRunApi, type DryRunRequest, type DryRunView } from '../../services/dryRunApi';
import { executePlan, removalOf, type Plan, type PlanRun } from './plan';
import { asksRemovalPreview } from './impact';
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

/** The key a dry run's answer is kept under: the exact request, for the firm and date the lists were read with. */
const dryRunKeyOf = (query: ProceduresQuery, request: DryRunRequest): string => `${query.firmId ?? ''}|${query.date}|${dryRunBody(request)}`;

/** A read that throws (a client other than the real one) is a failure like any other, so the step never waits for it for ever. */
const settle = <T,>(call: Promise<WriteResult<T>>): Promise<WriteResult<T>> => call.then(r => r, (err: unknown) => ({ ok: false as const, failure: toWriteFailure(err) }));

const abortAll = (inFlight: Map<string, AbortController>): void => {
  inFlight.forEach(c => c.abort());
  inFlight.clear();
};

export const WizardFrame: React.FC<WizardFrameProps> = props => {
  const { title, resetKey, nav, plan, readOnly } = props;
  const s = useWizardStyles();

  // Live values for the step predicates and `onFinish`: the shell keeps calling the closures it was handed on an earlier render.
  const live = React.useRef(props);
  live.current = props;

  // ── Reads in flight (task 084d-4, review round 1) ──────────────────────
  // Each read has its own abort controller. They are all abandoned when the wizard closes (unmount) or moves to another row, and an answer
  // that arrives after that is dropped: nothing sets state once the wizard is gone. The clients also give a read up after their timeout.
  const mounted = React.useRef(true);
  const epoch = React.useRef(0);
  const dryRunInFlight = React.useRef(new Map<string, AbortController>());
  const previewInFlight = React.useRef(new Map<string, AbortController>());
  React.useEffect(() => {
    mounted.current = true;
    const dry = dryRunInFlight.current;
    const prev = previewInFlight.current;
    return () => {
      mounted.current = false;
      abortAll(dry);
      abortAll(prev);
    };
  }, []);

  // ── The dry run of the proposal (task 084d-4) ──────────────────────────
  // Answers are kept by the exact request they answer (the body, the firm and the date), so a late answer to an earlier proposal is kept for
  // THAT proposal and can never replace the live proposal's. A failed answer stays until Retry forgets it and asks again.
  const mapping = dryRunRequestOf(plan);
  const dryRunKey = mapping.ok ? dryRunKeyOf(props.query, mapping.request) : null;
  const [dryRunAnswers, setDryRunAnswers] = React.useState<ReadonlyMap<string, WriteResult<DryRunView>>>(() => new Map());
  const answer = dryRunKey === null ? undefined : dryRunAnswers.get(dryRunKey);

  const dryRun: DryRunState = !mapping.ok
    ? { state: 'none' }
    : answer
      ? answer.ok
        ? { state: 'ready', value: answer.value }
        : { state: 'failed', failure: answer.failure, requestNamesFirm: requestFirmOf(mapping.request) !== null }
      : { state: 'loading' };
  const dryRunRef = React.useRef(dryRun);
  dryRunRef.current = dryRun;

  const loadDryRun = React.useCallback(() => {
    const p = live.current;
    const m = dryRunRequestOf(p.plan);
    if (!m.ok) return;
    const key = dryRunKeyOf(p.query, m.request);
    if (dryRunInFlight.current.has(key)) return;
    const ctrl = new AbortController();
    dryRunInFlight.current.set(key, ctrl);
    const asked = epoch.current;
    void settle(p.dryRunApi.dryRun(p.core, p.recordId, p.query, m.request, ctrl.signal)).then(result => {
      if (dryRunInFlight.current.get(key) === ctrl) dryRunInFlight.current.delete(key);
      if (!mounted.current || asked !== epoch.current || ctrl.signal.aborted) return;
      setDryRunAnswers(prev => new Map(prev).set(key, result));
    });
  }, []);

  // ── The removal preview of the override the plan removes ────────────────
  // Superseded by the dry run, which carries the same fields (`closes.d170` / `kinds` for `closesRule`, `closes.warning`,
  // `affectedOpenInvoices.count` and `.text`) from the same selection (084d-2b). It is kept as a FALLBACK, asked only after a dry run of a plan
  // that removes was refused with 403, 404 or 503 (`asksRemovalPreview`): the dry run asks more of the caller than the preview does
  // (finance.read on the record, the remove route's privileges, one Read check over every row of both lists, so one unreadable row is a 503:
  // O-K1a), and without the fallback the section 15 warning would not be shown at all before a changed rule's removal in that case. A bad
  // request, a conflict, a timeout or a network failure say nothing about the preview, so it is not asked then.
  const removeId = dryRun.state === 'failed' && asksRemovalPreview(dryRun.failure) ? removalOf(plan) : null;
  const [previewAnswers, setPreviewAnswers] = React.useState<ReadonlyMap<string, WriteResult<RemovalPreview>>>(() => new Map());
  const previewAnswer = removeId ? previewAnswers.get(removeId) : undefined;

  const preview: PreviewState = !removeId
    ? { state: 'none' }
    : previewAnswer
      ? previewAnswer.ok
        ? { state: 'ready', value: previewAnswer.value }
        : { state: 'failed', failure: previewAnswer.failure }
      : { state: 'loading' };
  const previewRef = React.useRef(preview);
  previewRef.current = preview;

  const loadPreview = React.useCallback(() => {
    const p = live.current;
    const id = removalOf(p.plan);
    if (!id || previewInFlight.current.has(id)) return;
    const ctrl = new AbortController();
    previewInFlight.current.set(id, ctrl);
    const asked = epoch.current;
    void settle(p.api.removalPreview(p.core, p.recordId, id, ctrl.signal)).then(result => {
      if (previewInFlight.current.get(id) === ctrl) previewInFlight.current.delete(id);
      if (!mounted.current || asked !== epoch.current || ctrl.signal.aborted) return;
      setPreviewAnswers(prev => new Map(prev).set(id, result));
    });
  }, []);

  /** Retry of a failed answer: forget it (and the fallback's), so the dry run is asked again. */
  const retryDryRun = React.useCallback(() => {
    const p = live.current;
    const m = dryRunRequestOf(p.plan);
    if (m.ok) {
      const key = dryRunKeyOf(p.query, m.request);
      setDryRunAnswers(prev => {
        const next = new Map(prev);
        next.delete(key);
        return next;
      });
    }
    setPreviewAnswers(new Map());
  }, []);

  // A partial save leaves the wizard on a row that is out of date; it is held (Finish off) until the panel hands back the re-read row.
  const [stale, setStale] = React.useState(false);
  const staleRef = React.useRef(false);
  staleRef.current = stale;
  // True from the first call of a save to its answer: nothing may move the wizard to another row or close it meanwhile.
  const saving = React.useRef(false);

  // The wizard moved to another row: forget the previous row's answers, and abandon the reads still in flight for it.
  const [seenKey, setSeenKey] = React.useState(resetKey);
  if (seenKey !== resetKey) {
    setSeenKey(resetKey);
    setDryRunAnswers(new Map());
    setPreviewAnswers(new Map());
    epoch.current += 1;
    abortAll(dryRunInFlight.current);
    abortAll(previewInFlight.current);
    setStale(false);
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

  // A removal that nothing checked: neither the dry run nor the fallback preview said what the change closes. Confirm says so; it does not block.
  const uncheckedClose = removalOf(plan) !== null && dryRun.state === 'failed' && preview.state !== 'ready';
  const uncheckedCloseRef = React.useRef(uncheckedClose);
  uncheckedCloseRef.current = uncheckedClose;

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
          onRetryDryRun={retryDryRun}
          preview={previewRef.current}
          onLoadPreview={loadPreview}
          extra={live.current.impactExtra}
        />
      ),
      // Next waits while a read is in flight, so an answer (a warning above all) is shown before Confirm. A refused or failed answer never
      // blocks: the dry run and the removal preview are advisory, and the write route decides the save (084d-4; the design note sets no
      // blocking case). 084b blocked a removal whose preview was refused; that is no longer so. A read that does not answer is given up on
      // by the clients at their timeout (a failure), so Next is never held longer than that.
      canAdvance: () => dryRunRef.current.state !== 'loading' && previewRef.current.state !== 'loading',
    },
    {
      id: 'confirm',
      label: 'Confirm',
      renderContent: () => <ConfirmStep rows={live.current.confirmRows} uncheckedClose={uncheckedCloseRef.current} />,
      canAdvance: () => !staleRef.current,
    },
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
