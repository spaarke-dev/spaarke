/**
 * TodoDetail — Main content component for the To Do Detail side pane.
 *
 * Layout (top to bottom):
 *   1. Description (editable, auto-expands up to 15 lines, then scrolls)
 *   2. Details: Due Date, Assigned To
 *   3. Status: Completed (boolean), Completed Date
 *   4. To Do Score section:
 *      - Title row with score circle + info button (Popover)
 *      - Priority Score slider (editable)
 *      - Effort Score slider (editable)
 *      - Urgency Score slider (read-only, computed from due date)
 *   5. Sticky footer: Save + Save & Close buttons
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
  Switch,
  Button,
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
  SwitchOnChangeData,
} from "@fluentui/react-components";
import { SaveRegular, DismissRegular, InfoRegular } from "@fluentui/react-icons";
import { ITodoRecord } from "../types/TodoRecord";
import { searchContacts } from "../services/todoService";
import type { ITodoFieldUpdates, IContactOption } from "../services/todoService";

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
    marginTop: "10px",
    marginBottom: "10px",
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
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITodoDetailProps {
  record: ITodoRecord | null;
  isLoading: boolean;
  error: string | null;
  onSaveFields: (
    eventId: string,
    fields: ITodoFieldUpdates
  ) => Promise<{ success: boolean; error?: string }>;
  /** Close the side pane (called after Save & Close). */
  onClose?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoDetail: React.FC<ITodoDetailProps> = React.memo(
  ({ record, isLoading, error, onSaveFields, onClose }) => {
    const styles = useStyles();

    // Auto-expand textarea ref (grows up to 15 lines, then scrolls)
    const textareaRef = React.useRef<HTMLTextAreaElement | null>(null);

    // Editable field values
    const [description, setDescription] = React.useState("");
    const [dueDate, setDueDate] = React.useState("");
    const [priority, setPriority] = React.useState<number>(50);
    const [effort, setEffort] = React.useState<number>(50);
    const [completed, setCompleted] = React.useState(false);
    const [completedDate, setCompletedDate] = React.useState("");

    // Assigned To state
    const [assignedToId, setAssignedToId] = React.useState<string | null>(null);
    const [assignedToName, setAssignedToName] = React.useState("");
    const [contactQuery, setContactQuery] = React.useState("");
    const [contactOptions, setContactOptions] = React.useState<IContactOption[]>([]);
    const [isSearching, setIsSearching] = React.useState(false);
    const [isEditingAssignedTo, setIsEditingAssignedTo] = React.useState(false);

    // Save state
    const [isSaving, setIsSaving] = React.useState(false);
    const [saveError, setSaveError] = React.useState<string | null>(null);

    // Snapshot of original values (for dirty detection)
    const origRef = React.useRef({
      description: "",
      dueDate: "",
      priority: 50,
      effort: 50,
      completed: false,
      completedDate: "",
      assignedToId: null as string | null,
    });

    // Reset when record changes
    React.useEffect(() => {
      if (record) {
        const desc = record.sprk_description ?? "";
        const dd = toDateInputValue(record.sprk_duedate);
        const pri = record.sprk_priorityscore ?? 50;
        const eff = record.sprk_effortscore ?? 50;
        const comp = record.sprk_completed ?? false;
        const compDate = toDateInputValue(record.sprk_completedate);
        const aId = record._sprk_assignedto_value ?? null;
        const aName =
          record[
            "_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"
          ] ?? "";
        setDescription(desc);
        setDueDate(dd);
        setPriority(pri);
        setEffort(eff);
        setCompleted(comp);
        setCompletedDate(compDate);
        setAssignedToId(aId);
        setAssignedToName(aName);
        setContactQuery("");
        setContactOptions([]);
        setIsEditingAssignedTo(false);
        setSaveError(null);
        origRef.current = {
          description: desc,
          dueDate: dd,
          priority: pri,
          effort: eff,
          completed: comp,
          completedDate: compDate,
          assignedToId: aId,
        };
      }
    }, [record?.sprk_eventid]); // eslint-disable-line react-hooks/exhaustive-deps

    // Dirty detection
    const isDirty =
      description !== origRef.current.description ||
      dueDate !== origRef.current.dueDate ||
      priority !== origRef.current.priority ||
      effort !== origRef.current.effort ||
      completed !== origRef.current.completed ||
      completedDate !== origRef.current.completedDate ||
      assignedToId !== origRef.current.assignedToId;

    // --- Handlers ---

    const handleDescriptionChange = React.useCallback(
      (_ev: unknown, data: { value: string }) => {
        setDescription(data.value);
        // Auto-resize after React updates the DOM
        requestAnimationFrame(() => {
          const el = textareaRef.current;
          if (!el) return;
          el.style.height = "auto";
          const maxH = 300;
          el.style.height = `${Math.min(el.scrollHeight, maxH)}px`;
          el.style.overflowY = el.scrollHeight > maxH ? "auto" : "hidden";
        });
      },
      []
    );

    // Auto-resize textarea on initial load
    React.useEffect(() => {
      const el = textareaRef.current;
      if (!el) return;
      el.style.height = "auto";
      const maxH = 300;
      el.style.height = `${Math.min(el.scrollHeight, maxH)}px`;
      el.style.overflowY = el.scrollHeight > maxH ? "auto" : "hidden";
    }, [description]);

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

    const handleCompletedChange = React.useCallback(
      (_ev: React.ChangeEvent<HTMLInputElement>, data: SwitchOnChangeData) => {
        setCompleted(data.checked);
        if (data.checked && !completedDate) {
          setCompletedDate(new Date().toISOString().split("T")[0]);
        }
      },
      [completedDate]
    );

    const handleCompletedDateChange = React.useCallback(
      (ev: React.ChangeEvent<HTMLInputElement>) => setCompletedDate(ev.target.value),
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

    // Save all dirty fields
    const handleSave = React.useCallback(async () => {
      if (!record || !isDirty) return;
      setIsSaving(true);
      setSaveError(null);

      const updates: ITodoFieldUpdates = {};
      if (description !== origRef.current.description) {
        updates.sprk_description = description;
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
      if (completed !== origRef.current.completed) {
        updates.sprk_completed = completed;
      }
      if (completedDate !== origRef.current.completedDate) {
        updates.sprk_completedate = completedDate || null;
      }
      if (assignedToId !== origRef.current.assignedToId) {
        updates["sprk_AssignedTo@odata.bind"] = assignedToId
          ? `/sprk_contacts(${assignedToId})`
          : null;
      }

      try {
        const result = await onSaveFields(record.sprk_eventid, updates);
        if (result.success) {
          origRef.current = {
            description,
            dueDate,
            priority,
            effort,
            completed,
            completedDate,
            assignedToId,
          };
        } else {
          setSaveError(result.error ?? "Save failed");
        }
      } catch {
        setSaveError("Save failed — unexpected error");
      } finally {
        setIsSaving(false);
      }
    }, [
      record,
      isDirty,
      description,
      dueDate,
      priority,
      effort,
      completed,
      completedDate,
      assignedToId,
      onSaveFields,
    ]);

    // Save & Close: save dirty fields then close the pane
    const handleSaveAndClose = React.useCallback(async () => {
      if (record && isDirty) {
        await handleSave();
      }
      onClose?.();
    }, [record, isDirty, handleSave, onClose]);

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
                style: { minHeight: "60px", maxHeight: "300px" },
              }}
            />
          </div>

          <div className={styles.divider} role="separator" />

          {/* ── Details: Due Date + Assigned To ────────────────────────── */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Details
            </Text>

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

          {/* ── Status: Completed + Completed Date ─────────────────────── */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Status
            </Text>

            <div className={styles.fieldRow}>
              <Switch
                checked={completed}
                onChange={handleCompletedChange}
                label="Completed"
              />
            </div>

            {completed && (
              <div className={styles.fieldRow}>
                <label className={styles.fieldLabel}>Completed Date</label>
                <Input
                  type="date"
                  value={completedDate}
                  onChange={handleCompletedDateChange}
                />
              </div>
            )}
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

            <div className={styles.sliderRow}>
              <div className={styles.sliderLabelRow}>
                <label className={styles.fieldLabel}>Urgency (30%)</label>
                <span className={styles.sliderValue}>{score.urgencyRaw}</span>
              </div>
              <Slider
                value={score.urgencyRaw}
                min={0}
                max={100}
                disabled
              />
            </div>
          </div>
        </div>

        {/* ── Sticky footer: Save + Save & Close ──────────────────────── */}
        <div className={styles.footer}>
          <Button
            appearance="secondary"
            icon={<DismissRegular />}
            onClick={handleSaveAndClose}
            disabled={isSaving}
          >
            {isDirty ? "Save & Close" : "Close"}
          </Button>
          <Button
            appearance="primary"
            icon={<SaveRegular />}
            onClick={handleSave}
            disabled={!isDirty || isSaving}
          >
            {isSaving ? "Saving..." : "Save"}
          </Button>
        </div>
      </div>
    );
  }
);

TodoDetail.displayName = "TodoDetail";
