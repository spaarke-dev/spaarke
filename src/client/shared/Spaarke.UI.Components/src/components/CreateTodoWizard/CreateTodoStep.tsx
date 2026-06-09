/**
 * CreateTodoStep.tsx
 * Entity-specific form for "Create New To Do" wizard (R3 — targets `sprk_todo`).
 *
 * Fields:
 *   - Title         (required, sprk_name)
 *   - Due Date      (optional, sprk_duedate)
 *   - Priority Score (0-100 slider, sprk_priorityscore)
 *   - Effort Score   (0-100 slider, sprk_effortscore)
 *   - Assignee      (optional, systemuser lookup → sprk_assignedto)
 *   - Notes         (optional, multi-line, sprk_notes)
 *
 * Per smart-todo-decoupling-r3 spec FR-15: This form writes to `sprk_todo`,
 * not to `sprk_event` with `todoflag=true`.
 *
 * @see formTypes.ts for the form-state shape
 * @see todoService.ts for the create handler
 */
import * as React from 'react';
import { Text, Input, Textarea, Slider, Field, makeStyles, tokens } from '@fluentui/react-components';
import type { ICreateTodoFormState } from './formTypes';
import { EMPTY_TODO_FORM } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateTodoStepProps {
  dataService: IDataService;
  /** Search callback for systemuser assignee lookup. */
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: ICreateTodoFormState) => void;
  initialFormValues?: ICreateTodoFormState;
}

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
  scoreRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  scoreLabel: {
    display: 'flex',
    justifyContent: 'space-between',
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateTodoStep: React.FC<ICreateTodoStepProps> = ({
  dataService: _dataService,
  onSearchUsers,
  onValidChange,
  onFormValues,
  initialFormValues,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<ICreateTodoFormState>(initialFormValues ?? EMPTY_TODO_FORM);

  React.useEffect(() => {
    const isValid = formValues.title.trim().length > 0;
    onValidChange(isValid);
    onFormValues(formValues);
  }, [formValues, onValidChange, onFormValues]);

  const handleTitleChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, title: e.target.value }));
  }, []);

  const handleDueDateChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, dueDate: e.target.value }));
  }, []);

  const handlePriorityChange = React.useCallback((_e: unknown, data: { value: number }) => {
    setFormValues(prev => ({ ...prev, priorityScore: data.value }));
  }, []);

  const handleEffortChange = React.useCallback((_e: unknown, data: { value: number }) => {
    setFormValues(prev => ({ ...prev, effortScore: data.value }));
  }, []);

  const handleNotesChange = React.useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setFormValues(prev => ({ ...prev, notes: e.target.value }));
  }, []);

  const handleAssigneeChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedToId: item?.id ?? '',
      assignedToName: item?.name ?? '',
    }));
  }, []);

  const assigneeValue: ILookupItem | null = formValues.assignedToId
    ? { id: formValues.assignedToId, name: formValues.assignedToName }
    : null;

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
          <Input type="date" value={formValues.dueDate} onChange={handleDueDateChange} />
        </Field>
        <LookupField
          label="Assigned To"
          value={assigneeValue}
          onChange={handleAssigneeChange}
          onSearch={onSearchUsers}
          placeholder="Search users..."
        />
      </div>

      <div className={styles.row}>
        <Field label="Priority Score">
          <div className={styles.scoreRow}>
            <Slider min={0} max={100} step={1} value={formValues.priorityScore} onChange={handlePriorityChange} />
            <div className={styles.scoreLabel}>
              <Text size={200}>0</Text>
              <Text size={200}>{formValues.priorityScore}</Text>
              <Text size={200}>100</Text>
            </div>
          </div>
        </Field>
        <Field label="Effort Score">
          <div className={styles.scoreRow}>
            <Slider min={0} max={100} step={1} value={formValues.effortScore} onChange={handleEffortChange} />
            <div className={styles.scoreLabel}>
              <Text size={200}>0</Text>
              <Text size={200}>{formValues.effortScore}</Text>
              <Text size={200}>100</Text>
            </div>
          </div>
        </Field>
      </div>

      <Field label="Notes">
        <Textarea
          value={formValues.notes}
          onChange={handleNotesChange}
          placeholder="Add notes or details..."
          rows={4}
          resize="vertical"
        />
      </Field>
    </div>
  );
};
