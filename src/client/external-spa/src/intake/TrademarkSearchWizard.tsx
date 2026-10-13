/**
 * TrademarkSearchWizard - the Trademark Search intake wizard (r3 task 053, FR-05).
 *
 * Built on the shared `WizardShell` (SprkModal wizard preset, ADR-050) like the 052 wizards. Three steps - The proposed
 * mark, How and where it will be used, Status and timing - over the field set in `trademarkModel.ts` (design note
 * section 4.2 as modified by the owner answers of 2026-10-12: no department question, the fixed territory list in
 * `trademarkTerritories.ts`, no class question). Submits through `submitServiceRequest`; a failure leaves the wizard on
 * its last step with every answer intact and the server's message (or the sign-in message on a 401) in the shell's error
 * bar.
 *
 * Privacy (sections 4.3 and 4.5): the proposed mark is the unsecured request title by design and the field says so;
 * the goods and services, meaning and notes live only in the field-secured answers. A logo file is added under Documents
 * after submitting (task 054). Fluent v9 tokens only (ADR-021).
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
import { getTrademarkTerritories } from './trademarkTerritories';
import {
  TRADEMARK_CURRENT_USES,
  TRADEMARK_LIMITS,
  TRADEMARK_MARK_FORMS,
  TRADEMARK_MARK_ROLES,
  TRADEMARK_STEP_FIELDS,
  asksOtherTerritories,
  buildTrademarkAnswers,
  emptyTrademarkForm,
  hasLogo,
  isPublicUse,
  neededByMax,
  trademarkReviewRows,
  trademarkTitle,
  validateTrademark,
  type TrademarkForm,
} from './trademarkModel';

const anyError = (errors: Record<string, string>, keys: readonly string[]): boolean => keys.some(k => k in errors);

export const TrademarkSearchWizard: React.FC<IntakeWizardProps> = ({ open, onClose, onOpenServiceRequests }) => {
  const [form, setForm] = React.useState<TrademarkForm>(emptyTrademarkForm);
  const { shows, touch } = useTouched();
  const openRecord = useOpenRecord();

  const today = todayIso();
  const errors = validateTrademark(form, today);
  const set = <K extends keyof TrademarkForm>(key: K, value: TrademarkForm[K]): void =>
    setForm(prev => ({ ...prev, [key]: value }));
  const row = (key: keyof TrademarkForm & string) => ({
    error: errors[key],
    showError: shows(key),
    onBlur: () => touch(key),
  });

  const onFinish = async () => {
    if (Object.keys(validateTrademark(form, todayIso())).length > 0) {
      throw new Error('Some answers need attention. Go back and check them.');
    }
    let requestId: string;
    try {
      const created = await submitServiceRequest({
        requestType: INTAKE_REQUEST_TYPE.trademarkSearch,
        title: trademarkTitle(form),
        answers: buildTrademarkAnswers(form),
      });
      requestId = created.sprk_servicerequestid;
    } catch (err) {
      throw new Error(intakeErrorMessage(err));
    }
    const mark = form.proposedMark.trim();
    return intakeSuccessConfig({
      heading: 'Trademark search requested',
      requestId,
      onClose,
      onOpenServiceRequests,
      openRecord,
      paragraphs: [
        <>
          Legal has received your request for &ldquo;{mark}&rdquo; and will let you know what it finds. You can follow
          it in <strong>Service Requests</strong>.
        </>,
        hasLogo(form) ? (
          <>
            <strong>Next step:</strong> add the logo file under <strong>Documents</strong> on the request.
          </>
        ) : null,
        <>
          Until Legal confirms, please don&rsquo;t announce the name, file applications, register domains or order
          packaging or signage. A quick search that finds no conflicts is not the same as clearance.
        </>,
      ].filter((p): p is React.ReactElement => p !== null),
    });
  };

  const steps: IWizardStepConfig[] = [
    {
      id: 'mark',
      label: 'The proposed mark',
      canAdvance: () => !anyError(errors, TRADEMARK_STEP_FIELDS.mark),
      renderContent: () => (
        <FormRows>
          <TextRow
            label="Proposed name or mark"
            required
            hint="Exact spelling, capitalisation and spacing. The mark appears in the request title."
            value={form.proposedMark}
            onChange={v => set('proposedMark', v)}
            maxLength={TRADEMARK_LIMITS.proposedMark.max}
            {...row('proposedMark')}
          />
          <RadioRow
            label="What kind of mark?"
            required
            options={TRADEMARK_MARK_FORMS}
            value={form.markForm}
            onChange={v => set('markForm', v)}
            {...row('markForm')}
          />
          {hasLogo(form) && (
            <MessageBar intent="info">
              <MessageBarBody>
                After you submit, add the logo or design file under <strong>Documents</strong>.
              </MessageBarBody>
            </MessageBar>
          )}
          <TextRow
            label="What does it mean, or how was it chosen?"
            hint="Include meanings in other languages, or say if it’s an acronym or a made-up word."
            value={form.markMeaning}
            onChange={v => set('markMeaning', v)}
            maxLength={TRADEMARK_LIMITS.markMeaning.max}
            {...row('markMeaning')}
          />
        </FormRows>
      ),
    },
    {
      id: 'use',
      label: 'How and where it will be used',
      canAdvance: () => !anyError(errors, TRADEMARK_STEP_FIELDS.use),
      renderContent: () => (
        <FormRows>
          <TextRow
            label="What will it be used for?"
            required
            multiline
            hint="Describe the product or service in plain words. Legal will work out the trademark classes."
            value={form.goodsServices}
            onChange={v => set('goodsServices', v)}
            maxLength={TRADEMARK_LIMITS.goodsServices.max}
            {...row('goodsServices')}
          />
          <SelectRow
            label="What will it name?"
            options={TRADEMARK_MARK_ROLES}
            value={form.markRole}
            onChange={v => set('markRole', v)}
            {...row('markRole')}
          />
          <CheckboxRow
            label="Where will it be used?"
            required
            hint="Where it will be sold, offered or marketed, now and within about 2 years."
            options={getTrademarkTerritories()}
            value={form.territories}
            onChange={codes => {
              touch('territories');
              set('territories', codes);
            }}
            error={errors.territories}
            showError={shows('territories')}
          />
          {asksOtherTerritories(form) && (
            <TextRow
              label="Other countries"
              required
              value={form.territoriesOther}
              onChange={v => set('territoriesOther', v)}
              maxLength={TRADEMARK_LIMITS.territoriesOther.max}
              {...row('territoriesOther')}
            />
          )}
        </FormRows>
      ),
    },
    {
      id: 'status',
      label: 'Status and timing',
      canAdvance: () => Object.keys(errors).length === 0,
      renderContent: () => (
        <FormRows>
          <RadioRow
            label="Is the name already in use?"
            required
            options={TRADEMARK_CURRENT_USES}
            value={form.currentUse}
            onChange={v => set('currentUse', v)}
            {...row('currentUse')}
          />
          {isPublicUse(form) && (
            <>
              <TextRow
                label="Since when?"
                type="date"
                value={form.firstUseDate}
                onChange={v => set('firstUseDate', v)}
                maxLength={10}
                max={today}
                {...row('firstUseDate')}
              />
              <TextRow
                label="Where is it used?"
                hint="Countries or channels."
                value={form.useLocations}
                onChange={v => set('useLocations', v)}
                maxLength={TRADEMARK_LIMITS.useLocations.max}
                {...row('useLocations')}
              />
            </>
          )}
          <TextRow
            label="Planned launch or announcement date"
            type="date"
            value={form.launchDate}
            onChange={v => set('launchDate', v)}
            maxLength={10}
            min={today}
            max={addDaysIso(today, TRADEMARK_LIMITS.launchDays)}
            {...row('launchDate')}
          />
          <TextRow
            label="When do you need the result?"
            type="date"
            hint="Full searches can take a few weeks, so ask early."
            value={form.neededBy}
            onChange={v => set('neededBy', v)}
            maxLength={10}
            min={today}
            max={neededByMax(form, today)}
            {...row('neededBy')}
          />
          <TextRow
            label="Anything else Legal should know?"
            multiline
            hint="e.g. an agency proposed the name."
            value={form.notes}
            onChange={v => set('notes', v)}
            maxLength={TRADEMARK_LIMITS.notes.max}
            {...row('notes')}
          />
          <ReviewList rows={trademarkReviewRows(form)} />
        </FormRows>
      ),
    },
  ];

  return (
    <WizardShell
      open={open}
      title="Trademark Search"
      steps={steps}
      onClose={onClose}
      onFinish={onFinish}
      finishLabel="Submit request"
      finishingLabel="Submitting…"
    />
  );
};

export default TrademarkSearchWizard;
