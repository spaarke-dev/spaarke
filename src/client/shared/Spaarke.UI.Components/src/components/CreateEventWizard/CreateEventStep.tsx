/**
 * CreateEventStep.tsx
 * Entity-specific form for "Create New Event" wizard.
 *
 * Fields:
 *   - Event Name (required, Input)
 *   - Event Type (LookupField -> sprk_eventtype_ref)
 *   - Due Date (Input type="date")
 *   - Priority (Dropdown: Low/Normal/High/Urgent)
 *   - Description (Textarea)
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *
 * @see IDataService — high-level data access abstraction
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
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import { EventService } from './eventService';
import type { ICreateEventFormState } from './formTypes';
import { EMPTY_EVENT_FORM } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateEventStepProps {
  dataService: IDataService;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: ICreateEventFormState) => void;
  initialFormValues?: ICreateEventFormState;
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

export const CreateEventStep: React.FC<ICreateEventStepProps> = ({
  dataService,
  onValidChange,
  onFormValues,
  initialFormValues,
}) => {
  const styles = useStyles();

  const [formValues, setFormValues] = React.useState<ICreateEventFormState>(
    initialFormValues ?? EMPTY_EVENT_FORM
  );

  const serviceRef = React.useRef<EventService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new EventService(dataService);
  }

  // Report validity whenever form changes
  React.useEffect(() => {
    const isValid = formValues.eventName.trim().length > 0;
    onValidChange(isValid);
    onFormValues(formValues);
  }, [formValues, onValidChange, onFormValues]);

  // -- Field handlers --------------------------------------------------------

  const handleNameChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormValues((prev) => ({ ...prev, eventName: e.target.value }));
    },
    []
  );

  const handleEventTypeChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormValues((prev) => ({
        ...prev,
        eventTypeId: item?.id ?? '',
        eventTypeName: item?.name ?? '',
      }));
    },
    []
  );

  const handleSearchEventTypes = React.useCallback(
    (query: string) => serviceRef.current!.searchEventTypes(query),
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

  // -- Render ----------------------------------------------------------------

  const eventTypeValue: ILookupItem | null = formValues.eventTypeId
    ? { id: formValues.eventTypeId, name: formValues.eventTypeName }
    : null;

  const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === formValues.priority)?.text ?? 'Normal';

  return (
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Event Details
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Enter the details for the new event.
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

      <LookupField
        label="Event Type"
        value={eventTypeValue}
        onChange={handleEventTypeChange}
        onSearch={handleSearchEventTypes}
        placeholder="Search event types..."
      />

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
          placeholder="Describe the event..."
          rows={4}
          resize="vertical"
        />
      </Field>
    </div>
  );
};
