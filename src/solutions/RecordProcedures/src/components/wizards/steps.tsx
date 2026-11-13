/**
 * The step contents the Procedure and Rule wizards share (ebilling-r1 task 084b; prototype `parts.tsx`): the choice list, the key/value block,
 * the Impact step (what changes, 089's removal preview where a removal is involved, the O-21 line) and the Confirm step. Presentational: the
 * wizards own the form, the plan and the calls.
 *
 * Fluent v9 semantic tokens only (ADR-021). Nothing here decides who may write: a refusal is shown by the wizard with the server's title.
 *
 * Task 084d-4: the Impact step shows the dry run (084d-2a/2b) in place of 084b's interim before/after: Now -> "If saved now" for both lists,
 * the D-170 and section 15 warnings and the change's close message (the server's words), and the affected open invoices ("can't be
 * counted" when the count is null). The choice's own before/after line shows only while there is no answer. 089's removal preview is shown
 * only when the dry run of a plan that removes was refused (WizardFrame says why).
 */

import * as React from 'react';
import { Button, Caption1, MessageBar, MessageBarBody, MessageBarTitle, Radio, RadioGroup, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import type { RemovalPreview, WriteFailure } from '../../services/overridesApi';
import type { DryRunView } from '../../services/dryRunApi';
import { NOT_ALLOWED_BOTH } from './plan';
import { ADVISORY_LINE, NOT_LISTED, UNCHECKED_CLOSE_LINE, affectedLine, dryRunFailureText, impactChanges, previewFailureText, type ImpactChange } from './impact';

export const useWizardStyles = makeStyles({
  step: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL, maxWidth: '640px' },
  kv: { display: 'grid', gridTemplateColumns: '160px minmax(0, 1fr)', rowGap: tokens.spacingVerticalS, columnGap: tokens.spacingHorizontalL },
  stack: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  muted: { color: tokens.colorNeutralForeground3 },
  choice: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
  choiceBody: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS, paddingLeft: tokens.spacingHorizontalXXL, paddingBottom: tokens.spacingVerticalS },
  change: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  arrow: { color: tokens.colorNeutralForeground3 },
});

/** Task O-21 (revised 2026-10-21): a change re-checks nothing. Shown on the Impact step and on the success screen of every change. */
export const O21_LINE =
  'Nothing is re-checked automatically. Affected open invoices show "The procedures that apply to this invoice have changed; re-check to apply them." Invoices checked from now on use the change.';

/** Task 084g (EB-D-221): the reason a read-only wizard gives, above its disabled choices (the same words a choice needing both kinds of call gives). */
export const READ_ONLY_LINE = NOT_ALLOWED_BOTH;

// ─────────────────────────────────────────────────────────────────────────────
// Key / value block
// ─────────────────────────────────────────────────────────────────────────────

