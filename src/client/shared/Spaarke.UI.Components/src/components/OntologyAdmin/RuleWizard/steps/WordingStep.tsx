/**
 * WordingStep - step 3 of the rule wizard: what the signal says (spaarke-ontology-platform-r1 task 102; D-22, D-23).
 *
 * The headline is plain text and is NOT templated (D-23). The message may quote values from the record; the ONLY values it may quote
 * are the ones `validate` returned as `templateEligibleFields` for this condition. They are offered as buttons; nothing else is
 * offered, and a value typed by hand that is not on the list is flagged (the BFF refuses it as well).
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Input, Text, Textarea } from '@fluentui/react-components';
import { RefusableField, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';

export interface WordingStepProps extends StepBaseProps {
  /**
   * The fields the message may quote, from the BFF for the condition as it now stands. Null when the condition has not compiled yet
   * (so the BFF has named none).
   */
  eligibleFields: readonly string[] | null;
  /** The BFF is being asked which fields the message may quote. */
  checking: boolean;
}

/** The `{{name}}` tokens a template quotes. */
export function tokensOf(template: string): string[] {
  const found: string[] = [];
  const pattern = /\{\{\s*([^{}\s]+)\s*\}\}/g;
  let match = pattern.exec(template);
  while (match !== null) {
    if (!found.includes(match[1])) {
      found.push(match[1]);
    }
    match = pattern.exec(template);
  }
  return found;
}

export const WordingStep: React.FC<WordingStepProps> = ({ draft, onChange, errors, locked, eligibleFields, checking }) => {
  const styles = useStepStyles();
  const templateRef = React.useRef<HTMLTextAreaElement>(null);

  const insertToken = (field: string) => {
    const token = `{{${field}}}`;
    const element = templateRef.current;
    onChange(d => {
      const at = element === null ? d.messageTemplate.length : Math.min(element.selectionStart, d.messageTemplate.length);
      return { ...d, messageTemplate: `${d.messageTemplate.slice(0, at)}${token}${d.messageTemplate.slice(at)}` };
    });
  };

  const notOffered =
    eligibleFields === null ? [] : tokensOf(draft.messageTemplate).filter(token => !eligibleFields.includes(token));

  return (
    <div className={styles.step} data-testid="rule-wizard-wording">
      <Text className={styles.intro}>Write what the signal says. The headline is shown as written.</Text>

      <RefusableField
        label="Headline"
        field="shortHeadline"
        errors={errors}
        required
        hint="Plain text. Values from the record are not filled in here."
      >
        <Input
          value={draft.shortHeadline}
          disabled={locked}
          onChange={(_event, data) => onChange(d => ({ ...d, shortHeadline: data.value }))}
          data-testid="rule-wizard-headline"
        />
      </RefusableField>

      <RefusableField
        label="Message"
        field="messageTemplate"
        errors={errors}
        required
        hint="You can quote a value from the record by choosing it below."
      >
        <Textarea
          ref={templateRef}
          rows={4}
          resize="vertical"
          value={draft.messageTemplate}
          disabled={locked}
          onChange={(_event, data) => onChange(d => ({ ...d, messageTemplate: data.value }))}
          data-testid="rule-wizard-message"
        />
      </RefusableField>

      <section className={styles.group} aria-label="Values the message can quote" data-testid="rule-wizard-eligible">
        <Text weight="semibold">Values the message can quote</Text>
        {eligibleFields === null ? (
          <Text className={styles.muted} data-testid="rule-wizard-eligible-pending">
            {checking
              ? 'Checking which values this condition allows.'
              : 'The values you can quote are listed once the condition is complete. Finish the Condition step first.'}
          </Text>
        ) : eligibleFields.length === 0 ? (
          <Text className={styles.muted} data-testid="rule-wizard-eligible-none">
            This condition has no value the message can quote. Write the message as plain text.
          </Text>
        ) : (
          <div className={styles.inline}>
            {eligibleFields.map(field => (
              <Button
                key={field}
                size="small"
                appearance="secondary"
                disabled={locked}
                onClick={() => insertToken(field)}
                data-testid={`rule-wizard-eligible-${field}`}
              >
                {field}
              </Button>
            ))}
          </div>
        )}
        {notOffered.length > 0 ? (
          <Text size={200} className={styles.error} data-testid="rule-wizard-wording-unknown">
            The message quotes {notOffered.map(t => `{{${t}}}`).join(', ')}, which this condition does not offer.
          </Text>
        ) : null}
      </section>

      <RefusableField
        label="Proposed action"
        field="proposedAction"
        errors={errors}
        required
        hint="What the person who sees the signal should do. Plain text."
      >
        <Textarea
          rows={2}
          resize="vertical"
          value={draft.proposedAction}
          disabled={locked}
          onChange={(_event, data) => onChange(d => ({ ...d, proposedAction: data.value }))}
          data-testid="rule-wizard-proposed-action"
        />
      </RefusableField>
    </div>
  );
};

WordingStep.displayName = 'WordingStep';
