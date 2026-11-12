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
 */

import * as React from 'react';
import { Button } from '@fluentui/react-components';
import { CheckmarkCircle48Filled } from '@fluentui/react-icons';
import { WizardShell, type IWizardStepConfig, type IWizardSuccessConfig } from '@spaarke/ui-components';
import type { CoreRoute } from '../../services/recordProceduresApi';
import { failureText, type OverridesApi, type RemovalPreview, type WriteFailure } from '../../services/overridesApi';
import { executePlan, removalOf, type Plan, type PlanRun } from './plan';
import { ConfirmStep, ImpactStep, O21_LINE, type PreviewState } from './steps';

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

export interface WizardFrameProps {
  title: string;
  /** Changes when the wizard is about another row: the shell starts over in place (no remount of the dialog). */
  resetKey: string;
  nav?: WizardNav;
  api: OverridesApi;
  core: CoreRoute;
  recordId: string;

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
  /** Words added to a refusal's text (a hint for one code), or null. */
  refusalHint?: (failure: WriteFailure) => string | null;
}

export const WizardFrame: React.FC<WizardFrameProps> = props => {
  const { title, resetKey, nav, api, core, recordId, plan } = props;

  // Live values for the step predicates and `onFinish`: the shell keeps calling the closures it was handed on an earlier render.
  const live = React.useRef(props);
  live.current = props;

  // ── The removal preview of the override the plan removes ────────────────
  const removeId = removalOf(plan);
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
    setStale(false);
    inFlight.current = null;
  }

  // ── The write ────────────────────────────────────────────────────────────
  const onFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const p = live.current;
    saving.current = true;
    let run: PlanRun;
    try {
      run = await executePlan(p.api, p.core, p.recordId, p.plan);
    } finally {
      saving.current = false;
    }
    if (!run.ok) {
      const failure = run.failure as WriteFailure;
      const text = [failureText(failure), p.refusalHint?.(failure)].filter((x): x is string => !!x).join(' ');
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
    const notes = run.removed.flatMap(r => [r.message, r.affectedText]).filter((x): x is string => !!x);
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
  const steps: IWizardStepConfig[] = [
    { id: 'review', label: props.reviewLabel, renderContent: () => live.current.reviewStep, canAdvance: () => true },
    { id: 'mode', label: 'On this record', renderContent: () => live.current.modeStep, canAdvance: () => live.current.canChoose },
    {
      id: 'impact',
      label: 'Impact',
      renderContent: () => (
        <ImpactStep
          before={live.current.before}
          after={live.current.after}
          preview={previewRef.current}
          onLoadPreview={loadPreview}
          onRetryPreview={retryPreview}
          extra={live.current.impactExtra}
        />
      ),
      // A removal is confirmed only after its preview was shown.
      canAdvance: () => previewRef.current.state === 'none' || previewRef.current.state === 'ready',
    },
    { id: 'confirm', label: 'Confirm', renderContent: () => <ConfirmStep rows={live.current.confirmRows} />, canAdvance: () => !staleRef.current },
  ];

  // The shell disables only its own footer buttons while a save runs; the header ‹ › and × are guarded here.
  const guardedNav = nav ? { ...nav, onBeforeNavigate: () => !saving.current } : undefined;
  const guardedClose = React.useCallback(() => {
    if (!saving.current) live.current.onClose();
  }, []);

  return (
    <WizardShell
      open
      title={title}
      steps={steps}
      nav={guardedNav}
      resetKey={resetKey}
      onClose={guardedClose}
      onFinish={onFinish}
      finishLabel={props.finishLabel}
      finishingLabel="Saving…"
    />
  );
};
