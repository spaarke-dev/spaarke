/**
 * SmartTodo — Task management component for the Secure Project Workspace SPA.
 *
 * Displays to-dos (sprk_todo records regarding the given project) for an
 * external user. External users can view to-dos, and create or update them
 * if their access level is Collaborate or Full Access.
 *
 * Contract change (R3 task 007): this component previously queried
 * `sprk_event` filtered by the legacy event-as-todo boolean toggle. To-dos
 * are now first-class `sprk_todo` records returned by the BFF route
 * `GET /api/v1/external/projects/{id}/todos` with ADR-024 polymorphic-resolver
 * fields populated server-side. Task 057: the same list reads a matter's or a work assignment's to-dos
 * (`GET /api/v1/external/{matters|workassignments}/{id}/todos`) when `root` says so - read-only there (create and
 * status toggle are project-only in this component).
 *
 * Design reference: @spaarke/ui-components/TodoDetail
 *   (src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx)
 *
 * Access level enforcement (ADR per project CLAUDE.md):
 *   - ViewOnly    (100000000): Read-only. Can view to-dos, cannot create or modify.
 *   - Collaborate (100000001): Can create to-dos and toggle status.
 *   - FullAccess  (100000002): Same as Collaborate plus invite rights (not relevant here).
 *
 * Delete-own (task 054, FR-26): independent of the access level, a to-do the caller CREATED (creator stamp = the
 * signed-in principal, passed as `caller`) shows Delete, which asks first. 204 removes it; a refusal shows an error and
 * the to-do stays. With no `caller` there is no Delete.
 *
 * Status values (FR-24):
 *   1         = Open
 *   659490001 = In Progress
 *   2         = Completed
 *   659490002 = Dismissed
 *
 * All colours via Fluent UI v9 design tokens (ADR-021). No hard-coded colors.
 * React 18 bundled (ADR-022, ADR-026). Fluent v9 makeStyles (ADR-021).
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
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
  MessageBarActions,
  Tooltip,
  Divider,
} from '@fluentui/react-components';
import {
  AddRegular,
  CheckmarkCircleRegular,
  CheckmarkCircleFilled,
  TaskListSquareLtrRegular,
  CalendarLtrRegular,
  DeleteRegular,
  WarningRegular,
} from '@fluentui/react-icons';
import { daysBetweenLocalMidnight, parseDueDate } from '@spaarke/ui-components/utils/dateLocal';

import {
  getRootTodos,
  createTodo,
  updateTodo,
  deleteOwnTodo,
  EVENTS_TODOS_ROOT_NOUN,
  type EventsTodosRoot,
  type ODataTodo,
} from '../api/web-api-client';
import { isCreatedByCaller, type CallerIdentity } from '../api/delete-own';
import { useDeleteOwn, DeleteOwnConfirm, DeleteFailureNotice } from './DeleteOwn';
import { TruncatedListNotice } from './TruncatedListNotice';
import { BffAccessState, accessFailureOf, type BffAccessFailure } from './auth/BffAccessState';
import { AccessLevel, ApiError } from '../types';
import { problemMessage } from '../auth/bff-client';
import { SectionCard } from './SectionCard';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  headerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
  },
  headerLeft: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  taskList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  taskItem: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalS}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  taskItemCompleted: {
    opacity: '0.65',
  },
  taskCheckArea: {
    flexShrink: 0,
    paddingTop: '2px',
    display: 'flex',
    alignItems: 'flex-start',
  },
  taskContent: {
    flex: '1 1 0',
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    minWidth: 0,
  },
  taskTitle: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    wordBreak: 'break-word',
  },
  taskTitleCompleted: {
    textDecorationLine: 'line-through',
    color: tokens.colorNeutralForeground4,
  },
  taskMeta: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  taskMetaText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    display: 'flex',
    alignItems: 'center',
    gap: '3px',
  },
  taskMetaOverdue: {
    color: tokens.colorPaletteRedForeground1,
  },
  statusToggleBtn: {
    minWidth: '0',
    padding: '0',
    border: 'none',
    background: 'transparent',
    cursor: 'pointer',
    color: tokens.colorNeutralForeground3,
    fontSize: '20px',
    lineHeight: '1',
    display: 'flex',
    alignItems: 'center',
    ':hover': {
      color: tokens.colorBrandForeground1,
    },
  },
  statusToggleBtnCompleted: {
    color: tokens.colorPaletteGreenForeground2,
    ':hover': {
      color: tokens.colorPaletteGreenForeground3,
    },
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground4,
  },
  emptyStateIcon: {
    fontSize: '40px',
    opacity: '0.5',
  },
  loadingState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  errorBanner: {
    marginBottom: tokens.spacingVerticalS,
  },
  // Dialog form styles
  dialogForm: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
  },
  fieldGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  fieldLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
  fieldLabelRequired: {
    color: tokens.colorPaletteRedForeground1,
    marginLeft: '2px',
  },
  priorityRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  priorityBadge: {
    cursor: 'pointer',
    userSelect: 'none',
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
// To-Do status mapping (sprk_todo.statuscode per FR-24)
// ---------------------------------------------------------------------------

/** A refused status toggle (403 from PATCH /todos/{id}); never the server's body (task 036 fix round 2). */
const TOGGLE_DENIED = 'You do not have permission to change this task.';

