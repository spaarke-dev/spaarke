/**
 * SmartTodo — Task management component for the Secure Project Workspace SPA.
 *
 * Displays tasks (sprk_event records with sprk_todoflag=true) associated with
 * a secure project. External users can view tasks, and create or update them
 * if their access level is Collaborate or Full Access.
 *
 * Design reference: src/solutions/TodoDetailSidePane/src/components/TodoDetail.tsx
 *
 * Access level enforcement (ADR per project CLAUDE.md):
 *   - ViewOnly    (100000000): Read-only. Can view tasks, cannot create or modify.
 *   - Collaborate (100000001): Can create tasks and toggle status.
 *   - FullAccess  (100000002): Same as Collaborate plus invite rights (not relevant here).
 *
 * All colours via Fluent UI v9 design tokens (ADR-021). No hard-coded colors.
 * React 18 bundled (ADR-022, ADR-026). Fluent v9 makeStyles (ADR-021).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Input,
  Textarea,
  Spinner,
  Badge,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  MessageBar,
  MessageBarBody,
  Tooltip,
  Divider,
} from "@fluentui/react-components";
import {
  AddRegular,
  CheckmarkCircleRegular,
  CheckmarkCircleFilled,
  TaskListSquareLtrRegular,
  CalendarLtrRegular,
  WarningRegular,
} from "@fluentui/react-icons";

import {
  getEvents,
  createEvent,
  updateEvent,
  type ODataEvent,
} from "../api/web-api-client";
import { AccessLevel } from "../types";
import { SectionCard } from "./SectionCard";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  headerRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalS,
  },
  headerLeft: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  taskList: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  taskItem: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    borderWidth: "1px",
    borderStyle: "solid",
    borderColor: tokens.colorNeutralStroke2,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  taskItemCompleted: {
    opacity: "0.65",
  },
  taskCheckArea: {
    flexShrink: 0,
    paddingTop: "2px",
    display: "flex",
    alignItems: "flex-start",
  },
  taskContent: {
    flex: "1 1 0",
    display: "flex",
    flexDirection: "column",
    gap: "2px",
    minWidth: 0,
  },
  taskTitle: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    wordBreak: "break-word",
  },
  taskTitleCompleted: {
    textDecorationLine: "line-through",
    color: tokens.colorNeutralForeground4,
  },
  taskMeta: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  taskMetaText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    display: "flex",
    alignItems: "center",
    gap: "3px",
  },
  taskMetaOverdue: {
    color: tokens.colorPaletteRedForeground1,
  },
  statusToggleBtn: {
    minWidth: "0",
    padding: "0",
    border: "none",
    background: "transparent",
    cursor: "pointer",
    color: tokens.colorNeutralForeground3,
    fontSize: "20px",
    lineHeight: "1",
    display: "flex",
    alignItems: "center",
    ":hover": {
      color: tokens.colorBrandForeground1,
    },
  },
  statusToggleBtnCompleted: {
    color: tokens.colorPaletteGreenForeground2,
    ":hover": {
      color: tokens.colorPaletteGreenForeground3,
    },
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground4,
  },
  emptyStateIcon: {
    fontSize: "40px",
    opacity: "0.5",
  },
  loadingState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  errorBanner: {
    marginBottom: tokens.spacingVerticalS,
  },
  // Dialog form styles
  dialogForm: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
  },
  fieldGroup: {
    display: "flex",
    flexDirection: "column",
    gap: "4px",
  },
  fieldLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
  fieldLabelRequired: {
    color: tokens.colorPaletteRedForeground1,
    marginLeft: "2px",
  },
  priorityRow: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  priorityBadge: {
    cursor: "pointer",
    userSelect: "none",
  },
  countBadge: {
    flexShrink: 0,
  },
  divider: {
    marginTop: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Priority mapping (sprk_status option set for tasks)
// Reusing event status field — 0=Not Started, 1=In Progress, 2=Completed
// Priority is a separate concern — stored as sprk_priorityscore not available
// in the external Web API. We use a local priority option for the create dialog.
// ---------------------------------------------------------------------------

/** Task status values on sprk_event.sprk_status */
const TASK_STATUS = {
  NOT_STARTED: 0,
  IN_PROGRESS: 1,
  COMPLETED: 2,
} as const;

/** Priority options for the create task dialog (stored in sprk_status initially,
 *  then sprk_priorityscore is not surfaced; we map to display only). */
interface PriorityOption {
  label: string;
  value: number;
  color: "informative" | "warning" | "danger" | "subtle";
}

