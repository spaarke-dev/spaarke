/**
 * AssignWorkStep.tsx
 * Follow-on step: "Assign Work" -- assign internal resources and law firm.
 *
 * Sections:
 *   - Internal Resources: Assigned Attorney (contact), Assigned Paralegal (contact)
 *   - Assigned Law Firm: Law Firm (organization), Law Firm Attorney (contact filtered by firm)
 *   - Notify assigned resources checkbox
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import { Text, Checkbox, makeStyles, tokens } from '@fluentui/react-components';
import { DataverseLookupField } from '../LookupField/DataverseLookupField';
import { ACTIVE_CONTACT_CONDITION_XML, ACTIVE_CONTACT_LOOKUP_FILTERS } from '../LookupField/lookupFilters';
import type { ILookupItem } from '../../types/LookupTypes';
import { cleanGuid } from '../../utils/guid';
import { searchContactsAsLookup, searchOrganizationsAsLookup } from './workAssignmentService';
import { WorkAssignmentService } from './workAssignmentService';
import type { IAssignWorkState } from './formTypes';
import { EMPTY_ASSIGN_WORK_STATE } from './formTypes';
import type { IDataService, INavigationService, LookupOptions } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAssignWorkStepProps {
  dataService: IDataService;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  containerId?: string;
  onFormValues: (values: IAssignWorkState) => void;
  initialValues?: IAssignWorkState;
  /**
   * Host navigation service. When provided the person/organization fields open the Dataverse lookup
   * side pane; when absent they use the inline search, as before.
   */
  navigationService?: INavigationService;
}

// ---------------------------------------------------------------------------
// Law Firm Attorney pane filter
// ---------------------------------------------------------------------------

