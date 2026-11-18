/**
 * EventEditAction — the Event record's toolbar "Edit" (spaarke-SPA-external-access-platform-r4 task 015, FR-36, owner Q-6,
 * S-1, S-4).
 *
 * OFFERED (hide, don't disable) only when the BFF said the caller may edit THIS event: `canEdit === true` on the event it
 * returned (`eventEditRightOf`). Collaborate and Full Access on every parent; View Only never. The server re-checks the PATCH.
 *
 * THE DIALOG. A `FormModal` child of the record (Cancel on the left; Esc keeps the draft, there is no local Esc handler: the
 * modal standard, project CLAUDE.md 2026-11-11), with Title, Due Date, Priority and Description, and the Event Type shown
 * READ-ONLY (S-4: the type cannot change, so no due-date re-validation). Assigned To appears only when the BFF said the event
 * may be reassigned (`canReassign === true`: open work, and a role that reads contacts); its options are the parent record's
 * assignable people ("Me" first) and its default is "no change", naming the current assignee. Choosing someone sends
 * `assignedToContactId`, which the server treats as a reassign (status Reassigned unless a status is also sent). Only the
 * fields that CHANGED are sent; with no change the Save button stays off, so nothing is submitted.
 *
 * AFTER A 204 the dialog closes and the record is re-read (`refreshRecord`), so it shows the new values; the list behind it
 * re-reads when the record closes (the section it was opened from). A refusal stays in the dialog (`eventSaveFailureOf`): a
 * 403 says the right is gone and re-reads the record, a 404 says "This event is no longer available." and re-reads it too (the dialog stays open so the
 * reason stays visible; the record behind it then shows its not-found state), a 503 is retryable, a 400 shows the server's own message. The multi-parent assignee refusal (`assignee_not_allowed`: a person
 * who may not see ONE of the event's other parents) is shown as that message: the SPA knows only the parent the event was
 * opened from, so it cannot tell beforehand (an accepted limit). The save in flight holds the record (`onBusyChange`).
 */
import * as React from 'react';
import {
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Select,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import {
  updateEvent,
  type ODataEvent,
  type ODataEventRegarding,
  type UpdateEventPatch,
} from '../../../api/web-api-client';
import { eventOf, eventsRootOfRegarding, regardingOf } from '../../events/eventDetail';
import { EVENT_PRIORITY_OPTIONS } from '../../events/eventFields';
import { eventSaveFailureOf } from '../../events/eventSave';
import { useAssignablePeople } from '../../events/useAssignablePeople';
import { eventEditRightOf, eventReassignRightOf } from '../recordAccess';
import type { RecordActionHostContext } from '../recordTemplate.types';
import { useOfferedDialog } from './offeredDialog';

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
  },
});

interface EditSnapshot {
  event: ODataEvent;
  regarding: ODataEventRegarding | null;
  canReassign: boolean;
}

interface EditFormProps extends EditSnapshot {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
  onRefreshRecord: () => void;
  onBusyChange: (busy: boolean) => void;
}

