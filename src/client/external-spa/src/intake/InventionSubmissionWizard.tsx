/**
 * InventionSubmissionWizard - the Invention Submission intake wizard (r3 task 053, FR-05).
 *
 * Built on the shared `WizardShell` (SprkModal wizard preset, ADR-050) like the 052 wizards. Four steps - The invention,
 * Inventors, Disclosure and outside involvement, Review and confirm - over the field set in `inventionModel.ts` (design
 * note section 3.2 as modified by the owner answers of 2026-10-12: no department question). Submits through
 * `submitServiceRequest`; a failure leaves the wizard on its last step with every answer intact and the server's message
 * (or the sign-in message on a 401) in the shell's error bar.
 *
 * Availability to a partner is not decided here: the registry/`isIntakeTypeAvailable` shows the entry point only when the
 * server lists this type in `/me` `intakeRequestTypes`. Privacy (section 3.5): the unsecured request title carries only the
 * non-confidential short title, and the field says so; the technical answers stay in the field-secured answers. Drawings,
 * slides and notes are added under Documents after submitting (task 054). Fluent v9 tokens only (ADR-021).
 */
import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import { AddRegular, DeleteRegular } from '@fluentui/react-icons';
import { WizardShell } from '@spaarke/ui-components/components/Wizard/WizardShell';
import type { IWizardStepConfig } from '@spaarke/ui-components/components/Wizard/wizardShellTypes';
import { INTAKE_REQUEST_TYPE, intakeErrorMessage, submitServiceRequest } from '../api/intake-client';
import { useOpenRecord } from '../components/detail/RecordOpener';
import { addDaysIso, todayIso } from './intakeValidation';
import { ConfirmRow, FormRows, RadioRow, ReviewList, SelectRow, TextRow, useTouched } from './intakeFormParts';
import { intakeSuccessConfig } from './intakeSuccess';
import type { IntakeWizardProps } from './intakeRegistry';
import {
  INVENTION_DISCLOSURE_METHODS,
  INVENTION_LIMITS,
  INVENTION_PRODUCT_USES,
  INVENTION_PUBLIC_DISCLOSURES,
  INVENTION_STEP_FIELDS,
  INVENTION_YES_NO,
  INVENTION_YES_NO_UNSURE,
  buildInventionAnswers,
  emptyCoInventor,
  emptyInventionForm,
  hasDisclosure,
  hasProduct,
  inventionReviewRows,
  inventionTitle,
  showsDisclosureWarning,
  stepHasError,
  validateInvention,
  type InventionForm,
} from './inventionModel';

const useStyles = makeStyles({
  inventors: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  inventor: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  inventorHead: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
  hint: { color: tokens.colorNeutralForeground3 },
  error: { color: tokens.colorPaletteRedForeground1 },
});