/** Escapes a value for use inside a double-quoted XML attribute. */
function escapeXmlAttribute(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

/**
 * Side-pane filter for Law Firm Attorney: ACTIVE contacts whose parent customer is the chosen firm,
 * in one filter element. `parentcustomerid` is the column the inline firm search matches
 * (`_parentcustomerid_value`, WorkAssignmentService.searchContactsByOrganization).
 */
export function buildLawFirmAttorneyFilters(lawFirmId: string): NonNullable<LookupOptions['filters']> {
  const firmCondition = `<condition attribute="parentcustomerid" operator="eq" value="${escapeXmlAttribute(cleanGuid(lawFirmId))}"/>`;
  return [
    {
      entityLogicalName: 'contact',
      filterXml: `<filter type="and">${ACTIVE_CONTACT_CONDITION_XML}${firmCondition}</filter>`,
    },
  ];
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
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalM,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sectionLabel: {
    color: tokens.colorNeutralForeground2,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingBottom: tokens.spacingVerticalXS,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AssignWorkStep: React.FC<IAssignWorkStepProps> = ({
  dataService,
  authenticatedFetch,
  bffBaseUrl,
  containerId,
  onFormValues,
  initialValues,
  navigationService,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<IAssignWorkState>(initialValues ?? EMPTY_ASSIGN_WORK_STATE);

  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(dataService, authenticatedFetch, bffBaseUrl, containerId);
  }

  React.useEffect(() => {
    onFormValues(formValues);
  }, [formValues, onFormValues]);

  // -- Internal Resources ----------------------------------------------------

  const handleAttorneyChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedAttorneyId: item?.id ?? '',
      assignedAttorneyName: item?.name ?? '',
    }));
  }, []);

  const handleSearchAttorneys = React.useCallback(
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );

  const handleParalegalChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedParalegalId: item?.id ?? '',
      assignedParalegalName: item?.name ?? '',
    }));
  }, []);

  const handleSearchParalegals = React.useCallback(
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );

  // -- Law Firm --------------------------------------------------------------

  const handleLawFirmChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedLawFirmId: item?.id ?? '',
      assignedLawFirmName: item?.name ?? '',
      // Clear attorney when firm changes
      assignedLawFirmAttorneyId: '',
      assignedLawFirmAttorneyName: '',
    }));
  }, []);

  const handleSearchLawFirms = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(dataService, query),
    [dataService]
  );

  const handleLawFirmAttorneyChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedLawFirmAttorneyId: item?.id ?? '',
      assignedLawFirmAttorneyName: item?.name ?? '',
    }));
  }, []);

  const handleSearchLawFirmAttorneys = React.useCallback(
    (query: string) => {
      if (!formValues.assignedLawFirmId) return Promise.resolve([]);
      return serviceRef.current!.searchContactsByOrganization(formValues.assignedLawFirmId, query);
    },
    [formValues.assignedLawFirmId]
  );

  // Pane filter for Law Firm Attorney: only meaningful once a firm is chosen.
  const lawFirmAttorneyFilters = React.useMemo(
    () => (formValues.assignedLawFirmId ? buildLawFirmAttorneyFilters(formValues.assignedLawFirmId) : undefined),
    [formValues.assignedLawFirmId]
  );

  // -- Notify ----------------------------------------------------------------

  const handleNotifyChange = React.useCallback((_e: unknown, data: { checked: boolean | 'mixed' }) => {
    setFormValues(prev => ({ ...prev, notifyResources: data.checked === true }));
  }, []);

  // -- Render ----------------------------------------------------------------

  const attorneyValue: ILookupItem | null = formValues.assignedAttorneyId
    ? { id: formValues.assignedAttorneyId, name: formValues.assignedAttorneyName }
    : null;
  const paralegalValue: ILookupItem | null = formValues.assignedParalegalId
    ? { id: formValues.assignedParalegalId, name: formValues.assignedParalegalName }
    : null;
  const lawFirmValue: ILookupItem | null = formValues.assignedLawFirmId
    ? { id: formValues.assignedLawFirmId, name: formValues.assignedLawFirmName }
    : null;
  const lawFirmAttorneyValue: ILookupItem | null = formValues.assignedLawFirmAttorneyId
    ? { id: formValues.assignedLawFirmAttorneyId, name: formValues.assignedLawFirmAttorneyName }
    : null;

  return (
    <div className={styles.form}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Assign Work
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Assign internal resources and law firm to this work assignment.
        </Text>
      </div>

      {/* Internal Resources */}
      <div className={styles.section}>
        <Text size={300} weight="semibold" className={styles.sectionLabel}>
          Internal Resources
        </Text>
        <div className={styles.row}>
          <DataverseLookupField
            label="Assigned Attorney"
            entityType="contact"
            filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
            navigationService={navigationService}
            value={attorneyValue}
            onChange={handleAttorneyChange}
            onSearch={handleSearchAttorneys}
            placeholder="Search contacts..."
          />
          <DataverseLookupField
            label="Assigned Paralegal"
            entityType="contact"
            filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
            navigationService={navigationService}
            value={paralegalValue}
            onChange={handleParalegalChange}
            onSearch={handleSearchParalegals}
            placeholder="Search contacts..."
          />
        </div>
      </div>

      {/* Assigned Law Firm */}
      <div className={styles.section}>
        <Text size={300} weight="semibold" className={styles.sectionLabel}>
          Assigned Law Firm
        </Text>
        <DataverseLookupField
          label="Law Firm"
          entityType="sprk_organization"
          navigationService={navigationService}
          value={lawFirmValue}
          onChange={handleLawFirmChange}
          onSearch={handleSearchLawFirms}
          placeholder="Search organizations..."
        />
        {/*
          No firm chosen: today's rule is that the search returns nothing ("Select a law firm first"), so the field
          stays on the inline path (no navigationService) and behaves exactly as before. With a firm, the pane opens
          limited to that firm's active contacts.
        */}
        <DataverseLookupField
          label="Law Firm Attorney"
          entityType="contact"
          filters={lawFirmAttorneyFilters}
          navigationService={formValues.assignedLawFirmId ? navigationService : undefined}
          value={lawFirmAttorneyValue}
          onChange={handleLawFirmAttorneyChange}
          onSearch={handleSearchLawFirmAttorneys}
          placeholder={formValues.assignedLawFirmId ? 'Search contacts at firm...' : 'Select a law firm first'}
        />
      </div>

      <Checkbox checked={formValues.notifyResources} onChange={handleNotifyChange} label="Notify assigned resources" />
    </div>
  );
};
