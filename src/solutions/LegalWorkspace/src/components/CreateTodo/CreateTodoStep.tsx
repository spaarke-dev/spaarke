/**
 * CreateTodoStep.tsx
 * Entity-specific form for "Create New To Do" wizard.
 *
 * Simpler form than Event: Title, Due Date, Priority, Description.
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
import type { ICreateTodoFormState } from './formTypes';
import { EMPTY_TODO_FORM } from './formTypes';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateTodoStepProps {
  webApi: IWebApi;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: ICreateTodoFormState) => void;
  initialFormValues?: ICreateTodoFormState;
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

export const CreateTodoStep: React.FC<ICreateTodoStepProps> = ({
  webApi: _webApi,
  onValidChange,
  onFormValues,
  initialFormValues,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<ICreateTodoFormState>(
    initialFormValues ?? EMPTY_TODO_FORM
  );

  React.useEffect(() => {
    const isValid = formValues.title.trim().length > 0;
    onValidChange(isValid);
    onFormValues(formValues);
  }, [formValues, onValidChange, onFormValues]);

  const handleTitleChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormValues((prev) => ({ ...prev, title: e.target.value }));
    },
    []
  );

  const handleDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormValues((prev) => ({ ...prev, dueDate: e.target.value }));
    },
    []
  );

  const handlePriorityChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      const val = parseInt(data.optionValue ?? '100000001', 10);
      setFormValues((prev) => ({ ...prev, priority: val }));
    },
    []
  );

  const handleDescriptionChange = React.useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      setFormValues((prev) => ({ ...prev, description: e.target.value }));
    },
    []
  );

  const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === formValues.priority)?.text ?? 'Normal';

  return (
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          To Do Details
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Enter the details for the new to do item.
        </Text>
      </div>

      <Field label="Title" required>
        <Input
          value={formValues.title}
          onChange={handleTitleChange}
          placeholder="What needs to be done?"
          autoComplete="off"
        />
      </Field>

      <div className={styles.row}>
        <Field label="Due Date">
          <Input
            type="date"
            value={formValues.dueDate}
            onChange={handleDueDateChange}
          />
        </Field>
        <Field label="Priority">
          <Dropdown
            value={selectedPriorityText}
            selectedOptions={[String(formValues.priority)]}
            onOptionSelect={handlePriorityChange}
          >
            {PRIORITY_OPTIONS.map((opt) => (
              <Option key={opt.key} value={String(opt.key)}>
                {opt.text}
              </Option>
            ))}
          </Dropdown>
        </Field>
      </div>

      <Field label="Description">
        <Textarea
          value={formValues.description}
          onChange={handleDescriptionChange}
          placeholder="Add notes or details..."
          rows={3}
          resize="vertical"
        />
      </Field>
    </div>
  );
};
