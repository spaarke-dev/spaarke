/**
 * TodoDetail — Shared content component for the To Do Detail side pane.
 *
 * Layout (top to bottom):
 *   1. Description (editable, auto-expands, no scroll)
 *   2. Details: Record Type, Record link, Due Date, Assigned To
 *   3. To Do Notes (editable, auto-expands, no scroll)
 *   4. To Do Score section (Priority, Effort, Urgency sliders)
 *   5. Sticky footer: Dismiss, Save, Complete buttons
 *
 * Single-entity model (R3 FR-09): all reads + writes target `sprk_todo`.
 * The legacy two-entity (`sprk_event` + `sprk_eventtodo`) load/save was removed
 * in smart-todo-decoupling-r3 Phase 2 (per OS-1: no compat shims).
 *
 * statuscode semantics (per task 009):
 *   - 1          = Open       (statecode 0)
 *   - 659490001  = In Progress(statecode 0)
 *   - 2          = Completed  (statecode 1)
 *   - 659490002  = Dismissed  (statecode 1)
 *
 * Context-agnostic (ADR-012): No Xrm, no PCF APIs.
 * All external I/O is via callback props.
 * All colours from Fluent UI v9 semantic tokens (ADR-021).
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Textarea,
  Input,
  Slider,
  Combobox,
  Option,
  Button,
  Badge,
  Link,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import type {
  SliderOnChangeData,
  ComboboxProps,
  OptionOnSelectData,
  SelectionEvents,
} from '@fluentui/react-components';
import { SaveRegular, InfoRegular, DeleteRegular, CheckmarkRegular, OpenRegular } from '@fluentui/react-icons';
import type { ITodoRecord, ITodoFieldUpdates, IContactOption } from './types';

// ---------------------------------------------------------------------------
// statuscode + statecode constants (mirror task 009 customization)
// ---------------------------------------------------------------------------

/** statecode = Inactive (Active = 0, kept implicit via record.statecode value). */
const STATECODE_INACTIVE = 1;

/** statuscode = Completed (Inactive). statuscode=Open (1) is the active default. */
const STATUSCODE_COMPLETED = 2;
/** statuscode = Dismissed (Inactive). */
const STATUSCODE_DISMISSED = 659490002;

// ---------------------------------------------------------------------------
// To Do Score computation (self-contained — no cross-solution imports)
// ---------------------------------------------------------------------------

/**
 * Compute To Do Score — mirrors LegalWorkspace computeTodoScore() exactly.
 *
 * Formula: priority*0.50 + invertedEffort*0.20 + urgencyRaw*0.30
 * Uses Math.ceil for diffDays and Math.round for the final score
 * to match the Kanban card computation.
 */
function computeScore(
  priority: number,
  effort: number,
  duedate: string | null | undefined
): {
  todoScore: number;
  priorityComponent: number;
  effortComponent: number;
  urgencyRaw: number;
  urgencyComponent: number;
} {
  const invertedEffort = 100 - effort;

  let urgencyRaw = 0;
  if (duedate) {
    const due = new Date(duedate);
    if (!isNaN(due.getTime())) {
      const now = new Date();
      const diffMs = due.getTime() - now.getTime();
      const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
      if (diffDays < 0) urgencyRaw = 100;
      else if (diffDays <= 3) urgencyRaw = 80;
      else if (diffDays <= 7) urgencyRaw = 50;
      else if (diffDays <= 10) urgencyRaw = 25;
    }
  }

  const priorityComponent = priority * 0.5;
  const effortComponent = invertedEffort * 0.2;
  const urgencyComponent = urgencyRaw * 0.3;
  const raw = priorityComponent + effortComponent + urgencyComponent;
  const todoScore = Math.max(0, Math.min(100, Math.round(raw)));

  return { todoScore, priorityComponent, effortComponent, urgencyRaw, urgencyComponent };
}

