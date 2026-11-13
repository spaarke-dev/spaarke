/**
 * CreateReportCardStep.tsx
 * Entity-specific "Enter Info" form for the "Create New Report Card" wizard.
 *
 * Fields (owner-provided, Phase-0-validated manifest — spec.md /
 * notes/field-manifests/reportcard.md):
 *   - Name (REQUIRED, Input)               -> sprk_name (schema NOT NULL)
 *   - Due Date (optional, Input type=date) -> sprk_duedate
 *   - Narrative (optional, Textarea)       -> sprk_narrative
 *   - 8 assigned-resource lookups (all optional), modeled on
 *     AssignWorkFollowOnStep's sectioned layout for UI consistency:
 *       Attorneys:   Assigned Attorney 1 / 2      -> contact
 *       Paralegals:  Assigned Paralegal 1 / 2      -> contact
 *       Law Firms:   Assigned Law Firm 1 / 2       -> sprk_organization
 *                    (note asymmetric field naming "assignedtolawfirm1" vs.
 *                    "assignedlawfirm2" — real schema names, not "fixed")
 *       External/Internal: Assigned External / Assigned Internal -> contact
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations (contact/org search)
 *   - navigationService (optional): host INavigationService. With it the eight lookups open the Dataverse side pane
 *     (owner D-219, 2026-11-12), the six contact fields limited to active contacts; without it they keep the
 *     inline search fields.
 *
 * @see IDataService — high-level data access abstraction
 * @see AssignWorkFollowOnStep.tsx — sectioned layout + LookupField pattern reference
 */
