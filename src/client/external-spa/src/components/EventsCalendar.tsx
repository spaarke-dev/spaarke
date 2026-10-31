/**
 * EventsCalendar — upcoming events list component for the Secure Project Workspace SPA.
 *
 * Displays events for a secure project in an upcoming/chronological view.
 * External users with Collaborate or Full Access can create new events via an
 * inline dialog. View Only users see a read-only list.
 *
 * Design pattern follows the internal EventsPage (src/solutions/EventsPage/) —
 * list format with date, title, status badge, and description.
 *
 * Props:
 *   projectId   — Dataverse GUID of the record whose events to list: a sprk_project unless `root` says otherwise
 *   root        — which kind of record that is (task 057): 'projects' (default), 'matters' or 'workassignments'. Reads
 *                 GET /api/v1/external/{root}/{id}/events; Create is a project-only action and is never offered otherwise.
 *   accessLevel — The authenticated user's access level (determines create visibility)
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
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Field,
  Input,
  Textarea,
  Select,
  MessageBar,
  MessageBarBody,
  MessageBarActions,
} from '@fluentui/react-components';
import { AddRegular, CalendarEmptyRegular, DeleteRegular } from '@fluentui/react-icons';
import { parseDueDate } from '@spaarke/ui-components/utils/dateLocal';
import {
  getRootEvents,
  createEvent,
  deleteOwnEvent,
  ODataEvent,
  CreateEventPayload,
  EVENTS_TODOS_ROOT_NOUN,
  type EventsTodosRoot,
} from '../api/web-api-client';
import { isCreatedByCaller, type CallerIdentity } from '../api/delete-own';
import { useDeleteOwn, DeleteOwnConfirm, DeleteFailureNotice } from './DeleteOwn';
import { TruncatedListNotice } from './TruncatedListNotice';
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
    minHeight: '180px',
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
  /** Show Delete (the caller created this event). */
  canDelete?: boolean;
  onDelete?: (event: ODataEvent) => void;
}

const EventRow: React.FC<EventRowProps> = ({ event, isLast, canDelete, onDelete }) => {
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
            {/*
             * To-Do badge removed (R3 task 008): events no longer carry a
             * to-do boolean toggle. To-dos are first-class `sprk_todo` records
             * surfaced by the SmartTodo component / `getProjectTodos` route.
             */}
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
  projectId: string;
  open: boolean;
  onClose: () => void;
  onCreated: (event: ODataEvent) => void;
}

