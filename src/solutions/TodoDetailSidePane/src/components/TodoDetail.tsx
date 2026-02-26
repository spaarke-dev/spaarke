/**
 * TodoDetail — Main content component for the To Do Detail side pane.
 *
 * Shows event name, editable description, score breakdown, badges, and action buttons.
 * All colours from Fluent UI v9 semantic tokens (ADR-021).
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Textarea,
  Button,
  Divider,
  Spinner,
} from "@fluentui/react-components";
import {
  EditRegular,
  MailRegular,
  ChatRegular,
  SparkleRegular,
} from "@fluentui/react-icons";
import { ITodoRecord } from "../types/TodoRecord";
import { openEventForm } from "../services/sidePaneService";

// ---------------------------------------------------------------------------
// To Do Score computation (self-contained — no cross-solution imports)
// ---------------------------------------------------------------------------

function computeScore(record: ITodoRecord): {
  todoScore: number;
  priorityComponent: number;
  effortComponent: number;
  urgencyComponent: number;
} {
  const priority = record.sprk_priorityscore ?? 0;
  const effort = record.sprk_effortscore ?? 0;
  const invertedEffort = 100 - effort;

  let urgencyRaw = 0;
  if (record.sprk_duedate) {
    const due = new Date(record.sprk_duedate);
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

function formatDueDate(dateStr: string): string {
  const date = new Date(dateStr);
  const now = new Date();
  const sameYear = date.getFullYear() === now.getFullYear();
  return date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    ...(sameYear ? {} : { year: "numeric" }),
  });
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
  descriptionActions: {
    display: "flex",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalS,
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
  actionsRow: {
    display: "flex",
    flexDirection: "row",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
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
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITodoDetailProps {
  record: ITodoRecord | null;
  isLoading: boolean;
  error: string | null;
  onSaveDescription: (eventId: string, description: string) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoDetail: React.FC<ITodoDetailProps> = React.memo(
  ({ record, isLoading, error, onSaveDescription }) => {
    const styles = useStyles();

    // Description editing state
    const [descriptionValue, setDescriptionValue] = React.useState("");
    const [isDirty, setIsDirty] = React.useState(false);
    const [isSaving, setIsSaving] = React.useState(false);

    // Reset when record changes
    React.useEffect(() => {
      if (record) {
        setDescriptionValue(record.sprk_description ?? "");
        setIsDirty(false);
        setIsSaving(false);
      }
    }, [record?.sprk_eventid]); // eslint-disable-line react-hooks/exhaustive-deps

    const handleDescriptionChange = React.useCallback(
      (_ev: unknown, data: { value: string }) => {
        setDescriptionValue(data.value);
        setIsDirty(data.value !== (record?.sprk_description ?? ""));
      },
      [record?.sprk_description]
    );

    const handleSave = React.useCallback(async () => {
      if (!record || !isDirty) return;
      setIsSaving(true);
      try {
        await onSaveDescription(record.sprk_eventid, descriptionValue);
        setIsDirty(false);
      } finally {
        setIsSaving(false);
      }
    }, [record, isDirty, descriptionValue, onSaveDescription]);

    const handleEdit = React.useCallback(() => {
      if (record) openEventForm(record.sprk_eventid);
    }, [record]);

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

    // Compute values
    const score = computeScore(record);
    const assignedTo = record["_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"] ?? null;
    const dueDateFormatted = record.sprk_duedate ? formatDueDate(record.sprk_duedate) : null;

    return (
      <div className={styles.container}>
        <div className={styles.content}>
          {/* Description section */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>Description</Text>
            <Textarea
              value={descriptionValue}
              onChange={handleDescriptionChange}
              placeholder="Add a description..."
              resize="vertical"
              rows={4}
            />
            {isDirty && (
              <div className={styles.descriptionActions}>
                <Button
                  appearance="primary"
                  size="small"
                  onClick={handleSave}
                  disabled={isSaving}
                >
                  {isSaving ? "Saving..." : "Save"}
                </Button>
              </div>
            )}
          </div>

          <Divider />

          {/* Details section */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>Details</Text>
            {dueDateFormatted && (
              <div className={styles.detailRow}>
                <span className={styles.detailLabel}>Due Date</span>
                <span className={styles.detailValue}>{dueDateFormatted}</span>
              </div>
            )}
            {assignedTo && (
              <div className={styles.detailRow}>
                <span className={styles.detailLabel}>Assigned To</span>
                <span className={styles.detailValue}>{assignedTo}</span>
              </div>
            )}
            {record.sprk_priorityscore != null && (
              <div className={styles.detailRow}>
                <span className={styles.detailLabel}>Priority Score</span>
                <span className={styles.detailValue}>{record.sprk_priorityscore}</span>
              </div>
            )}
            {record.sprk_effortscore != null && (
              <div className={styles.detailRow}>
                <span className={styles.detailLabel}>Effort Score</span>
                <span className={styles.detailValue}>{record.sprk_effortscore}</span>
              </div>
            )}
          </div>

          <Divider />

          {/* To Do Score breakdown */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>To Do Score</Text>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Priority (50%)</span>
              <span className={styles.detailValue}>{score.priorityComponent.toFixed(1)}</span>
            </div>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Effort (20%)</span>
              <span className={styles.detailValue}>{score.effortComponent.toFixed(1)}</span>
            </div>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Urgency (30%)</span>
              <span className={styles.detailValue}>{score.urgencyComponent.toFixed(1)}</span>
            </div>
            <div className={styles.detailRow}>
              <span className={styles.detailLabel}>Total</span>
              <span className={`${styles.detailValue} ${styles.scoreTotal}`}>
                {Math.round(score.todoScore)}
              </span>
            </div>
          </div>

          <Divider />

          {/* Actions */}
          <div className={styles.section}>
            <Text className={styles.sectionTitle} size={300}>Actions</Text>
            <div className={styles.actionsRow}>
              <Button
                appearance="subtle"
                size="small"
                icon={<EditRegular />}
                onClick={handleEdit}
              >
                Edit
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<MailRegular />}
                onClick={() => console.info("[TodoDetail] Email action — stub")}
              >
                Email
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<ChatRegular />}
                onClick={() => console.info("[TodoDetail] Teams action — stub")}
              >
                Teams
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<SparkleRegular />}
                onClick={() => console.info("[TodoDetail] AI action — stub")}
              >
                AI
              </Button>
            </div>
          </div>
        </div>
      </div>
    );
  }
);

TodoDetail.displayName = "TodoDetail";