const PRIORITY_OPTIONS: PriorityOption[] = [
  { label: "Low", value: 25, color: "subtle" },
  { label: "Medium", value: 50, color: "informative" },
  { label: "High", value: 75, color: "warning" },
  { label: "Critical", value: 100, color: "danger" },
];

// ---------------------------------------------------------------------------
// Helper utilities
// ---------------------------------------------------------------------------

/** Format an ISO date string as a short human-readable date. */
function formatDueDate(isoDate: string | null | undefined): string {
  if (!isoDate) return "";
  const d = new Date(isoDate);
  if (isNaN(d.getTime())) return "";
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

/** Check if a task due date is overdue. */
function isOverdue(isoDate: string | null | undefined, isCompleted: boolean): boolean {
  if (!isoDate || isCompleted) return false;
  const d = new Date(isoDate);
  if (isNaN(d.getTime())) return false;
  return d < new Date();
}

/** Convert YYYY-MM-DD input value to ISO string for Dataverse. */
function dateInputToIso(value: string): string | undefined {
  if (!value) return undefined;
  // Input type=date provides YYYY-MM-DD; convert to noon UTC to avoid timezone shifts
  const [year, month, day] = value.split("-").map(Number);
  const d = new Date(Date.UTC(year, month - 1, day, 12, 0, 0));
  return d.toISOString();
}

/** Convert ISO date string to YYYY-MM-DD for input[type="date"]. */
function toDateInputValue(isoDate: string | null | undefined): string {
  if (!isoDate) return "";
  const d = new Date(isoDate);
  if (isNaN(d.getTime())) return "";
  return d.toISOString().split("T")[0];
}

// ---------------------------------------------------------------------------
// CanEdit helper
// ---------------------------------------------------------------------------

/** Returns true if the access level allows creating or editing tasks. */
function canEdit(accessLevel: AccessLevel): boolean {
  return (
    accessLevel === AccessLevel.Collaborate ||
    accessLevel === AccessLevel.FullAccess
  );
}

// ---------------------------------------------------------------------------
// Task item component
// ---------------------------------------------------------------------------

interface TaskItemProps {
  task: ODataEvent;
  accessLevel: AccessLevel;
  isToggling: boolean;
  onToggleStatus: (task: ODataEvent) => void;
}

const TaskItem: React.FC<TaskItemProps> = ({
  task,
  accessLevel,
  isToggling,
  onToggleStatus,
}) => {
  const styles = useStyles();

  const isCompleted = task.sprk_status === TASK_STATUS.COMPLETED;
  const overdueFlag = isOverdue(task.sprk_duedate, isCompleted);
  const allowEdit = canEdit(accessLevel);

  const handleToggle = React.useCallback(() => {
    if (!allowEdit || isToggling) return;
    onToggleStatus(task);
  }, [allowEdit, isToggling, onToggleStatus, task]);

  return (
    <div
      className={`${styles.taskItem}${isCompleted ? ` ${styles.taskItemCompleted}` : ""}`}
      role="listitem"
    >
      {/* Status toggle — check/uncheck icon */}
      <div className={styles.taskCheckArea}>
        {allowEdit ? (
          <Tooltip
            content={isCompleted ? "Mark as incomplete" : "Mark as complete"}
            relationship="label"
          >
            <button
              className={`${styles.statusToggleBtn}${isCompleted ? ` ${styles.statusToggleBtnCompleted}` : ""}`}
              onClick={handleToggle}
              disabled={isToggling}
              aria-label={isCompleted ? "Mark task incomplete" : "Mark task complete"}
            >
              {isCompleted ? (
                <CheckmarkCircleFilled />
              ) : (
                <CheckmarkCircleRegular />
              )}
            </button>
          </Tooltip>
        ) : (
          /* View-only users: non-interactive status indicator */
          <span
            className={`${styles.statusToggleBtn}${isCompleted ? ` ${styles.statusToggleBtnCompleted}` : ""}`}
            aria-label={isCompleted ? "Completed" : "Not completed"}
            role="img"
            style={{ cursor: "default" }}
          >
            {isCompleted ? (
              <CheckmarkCircleFilled />
            ) : (
              <CheckmarkCircleRegular />
            )}
          </span>
        )}
      </div>

      {/* Task content */}
      <div className={styles.taskContent}>
        <Text
          className={`${styles.taskTitle}${isCompleted ? ` ${styles.taskTitleCompleted}` : ""}`}
        >
          {task.sprk_name}
        </Text>

        {/* Meta row: due date indicator */}
        {task.sprk_duedate && (
          <div className={styles.taskMeta}>
            <span
              className={`${styles.taskMetaText}${overdueFlag ? ` ${styles.taskMetaOverdue}` : ""}`}
            >
              {overdueFlag && <WarningRegular style={{ fontSize: "12px" }} />}
              <CalendarLtrRegular style={{ fontSize: "12px" }} />
              {overdueFlag ? "Overdue · " : ""}
              {formatDueDate(task.sprk_duedate)}
            </span>
          </div>
        )}

        {/* Status badge */}
        <div className={styles.taskMeta}>
          {task.sprk_status === TASK_STATUS.IN_PROGRESS && (
            <Badge appearance="tint" color="informative" size="small">
              In Progress
            </Badge>
          )}
          {task.sprk_status === TASK_STATUS.COMPLETED && (
            <Badge appearance="tint" color="success" size="small">
              Completed
            </Badge>
          )}
          {(task.sprk_status === TASK_STATUS.NOT_STARTED || task.sprk_status == null) && (
            <Badge appearance="tint" color="subtle" size="small">
              Not Started
            </Badge>
          )}
        </div>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Create Task Dialog
// ---------------------------------------------------------------------------

interface CreateTaskDialogProps {
  open: boolean;
  onDismiss: () => void;
  onSubmit: (payload: {
    title: string;
    description: string;
    dueDate: string;
    priority: number;
  }) => Promise<void>;
  isSubmitting: boolean;
  submitError: string | null;
}

const CreateTaskDialog: React.FC<CreateTaskDialogProps> = ({
  open,
  onDismiss,
  onSubmit,
  isSubmitting,
  submitError,
}) => {
  const styles = useStyles();

  const [title, setTitle] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [dueDate, setDueDate] = React.useState("");
  const [selectedPriority, setSelectedPriority] = React.useState<number>(50);

  // Reset form when dialog opens
  React.useEffect(() => {
    if (open) {
      setTitle("");
      setDescription("");
      setDueDate("");
      setSelectedPriority(50);
    }
  }, [open]);

  const isTitleValid = title.trim().length > 0;

  const handleSubmit = React.useCallback(async () => {
    if (!isTitleValid || isSubmitting) return;
    await onSubmit({
      title: title.trim(),
      description: description.trim(),
      dueDate,
      priority: selectedPriority,
    });
  }, [isTitleValid, isSubmitting, onSubmit, title, description, dueDate, selectedPriority]);

  const handleKeyDown = React.useCallback(
    (ev: React.KeyboardEvent) => {
      if (ev.key === "Enter" && (ev.ctrlKey || ev.metaKey)) {
        void handleSubmit();
      }
    },
    [handleSubmit]
  );

  return (
    <Dialog open={open} onOpenChange={(_ev, data) => { if (!data.open) onDismiss(); }}>
      <DialogSurface aria-label="Create Task dialog">
        <DialogBody>
          <DialogTitle>Create Task</DialogTitle>
          <DialogContent>
            {submitError && (
              <MessageBar intent="error" className={styles.errorBanner}>
                <MessageBarBody>{submitError}</MessageBarBody>
              </MessageBar>
            )}

            <div className={styles.dialogForm} onKeyDown={handleKeyDown}>
              {/* Title — required */}
              <div className={styles.fieldGroup}>
                <label className={styles.fieldLabel}>
                  Title
                  <span className={styles.fieldLabelRequired} aria-hidden="true">*</span>
                </label>
                <Input
                  value={title}
                  onChange={(_ev, data) => setTitle(data.value)}
                  placeholder="Enter task title..."
                  autoFocus
                  aria-required="true"
                  aria-invalid={!isTitleValid && title.length > 0}
                />
              </div>

              {/* Description — optional */}
              <div className={styles.fieldGroup}>
                <label className={styles.fieldLabel}>Description</label>
                <Textarea
                  value={description}
                  onChange={(_ev, data) => setDescription(data.value)}
                  placeholder="Add a description (optional)..."
                  resize="vertical"
                  rows={3}
                />
              </div>

              {/* Due Date — optional */}
              <div className={styles.fieldGroup}>
                <label className={styles.fieldLabel}>Due Date</label>
                <Input
                  type="date"
                  value={dueDate}
                  onChange={(ev) => setDueDate(ev.target.value)}
                />
              </div>

              {/* Priority — optional */}
              <div className={styles.fieldGroup}>
                <label className={styles.fieldLabel}>Priority</label>
                <div className={styles.priorityRow}>
                  {PRIORITY_OPTIONS.map((option) => (
                    <Badge
                      key={option.value}
                      className={styles.priorityBadge}
                      appearance={selectedPriority === option.value ? "filled" : "tint"}
                      color={option.color}
                      size="large"
                      onClick={() => setSelectedPriority(option.value)}
                      role="radio"
                      aria-checked={selectedPriority === option.value}
                      tabIndex={0}
                      onKeyDown={(ev) => {
                        if (ev.key === " " || ev.key === "Enter") {
                          setSelectedPriority(option.value);
                        }
                      }}
                    >
                      {option.label}
                    </Badge>
                  ))}
                </div>
              </div>
            </div>
          </DialogContent>

          <DialogActions>
            <Button
              appearance="secondary"
              onClick={onDismiss}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSubmit}
              disabled={!isTitleValid || isSubmitting}
            >
              {isSubmitting ? "Creating..." : "Create Task"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

// ---------------------------------------------------------------------------
// SmartTodo — Main component
// ---------------------------------------------------------------------------

export interface SmartTodoProps {
  /** Dataverse GUID of the sprk_project record whose tasks to display. */
  projectId: string;
  /** The current user's access level — controls create/edit permissions. */
  accessLevel: AccessLevel;
}

/**
 * SmartTodo — Task management panel for the Secure Project Workspace SPA.
 *
 * Fetches sprk_event records where sprk_todoflag=true for the given project.
 * Respects access level: View Only users can only read; Collaborate and
 * Full Access users can create tasks and toggle their completion status.
 */
export const SmartTodo: React.FC<SmartTodoProps> = ({ projectId, accessLevel }) => {
  const styles = useStyles();

  // Task list state
  const [tasks, setTasks] = React.useState<ODataEvent[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  // Status toggle state — tracks which task ID is currently being toggled
  const [togglingTaskId, setTogglingTaskId] = React.useState<string | null>(null);
  const [toggleError, setToggleError] = React.useState<string | null>(null);

  // Create dialog state
  const [isDialogOpen, setIsDialogOpen] = React.useState(false);
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [submitError, setSubmitError] = React.useState<string | null>(null);

  const allowEdit = canEdit(accessLevel);

  // ---------------------------------------------------------------------------
  // Load tasks
  // ---------------------------------------------------------------------------

  const loadTasks = React.useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);

    try {
      const events = await getEvents(projectId, {
        $filter: `_sprk_projectid_value eq '${projectId}' and sprk_todoflag eq true`,
        $select: "sprk_eventid,sprk_name,sprk_duedate,sprk_status,sprk_todoflag,_sprk_projectid_value,createdon",
        $orderby: "createdon desc",
        $top: 200,
      });
      setTasks(events);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Failed to load tasks";
      setLoadError(message);
    } finally {
      setIsLoading(false);
    }
  }, [projectId]);

  React.useEffect(() => {
    void loadTasks();
  }, [loadTasks]);

  // ---------------------------------------------------------------------------
  // Status toggle
  // ---------------------------------------------------------------------------

  const handleToggleStatus = React.useCallback(
    async (task: ODataEvent) => {
      if (!allowEdit || togglingTaskId) return;

      setTogglingTaskId(task.sprk_eventid);
      setToggleError(null);

      const isCurrentlyCompleted = task.sprk_status === TASK_STATUS.COMPLETED;
      const newStatus = isCurrentlyCompleted
        ? TASK_STATUS.NOT_STARTED
        : TASK_STATUS.COMPLETED;

      // Optimistic update
      setTasks((prev) =>
        prev.map((t) =>
          t.sprk_eventid === task.sprk_eventid
            ? { ...t, sprk_status: newStatus }
            : t
        )
      );

      try {
        await updateEvent(task.sprk_eventid, { sprk_status: newStatus });
      } catch (err) {
        // Revert on failure
        setTasks((prev) =>
          prev.map((t) =>
            t.sprk_eventid === task.sprk_eventid
              ? { ...t, sprk_status: task.sprk_status }
              : t
          )
        );
        const message = err instanceof Error ? err.message : "Failed to update task status";
        setToggleError(message);
      } finally {
        setTogglingTaskId(null);
      }
    },
    [allowEdit, togglingTaskId]
  );

  // ---------------------------------------------------------------------------
  // Create task
  // ---------------------------------------------------------------------------

  const handleOpenDialog = React.useCallback(() => {
    if (!allowEdit) return;
    setSubmitError(null);
    setIsDialogOpen(true);
  }, [allowEdit]);

  const handleDismissDialog = React.useCallback(() => {
    if (isSubmitting) return;
    setIsDialogOpen(false);
    setSubmitError(null);
  }, [isSubmitting]);

  const handleCreateTask = React.useCallback(
    async (formData: {
      title: string;
      description: string;
      dueDate: string;
      priority: number;
    }) => {
      setIsSubmitting(true);
      setSubmitError(null);

      try {
        const newEvent = await createEvent(projectId, {
          sprk_name: formData.title,
          sprk_todoflag: true,
          sprk_status: TASK_STATUS.NOT_STARTED,
          ...(formData.dueDate
            ? { sprk_duedate: dateInputToIso(formData.dueDate) }
            : {}),
        });

        // Add new task to the top of the list
        setTasks((prev) => [newEvent, ...prev]);
        setIsDialogOpen(false);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Failed to create task";
        setSubmitError(message);
      } finally {
        setIsSubmitting(false);
      }
    },
    [projectId]
  );

  // ---------------------------------------------------------------------------
  // Computed values
  // ---------------------------------------------------------------------------

  const pendingCount = tasks.filter(
    (t) => t.sprk_status !== TASK_STATUS.COMPLETED
  ).length;
  const totalCount = tasks.length;

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  const cardTitle = totalCount > 0
    ? `Tasks (${pendingCount} of ${totalCount} pending)`
    : "Tasks";

  const cardActions = allowEdit ? (
    <Button
      appearance="primary"
      icon={<AddRegular />}
      size="small"
      onClick={handleOpenDialog}
      aria-label="Create new task"
    >
      Add Task
    </Button>
  ) : undefined;

  return (
    <>
      <SectionCard title={cardTitle} actions={cardActions}>
        <div className={styles.container}>
          {/* Errors */}
          {loadError && (
            <MessageBar intent="error" className={styles.errorBanner}>
              <MessageBarBody>{loadError}</MessageBarBody>
            </MessageBar>
          )}
          {toggleError && (
            <MessageBar intent="warning" className={styles.errorBanner}>
              <MessageBarBody>{toggleError}</MessageBarBody>
            </MessageBar>
          )}

          {/* Loading state */}
          {isLoading && (
            <div className={styles.loadingState}>
              <Spinner size="medium" label="Loading tasks..." />
            </div>
          )}

          {/* Empty state */}
          {!isLoading && !loadError && tasks.length === 0 && (
            <div className={styles.emptyState} role="status" aria-live="polite">
              <TaskListSquareLtrRegular className={styles.emptyStateIcon} />
              <Text>No tasks yet</Text>
              {allowEdit && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground4 }}>
                  Use "Add Task" to create the first task for this project.
                </Text>
              )}
            </div>
          )}

          {/* Task list */}
          {!isLoading && tasks.length > 0 && (
            <>
              {/* Pending tasks first, then completed */}
              {(() => {
                const pending = tasks.filter(
                  (t) => t.sprk_status !== TASK_STATUS.COMPLETED
                );
                const completed = tasks.filter(
                  (t) => t.sprk_status === TASK_STATUS.COMPLETED
                );

                return (
                  <div className={styles.taskList} role="list" aria-label="Tasks">
                    {/* Pending tasks */}
                    {pending.map((task) => (
                      <TaskItem
                        key={task.sprk_eventid}
                        task={task}
                        accessLevel={accessLevel}
                        isToggling={togglingTaskId === task.sprk_eventid}
                        onToggleStatus={handleToggleStatus}
                      />
                    ))}

                    {/* Divider between pending and completed */}
                    {pending.length > 0 && completed.length > 0 && (
                      <Divider className={styles.divider}>Completed</Divider>
                    )}
                    {pending.length === 0 && completed.length > 0 && (
                      <Divider className={styles.divider}>Completed</Divider>
                    )}

                    {/* Completed tasks */}
                    {completed.map((task) => (
                      <TaskItem
                        key={task.sprk_eventid}
                        task={task}
                        accessLevel={accessLevel}
                        isToggling={togglingTaskId === task.sprk_eventid}
                        onToggleStatus={handleToggleStatus}
                      />
                    ))}
                  </div>
                );
              })()}
            </>
          )}
        </div>
      </SectionCard>

      {/* Create task dialog — only rendered when allowEdit */}
      {allowEdit && (
        <CreateTaskDialog
          open={isDialogOpen}
          onDismiss={handleDismissDialog}
          onSubmit={handleCreateTask}
          isSubmitting={isSubmitting}
          submitError={submitError}
        />
      )}
    </>
  );
};

export default SmartTodo;
