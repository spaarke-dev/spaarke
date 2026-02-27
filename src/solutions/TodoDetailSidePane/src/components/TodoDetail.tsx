/**
 * TodoDetail — Main content component for the To Do Detail side pane.
 *
 * Layout (top to bottom):
 *   1. Description (editable, auto-expands, no scroll)
 *   2. Details: Record Type, Record link, Due Date, Assigned To
 *   3. To Do Notes (editable, auto-expands, no scroll) — from sprk_eventtodo
 *   4. To Do Score section (Priority, Effort, Urgency sliders)
 *   5. Sticky footer: Remove, Save, Completed buttons
 *
 * Data spans TWO entities:
 *   - sprk_event: description, due date, scores, lookups
 *   - sprk_eventtodo: notes, completed flag/date, statuscode
 *
 * All colours from Fluent UI v9 semantic tokens (ADR-021).
 */

import * as React from "react";
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
} from "@fluentui/react-components";
import type {
  SliderOnChangeData,
  ComboboxProps,
} from "@fluentui/react-components";
import {
  SaveRegular,
  InfoRegular,
  DeleteRegular,
  CheckmarkRegular,
  OpenRegular,
} from "@fluentui/react-icons";
import { ITodoRecord } from "../types/TodoRecord";
import type { ITodoExtension } from "../types/TodoRecord";
import { searchContacts } from "../services/todoService";
import type {
  IEventFieldUpdates,
  ITodoExtensionUpdates,
  IContactOption,
} from "../services/todoService";
import { getXrm } from "../utils/xrmAccess";

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
  if (!dateStr) return "";
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return "";
  return d.toISOString().split("T")[0];
}