const CreateEventDialog: React.FC<CreateEventDialogProps> = ({ projectId, open, onClose, onCreated }) => {
  const styles = useStyles();

  const [title, setTitle] = React.useState<string>('');
  const [dueDate, setDueDate] = React.useState<string>('');
  const [description, setDescription] = React.useState<string>('');
  const [eventType, setEventType] = React.useState<string>('meeting');
  const [submitting, setSubmitting] = React.useState<boolean>(false);
  const [submitError, setSubmitError] = React.useState<string | null>(null);

  // Validation state
  const [titleError, setTitleError] = React.useState<string>('');

  // Reset form when dialog opens
  React.useEffect(() => {
    if (open) {
      setTitle('');
      setDueDate('');
      setDescription('');
      setEventType('meeting');
      setSubmitting(false);
      setSubmitError(null);
      setTitleError('');
    }
  }, [open]);

  const validate = (): boolean => {
    let valid = true;

    if (!title.trim()) {
      setTitleError('Event title is required.');
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
        // sprk_status omitted: the BFF creates the event Open (statuscode 659490001) — task 097 review F9.
        // Note: the event-as-todo toggle was removed in R3 task 007 — events are not to-dos.
        'sprk_RegardingProject@odata.bind': `/sprk_projects(${projectId})`, // R5 002: PascalCase nav prop (metadata-verified)
      };

      const created = await createEvent(projectId, payload);
      onCreated(created);
      onClose();
    } catch (err) {
      if (err instanceof ApiError) {
        setSubmitError(`Failed to create event: ${err.message}`);
      } else {
        setSubmitError('An unexpected error occurred. Please try again.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={(_ev, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogTitle>Create Event</DialogTitle>

        <DialogBody>
          <DialogContent>
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
                  placeholder="Enter event title"
                  disabled={submitting}
                  maxLength={200}
                />
              </Field>

              {/* Event type */}
              <Field label="Type">
                <Select value={eventType} onChange={(_ev, data) => setEventType(data.value)} disabled={submitting}>
                  <option value="meeting">Meeting</option>
                  <option value="deadline">Deadline</option>
                  <option value="milestone">Milestone</option>
                  <option value="task">Task</option>
                  <option value="reminder">Reminder</option>
                  <option value="filing">Filing</option>
                  <option value="other">Other</option>
                </Select>
              </Field>

              {/* Due date */}
              <Field label="Due Date">
                <Input
                  type="date"
                  value={dueDate}
                  onChange={(_ev, data) => setDueDate(data.value)}
                  disabled={submitting}
                />
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
          </DialogContent>

          <DialogActions>
            <Button appearance="secondary" onClick={onClose} disabled={submitting}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={() => void handleSubmit()}
              disabled={submitting || !title.trim()}
              icon={submitting ? <Spinner size="tiny" /> : undefined}
            >
              {submitting ? 'Creating...' : 'Create Event'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// ---------------------------------------------------------------------------
// Main component: EventsCalendar
// ---------------------------------------------------------------------------

export interface EventsCalendarProps {
  /** Dataverse GUID of the record whose events to list (a sprk_project unless `root` says otherwise). */
  projectId: string;
  /** Which kind of record `projectId` is (task 057). Defaults to a project. */
  root?: EventsTodosRoot;
  /** The authenticated user's access level — determines create visibility */
  accessLevel: AccessLevel;
  /**
   * The caller's write access is decided per record (`/me` `writeAccessPerRecord`, task 036 fix round 1): "create" then
   * follows the BFF's `canCreate` on the project's event list instead of `accessLevel`.
   */
  writeAccessPerRecord?: boolean;
  /** The signed-in principal. With it, events the caller created show Delete (task 054); without it, none do. */
  caller?: CallerIdentity | null;
}

/**
 * EventsCalendar — upcoming events list for external project participants.
 *
 * Fetches events from the Power Pages Web API (sprk_events entity set),
 * filtered by project and ordered by due date ascending (soonest first).
 *
 * - View Only users: read-only list
 * - Collaborate / Full Access users: "Create Event" button and dialog
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
}) => {
  const styles = useStyles();

  // -------------------------------------------------------------------------
  // State
  // -------------------------------------------------------------------------

  const [events, setEvents] = React.useState<ODataEvent[]>([]);
  const [loading, setLoading] = React.useState<boolean>(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = React.useState<boolean>(false);
  // Task 105: the BFF said the list was cut short — shown, never presented as the whole list.
  const [truncated, setTruncated] = React.useState<boolean>(false);
  // A 401/403 on the list is the shared sign-in / denied state; "Try again" re-runs the read (task 057).
  const [accessFailure, setAccessFailure] = React.useState<BffAccessFailure | null>(null);
  const [attempt, setAttempt] = React.useState<number>(0);
  const noun = EVENTS_TODOS_ROOT_NOUN[root];

  // -------------------------------------------------------------------------
  // Access level check
  // -------------------------------------------------------------------------

  // Creating an event is a project-only route (the matter and work assignment routes are read-only). Task 036 fix round
  // 1: with per-record write access the project's list `canCreate` decides (null = not said, read as "cannot").
  const [listCanCreate, setListCanCreate] = React.useState<boolean | null>(null);
  const canCreate =
    root === 'projects' &&
    (writeAccessPerRecord
      ? listCanCreate === true
      : accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess);

  // Delete-own (task 054): the event leaves the list on the server's 204; any refusal keeps it and is shown.
  const del = useDeleteOwn<ODataEvent>({
    noun: 'event',
    remove: event => deleteOwnEvent(event.sprk_eventid),
    onRemoved: event => setEvents(prev => prev.filter(e => e.sprk_eventid !== event.sprk_eventid)),
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

  return (
    <div className={styles.root}>
      {/* Toolbar: title + create button */}
      <div className={styles.toolbar}>
        <Text size={400} weight="semibold" className={styles.toolbarTitle}>
          Upcoming Events
        </Text>

        {canCreate && (
          <Button appearance="primary" icon={<AddRegular />} onClick={() => setDialogOpen(true)} size="small">
            Create Event
          </Button>
        )}
      </div>

      <TruncatedListNotice truncated={truncated} shown={events.length} noun="events" />
      <DeleteFailureNotice del={del} />

      {/* Event list or empty state */}
      {/* Task 105: an incomplete empty list is not "no events" — the notice says it could not be read. */}
      {events.length === 0 ? (
        truncated ? null : (
          <div className={styles.emptyState}>
            <CalendarEmptyRegular className={styles.emptyStateIcon} />
            <Text size={400} weight="semibold">
              No events yet
            </Text>
            <Text size={300} className={styles.emptyStateText}>
              {canCreate
                ? `No events have been added to this ${noun}. Use the Create Event button to add the first event.`
                : `No events have been added to this ${noun} yet.`}
            </Text>
          </div>
        )
      ) : (
        <div className={styles.eventList}>
          {events.map((event, index) => (
            <EventRow
              key={event.sprk_eventid}
              event={event}
              isLast={index === events.length - 1}
              canDelete={isCreatedByCaller(event, caller)}
              onDelete={e => del.ask(e, e.sprk_name)}
            />
          ))}
        </div>
      )}

      <DeleteOwnConfirm del={del} />

      {/* Create event dialog — rendered only for Collaborate/Full Access */}
      {canCreate && (
        <CreateEventDialog
          projectId={projectId}
          open={dialogOpen}
          onClose={() => setDialogOpen(false)}
          onCreated={handleEventCreated}
        />
      )}
    </div>
  );
};

export default EventsCalendar;
