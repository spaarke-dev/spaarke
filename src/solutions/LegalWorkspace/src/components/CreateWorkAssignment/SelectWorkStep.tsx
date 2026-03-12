/**
 * SelectWorkStep.tsx
 * Step 1: "Work to Assign" — select the entity record this work relates to.
 *
 * - Record Type dropdown: Matter, Project, Invoice, Event
 * - LookupField for record search (shown when type selected AND !assignWithoutRecord)
 * - Checkbox: "Assign Work without specific record"
 */
import * as React from 'react';
import {
  Text,
  Dropdown,
  Option,
  Checkbox,
  Field,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../../../../../client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField';
import type { ILookupItem } from '../../../../../client/shared/Spaarke.UI.Components/src/types/LookupTypes';
import { WorkAssignmentService } from './workAssignmentService';
import type { ICreateWorkAssignmentFormState } from './formTypes';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISelectWorkStepProps {
  webApi: IWebApi;
  containerId?: string;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName' | 'assignWithoutRecord'>) => void;
  initialValues?: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName' | 'assignWithoutRecord'>;
}

// ---------------------------------------------------------------------------
// Record Type options
// ---------------------------------------------------------------------------

const RECORD_TYPE_OPTIONS = [
  { key: 'matter' as const, text: 'Matter' },
  { key: 'project' as const, text: 'Project' },
  { key: 'invoice' as const, text: 'Invoice' },
  { key: 'event' as const, text: 'Event' },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SelectWorkStep: React.FC<ISelectWorkStepProps> = ({
  webApi,
  containerId,
  onValidChange,
  onFormValues,
  initialValues,
}) => {
  const styles = useStyles();

  const [recordType, setRecordType] = React.useState<'' | 'matter' | 'project' | 'invoice' | 'event'>(initialValues?.recordType ?? '');
  const [recordId, setRecordId] = React.useState(initialValues?.recordId ?? '');
  const [recordName, setRecordName] = React.useState(initialValues?.recordName ?? '');
  const [assignWithoutRecord, setAssignWithoutRecord] = React.useState(initialValues?.assignWithoutRecord ?? false);

  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(webApi, containerId);
  }

  // Report validity + values
  React.useEffect(() => {
    const isValid = assignWithoutRecord || recordId !== '';
    onValidChange(isValid);
    onFormValues({ recordType, recordId, recordName, assignWithoutRecord });
  }, [recordType, recordId, recordName, assignWithoutRecord, onValidChange, onFormValues]);

  const handleRecordTypeChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      const val = (data.optionValue ?? '') as '' | 'matter' | 'project' | 'invoice' | 'event';
      setRecordType(val);
      setRecordId('');
      setRecordName('');
    },
    []
  );

  const handleRecordChange = React.useCallback(
    (item: ILookupItem | null) => {
      setRecordId(item?.id ?? '');
      setRecordName(item?.name ?? '');
    },
    []
  );

  const handleSearchRecords = React.useCallback(
    (query: string) => {
      if (!recordType) return Promise.resolve([]);
      return serviceRef.current!.searchRecordsByType(recordType, query);
    },
    [recordType]
  );

  const handleAssignWithoutChange = React.useCallback(
    (_e: unknown, data: { checked: boolean | 'mixed' }) => {
      const checked = data.checked === true;
      setAssignWithoutRecord(checked);
      if (checked) {
        setRecordId('');
        setRecordName('');
      }
    },
    []
  );

  const selectedTypeText = RECORD_TYPE_OPTIONS.find((o) => o.key === recordType)?.text ?? '';
  const recordValue: ILookupItem | null = recordId ? { id: recordId, name: recordName } : null;

  return (
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Work to Assign
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Select the subject matter that is to be assigned for work responsibility.
        </Text>
      </div>

      <Field label="Record Type">
        <Dropdown
          value={selectedTypeText}
          selectedOptions={recordType ? [recordType] : []}
          onOptionSelect={handleRecordTypeChange}
          placeholder="Select record type..."
        >
          {RECORD_TYPE_OPTIONS.map((opt) => (
            <Option key={opt.key} value={opt.key}>
              {opt.text}
            </Option>
          ))}
        </Dropdown>
      </Field>

      {recordType && !assignWithoutRecord && (
        <LookupField
          label={`Select ${selectedTypeText}`}
          value={recordValue}
          onChange={handleRecordChange}
          onSearch={handleSearchRecords}
          placeholder={`Search ${selectedTypeText.toLowerCase()}s...`}
        />
      )}

      <Checkbox
        checked={assignWithoutRecord}
        onChange={handleAssignWithoutChange}
        label="Assign work without a specific record"
      />
    </div>
  );
};
