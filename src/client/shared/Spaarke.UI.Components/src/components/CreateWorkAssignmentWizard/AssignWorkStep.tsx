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
import {
  Text,
  Checkbox,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
} from './workAssignmentService';
import { WorkAssignmentService } from './workAssignmentService';
import type { IAssignWorkState } from './formTypes';
import { EMPTY_ASSIGN_WORK_STATE } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
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
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<IAssignWorkState>(
    initialValues ?? EMPTY_ASSIGN_WORK_STATE
  );

  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(dataService, authenticatedFetch, bffBaseUrl, containerId);
  }

  React.useEffect(() => {
    onFormValues(formValues);
  }, [formValues, onFormValues]);

  // -- Internal Resources ----------------------------------------------------

  const handleAttorneyChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormValues((prev) => ({
        ...prev,
        assignedAttorneyId: item?.id ?? '',
        assignedAttorneyName: item?.name ?? '',
      }));
    },
    []
  );

  const handleSearchAttorneys = React.useCallback(
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );

  const handleParalegalChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormValues((prev) => ({
        ...prev,
        assignedParalegalId: item?.id ?? '',
        assignedParalegalName: item?.name ?? '',
      }));
    },
    []
  );

  const handleSearchParalegals = React.useCallback(
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );

  // -- Law Firm --------------------------------------------------------------

  const handleLawFirmChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormValues((prev) => ({
        ...prev,
        assignedLawFirmId: item?.id ?? '',
        assignedLawFirmName: item?.name ?? '',
        // Clear attorney when firm changes
        assignedLawFirmAttorneyId: '',
        assignedLawFirmAttorneyName: '',
      }));
    },
    []
  );

  const handleSearchLawFirms = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(dataService, query),
    [dataService]
  );

  const handleLawFirmAttorneyChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormValues((prev) => ({
        ...prev,
        assignedLawFirmAttorneyId: item?.id ?? '',
        assignedLawFirmAttorneyName: item?.name ?? '',
      }));
    },
    []
  );

  const handleSearchLawFirmAttorneys = React.useCallback(
    (query: string) => {
      if (!formValues.assignedLawFirmId) return Promise.resolve([]);
      return serviceRef.current!.searchContactsByOrganization(formValues.assignedLawFirmId, query);
    },
    [formValues.assignedLawFirmId]
  );

  // -- Notify ----------------------------------------------------------------

  const handleNotifyChange = React.useCallback(
    (_e: unknown, data: { checked: boolean | 'mixed' }) => {
      setFormValues((prev) => ({ ...prev, notifyResources: data.checked === true }));
    },
    []
  );

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
          <LookupField
            label="Assigned Attorney"
            value={attorneyValue}
            onChange={handleAttorneyChange}
            onSearch={handleSearchAttorneys}
            placeholder="Search contacts..."
          />
          <LookupField
            label="Assigned Paralegal"
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
        <LookupField
          label="Law Firm"
          value={lawFirmValue}
          onChange={handleLawFirmChange}
          onSearch={handleSearchLawFirms}
          placeholder="Search organizations..."
        />
        <LookupField
          label="Law Firm Attorney"
          value={lawFirmAttorneyValue}
          onChange={handleLawFirmAttorneyChange}
          onSearch={handleSearchLawFirmAttorneys}
          placeholder={formValues.assignedLawFirmId ? 'Search contacts at firm...' : 'Select a law firm first'}
        />
      </div>

      <Checkbox
        checked={formValues.notifyResources}
        onChange={handleNotifyChange}
        label="Notify assigned resources"
      />
    </div>
  );
};