export const KeyValues: React.FC<{ rows: ReadonlyArray<readonly [string, React.ReactNode]>; testId?: string }> = ({ rows, testId }) => {
  const s = useWizardStyles();
  return (
    <div className={s.kv} data-testid={testId}>
      {rows.map(([k, v]) => (
        <React.Fragment key={k}>
          <Caption1 className={s.muted}>{k}</Caption1>
          <div>{typeof v === 'string' ? <Text>{v}</Text> : v}</div>
        </React.Fragment>
      ))}
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// The choice list ("On this record")
// ─────────────────────────────────────────────────────────────────────────────

export interface ChoiceOption<T extends string> {
  value: T;
  label: string;
  caption: string;
  disabled?: boolean;
  /** The choice's own fields (a version, a reason, a firm), shown while it is selected. */
  body?: React.ReactNode;
}

export function ChoiceGroup<T extends string>({
  ariaLabel,
  options,
  value,
  onChange,
}: {
  ariaLabel: string;
  options: ReadonlyArray<ChoiceOption<T>>;
  value: T | undefined;
  onChange: (value: T) => void;
}) {
  const s = useWizardStyles();
  return (
    <RadioGroup
      aria-label={ariaLabel}
      value={value ?? ''}
      onChange={(_, d) => {
        // A disabled choice cannot be chosen, however the event arrived.
        if (options.some(o => o.value === d.value && o.disabled)) return;
        onChange(d.value as T);
      }}
      className={s.choice}
    >
      {options.map(o => (
        <div key={o.value} data-testid={`choice-${o.value}`}>
          <Radio
            value={o.value}
            disabled={o.disabled}
            label={
              <span className={s.stack}>
                <Text weight="semibold">{o.label}</Text>
                <Caption1 className={s.muted}>{o.caption}</Caption1>
              </span>
            }
          />
          {value === o.value && o.body && <div className={s.choiceBody}>{o.body}</div>}
        </div>
      ))}
    </RadioGroup>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Impact
// ─────────────────────────────────────────────────────────────────────────────

/** The removal preview of the override the change removes: not asked for, loading, answered, or refused. */
export type PreviewState =
  | { state: 'none' }
  | { state: 'loading' }
  | { state: 'ready'; value: RemovalPreview }
  | { state: 'failed'; failure: WriteFailure };

/** 089's removal preview, in words: the warning (a changed rule's removal closes findings), then how many open invoices the caller can see. */
export function previewLines(p: RemovalPreview): string[] {
  const lines: string[] = [];
  if (p.warning) lines.push(p.warning);
  if (p.affectedText) lines.push(p.affectedText);
  else if (p.visibleOpenInvoices !== null && p.visibleOpenInvoices !== undefined) {
    lines.push(`${p.visibleOpenInvoices} open invoice${p.visibleOpenInvoices === 1 ? '' : 's'} you can see ${p.visibleOpenInvoices === 1 ? 'is' : 'are'} affected.`);
  } else if (p.closesRule) {
    lines.push('How many open invoices are affected could not be counted.');
  } else {
    lines.push('Removing this override closes no findings on open invoices.');
  }
  return lines;
}

/**
 * The dry run of the proposal (task 084d-4): not asked for (the plan changes nothing, or has no dry-run shape), loading, answered, or refused.
 * `requestNamesFirm` scopes R6's wording to a refusal of a request that names a firm.
 */
export type DryRunState =
  | { state: 'none' }
  | { state: 'loading' }
  | { state: 'ready'; value: DryRunView }
  | { state: 'failed'; failure: WriteFailure; requestNamesFirm: boolean };

const ChangeRows: React.FC<{ title: string; rows: readonly ImpactChange[]; testId: string }> = ({ title, rows, testId }) => {
  const s = useWizardStyles();
  if (rows.length === 0) return null;
  return (
    <div className={s.stack} data-testid={testId}>
      <Caption1 className={s.muted}>{title}</Caption1>
      {rows.map(r => (
        <span key={r.key} className={s.change} data-testid={`${testId}-row`}>
          <Text weight="semibold">{r.label}</Text>
          <Text>{r.now ?? NOT_LISTED}</Text>
          <Text className={s.arrow}>→</Text>
          <Text>{r.after ?? NOT_LISTED}</Text>
        </span>
      ))}
    </div>
  );
};

/** The dry run's answer: Now -> After of both lists under the server's label, what closes, the affected open invoices, and the disclosure notes. */
const DryRunAnswer: React.FC<{ view: DryRunView }> = ({ view }) => {
  const s = useWizardStyles();
  const changes = impactChanges(view);
  const { closes } = view;
  return (
    <div className={s.step} data-testid="dryrun">
      <div className={s.stack}>
        {/* The server's label ("If saved now"): After is what the lists would show, never a promise that saving succeeds. */}
        <Caption1 className={s.muted} data-testid="dryrun-label">
          Now → {view.afterLabel}
        </Caption1>
        {changes.procedures.length === 0 && changes.rules.length === 0 && <Text data-testid="dryrun-same">The lists read the same.</Text>}
      </div>
      <ChangeRows title="Procedures" rows={changes.procedures} testId="impact-procedures" />
      <ChangeRows title="Rules" rows={changes.rules} testId="impact-rules" />
      {closes.d170 && (
        <MessageBar intent="warning" layout="multiline" data-testid="impact-d170">
          <MessageBarBody>
            <MessageBarTitle>Open findings close</MessageBarTitle>
            {closes.warning}
          </MessageBarBody>
        </MessageBar>
      )}
      {!closes.d170 && closes.warning && (
        <MessageBar intent="warning" layout="multiline" data-testid="impact-warning">
          <MessageBarBody>{closes.warning}</MessageBarBody>
        </MessageBar>
      )}
      {closes.message && (
        <MessageBar intent="info" layout="multiline" data-testid="impact-message">
          <MessageBarBody>{closes.message}</MessageBarBody>
        </MessageBar>
      )}
      <Text data-testid="impact-affected">{affectedLine(view.affectedOpenInvoices)}</Text>
      {view.disclosure.notes.length > 0 && (
        <div className={s.stack} data-testid="dryrun-notes">
          {view.disclosure.notes.map(n => (
            <Caption1 key={n} className={s.muted}>
              {n}
            </Caption1>
          ))}
        </div>
      )}
    </div>
  );
};

/**
 * A refused dry run, with no fallback behind it (the plan removes nothing, or the refusal is not one the removal preview stands in for): one
 * warning bar with the reason, the advisory line in its own element, and Retry.
 */
const DryRunFailed: React.FC<{ failure: WriteFailure; requestNamesFirm: boolean; onRetry: () => void }> = ({ failure, requestNamesFirm, onRetry }) => (
  // Advisory: a refused or failed preview never blocks Confirm. The write route decides the save (WizardFrame's canAdvance).
  <MessageBar intent="warning" layout="multiline" role="alert" data-testid="dryrun-failed">
    <MessageBarBody>
      <MessageBarTitle>The preview is not shown</MessageBarTitle>
      <div data-testid="dryrun-failed-text">{dryRunFailureText(failure, requestNamesFirm)}</div>
      <div data-testid="dryrun-advisory">{ADVISORY_LINE}</div>
    </MessageBarBody>
    <Button size="small" onClick={onRetry} data-testid="dryrun-retry">
      Retry
    </Button>
  </MessageBar>
);

/**
 * The removal preview standing in for a refused dry run: ONE message either way. Answered, it is the preview alone (what removing the override
 * affects), with a note that the lists' preview was not available; it never sits under "The preview is not shown". Refused or failed, it is one
 * error bar saying both reads failed, with the advisory line in its own element and one Retry that asks for the dry run again.
 */
const RemovalFallback: React.FC<{ dryRun: Extract<DryRunState, { state: 'failed' }>; preview: PreviewState; onRetry: () => void }> = ({ dryRun, preview, onRetry }) => {
  const s = useWizardStyles();
  if (preview.state === 'loading') return <Spinner size="tiny" label="Checking what removing the override affects…" data-testid="preview-loading" />;
  if (preview.state === 'ready') {
    return (
      <MessageBar intent={preview.value.closesRule ? 'warning' : 'info'} layout="multiline" data-testid="removal-preview">
        <MessageBarBody>
          <MessageBarTitle>Removing the current override</MessageBarTitle>
          <div>{previewLines(preview.value).join(' ')}</div>
          <Caption1 className={s.muted} data-testid="removal-preview-note">
            The full preview of the lists could not be read, so only this was checked.
          </Caption1>
        </MessageBarBody>
      </MessageBar>
    );
  }
  if (preview.state !== 'failed') return null;
  return (
    <MessageBar intent="error" layout="multiline" role="alert" data-testid="preview-failed">
      <MessageBarBody>
        <MessageBarTitle>The preview is not shown</MessageBarTitle>
        <div data-testid="dryrun-failed-text">{dryRunFailureText(dryRun.failure, dryRun.requestNamesFirm)}</div>
        <div data-testid="preview-failed-text">What removing the override affects could not be checked either. {previewFailureText(preview.failure)}</div>
        <div data-testid="dryrun-advisory">{ADVISORY_LINE}</div>
      </MessageBarBody>
      <Button size="small" onClick={onRetry} data-testid="preview-retry">
        Retry
      </Button>
    </MessageBar>
  );
};

export const ImpactStep: React.FC<{
  /** The choice in the row's own words, shown only when the dry run has no answer (it is the choice, not a prediction). */
  before: string;
  after: string;
  dryRun: DryRunState;
  /** Ask for the dry run. Idempotent for one proposal: the frame ignores a second ask while one is in flight. */
  onLoadDryRun: () => void;
  /** Forget a failed answer (and the fallback's) so the dry run is asked for again. */
  onRetryDryRun: () => void;
  /** 089's removal preview: asked only after a dry run of a plan that removes was refused with 403, 404 or 503 (see WizardFrame). */
  preview: PreviewState;
  /** Ask for the preview. Idempotent: the wizard ignores a second ask while one is in flight. */
  onLoadPreview: () => void;
  extra?: React.ReactNode;
}> = ({ before, after, dryRun, onLoadDryRun, onRetryDryRun, preview, onLoadPreview, extra }) => {
  const s = useWizardStyles();
  // Each read is asked for when the step is shown with no answer yet for the current proposal; the frame holds the answers.
  const dryRunWaiting = dryRun.state === 'loading';
  React.useEffect(() => {
    if (dryRunWaiting) onLoadDryRun();
  }, [dryRunWaiting, onLoadDryRun]);
  const waiting = preview.state === 'loading';
  React.useEffect(() => {
    if (waiting) onLoadPreview();
  }, [waiting, onLoadPreview]);

  return (
    <div className={s.step} data-testid="impact-step">
      {dryRun.state !== 'ready' && (
        <div className={s.stack}>
          <Caption1 className={s.muted}>Your choice</Caption1>
          <span className={s.change} data-testid="impact-change">
            <Text>{before}</Text>
            <Text className={s.arrow}>→</Text>
            <Text weight="semibold">{after}</Text>
          </span>
        </div>
      )}
      {extra}
      {/* What a screen reader should hear: the answer, or the reason there is none. Busy while a read is in flight. */}
      <div aria-live="polite" aria-busy={dryRun.state === 'loading' || preview.state === 'loading'} className={s.step} data-testid="impact-result">
        {dryRun.state === 'loading' && <Spinner size="tiny" label="Reading what the lists would show…" data-testid="dryrun-loading" />}
        {dryRun.state === 'ready' && <DryRunAnswer view={dryRun.value} />}
        {dryRun.state === 'failed' && preview.state === 'none' && <DryRunFailed failure={dryRun.failure} requestNamesFirm={dryRun.requestNamesFirm} onRetry={onRetryDryRun} />}
        {dryRun.state === 'failed' && preview.state !== 'none' && <RemovalFallback dryRun={dryRun} preview={preview} onRetry={onRetryDryRun} />}
      </div>
      <Caption1 className={s.muted} data-testid="o21-line">
        {O21_LINE}
      </Caption1>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Confirm
// ─────────────────────────────────────────────────────────────────────────────

export const ConfirmStep: React.FC<{
  rows: ReadonlyArray<readonly [string, React.ReactNode]>;
  /** A removal that neither the dry run nor the removal preview could check: say so (it does not block). */
  uncheckedClose?: boolean;
}> = ({ rows, uncheckedClose }) => {
  const s = useWizardStyles();
  return (
    <div className={s.step} data-testid="confirm-step">
      {uncheckedClose && (
        <MessageBar intent="warning" layout="multiline" data-testid="confirm-unchecked">
          <MessageBarBody>{UNCHECKED_CLOSE_LINE}</MessageBarBody>
        </MessageBar>
      )}
      <KeyValues rows={rows} />
      <Caption1 className={s.muted}>{O21_LINE}</Caption1>
    </div>
  );
};
