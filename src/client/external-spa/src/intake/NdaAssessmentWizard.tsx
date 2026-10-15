/**
 * NdaAssessmentWizard - the NDA Assessment intake wizard (r3 task 052, FR-05).
 *
 * Built on the shared `WizardShell` (SprkModal wizard preset, ADR-050) like `CreateRecordWizard`. Three steps -
 * Counterparty, Purpose and terms, Timing and review - over the field set in `ndaModel.ts` (design note section 1.2 as
 * modified by the owner answers of 2026-10-12: review AND issue paths via `ndaAction`, no department question, no
 * document generation). Submits through `submitServiceRequest`; a failure leaves the wizard on its last step with every
 * answer intact and the server's message in the shell's error bar.
 *
 * Review path: the requester is told to add the counterparty's draft under Documents after submitting (task 054); no file
 * goes through this wizard. Fluent v9 tokens only (ADR-021).
 */
import * as React from 'react';
import { MessageBar, MessageBarBody } from '@fluentui/react-components';
import { WizardShell } from '@spaarke/ui-components/components/Wizard/WizardShell';
import type { IWizardStepConfig } from '@spaarke/ui-components/components/Wizard/wizardShellTypes';
import { INTAKE_REQUEST_TYPE, intakeErrorMessage, submitServiceRequest } from '../api/intake-client';
import { useOpenRecord } from '../components/detail/RecordOpener';
import { addDaysIso, todayIso } from './intakeValidation';
import { CheckboxRow, FormRows, RadioRow, ReviewList, SelectRow, TextRow, useTouched } from './intakeFormParts';
import { intakeSuccessConfig } from './intakeSuccess';
import type { IntakeWizardProps } from './intakeRegistry';
import {
  NDA_ACTIONS,
  NDA_COUNTERPARTY_TYPES,
  NDA_DISCLOSURE_DIRECTIONS,
  NDA_LIMITS,
  NDA_SENSITIVE_INFO,
  NDA_STEP_FIELDS,
  NDA_TERMS,
  buildNdaAnswers,
  emptyNdaForm,
  isIssue,
  isReview,
  ndaReviewRows,
  ndaTitle,
  showsNeededByReason,
  validateNda,
  type NdaForm,
} from './ndaModel';

const anyError = (errors: Record<string, string>, keys: readonly string[]): boolean => keys.some(k => k in errors);

