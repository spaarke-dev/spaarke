/**
 * EventsCalendar — the Events list of a record, for the Secure Project Workspace SPA.
 *
 * Displays the events of a record in an upcoming/chronological view, as ONE list (R4 task 012, owner 2026-11-14: "we
 * only want Events (which allow for an event type of 'Task')"; external users work only with sprk_event). Every event has a
 * type (owner Q-2, task 015): the row shows its type name, status, priority, assignee and a short description. A caller who
 * may create adds an event via an inline dialog: Title, Event Type (required; exactly the BFF's `creatableEventTypes`), Due Date
 * (required when the chosen type says so), Priority, Assigned To (the record's assignable people) and Description.
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
 *   recordTitle — the title of that record ("Matter MAT-001"): names the Regarding value of an event opened from a row.
 *   accessLevel — The caller's `/me` level, passed only for a record `/me` carries a level for (a project): for a caller
 *                 without `writeAccessPerRecord` it must allow create AND agree with the list's `canCreate`. Without one,
 *                 the list's `canCreate` decides and a missing one is "cannot" (FR-06; `createRightOf`, in the record
 *                 template and on the project page alike, D-9).
 *   onCreateActionChange — record template only: the section's Add event is the entry point (tasks 009 / 012); without
 *                 it this list keeps its own Create Event button.
 *   caller      — The signed-in principal (task 054, FR-26): an event the caller CREATED shows Delete (asks first;
 *                 a refusal shows an error and the event stays). Independent of accessLevel; none without `caller`.
 *
 * ADD EVENT IS OFFERED only when the list's create right is true AND `creatableEventTypes` names at least one type (task
 * 015, 014 K3): the right and the rule live in `recordAccess.ts` (`addEventRightOf`). A create posts the new shape and the list
 * then RE-READS (rows stay on screen while it does): the POST's returned row carries no formatted names and no rights.
 *
 * OPENING A ROW (task 015). Inside a record template (a child-record opener exists), the event's title is a link-style button
 * that opens the event as a child record over the record, from THIS row's data (a seed): the child does not read
 * `GET /events/{id}`, because a multi-parent event can be in a readable list yet 404 there (014 review K5). Its Refresh re-reads
 * this list and finds the row by id; a row no longer listed is the not-found state. Outside a template, and inside a child
 * record (no opener), the title is plain text.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + tokens. No hard-coded colors.
 * ADR-022: React 18 component (bundled, not platform-provided).
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Badge,
  Divider,
  Field,
  Input,
  Select,
  Textarea,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
} from '@fluentui/react-components';
import { AddRegular, CalendarEmptyRegular, DeleteRegular } from '@fluentui/react-icons';
import { EmptyState } from '@spaarke/ui-components/components/EmptyState';
import { FormModal } from '@spaarke/ui-components/components/SprkModal';
import { parseDueDate } from '@spaarke/ui-components/utils/dateLocal';
import {
  getRootEvents,
  createRootEvent,
  deleteOwnEvent,
  ODataEvent,
  ODataEventRegarding,
  CreateEventPayload,
  CreatableEventType,
  EVENTS_ROOT_NOUN,
  type EventsRoot,
} from '../api/web-api-client';
import { isCreatedByCaller, type CallerIdentity } from '../api/delete-own';
import { useDeleteOwn, DeleteOwnConfirm, DeleteFailureNotice } from './DeleteOwn';
import { TruncatedListNotice } from './TruncatedListNotice';
import { useChildDialog, useOfferCreateAction, type CreateActionReporter } from './createAction';
import { childRecordAttributesOf, useChildRecordOpener } from './record/childRecord';
import { addEventRightOf, levelVerdictOf, listCreateRightOf } from './record/recordAccess';
import { RecordNotFoundError } from './record/recordReads';
import { eventsRootOfRegarding, toEventDetailRecord } from './events/eventDetail';
import {
  DEFAULT_EVENT_PRIORITY,
  EVENT_PRIORITY_OPTIONS,
  getEventPriorityLabel,
  getEventStatusColor,
  getEventStatusLabel,
} from './events/eventFields';
import { useAssignablePeople } from './events/useAssignablePeople';
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
  // The list: its heading (with its own create button outside the record template) and its rows.
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
  // The shared EmptyState leaves the icon's colour to its caller (R4 task 024, FR-17).
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
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
    minWidth: 0,
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
  // The title as a control that opens the event record: link-style, as a document name in a grid.
  eventTitleButton: {
    margin: 0,
    padding: 0,
    border: 'none',
    backgroundColor: 'transparent',
    font: 'inherit',
    fontWeight: tokens.fontWeightSemibold,
    textAlign: 'start',
    color: tokens.colorBrandForeground1,
    cursor: 'pointer',
    ':hover': { textDecoration: 'underline' },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  eventMeta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  // A short description: one line, cut with an ellipsis (the full text is on the event record).
  eventDescription: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
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

/** The words of the one create dialog and its button. */
const CREATE_TEXT = {
  title: 'Create Event',
  create: 'Create Event',
  required: 'Event title is required.',
  placeholder: 'Enter event title',
} as const;