const EventEditForm: React.FC<EditFormProps> = ({
  open,
  event,
  regarding,
  canReassign,
  onClose,
  onSaved,
  onRefreshRecord,
  onBusyChange,
}) => {
  const styles = useStyles();
  const [title, setTitle] = React.useState(event.sprk_name);
  const [dueDate, setDueDate] = React.useState(event.sprk_duedate ?? '');
  // A priority the event does not have (an older event) starts blank, and is sent only if the user picks one.
  const [priority, setPriority] = React.useState(
    event.sprk_priority !== null && event.sprk_priority !== undefined ? String(event.sprk_priority) : ''
  );
  const [description, setDescription] = React.useState(event.sprk_description ?? '');
  const [assignee, setAssignee] = React.useState('');
  const [submitting, setSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // The save in flight holds the record; unmounting always reports "not busy".
  const busyRef = React.useRef(onBusyChange);
  busyRef.current = onBusyChange;
  React.useEffect(() => {
    busyRef.current(submitting);
  }, [submitting]);
  React.useEffect(
    () => () => {
      busyRef.current(false);
    },
    []
  );

  const root = eventsRootOfRegarding(regarding);
  const people = useAssignablePeople(root, regarding?.id ?? '', open && canReassign);
  const showAssignee = canReassign && people.status === 'ready' && people.people.length > 0;

  const priorityKnown = EVENT_PRIORITY_OPTIONS.some(o => String(o.value) === priority);
  const patch: UpdateEventPatch = {};
  if (title.trim() && title.trim() !== event.sprk_name) patch.sprk_name = title.trim();
  if (dueDate !== (event.sprk_duedate ?? '')) patch.sprk_duedate = dueDate === '' ? null : dueDate;
  if (priorityKnown && Number(priority) !== event.sprk_priority) patch.sprk_priority = Number(priority);
  if (description !== (event.sprk_description ?? '')) patch.sprk_description = description === '' ? null : description;
  if (showAssignee && assignee) patch.assignedToContactId = assignee;
  const changed = Object.keys(patch).length > 0;

  const handleSubmit = async (): Promise<void> => {
    if (!changed || !title.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await updateEvent(event.sprk_eventid, patch);
      setSubmitting(false);
      onSaved();
    } catch (err) {
      const failure = eventSaveFailureOf(err);
      setSubmitting(false);
      setError(failure.message);
      if (failure.rereadRecord) onRefreshRecord();
    }
  };

  const handleClose = (): void => {
    if (submitting) return;
    onClose();
  };

  return (
    <FormModal
      open={open}
      onClose={handleClose}
      onSubmit={() => void handleSubmit()}
      title="Edit event"
      submitLabel={submitting ? 'Saving...' : 'Save'}
      submitDisabled={!changed || !title.trim()}
      busy={submitting}
    >
      <div className={styles.form}>
        {error && (
          <MessageBar intent="error" role="alert" data-testid="event-edit-error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        )}

        <Field label="Title" required>
          <Input value={title} onChange={(_e, d) => setTitle(d.value)} maxLength={200} disabled={submitting} />
        </Field>

        {/* S-4: the type is fixed once the event exists, so it is shown, not editable. */}
        <Field label="Event Type">
          <Input value={event.eventTypeName ?? '—'} readOnly />
        </Field>

        <Field label="Due Date">
          <Input type="date" value={dueDate} onChange={(_e, d) => setDueDate(d.value)} disabled={submitting} />
        </Field>

        <Field label="Priority">
          <Select value={priority} onChange={(_e, d) => setPriority(d.value)} disabled={submitting}>
            {!priorityKnown && <option value="">Not set</option>}
            {EVENT_PRIORITY_OPTIONS.map(o => (
              <option key={o.value} value={String(o.value)}>
                {o.label}
              </option>
            ))}
          </Select>
        </Field>

        {showAssignee && people.status === 'ready' && (
          <Field label="Assigned To">
            <Select value={assignee} onChange={(_e, d) => setAssignee(d.value)} disabled={submitting}>
              <option value="">{event.assignedToName ? `No change (${event.assignedToName})` : 'No change'}</option>
              {people.people.map(p => (
                <option key={p.contactId} value={p.contactId}>
                  {p.isMe ? `${p.name} (Me)` : p.name}
                </option>
              ))}
            </Select>
          </Field>
        )}
        {canReassign && people.status === 'failed' && (
          <Text size={200} data-testid="event-assignee-unavailable">
            The people you can assign this to could not be loaded, so Assigned To is not shown. You can still save your
            other changes.
          </Text>
        )}

        <Field label="Description">
          <Textarea
            value={description}
            onChange={(_e, d) => setDescription(d.value)}
            maxLength={2000}
            resize="vertical"
            disabled={submitting}
          />
        </Field>
      </div>
    </FormModal>
  );
};

export const EventEditAction: React.FC<RecordActionHostContext> = ({
  record,
  onActionChange,
  onBusyChange,
  refreshRecord,
}) => {
  const event = eventOf(record);
  const regarding = regardingOf(record);
  const offered = event !== null && eventEditRightOf(event);
  const { open, shown, hide } = useOfferedDialog<EditSnapshot | null>(offered, onActionChange, () =>
    event ? { event, regarding, canReassign: eventReassignRightOf(event) } : null
  );
  // After a save: close, then show the new values (the record re-reads; the list behind it re-reads on close of the record).
  const refreshRef = React.useRef(refreshRecord);
  refreshRef.current = refreshRecord;
  const handleSaved = React.useCallback((): void => {
    hide();
    refreshRef.current();
  }, [hide]);
  const handleRefresh = React.useCallback((): void => refreshRef.current(), []);

  if (!shown?.value) return null;
  return (
    <EventEditForm
      key={shown.seq}
      {...shown.value}
      open={open}
      onClose={hide}
      onSaved={handleSaved}
      onRefreshRecord={handleRefresh}
      onBusyChange={onBusyChange}
    />
  );
};
