/**
 * TodoDetail — Main content component for the To Do Detail side pane.
 *
 * Editable fields: Description, Due Date, Priority Score, Effort Score.
 * Read-only: Assigned To (lookup field — edit via form).
 * Score breakdown computed live from current field values.
 * Single Save button persists all dirty fields at once.
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
  SpinButton,
  Button,
  Divider,
  Spinner,
  MessageBar,
  MessageBarBody,
} from "@fluentui/react-components";
import type { SpinButtonChangeEvent, SpinButtonOnChangeData } from "@fluentui/react-components";
import { SaveRegular } from "@fluentui/react-icons";
import { ITodoRecord } from "../types/TodoRecord";
import type { ITodoFieldUpdates } from "../services/todoService";

// ---------------------------------------------------------------------------
// To Do Score computation (self-contained — no cross-solution imports)
// ---------------------------------------------------------------------------

function computeScore(
  priority: number,
  effort: number,
  duedate: string | null | undefined
): {
  todoScore: number;
  priorityComponent: number;
  effortComponent: number;
  urgencyComponent: number;
} {
  const invertedEffort = 100 - effort;

  let urgencyRaw = 0;
  if (duedate) {
    const due = new Date(duedate);
    const now = new Date();
    const diffMs = due.getTime() - now.getTime();
    const diffDays = diffMs / (1000 * 60 * 60 * 24);
    if (diffDays < 0) urgencyRaw = 100;
    else if (diffDays <= 3) urgencyRaw = 80;
    else if (diffDays <= 7) urgencyRaw = 50;
    else if (diffDays <= 10) urgencyRaw = 25;
  }

  const priorityComponent = priority * 0.5;
  const effortComponent = invertedEffort * 0.2;
  const urgencyComponent = urgencyRaw * 0.3;
  const todoScore = Math.max(0, Math.min(100, priorityComponent + effortComponent + urgencyComponent));

  return { todoScore, priorityComponent, effortComponent, urgencyComponent };
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
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
  },
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  fieldRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  readOnlyValue: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
  },
  detailRow: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  detailLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  detailValue: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
  },
  scoreTotal: {
    fontWeight: tokens.fontWeightBold,
    color: tokens.colorBrandForeground1,
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    gap: tokens.spacingHorizontalS,
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
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoDetail: React.FC<ITodoDetailProps> = React.memo(
  ({ record, isLoading, error, onSaveFields }) => {
    const styles = useStyles();

    // Editable field values
    const [description, setDescription] = React.useState("");
    const [dueDate, setDueDate] = React.useState("");
    const [priority, setPriority] = React.useState<number>(0);
    const [effort, setEffort] = React.useState<number>(0);

    // Save state
    const [isSaving, setIsSaving] = React.useState(false);
    const [saveError, setSaveError] = React.useState<string | null>(null);

    // Snapshot of original values (for dirty detection)
    const origRef = React.useRef({ description: "", dueDate: "", priority: 0, effort: 0 });

    // Reset when record changes
    React.useEffect(() => {
      if (record) {
        const desc = record.sprk_description ?? "";
        const dd = toDateInputValue(record.sprk_duedate);
        const pri = record.sprk_priorityscore ?? 0;
        const eff = record.sprk_effortscore ?? 0;
        setDescription(desc);
        setDueDate(dd);
        setPriority(pri);
        setEffort(eff);
        setSaveError(null);
        origRef.current = { description: desc, dueDate: dd, priority: pri, effort: eff };
      }
    }, [record?.sprk_eventid]); // eslint-disable-line react-hooks/exhaustive-deps

    // Dirty detection
    const isDirty =
      description !== origRef.current.description ||
      dueDate !== origRef.current.dueDate ||
      priority !== origRef.current.priority ||
      effort !== origRef.current.effort;

    // Handlers
    const handleDescriptionChange = React.useCallback(
      (_ev: unknown, data: { value: string }) => setDescription(data.value),
      []
    );

    const handleDueDateChange = React.useCallback(
      (ev: React.ChangeEvent<HTMLInputElement>) => setDueDate(ev.target.value),
      []
    );

    const handlePriorityChange = React.useCallback(
      (_ev: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
        const val = data.value ?? data.displayValue ? Number(data.displayValue) : 0;
        setPriority(Math.max(0, Math.min(100, val)));
      },
      []
    );

    const handleEffortChange = React.useCallback(
      (_ev: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
        const val = data.value ?? data.displayValue ? Number(data.displayValue) : 0;
        setEffort(Math.max(0, Math.min(100, val)));
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

      try {
        const result = await onSaveFields(record.sprk_eventid, updates);
        if (result.success) {
          // Update snapshot so dirty detection resets
          origRef.current = { description, dueDate, priority, effort };
        } else {
          setSaveError(result.error ?? "Save failed");
        }
      } catch {
        setSaveError("Save failed — unexpected error");
      } finally {
        setIsSaving(false);
      }
    }, [record, isDirty, description, dueDate, priority, effort, onSaveFields]);

    // Loading state
    if (isLoading) {
      return (
        <div className={styles.loadingState}>
          <Spinner size="medium" label="Loading..." />
        </div>
      );
    }

    // Error state
    if (error) {
      return (
        <div className={styles.emptyState}>
          <Text>{error}</Text>
        </div>
      );
    }

    // Empty state
    if (!record) {
      return (
        <div className={styles.emptyState}>
          <Text>No event selected</Text>
        </div>
      );
    }

    // Compute score from CURRENT field values (live preview as user edits)
    const score = computeScore(priority, effort, dueDate || record.sprk_duedate);
    const assignedTo =
      record["_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"] ?? null;

    return (
      <div className={styles.container}>
        <div className={styles.content}>
          {/* Save error banner */}
          {saveError && (
            <MessageBar intent="error" className={styles.errorBanner}>
              <MessageBarBody>{saveError}</MessageBarBody>
            </MessageBar>
          )}

          {/* Description */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Description
            </Text>
            <Textarea
              value={description}
              onChange={handleDescriptionChange}
              placeholder="Add a description..."
              resize="vertical"
              rows={4}
            />
          </div>

          <Divider />

          {/* Editable details */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              Details
            </Text>

            {/* Due Date */}
            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Due Date</label>
              <Input
                type="date"
                value={dueDate}
                onChange={handleDueDateChange}
                size="small"
              />
            </div>

            {/* Priority Score */}
            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Priority Score</label>
              <SpinButton
                value={priority}
                onChange={handlePriorityChange}
                min={0}
                max={100}
                step={5}
                size="small"
              />
            </div>

            {/* Effort Score */}
            <div className={styles.fieldRow}>
              <label className={styles.fieldLabel}>Effort Score</label>
              <SpinButton
                value={effort}
                onChange={handleEffortChange}
                min={0}
                max={100}
                step={5}
                size="small"
              />
            </div>

            {/* Assigned To — read-only (lookup field) */}
            <div className={styles.fieldRow}>
              <span className={styles.fieldLabel}>Assigned To</span>
              <span className={styles.readOnlyValue}>
                {assignedTo ?? "—"}
              </span>
            </div>
          </div>

          <Divider />

          {/* To Do Score breakdown — live preview from current values */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>
              To Do Score
            </Text>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Priority (50%)</span>
              <span className={styles.detailValue}>
                {score.priorityComponent.toFixed(1)}
              </span>
            </div>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Effort (20%)</span>
              <span className={styles.detailValue}>
                {score.effortComponent.toFixed(1)}
              </span>
            </div>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Urgency (30%)</span>
              <span className={styles.detailValue}>
                {score.urgencyComponent.toFixed(1)}
              </span>
            </div>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Total</span>
              <span className={`${styles.detailValue} ${styles.scoreTotal}`}>
                {Math.round(score.todoScore)}
              </span>
            </div>
          </div>
        </div>

        {/* Sticky footer with Save button */}
        <div className={styles.footer}>
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
