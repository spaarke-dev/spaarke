/**
 * CreateTodoStep.tsx
 * Entity-specific form for "Create New To Do" wizard (R3 — targets `sprk_todo`).
 *
 * Fields:
 *   - Title         (required, sprk_name)
 *   - Due Date      (optional, sprk_duedate)
 *   - Priority      (Dropdown choice; drives `sprk_priorityscore` via the shared
 *                    mapping — smart-todo-r5 task 011 / FR-02)
 *   - Effort        (Dropdown choice; drives `sprk_effortscore` via the shared
 *                    mapping, Option B quick-wins-first — smart-todo-r5 task 011 / FR-03)
 *   - Assignee      (optional, OOB contact lookup → sprk_assignedto)
 *                   UAT 2026-06-21: migrated from systemuser to contact
 *   - Notes         (optional, multi-line, sprk_notes)
 *
 * Per smart-todo-decoupling-r3 spec FR-15: This form writes to `sprk_todo`,
 * not to `sprk_event` with `todoflag=true`.
 *
 * Per smart-todo-r5 task 011 (FR-02/FR-03): the raw 0-100 Priority/Effort
 * SLIDERS were replaced with Priority/Effort CHOICE dropdowns — "choosing a
 * priority sets the score" is the default path. The dropdowns resolve to
 * `sprk_priorityscore`/`sprk_effortscore` through the SAME shared mapping
 * module the `sprk_todo` form OnChange webresource and the SmartTodo Code
 * Page quick-add flow use (`todoScoreMappings.ts` — single source of truth).
 * The composite score formula in `todoScoring.ts` is untouched by this step;
 * only the choice→score INPUT is supplied here.
 *
 * @see formTypes.ts for the form-state shape
 * @see todoService.ts for the create handler
 * @see ../../utils/todoScoreMappings.ts for the shared choice→score mapping
 */
import * as React from 'react';
import { Text, Input, Textarea, Dropdown, Option, Field, makeStyles, tokens } from '@fluentui/react-components';
import type { ICreateTodoFormState } from './formTypes';
import { EMPTY_TODO_FORM } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import {
  TODO_PRIORITY_CHOICES,
  TODO_EFFORT_CHOICES,
  DEFAULT_PRIORITY_CHOICE,
  DEFAULT_EFFORT_CHOICE,
  priorityChoiceToScore,
  effortChoiceToScore,
  scoreToPriorityChoice,
  scoreToEffortChoice,
  type TodoPriorityChoice,
  type TodoEffortChoice,
} from '../../utils/todoScoreMappings';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateTodoStepProps {
  dataService: IDataService;
  /**
   * Search callback for the Assignee LookupField. Should return matching
   * OOB `contact` records (UAT 2026-06-21 — `sprk_assignedto` migrated from
   * systemuser to contact). Prop name retained for back-compat with the
   * generic CreateRecordWizard pattern.
   */
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

  // Priority/Effort are choice-driven (FR-02/FR-03, task 011) — the wizard
  // does not persist the raw choice today (only the derived score fields
  // that `todoService.ts` already writes; see notes/task-011-scoring.md for
  // the documented decision), so the SELECTED CHOICE is local UI state,
  // reverse-mapped from `initialFormValues`' numeric score when present so an
  // edit/re-open flow pre-selects the matching option.
  const [priorityChoice, setPriorityChoice] = React.useState<TodoPriorityChoice>(() =>
    scoreToPriorityChoice(initialFormValues?.priorityScore)
  );
  const [effortChoice, setEffortChoice] = React.useState<TodoEffortChoice>(() =>
    scoreToEffortChoice(initialFormValues?.effortScore)
  );

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

  // Selecting a Priority/Effort choice sets the score via the SHARED mapping
  // module (todoScoreMappings.ts) — the same table the `sprk_todo` form
  // OnChange webresource and the SmartTodo quick-add flow resolve through.
  const handlePriorityChange = React.useCallback((choice: TodoPriorityChoice | undefined) => {
    const resolved = choice ?? DEFAULT_PRIORITY_CHOICE;
    setPriorityChoice(resolved);
    setFormValues(prev => ({ ...prev, priorityScore: priorityChoiceToScore(resolved) }));
  }, []);

  const handleEffortChange = React.useCallback((choice: TodoEffortChoice | undefined) => {
    const resolved = choice ?? DEFAULT_EFFORT_CHOICE;
    setEffortChoice(resolved);
    setFormValues(prev => ({ ...prev, effortScore: effortChoiceToScore(resolved) }));
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
          placeholder="Search contacts..."
        />
      </div>

      <div className={styles.row}>
        <Field label="Priority" hint={`Score: ${formValues.priorityScore}`}>
          <Dropdown
            value={priorityChoice}
            selectedOptions={[priorityChoice]}
            onOptionSelect={(_e, data) => handlePriorityChange(data.optionValue as TodoPriorityChoice | undefined)}
          >
            {TODO_PRIORITY_CHOICES.map(choice => (
              <Option key={choice} value={choice}>
                {choice}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <Field label="Effort" hint={`Score: ${formValues.effortScore}`}>
          <Dropdown
            value={effortChoice}
            selectedOptions={[effortChoice]}
            onOptionSelect={(_e, data) => handleEffortChange(data.optionValue as TodoEffortChoice | undefined)}
          >
            {TODO_EFFORT_CHOICES.map(choice => (
              <Option key={choice} value={choice}>
                {choice}
              </Option>
            ))}
          </Dropdown>
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