export const InventionSubmissionWizard: React.FC<IntakeWizardProps> = ({ open, onClose, onOpenServiceRequests }) => {
  const s = useStyles();
  const [form, setForm] = React.useState<InventionForm>(emptyInventionForm);
  const { shows, touch } = useTouched();
  const openRecord = useOpenRecord();

  const today = todayIso();
  const errors = validateInvention(form, today);
  const set = <K extends keyof InventionForm>(key: K, value: InventionForm[K]): void =>
    setForm(prev => ({ ...prev, [key]: value }));

  // Props every text/choice row takes for one field (a key may be nested, e.g. `coInventors.0.name`).
  const row = (key: string) => ({
    error: errors[key],
    showError: shows(key),
    onBlur: () => touch(key),
  });

  const setCoInventor = (index: number, field: 'name' | 'email' | 'organization', value: string): void =>
    setForm(prev => ({
      ...prev,
      coInventors: prev.coInventors.map((c, i) => (i === index ? { ...c, [field]: value } : c)),
    }));

  const onFinish = async () => {
    if (Object.keys(validateInvention(form, todayIso())).length > 0) {
      throw new Error('Some answers need attention. Go back and check them.');
    }
    const title = inventionTitle(form);
    let requestId: string;
    try {
      const created = await submitServiceRequest({
        requestType: INTAKE_REQUEST_TYPE.inventionSubmission,
        title,
        answers: buildInventionAnswers(form),
      });
      requestId = created.sprk_servicerequestid;
    } catch (err) {
      throw new Error(intakeErrorMessage(err));
    }
    const shortTitle = form.shortTitle.trim();
    return intakeSuccessConfig({
      heading: 'Invention disclosure submitted',
      requestId,
      onClose,
      onOpenServiceRequests,
      openRecord,
      paragraphs: [
        <>
          Thank you. Legal has received &ldquo;{shortTitle}&rdquo; and will contact you, usually to set up a short call
          with the inventors.
        </>,
        <>
          <strong>Next step:</strong> add drawings, slides, lab notes or a draft paper under <strong>Documents</strong>{' '}
          on the request. You can follow it in <strong>Service Requests</strong>.
        </>,
        <>
          <strong>Keep it confidential.</strong> Please don&rsquo;t publish, present, demo or post about this invention
          until Legal has advised you.
          {hasDisclosure(form) && form.disclosureDate !== '' && (
            <>
              {' '}
              Legal has your {form.publicDisclosure === 'planned' ? 'planned disclosure' : 'disclosure'} date of{' '}
              {form.disclosureDate} and will prioritise accordingly.
            </>
          )}
        </>,
      ],
    });
  };

  const steps: IWizardStepConfig[] = [
    {
      id: 'invention',
      label: 'The invention',
      canAdvance: () => !stepHasError(errors, INVENTION_STEP_FIELDS.invention),
      renderContent: () => (
        <FormRows>
          <TextRow
            label="Short, non-confidential title"
            required
            hint="Appears in the request title; keep it generic. It is shown in request lists and notifications, so describe the area, not the idea: “Improved battery cooling method”, not how it works."
            value={form.shortTitle}
            onChange={v => set('shortTitle', v)}
            maxLength={INVENTION_LIMITS.shortTitle.max}
            {...row('shortTitle')}
          />
          <TextRow
            label="What problem does it solve?"
            required
            multiline
            hint="The problem or limitation you set out to fix."
            value={form.problemSolved}
            onChange={v => set('problemSolved', v)}
            maxLength={INVENTION_LIMITS.problemSolved.max}
            {...row('problemSolved')}
          />
          <TextRow
            label="How does it work, and what is new about it?"
            required
            multiline
            rows={6}
            hint="Summarise the key idea and what makes it different. After you submit, add drawings, slides, lab notes or a draft paper under Documents."
            value={form.description}
            onChange={v => set('description', v)}
            maxLength={INVENTION_LIMITS.description.max}
            {...row('description')}
          />
          <TextRow
            label="Similar products, methods or patents you know of"
            multiline
            hint="And how yours differs. “None I know of” is a fine answer."
            value={form.knownAlternatives}
            onChange={v => set('knownAlternatives', v)}
            maxLength={INVENTION_LIMITS.knownAlternatives.max}
            {...row('knownAlternatives')}
          />
        </FormRows>
      ),
    },
    {
      id: 'inventors',
      label: 'Inventors',
      canAdvance: () => !stepHasError(errors, INVENTION_STEP_FIELDS.inventors),
      renderContent: () => (
        <FormRows>
          <RadioRow
            label="Are you one of the inventors?"
            required
            hint="You’re already identified from your sign-in."
            options={INVENTION_YES_NO}
            value={form.submitterIsInventor}
            onChange={v => set('submitterIsInventor', v)}
            {...row('submitterIsInventor')}
          />
          <div role="group" aria-label="Other inventors" className={s.inventors}>
            <Text weight="semibold">Other inventors{form.submitterIsInventor === 'no' ? ' (at least one)' : ''}</Text>
            <Text size={200} className={s.hint}>
              Everyone who contributed to the idea itself, not only to building or testing it. Add the organisation for
              anyone outside the company.
            </Text>
            {form.coInventors.map((c, i) => (
              <div key={i} className={s.inventor}>
                <div className={s.inventorHead}>
                  <Text weight="semibold">Inventor {i + 1}</Text>
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<DeleteRegular />}
                    aria-label={`Remove inventor ${i + 1}`}
                    onClick={() =>
                      set(
                        'coInventors',
                        form.coInventors.filter((_c, j) => j !== i)
                      )
                    }
                  >
                    Remove
                  </Button>
                </div>
                <TextRow
                  label={`Inventor ${i + 1} name`}
                  required
                  value={c.name}
                  onChange={v => setCoInventor(i, 'name', v)}
                  maxLength={INVENTION_LIMITS.coInventorName.max}
                  {...row(`coInventors.${i}.name`)}
                />
                <TextRow
                  label={`Inventor ${i + 1} email`}
                  type="email"
                  value={c.email}
                  onChange={v => setCoInventor(i, 'email', v)}
                  maxLength={254}
                  {...row(`coInventors.${i}.email`)}
                />
                <TextRow
                  label={`Inventor ${i + 1} organization`}
                  value={c.organization}
                  onChange={v => setCoInventor(i, 'organization', v)}
                  maxLength={INVENTION_LIMITS.coInventorOrganization.max}
                  {...row(`coInventors.${i}.organization`)}
                />
              </div>
            ))}
            {errors.coInventors && (
              <Text size={200} className={s.error} role="alert">
                {errors.coInventors}
              </Text>
            )}
            <div>
              <Button
                appearance="secondary"
                icon={<AddRegular />}
                disabled={form.coInventors.length >= INVENTION_LIMITS.maxCoInventors}
                onClick={() => set('coInventors', [...form.coInventors, emptyCoInventor()])}
              >
                Add inventor
              </Button>
            </div>
          </div>
          <TextRow
            label="Roughly when did you come up with it?"
            type="date"
            hint="An estimate is fine."
            value={form.conceptionDate}
            onChange={v => set('conceptionDate', v)}
            maxLength={10}
            max={today}
            {...row('conceptionDate')}
          />
        </FormRows>
      ),
    },
    {
      id: 'disclosure',
      label: 'Disclosure and outside involvement',
      canAdvance: () => !stepHasError(errors, INVENTION_STEP_FIELDS.disclosure),
      renderContent: () => (
        <FormRows>
          <RadioRow
            label="Has it been shared publicly, or is that planned?"
            required
            hint="Includes papers, talks, posters, websites, demos, product sales or offers, and open-source releases."
            options={INVENTION_PUBLIC_DISCLOSURES}
            value={form.publicDisclosure}
            onChange={v => set('publicDisclosure', v)}
            {...row('publicDisclosure')}
          />
          {hasDisclosure(form) && (
            <>
              <TextRow
                label="Date (first or planned)"
                type="date"
                required
                value={form.disclosureDate}
                onChange={v => set('disclosureDate', v)}
                maxLength={10}
                min={form.publicDisclosure === 'planned' ? today : undefined}
                max={form.publicDisclosure === 'planned' ? addDaysIso(today, INVENTION_LIMITS.futureDays) : today}
                {...row('disclosureDate')}
              />
              <SelectRow
                label="How?"
                required
                options={INVENTION_DISCLOSURE_METHODS}
                value={form.disclosureMethod}
                onChange={v => set('disclosureMethod', v)}
                {...row('disclosureMethod')}
              />
              <TextRow
                label="Where?"
                hint="The venue or journal name only, not the content."
                value={form.disclosureVenue}
                onChange={v => set('disclosureVenue', v)}
                maxLength={INVENTION_LIMITS.disclosureVenue.max}
                {...row('disclosureVenue')}
              />
            </>
          )}
          {showsDisclosureWarning(form, today) && (
            <MessageBar intent="warning">
              <MessageBarBody>
                Public disclosure can limit or end patent protection, especially outside the US. Legal will contact you
                about timing.
              </MessageBarBody>
            </MessageBar>
          )}
          <RadioRow
            label="Has it been shared with anyone outside the company, even under NDA?"
            required
            hint="e.g. a supplier, customer or university partner."
            options={INVENTION_YES_NO_UNSURE}
            value={form.sharedExternally}
            onChange={v => set('sharedExternally', v)}
            {...row('sharedExternally')}
          />
          {form.sharedExternally === 'yes' && (
            <TextRow
              label="With whom, and was there an NDA?"
              required
              value={form.sharedWith}
              onChange={v => set('sharedWith', v)}
              maxLength={INVENTION_LIMITS.sharedWith.max}
              {...row('sharedWith')}
            />
          )}
          <RadioRow
            label="Is it used in a product or service?"
            required
            options={INVENTION_PRODUCT_USES}
            value={form.productUse}
            onChange={v => set('productUse', v)}
            {...row('productUse')}
          />
          {hasProduct(form) && (
            <>
              <TextRow
                label="Product or service name"
                required
                value={form.productName}
                onChange={v => set('productName', v)}
                maxLength={INVENTION_LIMITS.productName.max}
                {...row('productName')}
              />
              <TextRow
                label="Release date (actual or planned)"
                type="date"
                value={form.productDate}
                onChange={v => set('productDate', v)}
                maxLength={10}
                {...row('productDate')}
              />
            </>
          )}
          <RadioRow
            label="Was it developed with, or funded by, anyone outside the company?"
            required
            hint="e.g. a customer project, university collaboration, government grant or contractor."
            options={INVENTION_YES_NO_UNSURE}
            value={form.externalInvolvement}
            onChange={v => set('externalInvolvement', v)}
            {...row('externalInvolvement')}
          />
          {form.externalInvolvement === 'yes' && (
            <TextRow
              label="Who, and under what agreement if you know?"
              required
              value={form.externalInvolvementDetails}
              onChange={v => set('externalInvolvementDetails', v)}
              maxLength={INVENTION_LIMITS.externalInvolvementDetails.max}
              {...row('externalInvolvementDetails')}
            />
          )}
        </FormRows>
      ),
    },
    {
      id: 'confirm',
      label: 'Review and confirm',
      canAdvance: () => Object.keys(errors).length === 0,
      renderContent: () => (
        <FormRows>
          <TextRow
            label="Anything else?"
            multiline
            value={form.notes}
            onChange={v => set('notes', v)}
            maxLength={INVENTION_LIMITS.notes.max}
            {...row('notes')}
          />
          <ReviewList rows={inventionReviewRows(form)} />
          <ConfirmRow
            label="I confirm this is accurate to the best of my knowledge, and I will keep the invention confidential until Legal advises"
            checked={form.accuracyConfirmed}
            onChange={checked => {
              touch('accuracyConfirmed');
              set('accuracyConfirmed', checked);
            }}
            error={errors.accuracyConfirmed}
            showError={shows('accuracyConfirmed')}
          />
        </FormRows>
      ),
    },
  ];

  return (
    <WizardShell
      open={open}
      title="Invention Submission"
      steps={steps}
      onClose={onClose}
      onFinish={onFinish}
      finishLabel="Submit disclosure"
      finishingLabel="Submitting…"
    />
  );
};

export default InventionSubmissionWizard;
