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
 */

import * as React from 'react';
import { Button } from '@fluentui/react-components';
import { CheckmarkCircle48Filled } from '@fluentui/react-icons';
import { WizardShell, type IWizardStepConfig, type IWizardSuccessConfig } from '@spaarke/ui-components';
import type { CoreRoute } from '../../services/recordProceduresApi';
import { failureText, type OverridesApi, type RemovalPreview, type WriteFailure } from '../../services/overridesApi';
import { executePlan, removalOf, type Plan } from './plan';
import { ConfirmStep, ImpactStep, O21_LINE, type PreviewState } from './steps';

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
  onChanged: () => void;
  onClose: () => void;
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

  // The wizard moved to another row: forget the previous row's answer.
  const [seenKey, setSeenKey] = React.useState(resetKey);
  if (seenKey !== resetKey) {
    setSeenKey(resetKey);
    setFetched(null);
    inFlight.current = null;
  }

  // ── The write ────────────────────────────────────────────────────────────
  const onFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const p = live.current;
    const run = await executePlan(p.api, p.core, p.recordId, p.plan);
    if (!run.ok) {
      const failure = run.failure as WriteFailure;
      if (run.completed > 0) {
        // The first call of the plan was accepted and the second was refused: the record's override changed, so the lists are read again.
        p.onChanged();
        throw new Error(`${failureText(failure)} ${p.partialNote}`);
      }
      // Nothing changed: the wizard stays open and nothing is re-read.
      throw new Error(failureText(failure));
    }
    p.onChanged();
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
    { id: 'confirm', label: 'Confirm', renderContent: () => <ConfirmStep rows={live.current.confirmRows} />, canAdvance: () => true },
  ];

  return (
    <WizardShell
      open
      title={title}
      steps={steps}
      nav={nav}
      resetKey={resetKey}
      onClose={props.onClose}
      onFinish={onFinish}
      finishLabel={props.finishLabel}
      finishingLabel="Saving…"
    />
  );
};
