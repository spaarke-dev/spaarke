/**
 * EventsCalendar — the Tasks and Events lists of a record, for the Secure Project Workspace SPA.
 *
 * Displays the events of a record in an upcoming/chronological view, in two groups (R4 task 011, owner 2026-11-12:
 * external users work only with sprk_event; a Task is an event of type Task): the events the BFF flags `isTask` are the
 * record's Tasks, every other event is an Event. ONE read feeds both groups. A caller who may create can add an event
 * (no type sent) and, when the list also says `canCreateTask`, a task (`eventType: "task"`) via an inline dialog.
 * View Only users see a read-only list.
 *
 * Design pattern follows the internal EventsPage (src/solutions/EventsPage/) —
 * list format with date, title, status badge, and description.
 *
 * Props:
 *   projectId   — Dataverse GUID of the record whose events to list: a sprk_project unless `root` says otherwise
 *   root        — which kind of record that is (task 057): 'projects' (default), 'matters' or 'workassignments'. Reads
 *                 GET /api/v1/external/{root}/{id}/events; Create posts to the same record's route (`createRootEvent`,
 *                 R4 task 006 - every root has one since task 002).
 *   accessLevel — The caller's `/me` level, passed only for a record `/me` carries a level for (a project): for a caller
 *                 without `writeAccessPerRecord` it must allow create AND agree with the list's `canCreate`. Without one,
 *                 the list's `canCreate` decides and a missing one is "cannot" (FR-06; `createRightOf`, in the record
 *                 template and on the project page alike, D-9).
 *   onCreateActionChange / onCreateTaskActionChange — record template only: the section's Add event / Add task are the
 *                 entry points (tasks 009 / 011); without them this list keeps its own Create Event / Create Task buttons.
 *   caller      — The signed-in principal (task 054, FR-26): an event the caller CREATED shows Delete (asks first;
 *                 a refusal shows an error and the event stays). Independent of accessLevel; none without `caller`.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + tokens. No hard-coded colors.
 * ADR-022: React 18 component (bundled, not platform-provided).
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Spinner,
  Button,
  Badge,
  Divider,
  Field,
  Input,
  Textarea,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
} from '@fluentui/react-components';
import { AddRegular, CalendarEmptyRegular, DeleteRegular, TaskListLtrRegular } from '@fluentui/react-icons';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import { parseDueDate } from '@spaarke/ui-components/utils/dateLocal';
import {
  getRootEvents,
  createRootEvent,
  deleteOwnEvent,
  ODataEvent,
  CreateEventPayload,
  EVENTS_ROOT_NOUN,
  type EventsRoot,
} from '../api/web-api-client';
import { isCreatedByCaller, type CallerIdentity } from '../api/delete-own';
import { useDeleteOwn, DeleteOwnConfirm, DeleteFailureNotice } from './DeleteOwn';
import { TruncatedListNotice } from './TruncatedListNotice';
import { useChildDialog, useOfferCreateAction, type CreateActionReporter } from './createAction';
import { createTaskRightOf, levelVerdictOf, listCreateRightOf } from './record/recordAccess';
import { BffAccessState, accessFailureOf, type BffAccessFailure } from './auth/BffAccessState';
import { AccessLevel, ApiError } from '../types';
import { problemMessage } from '../auth/bff-client';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  // One group of the list: its heading (with the group's own create button outside the record template) and its rows.
  group: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingBottom: tokens.spacingVerticalS,
  },
  toolbarTitle: {
    color: tokens.colorNeutralForeground1,
  },
  loadingContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '180px',
    gap: tokens.spacingHorizontalM,
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '120px',
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalXL,
    ...shorthands.border('1px', 'dashed', tokens.colorNeutralStroke2),
  },
  emptyStateIcon: {
    fontSize: '40px',
    color: tokens.colorNeutralForeground4,
  },
  emptyStateText: {
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  eventList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0',
  },
  eventRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },
  eventDateCol: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    minWidth: '60px',
    paddingTop: '2px',
  },
  eventDateMonth: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase100,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    fontWeight: tokens.fontWeightSemibold,
  },
  eventDateDay: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: '1',
  },
  eventDateYear: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
  eventDateNoDate: {
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
    fontStyle: 'italic',
    textAlign: 'center',
  },
  eventContent: {
    flex: '1',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  eventTitleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  eventTitle: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  eventMeta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  divider: {
    marginTop: '0',
    marginBottom: '0',
  },
  errorBar: {
    marginBottom: tokens.spacingVerticalS,
  },
  // Dialog form layout
  dialogForm: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
  },
});

/**
 * The two groups of the list (task 011): an `event` is any event the BFF does not flag `isTask`; a `task` is one it does.
 * "Add task" creates a task; "Add event" creates an event.
 */