/** To-Do statuscode values per FR-24. */
const TODO_STATUS = {
  OPEN: 1,
  IN_PROGRESS: 659490001,
  COMPLETED: 2,
  DISMISSED: 659490002,
} as const;

/** Priority options for the create to-do dialog (maps to sprk_priorityscore 0-100). */
interface PriorityOption {
  label: string;
  value: number;
  color: 'informative' | 'warning' | 'danger' | 'subtle';
}

const PRIORITY_OPTIONS: PriorityOption[] = [
  { label: 'Low', value: 25, color: 'subtle' },
  { label: 'Medium', value: 50, color: 'informative' },
  { label: 'High', value: 75, color: 'warning' },
  { label: 'Critical', value: 100, color: 'danger' },
];

// ---------------------------------------------------------------------------
// Helper utilities
// ---------------------------------------------------------------------------

/**
 * A to-do due date is a calendar date ("yyyy-MM-dd" — Dataverse Date Only, task 106). `new Date("yyyy-MM-dd")` is UTC
 * midnight, which every zone west of UTC shows as the PREVIOUS day; parseDueDate builds the local calendar date.
 */
function formatDueDate(isoDate: string | null | undefined): string {
  const d = parseDueDate(isoDate);
  if (!d) return '';
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

/** A to-do is overdue once its due DAY has passed (a to-do due today is not overdue — task 106, D-43). */
function isOverdue(isoDate: string | null | undefined, isCompleted: boolean): boolean {
  if (isCompleted) return false;
  const d = parseDueDate(isoDate);
  if (!d) return false;
  return daysBetweenLocalMidnight(new Date(), d) < 0;
}

// ---------------------------------------------------------------------------
// CanEdit helper
// ---------------------------------------------------------------------------

/** Returns true if the access level allows creating or editing to-dos. */
function canEdit(accessLevel: AccessLevel): boolean {
  return accessLevel === AccessLevel.Collaborate || accessLevel === AccessLevel.FullAccess;
}

/** Whether the to-do is considered "done" (Completed or Dismissed). */
function isTerminalStatus(statuscode: number | null | undefined): boolean {
  return statuscode === TODO_STATUS.COMPLETED || statuscode === TODO_STATUS.DISMISSED;
}

// ---------------------------------------------------------------------------
// Task item component
// ---------------------------------------------------------------------------

interface TaskItemProps {
  task: ODataTodo;
  /** Offer the status toggle (the list decides: the level, or per-record write access — task 036 fix round 2). */
  allowEdit: boolean;
  isToggling: boolean;
  onToggleStatus: (task: ODataTodo) => void;
  /** Show Delete (the caller created this to-do). */
  canDelete?: boolean;
  onDelete?: (task: ODataTodo) => void;
}

const TaskItem: React.FC<TaskItemProps> = ({ task, allowEdit, isToggling, onToggleStatus, canDelete, onDelete }) => {
  const styles = useStyles();

  const isCompleted = isTerminalStatus(task.statuscode);
  const overdueFlag = isOverdue(task.sprk_duedate, isCompleted);

  const handleToggle = React.useCallback(() => {
    if (!allowEdit || isToggling) return;
    onToggleStatus(task);
  }, [allowEdit, isToggling, onToggleStatus, task]);

  return (
    <div className={`${styles.taskItem}${isCompleted ? ` ${styles.taskItemCompleted}` : ''}`} role="listitem">
      {/* Status toggle — check/uncheck icon */}
      <div className={styles.taskCheckArea}>
        {allowEdit ? (
          <Tooltip content={isCompleted ? 'Mark as incomplete' : 'Mark as complete'} relationship="label">
            <button
              className={`${styles.statusToggleBtn}${isCompleted ? ` ${styles.statusToggleBtnCompleted}` : ''}`}
              onClick={handleToggle}
              disabled={isToggling}
              aria-label={isCompleted ? 'Mark task incomplete' : 'Mark task complete'}
            >
              {isCompleted ? <CheckmarkCircleFilled /> : <CheckmarkCircleRegular />}
            </button>
          </Tooltip>
        ) : (
          /* View-only users: non-interactive status indicator */
          <span
            className={`${styles.statusToggleBtn}${isCompleted ? ` ${styles.statusToggleBtnCompleted}` : ''}`}
            aria-label={isCompleted ? 'Completed' : 'Not completed'}
            role="img"
            style={{ cursor: 'default' }}
          >
            {isCompleted ? <CheckmarkCircleFilled /> : <CheckmarkCircleRegular />}
          </span>
        )}
      </div>

      {/* Task content */}
      <div className={styles.taskContent}>
        <Text className={`${styles.taskTitle}${isCompleted ? ` ${styles.taskTitleCompleted}` : ''}`}>
          {task.sprk_name}
        </Text>

        {/* Meta row: due date indicator */}
        {task.sprk_duedate && (
          <div className={styles.taskMeta}>
            <span className={`${styles.taskMetaText}${overdueFlag ? ` ${styles.taskMetaOverdue}` : ''}`}>
              {overdueFlag && <WarningRegular style={{ fontSize: '12px' }} />}
              <CalendarLtrRegular style={{ fontSize: '12px' }} />
              {overdueFlag ? 'Overdue · ' : ''}
              {formatDueDate(task.sprk_duedate)}
            </span>
          </div>
        )}

        {/* Status badge */}
        <div className={styles.taskMeta}>
          {task.statuscode === TODO_STATUS.IN_PROGRESS && (
            <Badge appearance="tint" color="informative" size="small">
              In Progress
            </Badge>
          )}
          {task.statuscode === TODO_STATUS.COMPLETED && (
            <Badge appearance="tint" color="success" size="small">
              Completed
            </Badge>
          )}
          {task.statuscode === TODO_STATUS.DISMISSED && (
            <Badge appearance="tint" color="subtle" size="small">
              Dismissed
            </Badge>
          )}
          {(task.statuscode === TODO_STATUS.OPEN || task.statuscode == null) && (
            <Badge appearance="tint" color="brand" size="small">
              Open
            </Badge>
          )}
        </div>
      </div>

      {/* Delete: only a to-do the caller created (the server re-checks every delete). */}
      {canDelete && onDelete && (
        <Tooltip content="Delete" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<DeleteRegular />}
            onClick={() => onDelete(task)}
            aria-label={`Delete task ${task.sprk_name}`}
          />
        </Tooltip>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Create Task Dialog
// ---------------------------------------------------------------------------

interface CreateTaskDialogProps {
  open: boolean;
  onDismiss: () => void;
  onSubmit: (payload: { title: string; description: string; dueDate: string; priority: number }) => Promise<void>;
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

  const [title, setTitle] = React.useState('');
  const [description, setDescription] = React.useState('');
  const [dueDate, setDueDate] = React.useState('');
  const [selectedPriority, setSelectedPriority] = React.useState<number>(50);

  // Reset form when dialog opens
  React.useEffect(() => {
    if (open) {
      setTitle('');
      setDescription('');
      setDueDate('');
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
      if (ev.key === 'Enter' && (ev.ctrlKey || ev.metaKey)) {
        void handleSubmit();
      }
    },
    [handleSubmit]
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(_ev, data) => {
        if (!data.open) onDismiss();
      }}
    >
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
                  <span className={styles.fieldLabelRequired} aria-hidden="true">
                    *
                  </span>
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
                <Input type="date" value={dueDate} onChange={ev => setDueDate(ev.target.value)} />
              </div>

              {/* Priority — optional */}
              <div className={styles.fieldGroup}>
                <label className={styles.fieldLabel}>Priority</label>
                <div className={styles.priorityRow}>
                  {PRIORITY_OPTIONS.map(option => (
                    <Badge
                      key={option.value}
                      className={styles.priorityBadge}
                      appearance={selectedPriority === option.value ? 'filled' : 'tint'}
                      color={option.color}
                      size="large"
                      onClick={() => setSelectedPriority(option.value)}
                      role="radio"
                      aria-checked={selectedPriority === option.value}
                      tabIndex={0}
                      onKeyDown={ev => {
                        if (ev.key === ' ' || ev.key === 'Enter') {
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
            <Button appearance="secondary" onClick={onDismiss} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button appearance="primary" onClick={handleSubmit} disabled={!isTitleValid || isSubmitting}>
              {isSubmitting ? 'Creating...' : 'Create Task'}
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
  /** Dataverse GUID of the record whose to-dos to display (a sprk_project unless `root` says otherwise). */
  projectId: string;
  /** Which kind of record `projectId` is (task 057). Defaults to a project. */
  root?: EventsTodosRoot;
  /** The current user's access level — controls create/edit permissions. */
  accessLevel: AccessLevel;
  /** The signed-in principal. With it, to-dos the caller created show Delete (task 054); without it, none do. */
  caller?: CallerIdentity | null;
  /**
   * The caller's write access is decided per record (`/me` `writeAccessPerRecord`, task 036 fix round 1): "Add Task"
   * then follows the BFF's `canCreate` on this record's to-do list instead of `accessLevel`. The status toggle is offered
   * on a project's to-dos (fix round 2): PATCH confirms Write on each to-do against Dataverse, a per-row Write is not asked
   * of the list (owner round 59), so a refused toggle reverts and says so.
   */
  writeAccessPerRecord?: boolean;
}

/**
 * SmartTodo — Task management panel for the Secure Project Workspace SPA.
 *
 * Fetches `sprk_todo` records regarding the given project via the BFF route
 * `GET /api/v1/external/projects/{id}/todos`. Respects access level: View Only
 * users can only read; Collaborate and Full Access users can create to-dos
 * and toggle their completion status.
 */
export const SmartTodo: React.FC<SmartTodoProps> = ({
  projectId,
  root = 'projects',
  accessLevel: levelProp,
  caller,
  writeAccessPerRecord = false,
}) => {
  // Create and the status toggle are project routes' actions: a matter or work assignment list is read-only here whatever
  // level the host passes (task 057). Delete-own is independent of the level.
  const accessLevel = root === 'projects' ? levelProp : AccessLevel.ViewOnly;
  const styles = useStyles();

  // To-do list state
  const [tasks, setTasks] = React.useState<ODataTodo[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  // Task 105: the BFF said the list was cut short — shown, never presented as the whole list.
  const [truncated, setTruncated] = React.useState(false);
  // A 401/403 on the list is the shared sign-in / denied state (task 057); a failed read offers "Try again".
  const [accessFailure, setAccessFailure] = React.useState<BffAccessFailure | null>(null);
  const noun = EVENTS_TODOS_ROOT_NOUN[root];

  // Status toggle state — tracks which to-do ID is currently being toggled
  const [togglingTaskId, setTogglingTaskId] = React.useState<string | null>(null);
  const [toggleError, setToggleError] = React.useState<string | null>(null);

  // Create dialog state
  const [isDialogOpen, setIsDialogOpen] = React.useState(false);
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [submitError, setSubmitError] = React.useState<string | null>(null);

  const allowEdit = canEdit(accessLevel);
  // Task 036 fix round 1: the BFF's `canCreate` on this list (null = not said). With per-record write access it decides
  // "Add Task" (create is a project action here, task 057); otherwise the level does, as before.
  const [listCanCreate, setListCanCreate] = React.useState<boolean | null>(null);
  const allowCreate = writeAccessPerRecord ? root === 'projects' && listCanCreate === true : allowEdit;
  // Task 036 fix round 2: the status toggle. A per-record caller's level says only what they may SEE; PATCH /todos/{id}
  // asks Dataverse for Write on that to-do, so the toggle is offered on a project's to-dos and the PATCH decides (a 403
  // reverts it). Every other caller keeps the level rule, and a matter / work-assignment list stays read-only (task 057).
  const allowToggle = allowEdit || (writeAccessPerRecord && root === 'projects');

  // Delete-own (task 054): the to-do leaves the list on the server's 204; any refusal keeps it and is shown.
  const del = useDeleteOwn<ODataTodo>({
    noun: 'task',
    remove: task => deleteOwnTodo(task.sprk_todoid),
    onRemoved: task => setTasks(prev => prev.filter(t => t.sprk_todoid !== task.sprk_todoid)),
  });

  // ---------------------------------------------------------------------------
  // Load to-dos
  // ---------------------------------------------------------------------------

  const loadTasks = React.useCallback(async () => {
    setIsLoading(true);
    setLoadError(null);
    setAccessFailure(null);
    // Task 105: a flag from an earlier read must not sit beside this read's error.
    setTruncated(false);

    try {
      // BFF route /api/v1/external/{root}/{id}/todos returns sprk_todo records
      // regarding the given record (server-side resolver). No client-side
      // todoflag filter is needed — the new route returns only to-dos.
      // The BFF returns every to-do up to its row cap and says `truncated` when it stops short (task 105).
      const todos = await getRootTodos(root, projectId, {
        $orderby: 'createdon desc',
      });
      setTasks(todos.items);
      setTruncated(todos.truncated);
      setListCanCreate(todos.canCreate ?? null);
    } catch (err) {
      const failure = accessFailureOf(err);
      if (failure) {
        setAccessFailure(failure);
      } else {
        // The server's ProblemDetails `detail` when it wrote one; never the raw response body (an HTML 503 page or a JSON
        // problem document is not a message).
        setLoadError(problemMessage(err, 'Tasks could not be loaded just now.'));
      }
    } finally {
      setIsLoading(false);
    }
  }, [projectId, root]);

  React.useEffect(() => {
    void loadTasks();
  }, [loadTasks]);

  // ---------------------------------------------------------------------------
  // Status toggle
  // ---------------------------------------------------------------------------

  const handleToggleStatus = React.useCallback(
    async (task: ODataTodo) => {
      if (!allowToggle || togglingTaskId) return;

      setTogglingTaskId(task.sprk_todoid);
      setToggleError(null);

      const isCurrentlyCompleted = isTerminalStatus(task.statuscode);
      const newStatus = isCurrentlyCompleted ? TODO_STATUS.OPEN : TODO_STATUS.COMPLETED;

      // Optimistic update
      setTasks(prev => prev.map(t => (t.sprk_todoid === task.sprk_todoid ? { ...t, statuscode: newStatus } : t)));

      try {
        await updateTodo(task.sprk_todoid, { statuscode: newStatus });
      } catch (err) {
        // Revert on failure
        setTasks(prev =>
          prev.map(t => (t.sprk_todoid === task.sprk_todoid ? { ...t, statuscode: task.statuscode } : t))
        );
        // For every caller, a refusal says so plainly; no failure shows the server's raw body (task 036 fix round 2).
        setToggleError(
          err instanceof ApiError && err.statusCode === 403
            ? TOGGLE_DENIED
            : problemMessage(err, 'The task status could not be updated just now.')
        );
      } finally {
        setTogglingTaskId(null);
      }
    },
    [allowToggle, togglingTaskId]
  );

  // ---------------------------------------------------------------------------
  // Create to-do
  // ---------------------------------------------------------------------------

  const handleOpenDialog = React.useCallback(() => {
    if (!allowCreate) return;
    setSubmitError(null);
    setIsDialogOpen(true);
  }, [allowCreate]);

  const handleDismissDialog = React.useCallback(() => {
    if (isSubmitting) return;
    setIsDialogOpen(false);
    setSubmitError(null);
  }, [isSubmitting]);

  const handleCreateTask = React.useCallback(
    async (formData: { title: string; description: string; dueDate: string; priority: number }) => {
      setIsSubmitting(true);
      setSubmitError(null);

      try {
        // The BFF applies the regarding-project lookup + 4 ADR-024 resolver
        // fields server-side using the projectId from the route — clients
        // don't send those in the body.
        const newTodo = await createTodo(projectId, {
          sprk_name: formData.title,
          ...(formData.description ? { sprk_notes: formData.description } : {}),
          // The date input's own "yyyy-MM-dd" is the calendar day the user picked — the only shape a Date Only column
          // accepts (task 106; this used to send toISOString() of noon UTC, which Dataverse now refuses with 400).
          ...(formData.dueDate ? { sprk_duedate: formData.dueDate } : {}),
          sprk_priorityscore: formData.priority,
        });

        // Add new to-do to the top of the list
        setTasks(prev => [newTodo, ...prev]);
        setIsDialogOpen(false);
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Failed to create task';
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

  const pendingCount = tasks.filter(t => !isTerminalStatus(t.statuscode)).length;
  const totalCount = tasks.length;

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  const cardTitle = totalCount > 0 ? `Tasks (${pendingCount} of ${totalCount} pending)` : 'Tasks';

  const cardActions = allowCreate ? (
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
          {accessFailure && (
            <BffAccessState
              failure={accessFailure}
              deniedMessage={`You do not have permission to view tasks on this ${noun}.`}
            />
          )}
          {loadError && (
            <MessageBar intent="error" className={styles.errorBanner}>
              <MessageBarBody>{loadError}</MessageBarBody>
              <MessageBarActions>
                <Button appearance="secondary" size="small" onClick={() => void loadTasks()}>
                  Try again
                </Button>
              </MessageBarActions>
            </MessageBar>
          )}
          {toggleError && (
            <MessageBar intent="warning" className={styles.errorBanner}>
              <MessageBarBody>{toggleError}</MessageBarBody>
            </MessageBar>
          )}
          <DeleteFailureNotice del={del} />
          {!isLoading && <TruncatedListNotice truncated={truncated} shown={tasks.length} noun="tasks" />}

          {/* Loading state */}
          {isLoading && (
            <div className={styles.loadingState}>
              <Spinner size="medium" label="Loading tasks..." />
            </div>
          )}

          {/* Empty state */}
          {/* Task 105: an incomplete empty list is not "no tasks" — the notice says it could not be read. */}
          {!isLoading && !loadError && !accessFailure && !truncated && tasks.length === 0 && (
            <div className={styles.emptyState} role="status" aria-live="polite">
              <TaskListSquareLtrRegular className={styles.emptyStateIcon} />
              <Text>No tasks yet</Text>
              {allowCreate && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground4 }}>
                  Use "Add Task" to create the first task for this project.
                </Text>
              )}
            </div>
          )}

          {/* Task list */}
          {!isLoading && tasks.length > 0 && (
            <>
              {/* Pending to-dos first, then completed/dismissed */}
              {(() => {
                const pending = tasks.filter(t => !isTerminalStatus(t.statuscode));
                const completed = tasks.filter(t => isTerminalStatus(t.statuscode));

                return (
                  <div className={styles.taskList} role="list" aria-label="Tasks">
                    {/* Pending to-dos */}
                    {pending.map(task => (
                      <TaskItem
                        key={task.sprk_todoid}
                        task={task}
                        allowEdit={allowToggle}
                        isToggling={togglingTaskId === task.sprk_todoid}
                        onToggleStatus={handleToggleStatus}
                        canDelete={isCreatedByCaller(task, caller)}
                        onDelete={t => del.ask(t, t.sprk_name)}
                      />
                    ))}

                    {/* Divider between pending and completed */}
                    {pending.length > 0 && completed.length > 0 && (
                      <Divider className={styles.divider}>Completed</Divider>
                    )}
                    {pending.length === 0 && completed.length > 0 && (
                      <Divider className={styles.divider}>Completed</Divider>
                    )}

                    {/* Completed / dismissed to-dos */}
                    {completed.map(task => (
                      <TaskItem
                        key={task.sprk_todoid}
                        task={task}
                        allowEdit={allowToggle}
                        isToggling={togglingTaskId === task.sprk_todoid}
                        onToggleStatus={handleToggleStatus}
                        canDelete={isCreatedByCaller(task, caller)}
                        onDelete={t => del.ask(t, t.sprk_name)}
                      />
                    ))}
                  </div>
                );
              })()}
            </>
          )}
        </div>
      </SectionCard>

      <DeleteOwnConfirm del={del} />

      {/* Create task dialog — only rendered when allowCreate */}
      {allowCreate && (
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
