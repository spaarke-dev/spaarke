/**
 * CreateFollowOnEventStep.tsx
 * Follow-on step: "Create Event" — linked to the work assignment.
 *
 * Fields: Name (required, default "Assign Work"), Description, Priority,
 *         Due Date, Final Due Date, Assigned To (systemuser).
 */
import * as React from 'react';
import {
  Text,
  Input,
  Textarea,
  Dropdown,
  Option,
  Field,
  Checkbox,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { LookupField } from '../../../../../client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField';
import type { ILookupItem } from '../../../../../client/shared/Spaarke.UI.Components/src/types/LookupTypes';
import { searchUsersAsLookup } from './workAssignmentService';
import type { ICreateFollowOnEventState } from './formTypes';
import { EMPTY_FOLLOW_ON_EVENT_STATE } from './formTypes';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateFollowOnEventStepProps {
  webApi: IWebApi;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: ICreateFollowOnEventState) => void;
  initialValues?: ICreateFollowOnEventState;
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
  row: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
  todoCheckbox: {
    color: tokens.colorNeutralForeground1,
  },
  todoCheckboxActive: {
    color: tokens.colorBrandForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateFollowOnEventStep: React.FC<ICreateFollowOnEventStepProps> = ({
  webApi,
  onValidChange,
  onFormValues,
  initialValues,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<ICreateFollowOnEventState>(
    initialValues ?? EMPTY_FOLLOW_ON_EVENT_STATE
  );

  React.useEffect(() => {
    const isValid = formValues.eventName.trim().length > 0;
    onValidChange(isValid);
    onFormValues(formValues);
  }, [formValues, onValidChange, onFormValues]);

  const handleNameChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormValues((prev) => ({ ...prev, eventName: e.target.value }));
    },
    []
  );

  const handleDescriptionChange = React.useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      setFormValues((prev) => ({ ...prev, eventDescription: e.target.value }));
    },
    []
  );

  const handlePriorityChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      const val = parseInt(data.optionValue ?? '100000001', 10);
      setFormValues((prev) => ({ ...prev, eventPriority: val }));
    },
    []
  );

  const handleDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormValues((prev) => ({ ...prev, eventDueDate: e.target.value }));
    },
    []
  );

  const handleFinalDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormValues((prev) => ({ ...prev, eventFinalDueDate: e.target.value }));
    },
    []
  );

  const handleAssignedToChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormValues((prev) => ({
        ...prev,
        assignedToId: item?.id ?? '',
        assignedToName: item?.name ?? '',
      }));
    },
    []
  );

  const handleTodoChange = React.useCallback(
    (_e: unknown, data: { checked: boolean | 'mixed' }) => {
      setFormValues((prev) => ({ ...prev, addTodo: data.checked === true }));
    },
    []
  );

  const handleSearchUsers = React.useCallback(
    (query: string) => searchUsersAsLookup(webApi, query),
    [webApi]
  );

  const assignedToValue: ILookupItem | null = formValues.assignedToId
    ? { id: formValues.assignedToId, name: formValues.assignedToName }
    : null;
  const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === formValues.eventPriority)?.text ?? 'Normal';

  return (
    <div className={styles.form}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Create Event
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Create a follow-up event linked to this work assignment.
        </Text>
      </div>

      <Field label="Event Name" required>
        <Input
          value={formValues.eventName}
          onChange={handleNameChange}
          placeholder="Enter event name"
          autoComplete="off"
        />
      </Field>

      <Field label="Description">
        <Textarea
          value={formValues.eventDescription}
          onChange={handleDescriptionChange}
          placeholder="Describe the event..."
          rows={6}
          resize="vertical"
        />
      </Field>

      <div className={styles.row}>
        <Field label="Priority">
          <Dropdown
            value={selectedPriorityText}
            selectedOptions={[String(formValues.eventPriority)]}
            onOptionSelect={handlePriorityChange}
          >
            {PRIORITY_OPTIONS.map((opt) => (
              <Option key={opt.key} value={String(opt.key)}>
                {opt.text}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <LookupField
          label="Assigned To"
          value={assignedToValue}
          onChange={handleAssignedToChange}
          onSearch={handleSearchUsers}
          placeholder="Search users..."
        />
      </div>

      <div className={styles.row}>
        <Field label="Due Date">
          <Input
            type="date"
            value={formValues.eventDueDate}
            onChange={handleDueDateChange}
          />
        </Field>
        <Field label="Final Due Date">
          <Input
            type="date"
            value={formValues.eventFinalDueDate}
            onChange={handleFinalDueDateChange}
          />
        </Field>
      </div>

      <Checkbox
        checked={formValues.addTodo}
        onChange={handleTodoChange}
        label="Add a 'To Do' Item"
        className={mergeClasses(
          styles.todoCheckbox,
          formValues.addTodo && styles.todoCheckboxActive
        )}
      />
    </div>
  );
};
