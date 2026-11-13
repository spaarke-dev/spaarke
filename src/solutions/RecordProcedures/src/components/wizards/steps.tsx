/**
 * The step contents the Procedure and Rule wizards share (ebilling-r1 task 084b; prototype `parts.tsx`): the choice list, the key/value block,
 * the Impact step (what changes, 089's removal preview where a removal is involved, the O-21 line) and the Confirm step. Presentational: the
 * wizards own the form, the plan and the calls.
 *
 * Fluent v9 semantic tokens only (ADR-021). Nothing here decides who may write: a refusal is shown by the wizard with the server's title.
 */

import * as React from 'react';
import { Button, Caption1, MessageBar, MessageBarBody, MessageBarTitle, Radio, RadioGroup, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import type { RemovalPreview, WriteFailure } from '../../services/overridesApi';
import { failureText } from '../../services/overridesApi';
import { NOT_ALLOWED_BOTH } from './plan';

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

export const ImpactStep: React.FC<{
  before: string;
  after: string;
  preview: PreviewState;
  /** Ask for the preview. Idempotent: the wizard ignores a second ask while one is in flight. */
  onLoadPreview: () => void;
  /** Forget a failed answer so the preview is asked for again. */
  onRetryPreview: () => void;
  extra?: React.ReactNode;
}> = ({ before, after, preview, onLoadPreview, onRetryPreview, extra }) => {
  const s = useWizardStyles();
  // The preview is asked for when the step is shown with no answer yet (and again after Retry); the wizard holds the answer.
  const waiting = preview.state === 'loading';
  React.useEffect(() => {
    if (waiting) onLoadPreview();
  }, [waiting, onLoadPreview]);

  return (
    <div className={s.step} data-testid="impact-step">
      <div className={s.stack}>
        <Caption1 className={s.muted}>This record</Caption1>
        <span className={s.change} data-testid="impact-change">
          <Text>{before}</Text>
          <Text className={s.arrow}>→</Text>
          <Text weight="semibold">{after}</Text>
        </span>
      </div>
      {extra}
      {preview.state === 'loading' && <Spinner size="tiny" label="Checking what removing the override affects…" data-testid="preview-loading" />}
      {preview.state === 'ready' && (
        <MessageBar intent={preview.value.closesRule ? 'warning' : 'info'} layout="multiline" data-testid="removal-preview">
          <MessageBarBody>
            <MessageBarTitle>Removing the current override</MessageBarTitle>
            {previewLines(preview.value).join(' ')}
          </MessageBarBody>
        </MessageBar>
      )}
      {preview.state === 'failed' && (
        <MessageBar intent="error" layout="multiline" role="alert" data-testid="preview-failed">
          <MessageBarBody>
            <MessageBarTitle>The removal could not be checked</MessageBarTitle>
            {failureText(preview.failure)}
          </MessageBarBody>
          <Button size="small" onClick={onRetryPreview} data-testid="preview-retry">
            Retry
          </Button>
        </MessageBar>
      )}
      <Caption1 className={s.muted} data-testid="o21-line">
        {O21_LINE}
      </Caption1>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Confirm
// ─────────────────────────────────────────────────────────────────────────────

export const ConfirmStep: React.FC<{ rows: ReadonlyArray<readonly [string, React.ReactNode]> }> = ({ rows }) => {
  const s = useWizardStyles();
  return (
    <div className={s.step} data-testid="confirm-step">
      <KeyValues rows={rows} />
      <Caption1 className={s.muted}>{O21_LINE}</Caption1>
    </div>
  );
};