/** Map record type display name → Dataverse entity logical name for navigation. */
const RECORD_TYPE_ENTITY_MAP: Record<string, string> = {
  Matter: "sprk_matter",
  Project: "sprk_project",
};

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
  },
  content: {
    flex: "1 1 0",
    overflowY: "auto",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    display: "flex",
    flexDirection: "column",
    gap: "0px",
  },
  divider: {
    height: "1px",
    backgroundColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    marginTop: "25px",
    marginBottom: "25px",
  },
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  sectionTitleRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  sectionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  fieldRow: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  sliderRow: {
    display: "flex",
    flexDirection: "column",
    gap: "2px",
  },
  sliderLabelRow: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  sliderValue: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    minWidth: "24px",
    textAlign: "right" as const,
  },
  scoreCircle: {
    width: "36px",
    height: "36px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    fontWeight: tokens.fontWeightBold,
    fontSize: tokens.fontSizeBase300,
    flexShrink: 0,
  },
  infoPopover: {
    maxWidth: "320px",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },
  infoSection: {
    display: "flex",
    flexDirection: "column",
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
    display: "flex",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  emptyState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    color: tokens.colorNeutralForeground4,
    paddingTop: tokens.spacingVerticalXXXL,
  },
  loadingState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: "1 1 0",
    paddingTop: tokens.spacingVerticalXXXL,
  },
  errorBanner: {
    flexShrink: 0,
  },
  assignedToDisplay: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  assignedToName: {
    flex: "1 1 0",
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
  },
  recordLink: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    cursor: "pointer",
  },
  completedBtn: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ":hover": {
      backgroundColor: tokens.colorPaletteGreenForeground2,
    },
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITodoDetailProps {
  record: ITodoRecord | null;
  /** sprk_eventtodo extension record (notes, completed, statuscode). */
  todoExtension: ITodoExtension | null;
  isLoading: boolean;
  error: string | null;
  /** Save event fields (sprk_event). */
  onSaveEventFields: (
    eventId: string,
    fields: IEventFieldUpdates
  ) => Promise<{ success: boolean; error?: string }>;
  /** Save todo extension fields (sprk_eventtodo). */
  onSaveTodoExtFields: (
    todoId: string,
    fields: ITodoExtensionUpdates
  ) => Promise<{ success: boolean; error?: string }>;
  /** Remove from To Do (sets sprk_todoflag=false, then closes pane). */
  onRemoveTodo?: (eventId: string) => Promise<void>;
  /** Close the side pane. */
  onClose?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoDetail: React.FC<ITodoDetailProps> = React.memo(
  ({
    record,
    todoExtension,
    isLoading,
    error,
    onSaveEventFields,
    onSaveTodoExtFields,
    onRemoveTodo,
    onClose,
  }) => {
    const styles = useStyles();

    // Auto-expand textarea refs
    const textareaRef = React.useRef<HTMLTextAreaElement | null>(null);
    const notesTextareaRef = React.useRef<HTMLTextAreaElement | null>(null);

    // Editable field values (sprk_event fields)
    const [description, setDescription] = React.useState("");
    const [dueDate, setDueDate] = React.useState("");
    const [priority, setPriority] = React.useState<number>(50);
    const [effort, setEffort] = React.useState<number>(50);

    // Editable field value (sprk_eventtodo field)
    const [toDoNotes, setToDoNotes] = React.useState("");

    // Assigned To state
    const [assignedToId, setAssignedToId] = React.useState<string | null>(null);
    const [assignedToName, setAssignedToName] = React.useState("");
    const [contactQuery, setContactQuery] = React.useState("");
    const [contactOptions, setContactOptions] = React.useState<IContactOption[]>([]);
    const [isSearching, setIsSearching] = React.useState(false);
    const [isEditingAssignedTo, setIsEditingAssignedTo] = React.useState(false);

    // Save state
    const [isSaving, setIsSaving] = React.useState(false);
    const [isRemoving, setIsRemoving] = React.useState(false);
    const [isCompleting, setIsCompleting] = React.useState(false);
    const [saveError, setSaveError] = React.useState<string | null>(null);

    // Snapshot of original values (for dirty detection)
    const origRef = React.useRef({
      description: "",
      dueDate: "",
      priority: 50,
      effort: 50,
      assignedToId: null as string | null,
      toDoNotes: "",
    });

    // Reset when record changes
    React.useEffect(() => {
      if (record) {
        const desc = record.sprk_description ?? "";
        const dd = toDateInputValue(record.sprk_duedate);
        const pri = record.sprk_priorityscore ?? 50;
        const eff = record.sprk_effortscore ?? 50;
        const aId = record._sprk_assignedto_value ?? null;
        const aName =
          record[
            "_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"
          ] ?? "";
        setDescription(desc);
        setDueDate(dd);
        setPriority(pri);
        setEffort(eff);
        setAssignedToId(aId);
        setAssignedToName(aName);
        setContactQuery("");
        setContactOptions([]);
        setIsEditingAssignedTo(false);
        setSaveError(null);
        origRef.current = {
          ...origRef.current,
          description: desc,
          dueDate: dd,
          priority: pri,
          effort: eff,
          assignedToId: aId,
        };
      }
    }, [record?.sprk_eventid]); // eslint-disable-line react-hooks/exhaustive-deps

    // Reset notes when todoExtension changes
    React.useEffect(() => {
      const notes = todoExtension?.sprk_todonotes ?? "";
      setToDoNotes(notes);
      origRef.current = { ...origRef.current, toDoNotes: notes };
    }, [todoExtension?.sprk_eventtodoid]); // eslint-disable-line react-hooks/exhaustive-deps

    // Dirty detection
    const isEventDirty =
      description !== origRef.current.description ||
      dueDate !== origRef.current.dueDate ||
      priority !== origRef.current.priority ||
      effort !== origRef.current.effort ||
      assignedToId !== origRef.current.assignedToId;

    const isNotesDirty = toDoNotes !== origRef.current.toDoNotes;

    const isDirty = isEventDirty || isNotesDirty;

    // --- Handlers ---

    const handleDescriptionChange = React.useCallback(
      (_ev: unknown, data: { value: string }) => {
        setDescription(data.value);
        requestAnimationFrame(() => {
          const el = textareaRef.current;
          if (!el) return;
          el.style.height = "auto";
          el.style.height = `${el.scrollHeight}px`;
          el.style.overflowY = "hidden";
        });
      },
      []
    );

    // Auto-resize description textarea on initial load
    React.useEffect(() => {
      const el = textareaRef.current;
      if (!el) return;
      el.style.height = "auto";
      el.style.height = `${el.scrollHeight}px`;
      el.style.overflowY = "hidden";
    }, [description]);

    const handleNotesChange = React.useCallback(
      (_ev: unknown, data: { value: string }) => {
        setToDoNotes(data.value);
        requestAnimationFrame(() => {
          const el = notesTextareaRef.current;
          if (!el) return;
          el.style.height = "auto";
          el.style.height = `${el.scrollHeight}px`;
          el.style.overflowY = "hidden";
        });
      },
      []
    );

    // Auto-resize notes textarea on initial load
    React.useEffect(() => {
      const el = notesTextareaRef.current;
      if (!el) return;
      el.style.height = "auto";
      el.style.height = `${el.scrollHeight}px`;
      el.style.overflowY = "hidden";
    }, [toDoNotes]);

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

    // Debounced contact search
    const searchTimerRef = React.useRef<ReturnType<typeof setTimeout>>();
    const handleContactInput: ComboboxProps["onInput"] = React.useCallback(
      (ev: React.ChangeEvent<HTMLInputElement>) => {
        const q = ev.target.value;
        setContactQuery(q);
        clearTimeout(searchTimerRef.current);
        if (q.length < 2) {
          setContactOptions([]);
          return;
        }
        setIsSearching(true);
        searchTimerRef.current = setTimeout(async () => {
          const results = await searchContacts(q);
          setContactOptions(results);
          setIsSearching(false);
        }, 300);
      },
      []
    );

    const handleContactSelect: ComboboxProps["onOptionSelect"] = React.useCallback(
      (_ev, data) => {
        if (data.optionValue && data.optionText) {
          setAssignedToId(data.optionValue);
          setAssignedToName(data.optionText);
          setContactQuery("");
          setContactOptions([]);
          setIsEditingAssignedTo(false);
        }
      },
      []
    );

    // Save dirty fields to the correct entities
    const handleSave = React.useCallback(async () => {
      if (!record || !isDirty) return;
      setIsSaving(true);
      setSaveError(null);

      try {
        // Save event fields if any changed
        if (isEventDirty) {
          const eventUpdates: IEventFieldUpdates = {};
          if (description !== origRef.current.description) {
            eventUpdates.sprk_description = description;
          }
          if (dueDate !== origRef.current.dueDate) {
            eventUpdates.sprk_duedate = dueDate || null;
          }
          if (priority !== origRef.current.priority) {
            eventUpdates.sprk_priorityscore = priority;
          }
          if (effort !== origRef.current.effort) {
            eventUpdates.sprk_effortscore = effort;
          }
          if (assignedToId !== origRef.current.assignedToId) {
            eventUpdates["sprk_AssignedTo@odata.bind"] = assignedToId
              ? `/contacts(${assignedToId})`
              : null;
          }
          const eventResult = await onSaveEventFields(record.sprk_eventid, eventUpdates);
          if (!eventResult.success) {
            setSaveError(eventResult.error ?? "Failed to save event fields");
            setIsSaving(false);
            return;
          }
        }

        // Save notes if changed (requires todoExtension record)
        if (isNotesDirty && todoExtension?.sprk_eventtodoid) {
          const extUpdates: ITodoExtensionUpdates = {
            sprk_todonotes: toDoNotes,
          };
          const extResult = await onSaveTodoExtFields(
            todoExtension.sprk_eventtodoid,
            extUpdates
          );
          if (!extResult.success) {
            setSaveError(extResult.error ?? "Failed to save notes");
            setIsSaving(false);
            return;
          }
        }

        // Update snapshots on success
        origRef.current = {
          description,
          dueDate,
          priority,
          effort,
          assignedToId,
          toDoNotes,
        };
      } catch {
        setSaveError("Save failed — unexpected error");
      } finally {
        setIsSaving(false);
      }
    }, [
      record,
      todoExtension,
      isDirty,
      isEventDirty,
      isNotesDirty,
      description,
      dueDate,
      priority,
      effort,
      assignedToId,
      toDoNotes,
      onSaveEventFields,
      onSaveTodoExtFields,
    ]);

    // Remove from To Do: sets sprk_todoflag = false, notifies Kanban, closes pane
    const handleRemoveTodo = React.useCallback(async () => {
      if (!record || !onRemoveTodo) return;
      setIsRemoving(true);
      setSaveError(null);
      try {
        await onRemoveTodo(record.sprk_eventid);
      } catch {
        setSaveError("Failed to remove from To Do");
        setIsRemoving(false);
      }
    }, [record, onRemoveTodo]);

    // Completed: saves dirty fields + marks sprk_eventtodo as completed
    const handleCompleted = React.useCallback(async () => {
      if (!record) return;
      setIsCompleting(true);
      setSaveError(null);

      try {
        // Save any dirty event fields first
        if (isEventDirty) {
          const eventUpdates: IEventFieldUpdates = {};
          if (description !== origRef.current.description) {
            eventUpdates.sprk_description = description;
          }
          if (dueDate !== origRef.current.dueDate) {
            eventUpdates.sprk_duedate = dueDate || null;
          }
          if (priority !== origRef.current.priority) {
            eventUpdates.sprk_priorityscore = priority;
          }
          if (effort !== origRef.current.effort) {
            eventUpdates.sprk_effortscore = effort;
          }
          if (assignedToId !== origRef.current.assignedToId) {
            eventUpdates["sprk_AssignedTo@odata.bind"] = assignedToId
              ? `/contacts(${assignedToId})`
              : null;
          }
          const eventResult = await onSaveEventFields(record.sprk_eventid, eventUpdates);
          if (!eventResult.success) {
            setSaveError(eventResult.error ?? "Failed to save event fields");
            setIsCompleting(false);
            return;
          }
        }

        // Mark as completed on sprk_eventtodo
        if (todoExtension?.sprk_eventtodoid) {
          const extUpdates: ITodoExtensionUpdates = {
            sprk_completed: true,
            sprk_completeddate: new Date().toISOString(),
            statuscode: 2,
          };
          // Also save notes if dirty
          if (isNotesDirty) {
            extUpdates.sprk_todonotes = toDoNotes;
          }
          const extResult = await onSaveTodoExtFields(
            todoExtension.sprk_eventtodoid,
            extUpdates
          );
          if (!extResult.success) {
            setSaveError(extResult.error ?? "Failed to mark as completed");
            setIsCompleting(false);
            return;
          }
        }

        onClose?.();
      } catch {
        setSaveError("Failed to mark as completed — unexpected error");
      } finally {
        setIsCompleting(false);
      }
    }, [
      record,
      todoExtension,
      isEventDirty,
      isNotesDirty,
      description,
      dueDate,
      priority,
      effort,
      assignedToId,
      toDoNotes,
      onSaveEventFields,
      onSaveTodoExtFields,
      onClose,
    ]);

    // Open regarding record in a new browser tab
    const handleOpenRegardingRecord = React.useCallback(() => {
      if (!record?.sprk_regardingrecordid) return;
      const typeName =
        record[
          "_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"
        ] ?? "";
      const entityName = RECORD_TYPE_ENTITY_MAP[typeName];
      if (!entityName) return;

      const xrm = getXrm();
      if (xrm?.Navigation) {
        xrm.Navigation.navigateTo(
          {
            pageType: "entityrecord",
            entityName,
            entityId: record.sprk_regardingrecordid,
          },
          { target: 1 } // 1 = new window/tab
        ).catch(() => {
          // Fallback: open via URL
        });
      }
    }, [record]);

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
          <Text>No event selected</Text>
        </div>
      );
    }

    // Compute score from CURRENT field values (live preview)
    const score = computeScore(priority, effort, dueDate || record.sprk_duedate);

    return (
      <div className={styles.container}>
        <div className={styles.content}>
          {/* Save error banner */}
          {saveError && (
            <MessageBar intent="error" className={styles.errorBanner}>
              <MessageBarBody>{saveError}</MessageBarBody>
            </MessageBar>
          )}

          {/* ── Description (top) ──────────────────────────────────────── */}
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
                style: { minHeight: "160px" },
              }}
            />
          </div>

          <div className={styles.divider} role="separator" />

          {/* ── Details ────────────────────────────────────────────────── */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Details
            </Text>

            {/* Record Type tag */}
            {record["_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"] && (
              <div className={styles.fieldRow}>
                <label className={styles.fieldLabel}>Record Type</label>
                <div>
                  <Badge
                    appearance="filled"
                    color="informative"
                    size="medium"
                  >
                    {record["_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue"]}
                  </Badge>
                </div>
              </div>
            )}

            {/* Record link */}
            {record.sprk_regardingrecordname && record.sprk_regardingrecordid && (
              <div className={styles.fieldRow}>
                <label className={styles.fieldLabel}>Record</label>
                <Link
                  className={styles.recordLink}
                  onClick={handleOpenRegardingRecord}
                  as="button"
                >
                  {record.sprk_regardingrecordname}
                  <OpenRegular style={{ fontSize: "12px" }} />
                </Link>
              </div>
            )}

            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Due Date</label>
              <Input
                type="date"
                value={dueDate}
                onChange={handleDueDateChange}
              />
            </div>

            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Assigned To</label>
              {assignedToName && !isEditingAssignedTo ? (
                <div className={styles.assignedToDisplay}>
                  <Text className={styles.assignedToName}>{assignedToName}</Text>
                  <Button
                    appearance="subtle"
                    size="small"
                    onClick={() => setIsEditingAssignedTo(true)}
                  >
                    Change
                  </Button>
                </div>
              ) : (
                <Combobox
                  freeform
                  placeholder="Search contacts..."
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
                      No contacts found
                    </Option>
                  )}
                  {contactOptions.map((c) => (
                    <Option key={c.id} value={c.id} text={c.name}>
                      {c.name}
                    </Option>
                  ))}
                </Combobox>
              )}
            </div>
          </div>

          <div className={styles.divider} role="separator" />

          {/* ── To Do Notes (from sprk_eventtodo) ──────────────────────── */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              To Do Notes
            </Text>
            <Textarea
              value={toDoNotes}
              onChange={handleNotesChange}
              placeholder="Add notes..."
              resize="none"
              textarea={{
                ref: notesTextareaRef,
                style: { minHeight: "160px" },
              }}
            />
          </div>

          <div className={styles.divider} role="separator" />

          {/* ── To Do Score: title row with circle + info, then sliders ── */}
          <div className={styles.section}>
            <div className={styles.sectionTitleRow}>
              <Text className={styles.sectionTitle} size={300}>
                To Do Score
              </Text>
              <div className={styles.scoreCircle}>
                {Math.round(score.todoScore)}
              </div>
              <Popover withArrow>
                <PopoverTrigger disableButtonEnhancement>
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<InfoRegular />}
                    aria-label="Score information"
                  />
                </PopoverTrigger>
                <PopoverSurface>
                  <div className={styles.infoPopover}>
                    <div className={styles.infoSection}>
                      <Text className={styles.infoSectionTitle} size={300}>
                        How Scoring Works
                      </Text>
                      <Text className={styles.infoSectionBody}>
                        The To Do Score combines three factors into a single 0-100
                        number. Higher scores surface more important items first in
                        the Kanban board.
                      </Text>
                    </div>

                    <div className={styles.infoSection}>
                      <Text className={styles.infoSectionTitle} size={300}>
                        Score Formula
                      </Text>
                      <Text className={styles.infoSectionBody}>
                        Score = Priority (50%) + Inverted Effort (20%) + Urgency (30%).
                        Lower effort items score higher (quick wins bubble up).
                      </Text>
                    </div>

                    <div className={styles.infoSection}>
                      <Text className={styles.infoSectionTitle} size={300}>
                        Urgency Score
                      </Text>
                      <Text className={styles.infoSectionBody}>
                        Auto-calculated from due date: Overdue = 100, within 3 days = 80,
                        within 7 days = 50, within 10 days = 25, more than 10 days = 0.
                      </Text>
                    </div>
                  </div>
                </PopoverSurface>
              </Popover>
            </div>

            <div className={styles.sliderRow}>
              <div className={styles.sliderLabelRow}>
                <label className={styles.fieldLabel}>Priority (50%)</label>
                <span className={styles.sliderValue}>{priority}</span>
              </div>
              <Slider
                value={priority}
                onChange={handlePriorityChange}
                min={0}
                max={100}
                step={5}
              />
            </div>

            <div className={styles.sliderRow}>
              <div className={styles.sliderLabelRow}>
                <label className={styles.fieldLabel}>Effort (20%)</label>
                <span className={styles.sliderValue}>{effort}</span>
              </div>
              <Slider
                value={effort}
                onChange={handleEffortChange}
                min={0}
                max={100}
                step={5}
              />
            </div>

          </div>
        </div>

        {/* ── Sticky footer ────────────────────────────────────────────── */}
        <div className={styles.footer}>
          {onRemoveTodo && (
            <Button
              appearance="subtle"
              icon={<DeleteRegular />}
              onClick={handleRemoveTodo}
              disabled={isRemoving || isSaving || isCompleting}
              style={{ color: tokens.colorPaletteRedForeground1, marginRight: "auto" }}
            >
              {isRemoving ? "Removing..." : "Remove"}
            </Button>
          )}
          <Button
            appearance="primary"
            icon={<SaveRegular />}
            onClick={handleSave}
            disabled={!isDirty || isSaving || isCompleting}
          >
            {isSaving ? "Saving..." : "Save"}
          </Button>
          <Button
            icon={<CheckmarkRegular />}
            onClick={handleCompleted}
            disabled={isSaving || isCompleting}
            className={styles.completedBtn}
          >
            {isCompleting ? "Completing..." : "Completed"}
          </Button>
        </div>
      </div>
    );
  }
);

TodoDetail.displayName = "TodoDetail";