type EventKind = 'event' | 'task';

/**
 * One busy reporter per create dialog, combined for the host (task 011, review round 1). The host is told "busy" while
 * EITHER dialog is posting: a dialog going idle (or unmounting) releases only its own hold, never the other's. A report
 * that repeats a dialog's last value is ignored. Exported for its test.
 */
export function createBusyReporters(report: (busy: boolean) => void): Record<EventKind, (busy: boolean) => void> {
  const busyOf: Record<EventKind, boolean> = { event: false, task: false };
  const reporterFor =
    (kind: EventKind) =>
    (busy: boolean): void => {
      if (busyOf[kind] === busy) return;
      busyOf[kind] = busy;
      report(busyOf.event || busyOf.task);
    };
  return { event: reporterFor('event'), task: reporterFor('task') };
}

/** The words of each kind: its dialog, its create button and its messages. */
const KIND_TEXT: Readonly<Record<EventKind, { title: string; create: string; required: string; placeholder: string }>> =
  {
    event: {
      title: 'Create Event',
      create: 'Create Event',
      required: 'Event title is required.',
      placeholder: 'Enter event title',
    },
    task: {
      title: 'Create Task',
      create: 'Create Task',
      required: 'Task title is required.',
      placeholder: 'Enter task title',
    },
  };

// ---------------------------------------------------------------------------
// Event status helpers
// ---------------------------------------------------------------------------

/**
 * Map the event's `sprk_status` wire value to a label. The BFF reads it from the
 * event's status of record, Dataverse `statuscode` (task 097 review F2 — the same
 * column POST /api/v1/events/{id}/complete writes; the deprecated second status column is not used).
 * Live option set, verified 2026-10-05. An unknown or missing value is shown as
 * such — never defaulted to "Open".
 */
const EVENT_STATUS_LABELS: Record<number, string> = {
  1: 'Draft',
  659490001: 'Open',
  659490002: 'Completed',
  659490003: 'Closed',
  659490004: 'Cancelled',
  659490005: 'Transferred',
  659490006: 'On Hold',
  659490007: 'Reassigned',
  2: 'No Further Action',
};

function getEventStatusLabel(status: number | null | undefined): string {
  if (status === null || status === undefined) return 'Unknown';
  return EVENT_STATUS_LABELS[status] ?? 'Unknown';
}

/**
 * Map status to Fluent Badge color (statuscode values; see EVENT_STATUS_LABELS).
 */
function getEventStatusColor(
  status: number | null | undefined
): 'brand' | 'success' | 'warning' | 'danger' | 'informative' | undefined {
  switch (status) {
    case 659490001: // Open
      return 'brand';
    case 659490006: // On Hold
    case 659490007: // Reassigned
      return 'warning';
    case 659490002: // Completed
    case 659490003: // Closed
      return 'success';
    case 659490004: // Cancelled
      return 'danger';
    default: // Draft, Transferred, No Further Action, unknown
      return 'informative';
  }
}
// ---------------------------------------------------------------------------
// Date formatting helpers
// ---------------------------------------------------------------------------

interface ParsedDate {
  month: string;
  day: string;
  year: string;
  full: string;
}

/**
 * sprk_duedate is a calendar date ("yyyy-MM-dd" — Dataverse Date Only, task 098). `new Date("yyyy-MM-dd")` is UTC
 * midnight, which every zone west of UTC shows as the PREVIOUS day; parseDueDate builds the local calendar date.
 */
function parseDateParts(iso: string | null | undefined): ParsedDate | null {
  const d = parseDueDate(iso);
  if (!d) return null;

  return {
    month: d.toLocaleDateString('en-US', { month: 'short' }),
    day: String(d.getDate()),
    year: String(d.getFullYear()),
    full: d.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' }),
  };
}

// ---------------------------------------------------------------------------
// Sub-component: event date column
// ---------------------------------------------------------------------------

