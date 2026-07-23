/**
 * CreateEventStep.tsx
 * Entity-specific form for "Create New Event" wizard.
 *
 * Fields:
 *   - Event Name (required, Input)
 *   - Event Type (LookupField -> sprk_eventtype_ref)
 *   - Due Date (Input type="date")
 *   - Priority (Dropdown: Low/Normal/High/Urgent)
 *   - Assigned To (LookupField -> contact, optional, + "Assign to me")
 *   - Description (Textarea)
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *
 * "Assign to me" (spaarkeai-assistant-enhancements-r1 task 014 / FR-A4):
 * resolves the current Dataverse user onto their own `contact` record via
 * `resolveCurrentUserAsContactAssignee` (the shared current-user identity
 * mechanism — no new identity plumbing is introduced here). Failure to
 * resolve (no Xrm host, or no matching contact) degrades gracefully: the
 * field is simply left for manual search, never blocking event/task creation.
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
  Button,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PersonRegular } from '@fluentui/react-icons';
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import { EventService } from './eventService';
import type { ICreateEventFormState } from './formTypes';
import { EMPTY_EVENT_FORM } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import { searchContactsAsLookup, resolveCurrentUserAsContactAssignee } from '../../services/userLookup';

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
  assigneeRow: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  assigneeLookup: {
    flexGrow: 1,
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

  const [formValues, setFormValues] = React.useState<ICreateEventFormState>(initialFormValues ?? EMPTY_EVENT_FORM);
  const [isAssigningToMe, setIsAssigningToMe] = React.useState(false);

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

  const handleNameChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, eventName: e.target.value }));
  }, []);

  const handleEventTypeChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      eventTypeId: item?.id ?? '',
      eventTypeName: item?.name ?? '',
    }));
  }, []);

  const handleSearchEventTypes = React.useCallback((query: string) => serviceRef.current!.searchEventTypes(query), []);

  const handleDueDateChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setFormValues(prev => ({ ...prev, dueDate: e.target.value }));
  }, []);

  const handlePriorityChange = React.useCallback((_e: unknown, data: { optionValue?: string }) => {
    const val = parseInt(data.optionValue ?? '100000001', 10);
    setFormValues(prev => ({ ...prev, priority: val }));
  }, []);

  const handleDescriptionChange = React.useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setFormValues(prev => ({ ...prev, description: e.target.value }));
  }, []);

  const handleAssignedToChange = React.useCallback((item: ILookupItem | null) => {
    setFormValues(prev => ({
      ...prev,
      assignedToId: item?.id ?? '',
      assignedToName: item?.name ?? '',
    }));
  }, []);

  const handleSearchAssignees = React.useCallback(
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );

  /**
   * "Assign to me" (FR-A4): resolves the current Dataverse user onto their
   * own contact record and sets it as the assignee. Non-blocking — a `null`
   * resolution (no Xrm host, or no matching contact) is a silent no-op so
   * the user can still search manually.
   */
  const handleAssignToMe = React.useCallback(async () => {
    setIsAssigningToMe(true);
    try {
      const assignee = await resolveCurrentUserAsContactAssignee(dataService);
      if (assignee) {
        handleAssignedToChange(assignee);
      }
    } finally {
      setIsAssigningToMe(false);
    }
  }, [dataService, handleAssignedToChange]);

  // -- Render ----------------------------------------------------------------

  const eventTypeValue: ILookupItem | null = formValues.eventTypeId
    ? { id: formValues.eventTypeId, name: formValues.eventTypeName }
    : null;

  const assignedToValue: ILookupItem | null = formValues.assignedToId
    ? { id: formValues.assignedToId, name: formValues.assignedToName }
    : null;

  const selectedPriorityText = PRIORITY_OPTIONS.find(o => o.key === formValues.priority)?.text ?? 'Normal';

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
          <Input type="date" value={formValues.dueDate} onChange={handleDueDateChange} />
        </Field>
        <Field label="Priority">
          <Dropdown
            value={selectedPriorityText}
            selectedOptions={[String(formValues.priority)]}
            onOptionSelect={handlePriorityChange}
          >
            {PRIORITY_OPTIONS.map(opt => (
              <Option key={opt.key} value={String(opt.key)}>
                {opt.text}
              </Option>
            ))}
          </Dropdown>
        </Field>
      </div>

      <div className={styles.assigneeRow}>
        <div className={styles.assigneeLookup}>
          <LookupField
            label="Assigned To"
            value={assignedToValue}
            onChange={handleAssignedToChange}
            onSearch={handleSearchAssignees}
            placeholder="Search contacts..."
            minSearchLength={2}
          />
        </div>
        <Button
          appearance="secondary"
          icon={isAssigningToMe ? <Spinner size="tiny" /> : <PersonRegular />}
          onClick={handleAssignToMe}
          disabled={isAssigningToMe}
          aria-label="Assign to me"
        >
          Assign to me
        </Button>
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
