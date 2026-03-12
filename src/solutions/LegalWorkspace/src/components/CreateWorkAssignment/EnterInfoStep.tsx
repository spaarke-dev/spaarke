/**
 * EnterInfoStep.tsx
 * Step 3: "Enter Info" — core work assignment fields.
 *
 * Fields: Name (required), Description, Matter Type, Practice Area, Priority, Response Due Date.
 */
import * as React from 'react';
import {
  Text,
  Input,
  Textarea,
  Dropdown,
  Option,
  Field,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../../../../../client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField';
import type { ILookupItem } from '../../../../../client/shared/Spaarke.UI.Components/src/types/LookupTypes';
import { searchMatterTypes, searchPracticeAreas } from './workAssignmentService';
import type { ICreateWorkAssignmentFormState } from './formTypes';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IEnterInfoStepProps {
  webApi: IWebApi;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: Pick<
    ICreateWorkAssignmentFormState,
    'name' | 'description' | 'matterTypeId' | 'matterTypeName' | 'practiceAreaId' | 'practiceAreaName' | 'priority' | 'responseDueDate'
  >) => void;
  initialValues?: Partial<ICreateWorkAssignmentFormState>;
}

// ---------------------------------------------------------------------------
// Priority options
// ---------------------------------------------------------------------------

const PRIORITY_OPTIONS = [
  { key: 100000000, text: 'Low' },
  { key: 100000001, text: 'Normal' },
  { key: 100000002, text: 'High' },
  { key: 100000003, text: 'Urgent' },
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
  row: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EnterInfoStep: React.FC<IEnterInfoStepProps> = ({
  webApi,
  onValidChange,
  onFormValues,
  initialValues,
}) => {
  const styles = useStyles();

  const [name, setName] = React.useState(initialValues?.name ?? '');
  const [description, setDescription] = React.useState(initialValues?.description ?? '');
  const [matterTypeId, setMatterTypeId] = React.useState(initialValues?.matterTypeId ?? '');
  const [matterTypeName, setMatterTypeName] = React.useState(initialValues?.matterTypeName ?? '');
  const [practiceAreaId, setPracticeAreaId] = React.useState(initialValues?.practiceAreaId ?? '');
  const [practiceAreaName, setPracticeAreaName] = React.useState(initialValues?.practiceAreaName ?? '');
  const [priority, setPriority] = React.useState(initialValues?.priority ?? 100000001);
  const [responseDueDate, setResponseDueDate] = React.useState(initialValues?.responseDueDate ?? '');

  React.useEffect(() => {
    const isValid = name.trim().length > 0;
    onValidChange(isValid);
    onFormValues({ name, description, matterTypeId, matterTypeName, practiceAreaId, practiceAreaName, priority, responseDueDate });
  }, [name, description, matterTypeId, matterTypeName, practiceAreaId, practiceAreaName, priority, responseDueDate, onValidChange, onFormValues]);

  const handleNameChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => setName(e.target.value),
    []
  );

  const handleDescriptionChange = React.useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => setDescription(e.target.value),
    []
  );

  const handleMatterTypeChange = React.useCallback(
    (item: ILookupItem | null) => {
      setMatterTypeId(item?.id ?? '');
      setMatterTypeName(item?.name ?? '');
    },
    []
  );

  const handleSearchMatterTypes = React.useCallback(
    (query: string) => searchMatterTypes(webApi, query),
    [webApi]
  );

  const handlePracticeAreaChange = React.useCallback(
    (item: ILookupItem | null) => {
      setPracticeAreaId(item?.id ?? '');
      setPracticeAreaName(item?.name ?? '');
    },
    []
  );

  const handleSearchPracticeAreas = React.useCallback(
    (query: string) => searchPracticeAreas(webApi, query),
    [webApi]
  );

  const handlePriorityChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      setPriority(parseInt(data.optionValue ?? '100000001', 10));
    },
    []
  );

  const handleDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => setResponseDueDate(e.target.value),
    []
  );

  const matterTypeValue: ILookupItem | null = matterTypeId ? { id: matterTypeId, name: matterTypeName } : null;
  const practiceAreaValue: ILookupItem | null = practiceAreaId ? { id: practiceAreaId, name: practiceAreaName } : null;
  const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === priority)?.text ?? 'Normal';

  return (
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Enter Info
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Provide the details for the work assignment.
        </Text>
      </div>

      <Field label="Name" required>
        <Input
          value={name}
          onChange={handleNameChange}
          placeholder="Work assignment name"
          autoComplete="off"
        />
      </Field>

      <Field label="Description">
        <Textarea
          value={description}
          onChange={handleDescriptionChange}
          placeholder="Describe the work assignment..."
          rows={3}
          resize="vertical"
        />
      </Field>

      <div className={styles.row}>
        <LookupField
          label="Matter Type"
          value={matterTypeValue}
          onChange={handleMatterTypeChange}
          onSearch={handleSearchMatterTypes}
          placeholder="Search matter types..."
        />
        <LookupField
          label="Practice Area"
          value={practiceAreaValue}
          onChange={handlePracticeAreaChange}
          onSearch={handleSearchPracticeAreas}
          placeholder="Search practice areas..."
        />
      </div>

      <div className={styles.row}>
        <Field label="Priority">
          <Dropdown
            value={selectedPriorityText}
            selectedOptions={[String(priority)]}
            onOptionSelect={handlePriorityChange}
          >
            {PRIORITY_OPTIONS.map((opt) => (
              <Option key={opt.key} value={String(opt.key)}>
                {opt.text}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <Field label="Response Due Date">
          <Input
            type="date"
            value={responseDueDate}
            onChange={handleDueDateChange}
          />
        </Field>
      </div>
    </div>
  );
};
