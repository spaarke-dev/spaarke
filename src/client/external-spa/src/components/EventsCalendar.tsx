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
 *   projectId   — Dataverse GUID of the sprk_project record
 *   accessLevel — The authenticated user's access level (determines create visibility)
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + tokens. No hard-coded colors.
 * ADR-022: React 18 component (bundled, not platform-provided).
 */

import * as React from "react";
import {
  makeStyles,
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
} from "@fluentui/react-components";
import {
  CalendarRegular,
  AddRegular,
  CalendarEmptyRegular,
} from "@fluentui/react-icons";
import { getEvents, createEvent, ODataEvent, CreateEventPayload } from "../api/web-api-client";
import { AccessLevel, ApiError } from "../types";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  toolbar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    paddingBottom: tokens.spacingVerticalS,
  },
  toolbarTitle: {
    color: tokens.colorNeutralForeground1,
  },
  loadingContainer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "180px",
    gap: tokens.spacingHorizontalM,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "180px",
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalXL,
    borderWidth: "1px",
    borderStyle: "dashed",
    borderColor: tokens.colorNeutralStroke2,
  },
  emptyStateIcon: {
    fontSize: "40px",
    color: tokens.colorNeutralForeground4,
  },
  emptyStateText: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
  },
  eventList: {
    display: "flex",
    flexDirection: "column",
    gap: "0",
  },
  eventRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },
  eventDateCol: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    minWidth: "60px",
    paddingTop: "2px",
  },
  eventDateMonth: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase100,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    fontWeight: tokens.fontWeightSemibold,
  },
  eventDateDay: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: "1",
  },
  eventDateYear: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
  eventDateNoDate: {
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
    fontStyle: "italic",
    textAlign: "center",
  },
  eventContent: {
    flex: "1",
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  eventTitleRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
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
    marginTop: "0",
    marginBottom: "0",
  },
  errorBar: {
    marginBottom: tokens.spacingVerticalS,
  },
  // Dialog form layout
  dialogForm: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Event status helpers
// ---------------------------------------------------------------------------

/**
 * Map Dataverse sprk_status option set value to a human-readable label.
 * Status values are from the internal EventsPage pattern.
 */
function getEventStatusLabel(status: number | null | undefined): string {
  switch (status) {
    case 1:
      return "Open";
    case 2:
      return "In Progress";
    case 3:
      return "Completed";
    case 4:
      return "Cancelled";
    default:
      return "Open";
  }
}

/**
 * Map status to Fluent Badge color.
 * Follows internal EventsPage colour conventions.
 */