/** Convert ISO date string to YYYY-MM-DD for input[type="date"]. */
function toDateInputValue(dateStr?: string | null): string {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return '';
  return d.toISOString().split('T')[0];
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    overflow: 'hidden',
  },
  content: {
    flex: '1 1 0',
    overflowY: 'auto',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    gap: '0px',
  },
  divider: {
    height: '1px',
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    marginTop: '25px',
    marginBottom: '25px',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  sectionTitleRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  sectionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  fieldRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  sliderRow: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
  },
  sliderLabelRow: {
    display: 'flex',
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  sliderValue: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    minWidth: '24px',
    textAlign: 'right' as const,
  },
  scoreCircle: {
    width: '36px',
    height: '36px',
    borderRadius: '50%',
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: tokens.fontWeightBold,
    fontSize: tokens.fontSizeBase300,
    flexShrink: 0,
  },
  infoPopover: {
    maxWidth: '320px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  infoSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  infoSectionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  infoSectionBody: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  footer: {
    display: 'flex',
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  emptyState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: '1 1 0',
    color: tokens.colorNeutralForeground4,
    paddingTop: tokens.spacingVerticalXXXL,
  },
  loadingState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: '1 1 0',
    paddingTop: tokens.spacingVerticalXXXL,
  },
  errorBanner: {
    flexShrink: 0,
  },
  assignedToDisplay: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  assignedToName: {
    flex: '1 1 0',
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  recordLink: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    cursor: 'pointer',
  },
  removeButton: {
    color: tokens.colorPaletteRedForeground1,
    marginRight: 'auto',
  },
  openIcon: {
    fontSize: tokens.fontSizeBase200,
  },
  scoreCircleAlignRight: {
    marginLeft: 'auto',
  },
  textareaMinHeight: {
    minHeight: '160px',
  },
  completeBtn: {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
    ':hover': {
      backgroundColor: tokens.colorPaletteYellowForeground2,
    },
  },
  completedBtn: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': {
      backgroundColor: tokens.colorPaletteGreenForeground2,
    },
  },
  scoreSection: {
    marginBottom: '20px',
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITodoDetailProps {
  /** The `sprk_todo` record (or null while loading / no selection). */
  record: ITodoRecord | null;
  isLoading: boolean;
  error: string | null;
  /**
   * Save a subset of `sprk_todo` fields. Single Web API `updateRecord` call.
   * Host implements via injected Web API client (no hardcoded URLs).
   */
  onSaveTodo: (todoId: string, fields: ITodoFieldUpdates) => Promise<{ success: boolean; error?: string }>;
  /**
   * Dismiss the to-do (sets statuscode = 659490002 / Dismissed, statecode = 1 / Inactive).
   * Replaces the legacy "Remove from To Do" path that toggled `sprk_event.sprk_todoflag`.
   *
   * Hosts that prefer hard delete over Dismiss can implement this callback to call
   * `deleteRecord("sprk_todo", id)` instead — the component is agnostic to the
   * persistence semantics, it just invokes this callback when the user clicks "Dismiss".
   */
  onDismissTodo?: (todoId: string) => Promise<{ success: boolean; error?: string }>;
  /** Close the side pane. */
  onClose?: () => void;
  /**
   * Search the picker source (users or contacts) by name for the Assigned To picker.
   * Decoupled from Xrm — host provides the implementation (ADR-012).
   *
   * Note: `sprk_todo.sprk_assignedto` is a `systemuser` lookup. Hosts should resolve
   * the picker against `systemuser` (or whichever picker source matches the host's
   * binding). The IContactOption shape is generic (id + name).
   */
  onSearchContacts: (query: string) => Promise<IContactOption[]>;
  /**
   * Open the regarding record (matter/project/etc.) in a new tab/window.
   * Decoupled from Xrm — host provides the navigation implementation (ADR-012).
   * Called with the entity logical name and record ID.
   */
  onOpenRegardingRecord?: (entityName: string, recordId: string) => void;
}

/** Map record-type display name to Dataverse entity logical name for navigation. */
const RECORD_TYPE_ENTITY_MAP: Record<string, string> = {
  Matter: 'sprk_matter',
  Project: 'sprk_project',
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoDetail: React.FC<ITodoDetailProps> = React.memo(
  ({
    record,
    isLoading,
    error,
    onSaveTodo,
    onDismissTodo,
    onClose: _onClose,
    onSearchContacts,
    onOpenRegardingRecord,
  }) => {
    const styles = useStyles();

    // Auto-expand textarea refs
    const textareaRef = React.useRef<HTMLTextAreaElement | null>(null);
    const notesTextareaRef = React.useRef<HTMLTextAreaElement | null>(null);

    // Editable field values (all on sprk_todo)
    const [description, setDescription] = React.useState('');
    const [notes, setNotes] = React.useState('');
    const [dueDate, setDueDate] = React.useState('');
    const [priority, setPriority] = React.useState<number>(50);
    const [effort, setEffort] = React.useState<number>(50);

    // Assigned To state
    const [assignedToId, setAssignedToId] = React.useState<string | null>(null);
    const [assignedToName, setAssignedToName] = React.useState('');
    const [contactQuery, setContactQuery] = React.useState('');
    const [contactOptions, setContactOptions] = React.useState<IContactOption[]>([]);
    const [isSearching, setIsSearching] = React.useState(false);
    const [isEditingAssignedTo, setIsEditingAssignedTo] = React.useState(false);

    // Save state
    const [isSaving, setIsSaving] = React.useState(false);
    const [isDismissing, setIsDismissing] = React.useState(false);
    const [isCompleting, setIsCompleting] = React.useState(false);
    const [saveError, setSaveError] = React.useState<string | null>(null);

    // Snapshot of original values (for dirty detection)
    const origRef = React.useRef({
      description: '',
      notes: '',
      dueDate: '',
      priority: 50,
      effort: 50,
      assignedToId: null as string | null,
    });

    // Reset when record changes
    React.useEffect(() => {
      if (record) {
        const desc = record.sprk_description ?? '';
        const nts = record.sprk_notes ?? '';
        const dd = toDateInputValue(record.sprk_duedate);
        const pri = record.sprk_priorityscore ?? 50;
        const eff = record.sprk_effortscore ?? 50;
        const aId = record._sprk_assignedto_value ?? null;
        const aName = record['_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue'] ?? '';
        setDescription(desc);
        setNotes(nts);
        setDueDate(dd);
        setPriority(pri);
        setEffort(eff);
        setAssignedToId(aId);
        setAssignedToName(aName);
        setContactQuery('');
        setContactOptions([]);
        setIsEditingAssignedTo(false);
        setSaveError(null);
        origRef.current = {
          description: desc,
          notes: nts,
          dueDate: dd,
          priority: pri,
          effort: eff,
          assignedToId: aId,
        };
      }
    }, [record?.sprk_todoid]); // eslint-disable-line react-hooks/exhaustive-deps

    // Dirty detection (any tracked field changed)
    const isDirty =
      description !== origRef.current.description ||
      notes !== origRef.current.notes ||
      dueDate !== origRef.current.dueDate ||
      priority !== origRef.current.priority ||
      effort !== origRef.current.effort ||
      assignedToId !== origRef.current.assignedToId;

    // --- Handlers ---

    const handleDescriptionChange = React.useCallback((_ev: unknown, data: { value: string }) => {
      setDescription(data.value);
      requestAnimationFrame(() => {
        const el = textareaRef.current;
        if (!el) return;
        el.style.height = 'auto';
        el.style.height = `${el.scrollHeight}px`;
        el.style.overflowY = 'hidden';
      });
    }, []);

    // Auto-resize description textarea on initial load
    React.useEffect(() => {
      const el = textareaRef.current;
      if (!el) return;
      el.style.height = 'auto';
      el.style.height = `${el.scrollHeight}px`;
      el.style.overflowY = 'hidden';
    }, [description]);

    const handleNotesChange = React.useCallback((_ev: unknown, data: { value: string }) => {
      setNotes(data.value);
      requestAnimationFrame(() => {
        const el = notesTextareaRef.current;
        if (!el) return;
        el.style.height = 'auto';
        el.style.height = `${el.scrollHeight}px`;
        el.style.overflowY = 'hidden';
      });
    }, []);

    // Auto-resize notes textarea on initial load
    React.useEffect(() => {
      const el = notesTextareaRef.current;
      if (!el) return;
      el.style.height = 'auto';
      el.style.height = `${el.scrollHeight}px`;
      el.style.overflowY = 'hidden';
    }, [notes]);

    const handleDueDateChange = React.useCallback(
      (ev: React.ChangeEvent<HTMLInputElement>) => setDueDate(ev.target.value),
      []
    );

    const handlePriorityChange = React.useCallback(
      (_ev: React.ChangeEvent<HTMLInputElement>, data: SliderOnChangeData) => {
        setPriority(data.value);
      },
      []
    );

    const handleEffortChange = React.useCallback(
      (_ev: React.ChangeEvent<HTMLInputElement>, data: SliderOnChangeData) => {
        setEffort(data.value);
      },
      []
    );

    // Debounced contact search (uses onSearchContacts callback prop)
    const searchTimerRef = React.useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
    const handleContactInput: ComboboxProps['onInput'] = React.useCallback(
      (ev: React.FormEvent<HTMLInputElement>) => {
        const q = ev.currentTarget.value;
        setContactQuery(q);
        clearTimeout(searchTimerRef.current);
        if (q.length < 2) {
          setContactOptions([]);
          return;
        }
        setIsSearching(true);
        searchTimerRef.current = setTimeout(async () => {
          const results = await onSearchContacts(q);
          setContactOptions(results);
          setIsSearching(false);
        }, 300);
      },
      [onSearchContacts]
    );

    const handleContactSelect: ComboboxProps['onOptionSelect'] = React.useCallback(
      (_ev: SelectionEvents, data: OptionOnSelectData) => {
        if (data.optionValue && data.optionText) {
          setAssignedToId(data.optionValue);
          setAssignedToName(data.optionText);
          setContactQuery('');
          setContactOptions([]);
          setIsEditingAssignedTo(false);
        }
      },
      []
    );

    /** Build an `ITodoFieldUpdates` from the current vs. original state (diff-based). */
    const buildDirtyUpdates = React.useCallback((): ITodoFieldUpdates => {
      const updates: ITodoFieldUpdates = {};
      if (description !== origRef.current.description) {
        updates.sprk_description = description;
      }
      if (notes !== origRef.current.notes) {
        updates.sprk_notes = notes;
      }
      if (dueDate !== origRef.current.dueDate) {
        updates.sprk_duedate = dueDate || null;
      }
      if (priority !== origRef.current.priority) {
        updates.sprk_priorityscore = priority;
      }
      if (effort !== origRef.current.effort) {
        updates.sprk_effortscore = effort;
      }
      if (assignedToId !== origRef.current.assignedToId) {
        updates['sprk_AssignedTo@odata.bind'] = assignedToId ? `/systemusers(${assignedToId})` : null;
      }
      return updates;
    }, [description, notes, dueDate, priority, effort, assignedToId]);

    // Save: single updateRecord("sprk_todo", id, fields)
    const handleSave = React.useCallback(async () => {
      if (!record || !isDirty) return;
      setIsSaving(true);
      setSaveError(null);

      try {
        const updates = buildDirtyUpdates();
        const result = await onSaveTodo(record.sprk_todoid, updates);
        if (!result.success) {
          setSaveError(result.error ?? 'Failed to save');
          setIsSaving(false);
          return;
        }
        // Update snapshot on success
        origRef.current = {
          description,
          notes,
          dueDate,
          priority,
          effort,
          assignedToId,
        };
      } catch {
        setSaveError('Save failed — unexpected error');
      } finally {
        setIsSaving(false);
      }
    }, [record, isDirty, buildDirtyUpdates, onSaveTodo, description, notes, dueDate, priority, effort, assignedToId]);

    /**
     * Dismiss: statuscode=659490002 (Dismissed), statecode=1 (Inactive).
     *
     * Per R3 OS-1 — the legacy "Remove from To Do" path that toggled
     * `sprk_event.sprk_todoflag=false` is removed. The semantic equivalent for the new
     * first-class `sprk_todo` model is statuscode=Dismissed. Hosts that prefer hard
     * delete can implement the `onDismissTodo` callback as `deleteRecord("sprk_todo", id)`.
     */
    const handleDismiss = React.useCallback(async () => {
      if (!record || !onDismissTodo) return;
      setIsDismissing(true);
      setSaveError(null);
      try {
        const result = await onDismissTodo(record.sprk_todoid);
        if (!result.success) {
          setSaveError(result.error ?? 'Failed to dismiss');
          setIsDismissing(false);
        }
      } catch {
        setSaveError('Failed to dismiss — unexpected error');
        setIsDismissing(false);
      }
    }, [record, onDismissTodo]);

    /**
     * Complete: saves dirty fields + sets statecode=1 + statuscode=2 (Completed) +
     * sprk_completedon. Single `updateRecord` call.
     */
    const handleCompleted = React.useCallback(async () => {
      if (!record) return;
      setIsCompleting(true);
      setSaveError(null);

      try {
        const updates: ITodoFieldUpdates = {
          ...buildDirtyUpdates(),
          statecode: STATECODE_INACTIVE,
          statuscode: STATUSCODE_COMPLETED,
          sprk_completedon: new Date().toISOString(),
        };
        const result = await onSaveTodo(record.sprk_todoid, updates);
        if (!result.success) {
          setSaveError(result.error ?? 'Failed to complete');
          setIsCompleting(false);
          return;
        }
        // Update snapshot to current values
        origRef.current = {
          description,
          notes,
          dueDate,
          priority,
          effort,
          assignedToId,
        };
      } catch {
        setSaveError('Failed to mark as completed — unexpected error');
      } finally {
        setIsCompleting(false);
      }
    }, [record, buildDirtyUpdates, onSaveTodo, description, notes, dueDate, priority, effort, assignedToId]);

    // Open regarding record — delegates to host via callback prop
    const handleOpenRegardingRecord = React.useCallback(() => {
      if (!record?.sprk_regardingrecordid || !onOpenRegardingRecord) return;
      const typeName = record['_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue'] ?? '';
      const entityName = RECORD_TYPE_ENTITY_MAP[typeName];
      if (!entityName) return;
      onOpenRegardingRecord(entityName, record.sprk_regardingrecordid);
    }, [record, onOpenRegardingRecord]);

    // --- Render states ---

    if (isLoading) {
      return (
        <div className={styles.loadingState}>
          <Spinner size="medium" label="Loading..." />
        </div>
      );
    }

    if (error) {
      return (
        <div className={styles.emptyState}>
          <Text>{error}</Text>
        </div>
      );
    }

    if (!record) {
      return (
        <div className={styles.emptyState}>
          <Text>No to-do selected</Text>
        </div>
      );
    }

    // Compute score from CURRENT field values (live preview)
    const score = computeScore(priority, effort, dueDate || record.sprk_duedate);

    // Derived: is the record already inactive (Completed or Dismissed)?
    const isInactive = record.statecode === STATECODE_INACTIVE;
    const isCompleted = isInactive && record.statuscode === STATUSCODE_COMPLETED;
    const isDismissed = isInactive && record.statuscode === STATUSCODE_DISMISSED;

    return (
      <div className={styles.container}>
        <div className={styles.content}>
          {/* Save error banner */}
          {saveError && (
            <MessageBar intent="error" className={styles.errorBanner}>
              <MessageBarBody>{saveError}</MessageBarBody>
            </MessageBar>
          )}

          {/* -- Description (top) -- */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Description
            </Text>
            <Textarea
              value={description}
              onChange={handleDescriptionChange}
              placeholder="Add a description..."
              resize="none"
              textarea={{
                ref: textareaRef,
                className: styles.textareaMinHeight,
              }}
            />
          </div>

          <div className={styles.divider} role="separator" />

          {/* -- Details -- */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Details
            </Text>

            {/* Record Type tag */}
            {record['_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue'] && (
              <div className={styles.fieldRow}>
                <label className={styles.fieldLabel}>Record Type</label>
                <div>
                  <Badge appearance="filled" color="informative" size="medium">
                    {record['_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue']}
                  </Badge>
                </div>
              </div>
            )}

            {/* Record link */}
            {record.sprk_regardingrecordname && record.sprk_regardingrecordid && (
              <div className={styles.fieldRow}>
                <label className={styles.fieldLabel}>Record</label>
                <Link className={styles.recordLink} onClick={handleOpenRegardingRecord} as="button">
                  {record.sprk_regardingrecordname}
                  <OpenRegular className={styles.openIcon} />
                </Link>
              </div>
            )}

            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Due Date</label>
              <Input type="date" value={dueDate} onChange={handleDueDateChange} />
            </div>

            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Assigned To</label>
              {assignedToName && !isEditingAssignedTo ? (
                <div className={styles.assignedToDisplay}>
                  <Text className={styles.assignedToName}>{assignedToName}</Text>
                  <Button appearance="subtle" size="small" onClick={() => setIsEditingAssignedTo(true)}>
                    Change
                  </Button>
                </div>
              ) : (
                <Combobox
                  freeform
                  placeholder="Search users..."
                  value={contactQuery}
                  onInput={handleContactInput}
                  onOptionSelect={handleContactSelect}
                  selectedOptions={assignedToId ? [assignedToId] : []}
                >
                  {isSearching && (
                    <Option key="__loading" value="" text="" disabled>
                      Searching...
                    </Option>
                  )}
                  {!isSearching && contactOptions.length === 0 && contactQuery.length >= 2 && (
                    <Option key="__empty" value="" text="" disabled>
                      No users found
                    </Option>
                  )}
                  {contactOptions.map(c => (
                    <Option key={c.id} value={c.id} text={c.name}>
                      {c.name}
                    </Option>
                  ))}
                </Combobox>
              )}
            </div>
          </div>

          <div className={styles.divider} role="separator" />

          {/* -- Notes (rich/multiline on sprk_todo.sprk_notes) -- */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Notes
            </Text>
            <Textarea
              value={notes}
              onChange={handleNotesChange}
              placeholder="Add notes..."
              resize="none"
              textarea={{
                ref: notesTextareaRef,
                className: styles.textareaMinHeight,
              }}
            />
          </div>

          <div className={styles.divider} role="separator" />

          {/* -- To Do Score: title row with circle + info, then sliders -- */}
          <div className={`${styles.section} ${styles.scoreSection}`}>
            <div className={styles.sectionTitleRow}>
              <Text className={styles.sectionTitle} size={300}>
                To Do Score
              </Text>
              <Popover withArrow>
                <PopoverTrigger disableButtonEnhancement>
                  <Button appearance="subtle" size="small" icon={<InfoRegular />} aria-label="Score information" />
                </PopoverTrigger>
                <PopoverSurface>
                  <div className={styles.infoPopover}>
                    <div className={styles.infoSection}>
                      <Text className={styles.infoSectionTitle} size={300}>
                        How Scoring Works
                      </Text>
                      <Text className={styles.infoSectionBody}>
                        The To Do Score combines three factors into a single 0-100 number. Higher scores surface more
                        important items first in the Kanban board.
                      </Text>
                    </div>

                    <div className={styles.infoSection}>
                      <Text className={styles.infoSectionTitle} size={300}>
                        Score Formula
                      </Text>
                      <Text className={styles.infoSectionBody}>
                        Score = Priority (50%) + Inverted Effort (20%) + Urgency (30%). Lower effort items score higher
                        (quick wins bubble up).
                      </Text>
                    </div>

                    <div className={styles.infoSection}>
                      <Text className={styles.infoSectionTitle} size={300}>
                        Urgency Score
                      </Text>
                      <Text className={styles.infoSectionBody}>
                        Auto-calculated from due date: Overdue = 100, within 3 days = 80, within 7 days = 50, within 10
                        days = 25, more than 10 days = 0.
                      </Text>
                    </div>
                  </div>
                </PopoverSurface>
              </Popover>
              <div className={`${styles.scoreCircle} ${styles.scoreCircleAlignRight}`}>{Math.round(score.todoScore)}</div>
            </div>

            <div className={styles.sliderRow}>
              <div className={styles.sliderLabelRow}>
                <label className={styles.fieldLabel}>Priority (50%)</label>
                <span className={styles.sliderValue}>{priority}</span>
              </div>
              <Slider value={priority} onChange={handlePriorityChange} min={0} max={100} step={5} />
            </div>

            <div className={styles.sliderRow}>
              <div className={styles.sliderLabelRow}>
                <label className={styles.fieldLabel}>Effort (20%)</label>
                <span className={styles.sliderValue}>{effort}</span>
              </div>
              <Slider value={effort} onChange={handleEffortChange} min={0} max={100} step={5} />
            </div>
          </div>
        </div>

        {/* -- Sticky footer -- */}
        <div className={styles.footer}>
          {onDismissTodo && !isInactive && (
            <Button
              appearance="subtle"
              icon={<DeleteRegular />}
              onClick={handleDismiss}
              disabled={isDismissing || isSaving || isCompleting}
              className={styles.removeButton}
            >
              {isDismissing ? 'Dismissing...' : 'Dismiss'}
            </Button>
          )}
          <Button
            appearance="primary"
            icon={<SaveRegular />}
            onClick={handleSave}
            disabled={!isDirty || isSaving || isCompleting || isInactive}
          >
            {isSaving ? 'Saving...' : 'Save'}
          </Button>
          {isCompleted ? (
            <Button icon={<CheckmarkRegular />} disabled className={styles.completedBtn}>
              Completed
            </Button>
          ) : isDismissed ? (
            <Button icon={<DeleteRegular />} disabled className={styles.completedBtn}>
              Dismissed
            </Button>
          ) : (
            <Button
              icon={<CheckmarkRegular />}
              onClick={handleCompleted}
              disabled={isSaving || isCompleting}
              className={styles.completeBtn}
            >
              {isCompleting ? 'Completing...' : 'Complete'}
            </Button>
          )}
        </div>
      </div>
    );
  }
);

TodoDetail.displayName = 'TodoDetail';