export const NdaAssessmentWizard: React.FC<IntakeWizardProps> = ({ open, onClose, onOpenServiceRequests }) => {
  const [form, setForm] = React.useState<NdaForm>(emptyNdaForm);
  const { shows, touch } = useTouched();
  const openRecord = useOpenRecord();

  const today = todayIso();
  const errors = validateNda(form, today);
  const set = <K extends keyof NdaForm>(key: K, value: NdaForm[K]): void =>
    setForm(prev => ({ ...prev, [key]: value }));

  // Props every text/choice row takes for one field.
  const row = (key: keyof NdaForm & string) => ({
    error: errors[key],
    showError: shows(key),
    onBlur: () => touch(key),
  });

  const onFinish = async () => {
    // The Finish button is disabled while invalid; this is the last guard before anything is sent.
    if (Object.keys(validateNda(form, todayIso())).length > 0) {
      throw new Error('Some answers need attention. Go back and check them.');
    }
    const title = ndaTitle(form);
    let requestId: string;
    try {
      const created = await submitServiceRequest({
        requestType: INTAKE_REQUEST_TYPE.ndaAssessment,
        title,
        answers: buildNdaAnswers(form, todayIso()),
      });
      requestId = created.sprk_servicerequestid;
    } catch (err) {
      throw new Error(intakeErrorMessage(err));
    }
    const name = form.counterpartyName.trim();
    return intakeSuccessConfig({
      heading: 'NDA request sent to Legal',
      requestId,
      onClose,
      onOpenServiceRequests,
      openRecord,
      paragraphs: [
        <>
          Your request &ldquo;{title}&rdquo; has been created. You can follow its progress in{' '}
          <strong>Service Requests</strong>.
        </>,
        isReview(form) ? (
          <>
            <strong>Next step:</strong> open the request and add the other party&rsquo;s draft under{' '}
            <strong>Documents</strong>. Legal will start the review once the draft is there.
          </>
        ) : (
          <>Legal will use your details to issue our standard NDA to {form.counterpartySignerName.trim()}.</>
        ),
        <>Please don&rsquo;t share confidential information with {name} until the NDA is signed.</>,
      ],
    });
  };

  const steps: IWizardStepConfig[] = [
    {
      id: 'counterparty',
      label: 'Counterparty',
      canAdvance: () => !anyError(errors, NDA_STEP_FIELDS.counterparty),
      renderContent: () => (
        <FormRows>
          <RadioRow
            label="What do you need?"
            required
            options={NDA_ACTIONS}
            value={form.ndaAction}
            onChange={v => set('ndaAction', v)}
            {...row('ndaAction')}
          />
          <TextRow
            label="Who is the other party?"
            required
            hint="Full legal name if you know it, e.g. “Contoso Ltd”. For a confidential deal you can use the project codename. This name appears in the request title."
            value={form.counterpartyName}
            onChange={v => set('counterpartyName', v)}
            maxLength={NDA_LIMITS.counterpartyName.max}
            {...row('counterpartyName')}
          />
          <SelectRow
            label="What is your relationship with them?"
            required
            options={NDA_COUNTERPARTY_TYPES}
            value={form.counterpartyType}
            onChange={v => set('counterpartyType', v)}
            {...row('counterpartyType')}
          />
          {form.counterpartyType === 'other' && (
            <TextRow
              label="Describe the relationship"
              required
              value={form.counterpartyTypeOther}
              onChange={v => set('counterpartyTypeOther', v)}
              maxLength={NDA_LIMITS.counterpartyTypeOther.max}
              {...row('counterpartyTypeOther')}
            />
          )}
          <TextRow
            label="Where is the other party based?"
            hint="Country or state. Helps Legal choose the governing law."
            value={form.counterpartyCountry}
            onChange={v => set('counterpartyCountry', v)}
            maxLength={NDA_LIMITS.counterpartyCountry.max}
            {...row('counterpartyCountry')}
          />
        </FormRows>
      ),
    },
    {
      id: 'terms',
      label: 'Purpose and terms',
      canAdvance: () => !anyError(errors, NDA_STEP_FIELDS.terms),
      renderContent: () => (
        <FormRows>
          <TextRow
            label="What is the NDA for?"
            required
            multiline
            hint="What will be discussed or shared, and why, e.g. “Evaluate their analytics platform for the Q3 pilot”."
            value={form.purpose}
            onChange={v => set('purpose', v)}
            maxLength={NDA_LIMITS.purpose.max}
            {...row('purpose')}
          />
          <RadioRow
            label="Who will share confidential information?"
            required
            hint="Sets whether the NDA is mutual or one-way."
            options={NDA_DISCLOSURE_DIRECTIONS}
            value={form.disclosureDirection}
            onChange={v => set('disclosureDirection', v)}
            {...row('disclosureDirection')}
          />
          {isReview(form) && (
            <MessageBar intent="info">
              <MessageBarBody>
                After you submit, open the request and add the other party&rsquo;s draft under{' '}
                <strong>Documents</strong>. Legal can&rsquo;t review it without the file.
              </MessageBarBody>
            </MessageBar>
          )}
          <CheckboxRow
            label="Will any of the following be shared?"
            hint="Tick everything that applies. Don’t paste the information itself."
            options={NDA_SENSITIVE_INFO}
            value={form.sensitiveInfo}
            onChange={codes => {
              // `none` excludes the others: ticking it clears them, ticking another clears it.
              const justAdded = codes.find(c => !form.sensitiveInfo.includes(c));
              set('sensitiveInfo', justAdded === 'none' ? ['none'] : codes.filter(c => c !== 'none'));
            }}
            error={errors.sensitiveInfo}
            showError={shows('sensitiveInfo')}
          />
          {isIssue(form) && (
            <>
              <TextRow
                label="Signer name (other party)"
                required
                value={form.counterpartySignerName}
                onChange={v => set('counterpartySignerName', v)}
                maxLength={NDA_LIMITS.signerName.max}
                {...row('counterpartySignerName')}
              />
              <TextRow
                label="Signer email (other party)"
                required
                type="email"
                value={form.counterpartySignerEmail}
                onChange={v => set('counterpartySignerEmail', v)}
                maxLength={NDA_LIMITS.signerEmail.max}
                {...row('counterpartySignerEmail')}
              />
              <RadioRow
                label="Term"
                required
                options={NDA_TERMS}
                value={form.term}
                onChange={v => set('term', v)}
                {...row('term')}
              />
              <TextRow
                label="Governing law"
                required
                hint="Country or state, e.g. “Delaware, USA”."
                value={form.governingLaw}
                onChange={v => set('governingLaw', v)}
                maxLength={NDA_LIMITS.governingLaw.max}
                {...row('governingLaw')}
              />
            </>
          )}
        </FormRows>
      ),
    },
    {
      id: 'timing',
      label: 'Timing and review',
      canAdvance: () => Object.keys(errors).length === 0,
      renderContent: () => (
        <FormRows>
          <TextRow
            label="When do you need it signed?"
            type="date"
            required={isIssue(form)}
            hint={isIssue(form) ? undefined : 'Leave blank if there is no fixed date.'}
            value={form.neededBy}
            onChange={v => set('neededBy', v)}
            maxLength={10}
            min={today}
            max={addDaysIso(today, NDA_LIMITS.neededByDays)}
            {...row('neededBy')}
          />
          {showsNeededByReason(form, today) && (
            <TextRow
              label="What’s driving the date?"
              hint="e.g. “Kick-off meeting on the 14th”."
              value={form.neededByReason}
              onChange={v => set('neededByReason', v)}
              maxLength={NDA_LIMITS.neededByReason.max}
              {...row('neededByReason')}
            />
          )}
          <TextRow
            label="Anything else Legal should know?"
            multiline
            hint="e.g. terms they have already asked for."
            value={form.notes}
            onChange={v => set('notes', v)}
            maxLength={NDA_LIMITS.notes.max}
            {...row('notes')}
          />
          <ReviewList rows={ndaReviewRows(form, today)} />
        </FormRows>
      ),
    },
  ];

  return (
    <WizardShell
      open={open}
      title="NDA Assessment"
      steps={steps}
      onClose={onClose}
      onFinish={onFinish}
      finishLabel="Submit request"
      finishingLabel="Submitting…"
    />
  );
};

export default NdaAssessmentWizard;