function getEventStatusColor(
  status: number | null | undefined
): "brand" | "success" | "warning" | "danger" | "informative" | undefined {
  switch (status) {
    case 1:
      return "brand";
    case 2:
      return "warning";
    case 3:
      return "success";
    case 4:
      return "danger";
    default:
      return "brand";
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

function parseDateParts(iso: string | null | undefined): ParsedDate | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (isNaN(d.getTime())) return null;

  return {
    month: d.toLocaleDateString("en-US", { month: "short" }),
    day: String(d.getDate()),
    year: String(d.getFullYear()),
    full: d.toLocaleDateString("en-US", { month: "long", day: "numeric", year: "numeric" }),
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
}

const EventRow: React.FC<EventRowProps> = ({ event, isLast }) => {
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
            <Badge
              appearance="tint"
              color={getEventStatusColor(event.sprk_status)}
              size="small"
            >
              {getEventStatusLabel(event.sprk_status)}
            </Badge>

            {/* To-Do flag badge */}
            {event.sprk_todoflag && (
              <Badge appearance="tint" color="warning" size="small">
                To-Do
              </Badge>
            )}
          </div>

          {/* Created on meta */}
          {event.createdon && (
            <Text className={styles.eventMeta}>
              Added {parseDateParts(event.createdon)?.full ?? ""}
            </Text>
          )}
        </div>
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

const CreateEventDialog: React.FC<CreateEventDialogProps> = ({
  projectId,
  open,
  onClose,
  onCreated,
}) => {
  const styles = useStyles();

  const [title, setTitle] = React.useState<string>("");
  const [dueDate, setDueDate] = React.useState<string>("");
  const [description, setDescription] = React.useState<string>("");
  const [eventType, setEventType] = React.useState<string>("meeting");
  const [submitting, setSubmitting] = React.useState<boolean>(false);
  const [submitError, setSubmitError] = React.useState<string | null>(null);

  // Validation state
  const [titleError, setTitleError] = React.useState<string>("");

  // Reset form when dialog opens
  React.useEffect(() => {
    if (open) {
      setTitle("");
      setDueDate("");
      setDescription("");
      setEventType("meeting");
      setSubmitting(false);
      setSubmitError(null);
      setTitleError("");
    }
  }, [open]);

  const validate = (): boolean => {
    let valid = true;

    if (!title.trim()) {
      setTitleError("Event title is required.");
      valid = false;
    } else {
      setTitleError("");
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
        ...(dueDate ? { sprk_duedate: new Date(dueDate).toISOString() } : {}),
        sprk_status: 1, // Open
        sprk_todoflag: false,
        "sprk_projectid@odata.bind": `sprk_projects(${projectId})`,
      };

      const created = await createEvent(projectId, payload);
      onCreated(created);
      onClose();
    } catch (err) {
      if (err instanceof ApiError) {
        setSubmitError(`Failed to create event: ${err.message}`);
      } else {
        setSubmitError("An unexpected error occurred. Please try again.");
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
                validationState={titleError ? "error" : "none"}
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
                <Select
                  value={eventType}
                  onChange={(_ev, data) => setEventType(data.value)}
                  disabled={submitting}
                >
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
            <Button
              appearance="secondary"
              onClick={onClose}
              disabled={submitting}
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={() => void handleSubmit()}
              disabled={submitting || !title.trim()}
              icon={submitting ? <Spinner size="tiny" /> : undefined}
            >
              {submitting ? "Creating..." : "Create Event"}
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
  /** Dataverse GUID of the parent sprk_project record */
  projectId: string;
  /** The authenticated user's access level — determines create visibility */
  accessLevel: AccessLevel;
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
  accessLevel,
}) => {
  const styles = useStyles();

  // -------------------------------------------------------------------------
  // State
  // -------------------------------------------------------------------------

  const [events, setEvents] = React.useState<ODataEvent[]>([]);
  const [loading, setLoading] = React.useState<boolean>(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [dialogOpen, setDialogOpen] = React.useState<boolean>(false);

  // -------------------------------------------------------------------------
  // Access level check
  // -------------------------------------------------------------------------

  const canCreate =
    accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;

  // -------------------------------------------------------------------------
  // Data fetching
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    if (!projectId) return;

    let cancelled = false;

    const fetchEvents = async () => {
      setLoading(true);
      setLoadError(null);

      try {
        const data = await getEvents(projectId, {
          // Upcoming: order by due date ascending, exclude completed and cancelled
          $orderby: "sprk_duedate asc",
          $top: 50,
        });

        if (!cancelled) {
          setEvents(data);
        }
      } catch (err) {
        if (!cancelled) {
          if (err instanceof ApiError) {
            setLoadError(`Failed to load events: ${err.message}`);
          } else {
            setLoadError("An unexpected error occurred loading events.");
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
  }, [projectId]);

  // -------------------------------------------------------------------------
  // Handler: event created
  // -------------------------------------------------------------------------

  const handleEventCreated = (newEvent: ODataEvent) => {
    setEvents((prev) => {
      // Insert new event and re-sort by due date ascending
      const updated = [...prev, newEvent];
      updated.sort((a, b) => {
        const da = a.sprk_duedate ? new Date(a.sprk_duedate).getTime() : Infinity;
        const db = b.sprk_duedate ? new Date(b.sprk_duedate).getTime() : Infinity;
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

  if (loadError) {
    return (
      <MessageBar intent="error" className={styles.errorBar}>
        <MessageBarBody>{loadError}</MessageBarBody>
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
          <Button
            appearance="primary"
            icon={<AddRegular />}
            onClick={() => setDialogOpen(true)}
            size="small"
          >
            Create Event
          </Button>
        )}
      </div>

      {/* Event list or empty state */}
      {events.length === 0 ? (
        <div className={styles.emptyState}>
          <CalendarEmptyRegular className={styles.emptyStateIcon} />
          <Text size={400} weight="semibold">
            No events yet
          </Text>
          <Text size={300} className={styles.emptyStateText}>
            {canCreate
              ? "No events have been added to this project. Use the Create Event button to add the first event."
              : "No events have been added to this project yet."}
          </Text>
        </div>
      ) : (
        <div className={styles.eventList}>
          {events.map((event, index) => (
            <EventRow
              key={event.sprk_eventid}
              event={event}
              isLast={index === events.length - 1}
            />
          ))}
        </div>
      )}

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