interface EventDateColumnProps {
  duedate: string | null | undefined;
}

const EventDateColumn: React.FC<EventDateColumnProps> = ({ duedate }) => {
  const styles = useStyles();
  const parsed = parseDateParts(duedate);

  if (!parsed) {
    return (
      <div className={styles.eventDateCol}>
        <Text className={styles.eventDateNoDate}>No date</Text>
      </div>
    );
  }

  return (
    <div className={styles.eventDateCol}>
      <Text className={styles.eventDateMonth}>{parsed.month}</Text>
      <Text className={styles.eventDateDay}>{parsed.day}</Text>
      <Text className={styles.eventDateYear}>{parsed.year}</Text>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Sub-component: single event row
// ---------------------------------------------------------------------------

interface EventRowProps {
  event: ODataEvent;
  isLast: boolean;
  /** Which group the row is in; names the Delete button ("Delete task X" / "Delete event X"). */
  kind: EventKind;
  /** Show Delete (the caller created this event). */
  canDelete?: boolean;
  onDelete?: (event: ODataEvent) => void;
}

const EventRow: React.FC<EventRowProps> = ({ event, isLast, kind, canDelete, onDelete }) => {
  const styles = useStyles();

  return (
    <>
      <div className={styles.eventRow}>
        {/* Date column */}
        <EventDateColumn duedate={event.sprk_duedate} />

        {/* Event content */}
        <div className={styles.eventContent}>
          <div className={styles.eventTitleRow}>
            <Text className={styles.eventTitle}>{event.sprk_name}</Text>

            {/* Status badge */}
            <Badge appearance="tint" color={getEventStatusColor(event.sprk_status)} size="small">
              {getEventStatusLabel(event.sprk_status)}
            </Badge>
          </div>

          {/* Created on meta */}
          {event.createdon && (
            <Text className={styles.eventMeta}>Added {parseDateParts(event.createdon)?.full ?? ''}</Text>
          )}
        </div>

        {/* Delete: only an event the caller created (the server re-checks every delete). */}
        {canDelete && onDelete && (
          <Button
            appearance="subtle"
            size="small"
            icon={<DeleteRegular />}
            onClick={() => onDelete(event)}
            aria-label={`Delete ${kind} ${event.sprk_name}`}
          />
        )}
      </div>

      {!isLast && <Divider className={styles.divider} />}
    </>
  );
};

// ---------------------------------------------------------------------------
// Sub-component: Create Event dialog
// ---------------------------------------------------------------------------

interface CreateEventDialogProps {
  /** Which kind of record the event is created under, and its id (POST /api/v1/external/{root}/{id}/events). */
  root: EventsRoot;
  recordId: string;
  /** `task` sends `eventType: "task"`; `event` sends no type (task 011). */
  kind: EventKind;
  open: boolean;
  onClose: () => void;
  onCreated: (event: ODataEvent) => void;
  /** Called with `true` while the create is in flight and `false` otherwise (and on unmount), so a host can hold. */
  onBusyChange?: (busy: boolean) => void;
}

const CreateEventDialog: React.FC<CreateEventDialogProps> = ({
  root,
  recordId,
  kind,
  open,
  onClose,
  onCreated,
  onBusyChange,
}) => {
  const styles = useStyles();
  const text = KIND_TEXT[kind];

  const [title, setTitle] = React.useState<string>('');
  const [dueDate, setDueDate] = React.useState<string>('');
  const [description, setDescription] = React.useState<string>('');
  const [submitting, setSubmitting] = React.useState<boolean>(false);
  const [submitError, setSubmitError] = React.useState<string | null>(null);

  // Validation state
  const [titleError, setTitleError] = React.useState<string>('');

  // The record template holds browsing, Close and Refresh while the post is in flight (task 004). The latest callback is
  // read through a ref so a new function identity never re-fires the effect, and unmount always reports "not busy".
  const onBusyChangeRef = React.useRef(onBusyChange);
  onBusyChangeRef.current = onBusyChange;
  React.useEffect(() => {
    onBusyChangeRef.current?.(submitting);
  }, [submitting]);
  React.useEffect(
    () => () => {
      onBusyChangeRef.current?.(false);
    },
    []
  );

  // Reset form when dialog opens
  React.useEffect(() => {
    if (open) {
      setTitle('');
      setDueDate('');
      setDescription('');
      setSubmitting(false);
      setSubmitError(null);
      setTitleError('');
    }
  }, [open]);

  const validate = (): boolean => {
    let valid = true;

    if (!title.trim()) {
      setTitleError(text.required);
      valid = false;
    } else {
      setTitleError('');
    }

    return valid;
  };

  const handleSubmit = async () => {
    if (!validate()) return;

    setSubmitting(true);
    setSubmitError(null);

    try {
      const payload: CreateEventPayload = {
        sprk_name: title.trim(),
        // Task 098: the date input's own "yyyy-MM-dd" — the column is Date Only and Dataverse refuses a timestamp
        // (the former toISOString() also turned the picked day into a UTC instant).
        ...(dueDate ? { sprk_duedate: dueDate } : {}),
        // Task 011: a task is an event of the closed type set's one member; an event sends no type at all.
        ...(kind === 'task' ? { eventType: 'task' as const } : {}),
        // sprk_status omitted: the BFF creates the event Open (statuscode 659490001) — task 097 review F9.
        // The parent comes from the route (task 006); `createRootEvent` keeps the project route's body as it was.
      };

      const created = await createRootEvent(root, recordId, payload);
      onCreated(created);
      onClose();
    } catch (err) {
      if (err instanceof ApiError) {
        setSubmitError(`Failed to create ${kind}: ${err.message}`);
      } else {
        setSubmitError('An unexpected error occurred. Please try again.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  // Never closes mid-post (Cancel and the FormModal's busy state already refuse it; the × and Esc route here too).
  const handleClose = () => {
    if (submitting) return;
    onClose();
  };

  // A FormModal (task 004, FR-03): Cancel in the left footer slot, Create Event / Create Task on the right. Esc does
  // nothing (modal standard: an explicit dialog keeps a draft).
  return (
    <FormModal
      open={open}
      onClose={handleClose}
      onSubmit={() => void handleSubmit()}
      title={text.title}
      submitLabel={submitting ? 'Creating...' : text.create}
      submitDisabled={!title.trim()}
      busy={submitting}
    >
      <div className={styles.dialogForm}>
        {/* Submit error */}
        {submitError && (
          <MessageBar intent="error" className={styles.errorBar}>
            <MessageBarBody>{submitError}</MessageBarBody>
          </MessageBar>
        )}

        {/* Title (required) */}
        <Field
          label="Title"
          required
          validationMessage={titleError || undefined}
          validationState={titleError ? 'error' : 'none'}
        >
          <Input
            value={title}
            onChange={(_ev, data) => setTitle(data.value)}
            placeholder={text.placeholder}
            disabled={submitting}
            maxLength={200}
          />
        </Field>

        {/* No type select (task 011, D-6): the old one was never sent. The kind is chosen by the action: Add task / Add event. */}

        {/* Due date */}
        <Field label="Due Date">
          <Input type="date" value={dueDate} onChange={(_ev, data) => setDueDate(data.value)} disabled={submitting} />
        </Field>

        {/* Description */}
        <Field label="Description">
          <Textarea
            value={description}
            onChange={(_ev, data) => setDescription(data.value)}
            placeholder="Optional description or notes"
            disabled={submitting}
            rows={3}
            maxLength={2000}
          />
        </Field>
      </div>
    </FormModal>
  );
};

// ---------------------------------------------------------------------------
// Main component: EventsCalendar
// ---------------------------------------------------------------------------

export interface EventsCalendarProps {
  /** Dataverse GUID of the record whose events to list (a sprk_project unless `root` says otherwise). */
  projectId: string;
  /** Which kind of record `projectId` is (task 057). Defaults to a project. */
  root?: EventsRoot;
  /**
   * The caller's `/me` level for this record. A level DECIDES a create only for a project caller without
   * `writeAccessPerRecord`: pass it for that case and omit it otherwise. A matter or work assignment never passes one
   * (its `/me` `records` level is display only, task 011): Add event follows the list's `canCreate`, and Add task also
   * needs the list's `canCreateTask` (task 006 / 011).
   */
  accessLevel?: AccessLevel;
  /**
   * The caller's write access is decided per record (`/me` `writeAccessPerRecord`, task 036 fix round 1): "create" then
   * follows the BFF's `canCreate` on the project's event list instead of `accessLevel`.
   */
  writeAccessPerRecord?: boolean;
  /** The signed-in principal. With it, events the caller created show Delete (task 054); without it, none do. */
  caller?: CallerIdentity | null;
  /**
   * Record template only (task 004, FR-03; task 009). When given, the Add event in the section's header is the entry
   * point: this list shows no "Create Event" button of its own, offers its action through this callback (shown exactly
   * when its own button would have been) and renders the create dialog itself, so the dialog is a descendant of the host.
   * Omit it everywhere else (the project page keeps its button).
   */
  onCreateActionChange?: CreateActionReporter;
  /**
   * Record template only (task 011): the same for Add task (shown exactly when the list's `canCreateTask` and the event
   * right both say yes). Without it the list keeps its own "Create Task" button (the project page), under the same rule.
   * `onCreateActionChange` alone decides whether the list is inside the record template.
   */
  onCreateTaskActionChange?: CreateActionReporter;
  /**
   * Called with `true` while a create is in flight and `false` otherwise (and on unmount), so the host can hold. Task 011:
   * the list has two create dialogs (task, event); the hold is on while EITHER is posting, and one going idle never
   * releases the other's in-flight hold.
   */
  onBusyChange?: (busy: boolean) => void;
}

/**
 * EventsCalendar — the Tasks and Events lists of a record for external participants.
 *
 * Fetches the record's events (GET /api/v1/external/{root}/{id}/events), ordered by due date ascending (soonest
 * first), and shows them in two groups: Tasks (`isTask`) and Events (the rest).
 *
 * - View Only users: read-only list
 * - Collaborate / Full Access users: "Create Event" button and dialog; "Create Task" too when the list's
 *   `canCreateTask` is true (outside the record template; inside it both are the section's header actions)
 *
 * Loading and empty states follow the SectionCard + PageContainer pattern
 * established by other SPA components.
 */
export const EventsCalendar: React.FC<EventsCalendarProps> = ({
  projectId,
  root = 'projects',
  accessLevel,
  caller,
  writeAccessPerRecord = false,
  onCreateActionChange,
  onCreateTaskActionChange,
  onBusyChange,
}) => {
  const styles = useStyles();

  // -------------------------------------------------------------------------
  // State
  // -------------------------------------------------------------------------

  const [events, setEvents] = React.useState<ODataEvent[]>([]);
  const [loading, setLoading] = React.useState<boolean>(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const createDialog = useChildDialog();
  const taskDialog = useChildDialog();

  // Task 011: the hold per dialog. Each create dialog reports its own busy state; the host is told "busy" while either
  // is posting, so one going idle (or unmounting) never releases the other's hold. The host's callback is read through a
  // ref so a new function identity never re-reports.
  const onBusyChangeRef = React.useRef(onBusyChange);
  onBusyChangeRef.current = onBusyChange;
  const busyReporters = React.useMemo(
    () => createBusyReporters((busy: boolean) => onBusyChangeRef.current?.(busy)),
    []
  );
  // Task 105: the BFF said the list was cut short — shown, never presented as the whole list.
  const [truncated, setTruncated] = React.useState<boolean>(false);
  // A 401/403 on the list is the shared sign-in / denied state; "Try again" re-runs the read (task 057).
  const [accessFailure, setAccessFailure] = React.useState<BffAccessFailure | null>(null);
  const [attempt, setAttempt] = React.useState<number>(0);
  const noun = EVENTS_ROOT_NOUN[root];

  // -------------------------------------------------------------------------
  // Access level check
  // -------------------------------------------------------------------------

  // Every root has an event create route (task 002). Task 006 (FR-06): the create rule lives in recordAccess.ts
  // (`listCreateRightOf`, shared with DocumentLibrary). Offered only when the right is known `true`: the
  // list has answered, its `canCreate` allows (task 036 fix round 1), and a `/me` level that decides (a level the host has
  // - a project - for a caller whose write access is not per record) allows too. The same rule in the record template and
  // on the project page (D-9 #2716: the page used to offer on the level alone, which a role ceiling refuses with 403).
  const [listCanCreate, setListCanCreate] = React.useState<boolean | null>(null);
  // Task 011: the list's own answer for the Task type (`canCreateTask`); null = the BFF said nothing (hide "Add task").
  const [listCanCreateTask, setListCanCreateTask] = React.useState<boolean | null>(null);
  const inRecordTemplate = onCreateActionChange !== undefined;
  const loaded = !loading && !accessFailure && !loadError;
  const { right, allowed: canCreate } = listCreateRightOf({
    loaded,
    listCanCreate,
    levelAllows: levelVerdictOf(accessLevel, writeAccessPerRecord),
  });
  // Add task = the event right AND the list's `canCreateTask`, both an explicit true (`createTaskRightOf`). For a
  // `writeAccessPerRecord` caller no level is involved: the label never decides a write action (uac-r2 A2).
  const taskRight = createTaskRightOf(right, listCanCreateTask);
  const canCreateTask = taskRight === true;
  // In the record template the section's Add event / Add task replace this list's buttons (task 004 / 009 / 011): offered
  // exactly when the right is known to be `true` (the main view is showing), and the dialogs stay here. The right goes
  // with the offer.
  const ownCreateButton = !inRecordTemplate;
  useOfferCreateAction(onCreateActionChange, canCreate && loaded, createDialog.show, right);
  useOfferCreateAction(onCreateTaskActionChange, canCreateTask && loaded, taskDialog.show, taskRight);

  // Delete-own (task 054): the event leaves the list on the server's 204; any refusal keeps it and is shown. One state per
  // group, so the confirm and the failure notice name what the row is ("task" / "event").
  const removeEvent = (event: ODataEvent): void =>
    setEvents(prev => prev.filter(e => e.sprk_eventid !== event.sprk_eventid));
  const delEvent = useDeleteOwn<ODataEvent>({
    noun: 'event',
    remove: event => deleteOwnEvent(event.sprk_eventid),
    onRemoved: removeEvent,
  });
  const delTask = useDeleteOwn<ODataEvent>({
    noun: 'task',
    remove: event => deleteOwnEvent(event.sprk_eventid),
    onRemoved: removeEvent,
  });

  // -------------------------------------------------------------------------
  // Data fetching
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    if (!projectId) return;

    let cancelled = false;

    const fetchEvents = async () => {
      setLoading(true);
      setLoadError(null);
      setAccessFailure(null);

      try {
        // The BFF returns every event up to its row cap and says `truncated` when it stops short (task 105).
        const data = await getRootEvents(root, projectId, {
          $orderby: 'sprk_duedate asc',
        });

        if (!cancelled) {
          setEvents(data.items);
          setTruncated(data.truncated);
          setListCanCreate(data.canCreate ?? null);
          setListCanCreateTask(data.canCreateTask ?? null);
        }
      } catch (err) {
        if (!cancelled) {
          const failure = accessFailureOf(err);
          if (failure) {
            setAccessFailure(failure);
          } else {
            // The server's ProblemDetails `detail` when it wrote one; never the raw response body.
            setLoadError(problemMessage(err, 'Events could not be loaded just now.'));
          }
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void fetchEvents();

    return () => {
      cancelled = true;
    };
  }, [projectId, root, attempt]);

  // -------------------------------------------------------------------------
  // Handler: event created
  // -------------------------------------------------------------------------

  const handleEventCreated = (newEvent: ODataEvent) => {
    setEvents(prev => {
      // Insert new event and re-sort by due date ascending
      const updated = [...prev, newEvent];
      updated.sort((a, b) => {
        const da = parseDueDate(a.sprk_duedate)?.getTime() ?? Infinity;
        const db = parseDueDate(b.sprk_duedate)?.getTime() ?? Infinity;
        return da - db;
      });
      return updated;
    });
  };

  // -------------------------------------------------------------------------
  // Render: loading
  // -------------------------------------------------------------------------

  if (loading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="small" />
        <Text size={300} color="neutralForeground3">
          Loading events...
        </Text>
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Render: error
  // -------------------------------------------------------------------------

  if (accessFailure) {
    return (
      <BffAccessState
        failure={accessFailure}
        deniedMessage={`You do not have permission to view events on this ${noun}.`}
      />
    );
  }

  if (loadError) {
    return (
      <MessageBar intent="error" className={styles.errorBar}>
        <MessageBarBody>{loadError}</MessageBarBody>
        <MessageBarActions>
          <Button appearance="secondary" size="small" onClick={() => setAttempt(n => n + 1)}>
            Try again
          </Button>
        </MessageBarActions>
      </MessageBar>
    );
  }

  // -------------------------------------------------------------------------
  // Render: main view
  // -------------------------------------------------------------------------

  // Task 011: one read, two groups. `isTask` is the BFF's flag (absent = not a task, so an older BFF lists everything
  // under Events, as before). Order inside a group is the list's own (due date ascending).
  const tasks = events.filter(e => e.isTask === true);
  const plainEvents = events.filter(e => e.isTask !== true);

  const renderGroup = (kind: EventKind, rows: ODataEvent[]): React.ReactNode => {
    const isTaskGroup = kind === 'task';
    const mayCreate = isTaskGroup ? canCreateTask : canCreate;
    const del = isTaskGroup ? delTask : delEvent;
    const label = isTaskGroup ? 'Tasks' : 'Events';
    const plural = isTaskGroup ? 'tasks' : 'events';
    const EmptyIcon = isTaskGroup ? TaskListLtrRegular : CalendarEmptyRegular;
    const createText = KIND_TEXT[kind].create;
    const addAction = isTaskGroup ? 'Add task' : 'Add event';
    // Task 105: a cut-short list is not "no {tasks|events}" — the notice says it could not be read, so a group that is
    // empty in a truncated list claims nothing: no empty-state text. Its heading and its own create button stay (the
    // caller may still add one; ProjectPage's Calendar tab has no section action). With neither to show, nothing renders.
    const claimsNothing = rows.length === 0 && truncated;
    if (claimsNothing && !(mayCreate && ownCreateButton)) return null;
    return (
      <div className={styles.group} data-testid={`events-group-${plural}`}>
        <div className={styles.toolbar}>
          <Text size={400} weight="semibold" className={styles.toolbarTitle}>
            {label}
          </Text>

          {mayCreate && ownCreateButton && (
            <Button
              appearance={isTaskGroup ? 'secondary' : 'primary'}
              icon={<AddRegular />}
              onClick={isTaskGroup ? taskDialog.show : createDialog.show}
              size="small"
            >
              {createText}
            </Button>
          )}
        </div>

        {claimsNothing ? null : rows.length === 0 ? (
          <div className={styles.emptyState}>
            <EmptyIcon className={styles.emptyStateIcon} />
            <Text size={400} weight="semibold">
              No {plural} yet
            </Text>
            <Text size={300} className={styles.emptyStateText}>
              {mayCreate
                ? ownCreateButton
                  ? `No ${plural} have been added to this ${noun}. Use the ${createText} button to add the first ${kind}.`
                  : `No ${plural} have been added to this ${noun}. Use ${addAction} above to add the first ${kind}.`
                : `No ${plural} have been added to this ${noun} yet.`}
            </Text>
          </div>
        ) : (
          <div className={styles.eventList}>
            {rows.map((event, index) => (
              <EventRow
                key={event.sprk_eventid}
                event={event}
                kind={kind}
                isLast={index === rows.length - 1}
                canDelete={isCreatedByCaller(event, caller)}
                onDelete={e => del.ask(e, e.sprk_name)}
              />
            ))}
          </div>
        )}
      </div>
    );
  };

  return (
    <div className={styles.root}>
      <TruncatedListNotice truncated={truncated} shown={events.length} noun="tasks and events" />
      <DeleteFailureNotice del={delTask} />
      <DeleteFailureNotice del={delEvent} />

      {renderGroup('task', tasks)}
      {renderGroup('event', plainEvents)}

      <DeleteOwnConfirm del={delTask} />
      <DeleteOwnConfirm del={delEvent} />

      {/* Create dialogs — each rendered only while the caller may create that kind here (`createRightOf`: the list's
          `canCreate`, the `/me` level where it decides, and for a task the list's `canCreateTask`) */}
      {canCreateTask && (
        <CreateEventDialog
          root={root}
          recordId={projectId}
          kind="task"
          open={taskDialog.open}
          onClose={taskDialog.hide}
          onCreated={handleEventCreated}
          onBusyChange={busyReporters.task}
        />
      )}
      {canCreate && (
        <CreateEventDialog
          root={root}
          recordId={projectId}
          kind="event"
          open={createDialog.open}
          onClose={createDialog.hide}
          onCreated={handleEventCreated}
          onBusyChange={busyReporters.event}
        />
      )}
    </div>
  );
};

export default EventsCalendar;