import * as React from 'react';
import { Text, Input, Textarea, Field, makeStyles, tokens } from '@fluentui/react-components';
import { DataverseLookupField } from '../LookupField/DataverseLookupField';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../LookupField/lookupFilters';
import type { ILookupItem } from '../../types/LookupTypes';
import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
} from '../CreateWorkAssignmentWizard/workAssignmentService';
import { buildEmptyReportCardForm } from './formTypes';
import type { ICreateReportCardFormState } from './formTypes';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateReportCardStepProps {
  dataService: IDataService;
  /** Host navigation service: when present the lookups open the Dataverse side pane (owner D-219). */
  navigationService?: INavigationService;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: ICreateReportCardFormState) => void;
  initialFormValues?: ICreateReportCardFormState;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground1,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingBottom: tokens.spacingVerticalXS,
  },
  sectionFields: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalS,
  },
  twoColumn: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateReportCardStep: React.FC<ICreateReportCardStepProps> = ({
  dataService,
  navigationService,
  onValidChange,
  onFormValues,
  initialFormValues,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<ICreateReportCardFormState>(
    initialFormValues ?? buildEmptyReportCardForm()
  );

  // Report validity + values whenever form changes. Name is the sole required
  // field (sprk_name is NOT NULL on the schema per the Phase-0 manifest).
  React.useEffect(() => {
    const isValid = formValues.name.trim().length > 0;
    onValidChange(isValid);
    onFormValues(formValues);
  }, [formValues, onValidChange, onFormValues]);

  // -- Field handlers ------------------------------------------------------

  const handleNameChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, name: e.target.value }));
  }, []);

  const handleDueDateChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, dueDate: e.target.value }));
  }, []);

  const handleNarrativeChange = React.useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setFormValues(prev => ({ ...prev, narrative: e.target.value }));
  }, []);

  const handleSearchContacts = React.useCallback(
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );
  const handleSearchOrganizations = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(dataService, query),
    [dataService]
  );

  // -- Resource lookup handlers (8 fields) ---------------------------------

  const handleAttorney1Change = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({ ...prev, assignedAttorney1Id: item?.id ?? '', assignedAttorney1Name: item?.name ?? '' }));
  }, []);
  const handleAttorney2Change = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({ ...prev, assignedAttorney2Id: item?.id ?? '', assignedAttorney2Name: item?.name ?? '' }));
  }, []);
  const handleParalegal1Change = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedParalegal1Id: item?.id ?? '',
      assignedParalegal1Name: item?.name ?? '',
    }));
  }, []);
  const handleParalegal2Change = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedParalegal2Id: item?.id ?? '',
      assignedParalegal2Name: item?.name ?? '',
    }));
  }, []);
  const handleLawFirm1Change = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedToLawFirm1Id: item?.id ?? '',
      assignedToLawFirm1Name: item?.name ?? '',
    }));
  }, []);
  const handleLawFirm2Change = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({ ...prev, assignedLawFirm2Id: item?.id ?? '', assignedLawFirm2Name: item?.name ?? '' }));
  }, []);
  const handleExternalChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedToExternalId: item?.id ?? '',
      assignedToExternalName: item?.name ?? '',
    }));
  }, []);
  const handleInternalChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedToInternalId: item?.id ?? '',
      assignedToInternalName: item?.name ?? '',
    }));
  }, []);

  const attorney1Value: ILookupItem | null = formValues.assignedAttorney1Id
    ? { id: formValues.assignedAttorney1Id, name: formValues.assignedAttorney1Name }
    : null;
  const attorney2Value: ILookupItem | null = formValues.assignedAttorney2Id
    ? { id: formValues.assignedAttorney2Id, name: formValues.assignedAttorney2Name }
    : null;
  const paralegal1Value: ILookupItem | null = formValues.assignedParalegal1Id
    ? { id: formValues.assignedParalegal1Id, name: formValues.assignedParalegal1Name }
    : null;
  const paralegal2Value: ILookupItem | null = formValues.assignedParalegal2Id
    ? { id: formValues.assignedParalegal2Id, name: formValues.assignedParalegal2Name }
    : null;
  const lawFirm1Value: ILookupItem | null = formValues.assignedToLawFirm1Id
    ? { id: formValues.assignedToLawFirm1Id, name: formValues.assignedToLawFirm1Name }
    : null;
  const lawFirm2Value: ILookupItem | null = formValues.assignedLawFirm2Id
    ? { id: formValues.assignedLawFirm2Id, name: formValues.assignedLawFirm2Name }
    : null;
  const externalValue: ILookupItem | null = formValues.assignedToExternalId
    ? { id: formValues.assignedToExternalId, name: formValues.assignedToExternalName }
    : null;
  const internalValue: ILookupItem | null = formValues.assignedToInternalId
    ? { id: formValues.assignedToInternalId, name: formValues.assignedToInternalName }
    : null;

  return (
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Report Card Details
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Enter the details for the new report card. All fields except Name are optional.
        </Text>
      </div>

      {/* Details section */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Details
        </Text>
        <div className={styles.sectionFields}>
          <div className={styles.row}>
            <Field label="Name" required>
              <Input
                value={formValues.name}
                onChange={handleNameChange}
                placeholder="Enter report card name"
                autoComplete="off"
              />
            </Field>
            <Field label="Due Date">
              <Input type="date" value={formValues.dueDate} onChange={handleDueDateChange} />
            </Field>
          </div>
          <Field label="Narrative">
            <Textarea
              value={formValues.narrative}
              onChange={handleNarrativeChange}
              placeholder="Describe the report card..."
              rows={4}
              resize="vertical"
            />
          </Field>
        </div>
      </div>

      {/* Assigned Resources section */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Assigned Resources
        </Text>
        <div className={styles.sectionFields}>
          <div className={styles.twoColumn}>
            <DataverseLookupField
              label="Assigned Attorney 1"
              entityType="contact"
              placeholder="Search contacts..."
              value={attorney1Value}
              onChange={handleAttorney1Change}
              navigationService={navigationService}
              filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
              onSearch={handleSearchContacts}
              minSearchLength={2}
            />
            <DataverseLookupField
              label="Assigned Attorney 2"
              entityType="contact"
              placeholder="Search contacts..."
              value={attorney2Value}
              onChange={handleAttorney2Change}
              navigationService={navigationService}
              filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
              onSearch={handleSearchContacts}
              minSearchLength={2}
            />
          </div>
          <div className={styles.twoColumn}>
            <DataverseLookupField
              label="Assigned Paralegal 1"
              entityType="contact"
              placeholder="Search contacts..."
              value={paralegal1Value}
              onChange={handleParalegal1Change}
              navigationService={navigationService}
              filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
              onSearch={handleSearchContacts}
              minSearchLength={2}
            />
            <DataverseLookupField
              label="Assigned Paralegal 2"
              entityType="contact"
              placeholder="Search contacts..."
              value={paralegal2Value}
              onChange={handleParalegal2Change}
              navigationService={navigationService}
              filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
              onSearch={handleSearchContacts}
              minSearchLength={2}
            />
          </div>
          <div className={styles.twoColumn}>
            <DataverseLookupField
              label="Assigned Law Firm 1"
              entityType="sprk_organization"
              placeholder="Search organizations..."
              value={lawFirm1Value}
              onChange={handleLawFirm1Change}
              navigationService={navigationService}
              onSearch={handleSearchOrganizations}
              minSearchLength={2}
            />
            <DataverseLookupField
              label="Assigned Law Firm 2"
              entityType="sprk_organization"
              placeholder="Search organizations..."
              value={lawFirm2Value}
              onChange={handleLawFirm2Change}
              navigationService={navigationService}
              onSearch={handleSearchOrganizations}
              minSearchLength={2}
            />
          </div>
          <div className={styles.twoColumn}>
            <DataverseLookupField
              label="Assigned External"
              entityType="contact"
              placeholder="Search contacts..."
              value={externalValue}
              onChange={handleExternalChange}
              navigationService={navigationService}
              filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
              onSearch={handleSearchContacts}
              minSearchLength={2}
            />
            <DataverseLookupField
              label="Assigned Internal"
              entityType="contact"
              placeholder="Search contacts..."
              value={internalValue}
              onChange={handleInternalChange}
              navigationService={navigationService}
              filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
              onSearch={handleSearchContacts}
              minSearchLength={2}
            />
          </div>
        </div>
      </div>
    </div>
  );
};
