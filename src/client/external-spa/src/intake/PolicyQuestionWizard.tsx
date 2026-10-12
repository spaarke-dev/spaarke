/**
 * PolicyQuestionWizard - the Policy Question intake wizard (r3 task 052, FR-05).
 *
 * Built on the shared `WizardShell` (SprkModal wizard preset, ADR-050). Two steps - Your question, Timing and review -
 * over the field set in `policyModel.ts` (design note section 2.2 as modified by the owner answers of 2026-10-12: no
 * department question, no incident routing or link, the fixed topic list in `policyTopics.ts`). Submits through
 * `submitServiceRequest`; a failure leaves the wizard on its last step with every answer intact and the server's message
 * in the shell's error bar.
 *
 * Privacy (design note sections 2.3 and 2.5): the question and background live only in the field-secured answers; the
 * unsecured request title carries the topic label only. Fluent v9 tokens only (ADR-021).
 */
import * as React from 'react';
import { WizardShell } from '@spaarke/ui-components/components/Wizard/WizardShell';
import type { IWizardStepConfig } from '@spaarke/ui-components/components/Wizard/wizardShellTypes';
import { INTAKE_REQUEST_TYPE, intakeErrorMessage, submitServiceRequest } from '../api/intake-client';
import { useOpenRecord } from '../components/detail/RecordOpener';
import { addDaysIso, todayIso } from './intakeValidation';
import { FormRows, RadioRow, ReviewList, SelectRow, TextRow, useTouched } from './intakeFormParts';
import { intakeSuccessConfig } from './intakeSuccess';
import type { IntakeWizardProps } from './intakeRegistry';
import { getPolicyTopics, POLICY_TOPIC_OTHER } from './policyTopics';
import {
  POLICY_LIMITS,
  POLICY_SITUATIONS,
  POLICY_STEP_FIELDS,
  POLICY_URGENCIES,
  asksNeededBy,
  buildPolicyAnswers,
  emptyPolicyForm,
  policyReviewRows,
  policyTitle,
  policyTopicText,
  validatePolicy,
  type PolicyForm,
} from './policyModel';

const anyError = (errors: Record<string, string>, keys: readonly string[]): boolean => keys.some(k => k in errors);

export const PolicyQuestionWizard: React.FC<IntakeWizardProps> = ({ open, onClose, onOpenServiceRequests }) => {
  const [form, setForm] = React.useState<PolicyForm>(emptyPolicyForm);
  const { shows, touch } = useTouched();
  const openRecord = useOpenRecord();

  const today = todayIso();
  const errors = validatePolicy(form, today);
  const set = <K extends keyof PolicyForm>(key: K, value: PolicyForm[K]): void =>
    setForm(prev => ({ ...prev, [key]: value }));
  const row = (key: keyof PolicyForm & string) => ({
    error: errors[key],
    showError: shows(key),
    onBlur: () => touch(key),
  });

  const onFinish = async () => {
    if (Object.keys(validatePolicy(form, todayIso())).length > 0) {
      throw new Error('Some answers need attention. Go back and check them.');
    }
    let requestId: string;
    try {
      const created = await submitServiceRequest({
        requestType: INTAKE_REQUEST_TYPE.policyQuestion,
        title: policyTitle(form),
        answers: buildPolicyAnswers(form),
      });
      requestId = created.sprk_servicerequestid;
    } catch (err) {
      throw new Error(intakeErrorMessage(err));
    }
    return intakeSuccessConfig({
      heading: 'Question sent to Legal',
      requestId,
      onClose,
      onOpenServiceRequests,
      openRecord,
      paragraphs: [
        <>
          We&rsquo;ve logged your {policyTopicText(form)} question. You&rsquo;ll get the answer as a message on the
          request; you can follow it in <strong>Service Requests</strong>.
        </>,
        form.urgency === 'urgent' ? <>If this can&rsquo;t wait, contact Legal directly as well.</> : null,
        <>
          If you have related emails or documents, add them under <strong>Documents</strong> on the request.
        </>,
      ].filter((p): p is React.ReactElement => p !== null),
    });
  };

  const steps: IWizardStepConfig[] = [
    {
      id: 'question',
      label: 'Your question',
      canAdvance: () => !anyError(errors, POLICY_STEP_FIELDS.question),
      renderContent: () => (
        <FormRows>
          <SelectRow
            label="What is it about?"
            required
            hint="Pick the closest. Legal will re-categorise if needed. The topic appears in the request title."
            options={getPolicyTopics()}
            value={form.topic}
            onChange={v => set('topic', v)}
            {...row('topic')}
          />
          {form.topic === POLICY_TOPIC_OTHER && (
            <TextRow
              label="What topic?"
              required
              value={form.topicOther}
              onChange={v => set('topicOther', v)}
              maxLength={POLICY_LIMITS.topicOther.max}
              {...row('topicOther')}
            />
          )}
          <TextRow
            label="What do you need to know?"
            required
            multiline
            hint="One clear question, e.g. “Can I accept a conference ticket worth about $400 from a supplier?”"
            value={form.question}
            onChange={v => set('question', v)}
            maxLength={POLICY_LIMITS.question.max}
            {...row('question')}
          />
          <TextRow
            label="Background"
            multiline
            rows={5}
            hint="Who is involved, amounts, dates, and anything you’ve already been told. Leave out personal details Legal doesn’t need."
            value={form.background}
            onChange={v => set('background', v)}
            maxLength={POLICY_LIMITS.background.max}
            {...row('background')}
          />
          <RadioRow
            label="Which best describes this?"
            required
            options={POLICY_SITUATIONS}
            value={form.situation}
            onChange={v => set('situation', v)}
            {...row('situation')}
          />
        </FormRows>
      ),
    },
    {
      id: 'timing',
      label: 'Timing and review',
      canAdvance: () => Object.keys(errors).length === 0,
      renderContent: () => (
        <FormRows>
          <RadioRow
            label="When do you need an answer?"
            required
            hint="Legal will confirm the timing."
            options={POLICY_URGENCIES}
            value={form.urgency}
            onChange={v => set('urgency', v)}
            {...row('urgency')}
          />
          {asksNeededBy(form) && (
            <TextRow
              label="By when?"
              type="date"
              required
              value={form.neededBy}
              onChange={v => set('neededBy', v)}
              maxLength={10}
              min={today}
              max={addDaysIso(today, POLICY_LIMITS.neededByDays)}
              {...row('neededBy')}
            />
          )}
          <ReviewList rows={policyReviewRows(form)} />
        </FormRows>
      ),
    },
  ];

  return (
    <WizardShell
      open={open}
      title="Policy Question"
      steps={steps}
      onClose={onClose}
      onFinish={onFinish}
      finishLabel="Submit question"
      finishingLabel="Submitting…"
    />
  );
};

export default PolicyQuestionWizard;
