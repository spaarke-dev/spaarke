/**
 * EventStatusAction — the Event record's toolbar "Change status" (spaarke-SPA-external-access-platform-r4 task 015, FR-36,
 * owner Q-5, S-1, S-3).
 *
 * OFFERED (hide, don't disable) only when the BFF said the caller may change THIS event's status (`canChangeStatus === true`)
 * AND named at least one status to change it to (`allowedStatuses`, every status but the current one: owner S-3). The dialog
 * offers exactly `allowedStatuses`, labelled from the shared status map (`eventFields.ts`), and sends `{ sprk_status }`; the
 * server pairs the matching state. Nothing is chosen at first, so Save stays off until the user picks a status.
 *
 * The dialog is a `FormModal` child of the record (Cancel on the left; Esc keeps it open, no local Esc handler), and the save
 * follows the same rules as Edit (`EventEditAction`): a 204 closes it and re-reads the record, a refusal stays in the dialog
 * (`eventSaveFailureOf`; a 404 too: "This event is no longer available." stays visible while the record is re-read), and the
 * save in flight holds the record.
 */
import * as React from 'react';
import { Field, MessageBar, MessageBarBody, Select, makeStyles, tokens } from '@fluentui/react-components';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import { updateEvent, type ODataEvent } from '../../../api/web-api-client';
import { eventOf } from '../../events/eventDetail';
import { getEventStatusLabel } from '../../events/eventFields';
import { eventSaveFailureOf } from '../../events/eventSave';
import { eventStatusChoicesOf } from '../recordAccess';
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

interface StatusSnapshot {
  event: ODataEvent;
  choices: readonly number[];
}

interface StatusFormProps extends StatusSnapshot {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
  onRefreshRecord: () => void;
  onBusyChange: (busy: boolean) => void;
}

const EventStatusForm: React.FC<StatusFormProps> = ({
  open,
  event,
  choices,
  onClose,
  onSaved,
  onRefreshRecord,
  onBusyChange,
}) => {
  const styles = useStyles();
  const [status, setStatus] = React.useState('');
  const [submitting, setSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

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

  const chosen = choices.find(c => String(c) === status);

  const handleSubmit = async (): Promise<void> => {
    if (chosen === undefined || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await updateEvent(event.sprk_eventid, { sprk_status: chosen });
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
      title="Change status"
      submitLabel={submitting ? 'Saving...' : 'Save'}
      submitDisabled={chosen === undefined}
      busy={submitting}
    >
      <div className={styles.form}>
        {error && (
          <MessageBar intent="error" role="alert" data-testid="event-status-error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        )}
        <Field label="Status" required>
          <Select value={status} onChange={(_e, d) => setStatus(d.value)} disabled={submitting}>
            <option value="">Select a status</option>
            {choices.map(c => (
              <option key={c} value={String(c)}>
                {getEventStatusLabel(c)}
              </option>
            ))}
          </Select>
        </Field>
      </div>
    </FormModal>
  );
};

export const EventStatusAction: React.FC<RecordActionHostContext> = ({
  record,
  onActionChange,
  onBusyChange,
  refreshRecord,
}) => {
  const event = eventOf(record);
  const choices = eventStatusChoicesOf(event);
  const offered = event !== null && choices.length > 0;
  const { open, shown, hide } = useOfferedDialog<StatusSnapshot | null>(offered, onActionChange, () =>
    event ? { event, choices } : null
  );
  const refreshRef = React.useRef(refreshRecord);
  refreshRef.current = refreshRecord;
  const handleSaved = React.useCallback((): void => {
    hide();
    refreshRef.current();
  }, [hide]);
  const handleRefresh = React.useCallback((): void => refreshRef.current(), []);

  if (!shown?.value) return null;
  return (
    <EventStatusForm
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
