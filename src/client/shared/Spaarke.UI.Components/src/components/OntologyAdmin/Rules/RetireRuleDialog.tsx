/**
 * RetireRuleDialog - the Retire confirmation for one Ontology rule (task 100; spec FR-57, #43).
 *
 * Retire needs a reason: the button stays disabled until there is one, and the BFF refuses a request without one as well. On confirm
 * the host's `onRetire` writes the rule (Retired + the reason) and closes its open items at once; no Decision Record is written.
 * The dialog then shows what the closure did, and says so plainly when some open items are still open (the nightly pass finishes
 * them) or the closure could not run. A refusal (Dataverse says the caller is not an Ontology Administrator, the rule is already
 * retired, a fault) is shown in the dialog with the rule unchanged.
 *
 * Built on SprkModal (`size="sm"`, `dismiss="alert"`, as the other destructive confirmations); the danger style is the preset's
 * `useDangerButtonClassName`. Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Field, MessageBar, MessageBarBody, Text, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { SprkModal, useDangerButtonClassName } from '../../SprkModal';
import type { RuleClosure, RuleRetiredResult, RulesResult } from './rulesService';

/** The longest reason the BFF accepts (sprk_retiredreason is a 2000-character memo). */
export const MAX_RETIRE_REASON_LENGTH = 2000;

export interface RetireRuleDialogProps {
  open: boolean;
  ruleCode: string;
  ruleName: string;
  /** Retire the rule with this (trimmed, non-empty) reason. Never throws. */
  onRetire: (reason: string) => Promise<RulesResult<RuleRetiredResult>>;
  /** The dialog was closed without retiring (Cancel, or the close control before the answer). */
  onCancel: () => void;
  /** The dialog was closed after the rule was retired; the host reloads. */
  onDone: (result: RuleRetiredResult) => void;
}

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
  },
});

/** What the closure did, in a sentence the admin can act on. */
export function describeClosure(closure: RuleClosure): { intent: 'success' | 'warning'; text: string } {
  if (closure.unavailable) {
    return {
      intent: 'warning',
      text: 'The rule is retired, but its open items could not be closed just now. They close at the next nightly pass.',
    };
  }
  if (closure.complete) {
    return {
      intent: 'success',
      text:
        closure.closed === 0
          ? 'The rule is retired. It had no open items.'
          : `The rule is retired and ${closure.closed} open ${closure.closed === 1 ? 'item was' : 'items were'} closed.`,
    };
  }
  const problems =
    closure.failed + closure.policyFailures > 0
      ? ` ${closure.failed + closure.policyFailures} could not be closed.`
      : '';
  return {
    intent: 'warning',
    text: `The rule is retired and ${closure.closed} open ${closure.closed === 1 ? 'item was' : 'items were'} closed, but some are still open.${problems} The nightly pass finishes them.`,
  };
}

export const RetireRuleDialog: React.FC<RetireRuleDialogProps> = ({ open, ruleCode, ruleName, onRetire, onCancel, onDone }) => {
  const styles = useStyles();
  const dangerClassName = useDangerButtonClassName();
  const [reason, setReason] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | undefined>(undefined);
  const [result, setResult] = React.useState<RuleRetiredResult | undefined>(undefined);

  React.useEffect(() => {
    if (open) {
      setReason('');
      setBusy(false);
      setError(undefined);
      setResult(undefined);
    }
  }, [open]);

  const trimmed = reason.trim();
  const tooLong = trimmed.length > MAX_RETIRE_REASON_LENGTH;
  const canRetire = trimmed.length > 0 && !tooLong && !busy;

  const handleRetire = React.useCallback(async () => {
    setBusy(true);
    setError(undefined);
    const answer = await onRetire(trimmed);
    setBusy(false);
    if (answer.ok) {
      setResult(answer.data);
    } else {
      setError(answer.message);
    }
  }, [onRetire, trimmed]);

  const handleClose = React.useCallback(() => {
    if (busy) {
      return;
    }
    if (result) {
      onDone(result);
    } else {
      onCancel();
    }
  }, [busy, result, onDone, onCancel]);

  const closure = result ? describeClosure(result.closure) : undefined;

  return (
    <SprkModal
      open={open}
      onClose={handleClose}
      title={`Retire ${ruleCode}`}
      size="sm"
      dismiss="alert"
      maximizable={false}
      footerStart={
        result ? undefined : (
          <Button appearance="secondary" onClick={handleClose} disabled={busy}>
            Cancel
          </Button>
        )
      }
      footer={
        result ? (
          <Button appearance="primary" onClick={handleClose}>
            Done
          </Button>
        ) : (
          <Button appearance="primary" className={dangerClassName} onClick={handleRetire} disabled={!canRetire}>
            {busy ? 'Retiring…' : 'Retire rule'}
          </Button>
        )
      }
    >
      <div className={styles.body} data-testid="retire-rule-dialog">
        {closure ? (
          <MessageBar intent={closure.intent} data-testid="retire-rule-result">
            <MessageBarBody>{closure.text}</MessageBarBody>
          </MessageBar>
        ) : (
          <>
            <Text>
              Retiring <strong>{ruleName}</strong> stops it for good and closes its open items now. No decision record is written for them.
              This cannot be undone here.
            </Text>
            <Field
              label="Why is this rule being retired?"
              required
              validationState={tooLong ? 'error' : 'none'}
              validationMessage={tooLong ? `Keep it to ${MAX_RETIRE_REASON_LENGTH} characters.` : undefined}
            >
              <Textarea
                value={reason}
                onChange={(_event, data) => setReason(data.value)}
                resize="vertical"
                rows={4}
                disabled={busy}
                data-testid="retire-rule-reason"
              />
            </Field>
            {error ? (
              <MessageBar intent="error" data-testid="retire-rule-error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            ) : null}
          </>
        )}
      </div>
    </SprkModal>
  );
};

RetireRuleDialog.displayName = 'RetireRuleDialog';