/** The longest description the BFF accepts. */
const DESCRIPTION_MAX = 2000;

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
  /** Show Delete (the caller created this event). */
  canDelete?: boolean;
  onDelete?: (event: ODataEvent) => void;
  /** Task 015: inside a record template, the title opens the event record; absent = plain text. */
  onOpen?: (event: ODataEvent, trigger: HTMLElement) => void;
}

const EventRow: React.FC<EventRowProps> = ({ event, isLast, canDelete, onDelete, onOpen }) => {
  const styles = useStyles();
  const priorityLabel = getEventPriorityLabel(event.sprk_priority);
  const meta = [
    event.assignedToName ? `Assigned to ${event.assignedToName}` : null,
    event.createdon ? `Added ${parseDateParts(event.createdon)?.full ?? ''}` : null,
  ].filter((part): part is string => !!part);

  return (
    <>
      <div className={styles.eventRow} data-testid="event-row">
        {/* Date column */}
        <EventDateColumn duedate={event.sprk_duedate} />

        {/* Event content */}
        <div className={styles.eventContent}>
          <div className={styles.eventTitleRow} data-testid="event-title-row">
            {onOpen ? (
              <button
                type="button"
                className={styles.eventTitleButton}
                aria-label={`Open event ${event.sprk_name}`}
                {...childRecordAttributesOf('sprk_event', event.sprk_eventid)}
                onClick={e => onOpen(event, e.currentTarget)}
              >
                {event.sprk_name}
              </button>
            ) : (
              <Text className={styles.eventTitle}>{event.sprk_name}</Text>
            )}

            {/* Event type (every event has one, task 015); an untyped older event has no badge. */}
            {event.eventTypeName && (
              <Badge appearance="tint" color="important" size="small" data-testid="event-type-badge">
                {event.eventTypeName}
              </Badge>
            )}

            {/* Status badge */}
            <Badge appearance="tint" color={getEventStatusColor(event.sprk_status)} size="small">
              {getEventStatusLabel(event.sprk_status)}
            </Badge>

            {priorityLabel && (
              <Badge appearance="outline" size="small" data-testid="event-priority">
                {priorityLabel}
              </Badge>
            )}
          </div>

          {event.sprk_description && (
            <Text className={styles.eventDescription} data-testid="event-description" title={event.sprk_description}>
              {event.sprk_description}
            </Text>
          )}

          {/* Assignee and created-on meta */}
          {meta.length > 0 && (
            <Text className={styles.eventMeta} data-testid="event-meta">
              {meta.join(' · ')}
            </Text>
          )}
        </div>

        {/* Delete: only an event the caller created (the server re-checks every delete). */}
        {canDelete && onDelete && (
          <Button
            appearance="subtle"
            size="small"
            icon={<DeleteRegular />}
            onClick={() => onDelete(event)}
            aria-label={`Delete event ${event.sprk_name}`}
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
  /** The event types the caller may pick: exactly the list's `creatableEventTypes` (never a client list). */
  eventTypes: readonly CreatableEventType[];
  open: boolean;
  /** A new value per opening, so the people list is asked again each time. */
  opening: number;
  onClose: () => void;
  /** The create answered 2xx: the list re-reads (the returned row has no names or rights, so it is not inserted). */
  onCreated: () => void;
  /** Called with `true` while the create is in flight and `false` otherwise (and on unmount), so a host can hold. */
  onBusyChange?: (busy: boolean) => void;
}

const CreateEventDialog: React.FC<CreateEventDialogProps> = ({
  root,
  recordId,
  eventTypes,
  open,
  opening,
  onClose,
  onCreated,
  onBusyChange,
}) => {
  const styles = useStyles();
  const text = CREATE_TEXT;

  const [title, setTitle] = React.useState<string>('');
  const [typeId, setTypeId] = React.useState<string>('');
  const [dueDate, setDueDate] = React.useState<string>('');
  const [priority, setPriority] = React.useState<string>(String(DEFAULT_EVENT_PRIORITY));
  const [assignee, setAssignee] = React.useState<string>('');
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
      setTypeId('');
      setDueDate('');
      setPriority(String(DEFAULT_EVENT_PRIORITY));
      setAssignee('');
      setDescription('');
      setSubmitting(false);
      setSubmitError(null);
      setTitleError('');
    }
  }, [open]);

  // Assigned To: the record's assignable people, asked when the dialog opens (never before). A failed read hides the field
  // and says so; it never blocks the create (FR-37).
  const people = useAssignablePeople(root, recordId, open, opening);
  const showAssignee = people.status === 'ready' && people.people.length > 0;

  // A chosen type the list no longer offers (it was re-read) is no choice at all: never posts a type the BFF did not name.
  const chosenType = eventTypes.find(t => t.id === typeId);
  const dueRequired = chosenType?.requiresDueDate === true;
  const dueMissing = dueRequired && !dueDate;
  const ready = !!title.trim() && !!chosenType && !dueMissing;

  const validate = (): boolean => {
    if (!title.trim()) {
      setTitleError(text.required);
      return false;
    }
    setTitleError('');
    return !!chosenType && !dueMissing;
  };

  const handleSubmit = async () => {
    if (!validate() || !chosenType) return;

    setSubmitting(true);
    setSubmitError(null);

    try {
      const payload: CreateEventPayload = {
        sprk_name: title.trim(),
        eventTypeId: chosenType.id,
        sprk_priority: Number(priority),
        // Task 098: the date input's own "yyyy-MM-dd" — the column is Date Only and Dataverse refuses a timestamp
        // (the former toISOString() also turned the picked day into a UTC instant).
        ...(dueDate ? { sprk_duedate: dueDate } : {}),
        ...(description.trim() ? { sprk_description: description.trim() } : {}),
        ...(showAssignee && assignee ? { assignedToContactId: assignee } : {}),
        // sprk_status omitted: the BFF creates the event Open (statuscode 659490001) — task 097 review F9.
        // The parent comes from the route (task 006); `createRootEvent` keeps the project route's body as it was.
      };

      await createRootEvent(root, recordId, payload);
      onCreated();
      onClose();
    } catch (err) {
      if (err instanceof ApiError) {
        // The server's own message for a 400 (a stable `errorCode` per field); the generic line only when it wrote none.
        setSubmitError(problemMessage(err, `Failed to create event: ${err.message}`));
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

  // A FormModal (task 004, FR-03): Cancel in the left footer slot, Create Event on the right. Esc does nothing (modal
  // standard: an explicit dialog keeps a draft).
  return (
    <FormModal
      open={open}
      onClose={handleClose}
      onSubmit={() => void handleSubmit()}
      title={text.title}
      submitLabel={submitting ? 'Creating...' : text.create}
      submitDisabled={!ready}
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

        {/* Event Type (required): exactly the BFF's `creatableEventTypes`, no client list, no id literal. */}
        <Field label="Event Type" required>
          <Select value={typeId} onChange={(_ev, data) => setTypeId(data.value)} disabled={submitting}>
            <option value="">Select a type</option>
            {eventTypes.map(t => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </Select>
        </Field>

        {/* Due date: required when the chosen type says so. */}
        <Field
          label="Due Date"
          required={dueRequired}
          validationMessage={
            dueMissing ? `A due date is required for ${chosenType?.name ?? 'this'} events.` : undefined
          }
          validationState={dueMissing ? 'error' : 'none'}
        >
          <Input type="date" value={dueDate} onChange={(_ev, data) => setDueDate(data.value)} disabled={submitting} />
        </Field>

        <Field label="Priority">
          <Select value={priority} onChange={(_ev, data) => setPriority(data.value)} disabled={submitting}>
            {EVENT_PRIORITY_OPTIONS.map(o => (
              <option key={o.value} value={String(o.value)}>
                {o.label}
              </option>
            ))}
          </Select>
        </Field>

        {/* Assigned To: only the people the BFF lists for this record, "Me" first; nobody by default. */}
        {showAssignee && people.status === 'ready' && (
          <Field label="Assigned To">
            <Select value={assignee} onChange={(_ev, data) => setAssignee(data.value)} disabled={submitting}>
              <option value="">Nobody</option>
              {people.people.map(p => (
                <option key={p.contactId} value={p.contactId}>
                  {p.isMe ? `${p.name} (Me)` : p.name}
                </option>
              ))}
            </Select>
          </Field>
        )}
        {people.status === 'failed' && (
          <Text size={200} data-testid="event-assignee-unavailable">
            The people you can assign this to could not be loaded, so Assigned To is not shown. You can still create the
            event.
          </Text>
        )}

        <Field label="Description">
          <Textarea
            value={description}
            onChange={(_ev, data) => setDescription(data.value)}
            maxLength={DESCRIPTION_MAX}
            resize="vertical"
            disabled={submitting}
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
   * The title of that record ("Matter MAT-001", task 015): what an event opened from a row names as its Regarding record.
   * Absent = the record is named by its kind ("matter").
   */
  recordTitle?: string;
  /**
   * The caller's `/me` level for this record. A level DECIDES a create only for a project caller without
   * `writeAccessPerRecord`: pass it for that case and omit it otherwise. A matter or work assignment never passes one
   * (its `/me` `records` level is display only, task 011): Add event follows the list's `canCreate` and its
   * `creatableEventTypes` (task 006 / 015).
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
   * Called with `true` while a create is in flight and `false` otherwise (and on unmount), so the host can hold. The list
   * has one create dialog (task 012), so one flag.
   */
  onBusyChange?: (busy: boolean) => void;
}

const REGARDING_KIND_OF_ROOT: Readonly<Record<EventsRoot, ODataEventRegarding['kind']>> = {
  projects: 'project',
  matters: 'matter',
  workassignments: 'workassignment',
};

/**
 * EventsCalendar — the Events list of a record for external participants.
 *
 * Fetches the record's events (GET /api/v1/external/{root}/{id}/events), ordered by due date ascending (soonest
 * first), and shows them as one list; each row shows its type name.
 *
 * - View Only users: read-only list
 * - Collaborate / Full Access users: "Create Event" button and dialog (Event Type from the list's `creatableEventTypes`);
 *   inside the record template the button is the section's "Add event" header action.
 *
 * Loading and empty states follow the SectionCard + PageContainer pattern
 * established by other SPA components.
 */
export const EventsCalendar: React.FC<EventsCalendarProps> = ({
  projectId,
  root = 'projects',
  recordTitle,
  accessLevel,
  caller,
  writeAccessPerRecord = false,
  onCreateActionChange,
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
  // A counter per opening of the create dialog (its people list is asked again each time).
  const [createSeq, setCreateSeq] = React.useState<number>(0);
  const showCreate = React.useCallback(() => {
    setCreateSeq(n => n + 1);
    createDialog.show();
  }, [createDialog.show]); // eslint-disable-line react-hooks/exhaustive-deps

  // Task 105: the BFF said the list was cut short — shown, never presented as the whole list.
  const [truncated, setTruncated] = React.useState<boolean>(false);
  // A 401/403 on the list is the shared sign-in / denied state; "Try again" re-runs the read (task 057).
  const [accessFailure, setAccessFailure] = React.useState<BffAccessFailure | null>(null);
  const [attempt, setAttempt] = React.useState<number>(0);
  // A re-read after a create keeps the rows on screen (no spinner): set just before `reloadSeq` changes.
  const [reloadSeq, setReloadSeq] = React.useState<number>(0);
  const silentNext = React.useRef<boolean>(false);
  // A silent re-read that fails keeps the rows on screen (the create DID succeed) and says so above them, with Try again.
  const [refreshError, setRefreshError] = React.useState<string | null>(null);
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
  // The event types the caller may pick (`creatableEventTypes`); null = the BFF said nothing (an older BFF: no Add event).
  const [eventTypes, setEventTypes] = React.useState<CreatableEventType[] | null>(null);
  const inRecordTemplate = onCreateActionChange !== undefined;
  const loaded = !loading && !accessFailure && !loadError;
  const levelAllows = levelVerdictOf(accessLevel, writeAccessPerRecord);
  const { right: eventRight } = listCreateRightOf({ loaded, listCanCreate, levelAllows });
  // "Add event" exists when the caller may create AND at least one type may be picked (task 015, 014 K3).
  const addEventRight = addEventRightOf(eventRight, eventTypes);
  const canAddEvent = addEventRight === true;
  // In the record template the section's Add event replaces this list's button (task 004 / 009 / 012): offered exactly
  // when the right is known to be `true` (the main view is showing), and the dialog stays here. The right goes with the
  // offer.
  const ownCreateButton = !inRecordTemplate;
  useOfferCreateAction(onCreateActionChange, canAddEvent && loaded, showCreate, addEventRight);

  // Delete-own (task 054): the event leaves the list on the server's 204; any refusal keeps it and is shown.
  const delEvent = useDeleteOwn<ODataEvent>({
    noun: 'event',
    remove: event => deleteOwnEvent(event.sprk_eventid),
    onRemoved: (event: ODataEvent): void => setEvents(prev => prev.filter(e => e.sprk_eventid !== event.sprk_eventid)),
  });

  // -------------------------------------------------------------------------
  // Data fetching
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    if (!projectId) return;

    let cancelled = false;
    const silent = silentNext.current;
    silentNext.current = false;

    const fetchEvents = async () => {
      if (!silent) setLoading(true);
      setLoadError(null);
      setRefreshError(null);
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
          setEventTypes(data.creatableEventTypes ?? null);
        }
      } catch (err) {
        if (!cancelled) {
          const failure = accessFailureOf(err);
          if (failure) {
            setAccessFailure(failure);
          } else {
            // The server's ProblemDetails `detail` when it wrote one; never the raw response body.
            const message = problemMessage(err, 'Events could not be loaded just now.');
            if (silent) setRefreshError(message);
            else setLoadError(message);
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
  }, [projectId, root, attempt, reloadSeq]);

  // -------------------------------------------------------------------------
  // Handler: event created
  // -------------------------------------------------------------------------

  // The POST's returned row has no formatted names (type, assignee, creator) and no rights, so the list is read again rather
  // than given the returned row; the rows already shown stay on screen while it does.
  const reloadSilently = React.useCallback(() => {
    silentNext.current = true;
    setReloadSeq(n => n + 1);
  }, []);

  // -------------------------------------------------------------------------
  // Opening a row (task 015)
  // -------------------------------------------------------------------------

  const openChild = useChildRecordOpener();
  const regarding = React.useMemo<ODataEventRegarding>(
    () => ({ kind: REGARDING_KIND_OF_ROOT[root], id: projectId, name: recordTitle ?? null }),
    [root, projectId, recordTitle]
  );
  // How an opened event is read again (Refresh, after a save): this list, finding the row by id. A row no longer listed is
  // "not found". The child never reads `GET /events/{id}` (014 review K5).
  const readFromList = React.useCallback(
    async (id: string) => {
      const data = await getRootEvents(root, projectId, { $orderby: 'sprk_duedate asc' });
      const row = data.items.find(e => e.sprk_eventid.toLowerCase() === id.toLowerCase());
      if (!row) throw new RecordNotFoundError('event not in the list');
      return toEventDetailRecord(row, regarding);
    },
    [root, projectId, regarding]
  );
  const handleOpen = React.useMemo(
    () =>
      openChild && eventsRootOfRegarding(regarding)
        ? (event: ODataEvent, trigger: HTMLElement): void =>
            openChild('sprk_event', event.sprk_eventid, trigger, 'tasksEvents', {
              seed: toEventDetailRecord(event, regarding),
              read: readFromList,
            })
        : undefined,
    [openChild, regarding, readFromList]
  );

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

  // One read, one list (task 012). Each row shows the type the BFF named for it. The order is the list's own (due date ascending).
  //
  // Task 105: a cut-short list is not "no events" - the notice says it could not be read, so an empty list that is truncated
  // claims nothing: no empty-state text. Its heading and its own create button stay (the caller may still add one;
  // ProjectPage's Calendar tab has no section action). With neither to show, nothing renders. A truncated list never hides
  // the create action (task 011 F1): in the record template that action is the section's, offered above regardless of rows.
  const claimsNothing = events.length === 0 && truncated;
  const showList = !(claimsNothing && !(canAddEvent && ownCreateButton));

  return (
    <div className={styles.root}>
      <TruncatedListNotice truncated={truncated} shown={events.length} noun="events" />
      <DeleteFailureNotice del={delEvent} />
      {refreshError && (
        <MessageBar intent="error" className={styles.errorBar} data-testid="events-refresh-error">
          <MessageBarBody>{refreshError}</MessageBarBody>
          <MessageBarActions>
            <Button appearance="secondary" size="small" onClick={reloadSilently}>
              Try again
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {showList && (
        <div className={styles.group} data-testid="events-list">
          {/* The record template's section card already titles the list "Events"; the page's own list brings its heading and button. */}
          {ownCreateButton && (
            <div className={styles.toolbar}>
              <Text size={400} weight="semibold" className={styles.toolbarTitle}>
                Events
              </Text>

              {canAddEvent && (
                <Button appearance="primary" icon={<AddRegular />} onClick={showCreate} size="small">
                  {CREATE_TEXT.create}
                </Button>
              )}
            </div>
          )}

          {claimsNothing ? null : events.length === 0 ? (
            <EmptyState
              size="compact"
              icon={<CalendarEmptyRegular className={styles.emptyIcon} />}
              heading="No events yet"
              description={
                canAddEvent
                  ? ownCreateButton
                    ? `No events have been added to this ${noun}. Use the ${CREATE_TEXT.create} button to add the first event.`
                    : `No events have been added to this ${noun}. Use Add event above to add the first event.`
                  : `No events have been added to this ${noun} yet.`
              }
            />
          ) : (
            <div className={styles.eventList}>
              {events.map((event, index) => (
                <EventRow
                  key={event.sprk_eventid}
                  event={event}
                  isLast={index === events.length - 1}
                  canDelete={isCreatedByCaller(event, caller)}
                  onDelete={e => delEvent.ask(e, e.sprk_name)}
                  onOpen={handleOpen}
                />
              ))}
            </div>
          )}
        </div>
      )}

      <DeleteOwnConfirm del={delEvent} />

      {/* The one create dialog - rendered only while the caller may create an event here (`addEventRightOf`: the list's
          `canCreate`, the `/me` level where it decides, and at least one creatable type). */}
      {canAddEvent && eventTypes && (
        <CreateEventDialog
          root={root}
          recordId={projectId}
          eventTypes={eventTypes}
          open={createDialog.open}
          opening={createSeq}
          onClose={createDialog.hide}
          onCreated={reloadSilently}
          onBusyChange={onBusyChange}
        />
      )}
    </div>
  );
};

export default EventsCalendar;
