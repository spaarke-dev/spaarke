/**
 * TodoDetailPane — Expandable side pane showing full todo details with
 * editable description field.
 *
 * Layout:
 *   Header: Todo name + close button
 *   Description: Editable textarea with save button (shown when dirty)
 *   Details: Priority, effort, due, and assigned badges
 *   To Do Score: Breakdown showing weighted components
 *   Actions: Edit, Email (stub), Teams (stub), AI Summary (stub)
 *
 * Per R3 FR-29 / OS-1: this pane operates on `sprk_todo` records (not the
 * legacy `sprk_event` + `sprk_todoflag` shape).
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) for layout styles
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Textarea,
  Button,
  Divider,
} from "@fluentui/react-components";
import {
  EditRegular,
  MailRegular,
  ChatRegular,
  SparkleRegular,
} from "@fluentui/react-icons";
import { ITodo } from "../../types/entities";
import { PriorityLevel, EffortLevel } from "../../types/enums";
import { computeTodoScore, ITodoScoreBreakdown } from "../../utils/todoScoreUtils";
import { computeDueLabel, parseDueDate, DueUrgency } from "../../utils/dueLabelUtils";
import { navigateToEntity } from "../../utils/navigation";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ITodoDetailPaneProps {
  /** The todo to display. Null when pane should show empty state. */
  todo: ITodo | null;
  /** Called when description is saved. Parent handles Dataverse write. */
  onSaveDescription: (todoId: string, description: string) => Promise<void>;
  /** Called when close button is clicked. */
  onClose: () => void;
}

// ---------------------------------------------------------------------------
// Badge style maps (using tokens for all colours — zero hardcoded hex)
// ---------------------------------------------------------------------------

const PRIORITY_BADGE_STYLE: Record<PriorityLevel, React.CSSProperties> = {
  Urgent: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  High: {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  Normal: {
    backgroundColor: tokens.colorPaletteBlueBorderActive,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  Low: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
  },
};

const EFFORT_BADGE_STYLE: Record<EffortLevel, React.CSSProperties> = {
  High: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  Med: {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  Low: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForeground1,
  },
};

const DUE_BADGE_STYLE: Record<Exclude<DueUrgency, "none">, React.CSSProperties> = {
  overdue: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  "3d": {
    backgroundColor: tokens.colorPaletteDarkOrangeBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  "7d": {
    backgroundColor: tokens.colorPaletteYellowBackground3,
    color: tokens.colorNeutralForeground1,
  },
  "10d": {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
};

// ---------------------------------------------------------------------------
// Derivation helpers
// ---------------------------------------------------------------------------

/**
 * Map sprk_priorityscore (0-100) to a display label string.
 *
 * Per R3 FR-09: sprk_todo carries a native 0-100 priority score. We bucket
 * for the badge: >=75 Urgent, >=50 High, >=25 Normal, otherwise Low.
 */
function derivePriorityLabel(priorityScore: number | undefined): PriorityLevel | null {
  if (priorityScore === undefined || priorityScore === null) return null;
  if (priorityScore >= 75) return "Urgent";
  if (priorityScore >= 50) return "High";
  if (priorityScore >= 25) return "Normal";
  return "Low";
}

/**
 * Map sprk_effortscore (0-100) to an effort label.
 *   >=70 = High, >=35 = Med, <35 = Low
 */
function deriveEffortLabel(effortScore: number | undefined): EffortLevel | null {
  if (effortScore === undefined || effortScore === null) return null;
  if (effortScore >= 70) return "High";
  if (effortScore >= 35) return "Med";
  return "Low";
}

// ---------------------------------------------------------------------------
// Shared inline badge (same pattern as TodoItem.tsx)
// ---------------------------------------------------------------------------

interface IBadgeProps {
  style: React.CSSProperties;
  ariaLabel: string;
  children: React.ReactNode;
}

const InlineBadge: React.FC<IBadgeProps> = ({ style, ariaLabel, children }) => (
  <span
    role="img"
    aria-label={ariaLabel}
    style={{
      display: "inline-flex",
      alignItems: "center",
      justifyContent: "center",
      borderRadius: tokens.borderRadiusSmall,
      paddingTop: "1px",
      paddingBottom: "1px",
      paddingLeft: tokens.spacingHorizontalXS,
      paddingRight: tokens.spacingHorizontalXS,
      fontSize: tokens.fontSizeBase100,
      fontWeight: tokens.fontWeightSemibold,
      lineHeight: tokens.lineHeightBase100,
      whiteSpace: "nowrap",
      ...style,
    }}
  >
    {children}
  </span>
);

// ---------------------------------------------------------------------------
// Griffel styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  pane: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
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
    gap: tokens.spacingVerticalM,
  },
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  detailRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  detailLabel: {
    color: tokens.colorNeutralForeground3,
    minWidth: "80px",
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
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const TodoDetailPane: React.FC<ITodoDetailPaneProps> = React.memo(
  ({ todo, onSaveDescription }) => {
    const styles = useStyles();

    // -- Description editing state --
    const [descriptionValue, setDescriptionValue] = React.useState("");
    const [isDirty, setIsDirty] = React.useState(false);
    const [isSaving, setIsSaving] = React.useState(false);

    // Reset description state when todo changes
    React.useEffect(() => {
      if (todo) {
        const initial = todo.sprk_description ?? "";
        setDescriptionValue(initial);
        setIsDirty(false);
        setIsSaving(false);
      }
      // Only reset when the selected todo identity changes
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [todo?.sprk_todoid]);

    // -- Handlers --

    const handleDescriptionChange = React.useCallback(
      (_ev: React.ChangeEvent<HTMLTextAreaElement>, data: { value: string }) => {
        setDescriptionValue(data.value);
        // Compare with the original value from the todo
        setIsDirty(data.value !== (todo?.sprk_description ?? ""));
      },
      [todo?.sprk_description]
    );

    const handleSave = React.useCallback(async () => {
      if (!todo || !isDirty) return;
      setIsSaving(true);
      try {
        await onSaveDescription(todo.sprk_todoid, descriptionValue);
        setIsDirty(false);
      } finally {
        setIsSaving(false);
      }
    }, [todo, isDirty, descriptionValue, onSaveDescription]);

    const handleEdit = React.useCallback(() => {
      if (!todo) return;
      navigateToEntity({
        action: "openRecord",
        entityName: "sprk_todo",
        entityId: todo.sprk_todoid,
      });
    }, [todo]);

    const handleEmail = React.useCallback(() => {
      console.info("[TodoDetailPane] Email action — stub");
    }, []);

    const handleTeams = React.useCallback(() => {
      console.info("[TodoDetailPane] Teams action — stub");
    }, []);

    const handleAiSummary = React.useCallback(() => {
      console.info("[TodoDetailPane] AI Summary action — stub");
    }, []);

    // -- Empty state --
    if (!todo) {
      return (
        <div className={styles.pane}>
          <div className={styles.emptyState}>
            <Text size={300}>Select an item to view details</Text>
          </div>
        </div>
      );
    }

    // -- Derived display values --
    const priorityLevel = derivePriorityLabel(todo.sprk_priorityscore);
    const effortLevel = deriveEffortLabel(todo.sprk_effortscore);
    const dueDate = parseDueDate(todo.sprk_duedate);
    const dueLabel = computeDueLabel(dueDate);
    const breakdown: ITodoScoreBreakdown = computeTodoScore(todo);

    return (
      <div className={styles.pane}>
        {/* Scrollable content area */}
        <div className={styles.content}>
          {/* Description section */}
          <div className={styles.section}>
            <Text weight="semibold" size={300}>
              Description
            </Text>
            <Textarea
              value={descriptionValue}
              onChange={handleDescriptionChange}
              resize="vertical"
              rows={4}
              aria-label="To Do description"
            />
            {isDirty && (
              <Button
                appearance="primary"
                size="small"
                onClick={handleSave}
                disabled={isSaving}
              >
                {isSaving ? "Saving..." : "Save"}
              </Button>
            )}
          </div>

          <Divider />

          {/* Details section */}
          <div className={styles.section}>
            <Text weight="semibold" size={300}>
              Details
            </Text>
            {priorityLevel && (
              <div className={styles.detailRow}>
                <Text size={200} className={styles.detailLabel}>
                  Priority:
                </Text>
                <InlineBadge
                  style={PRIORITY_BADGE_STYLE[priorityLevel]}
                  ariaLabel={`Priority: ${priorityLevel}`}
                >
                  {priorityLevel}
                </InlineBadge>
              </div>
            )}
            {effortLevel && (
              <div className={styles.detailRow}>
                <Text size={200} className={styles.detailLabel}>
                  Effort:
                </Text>
                <InlineBadge
                  style={EFFORT_BADGE_STYLE[effortLevel]}
                  ariaLabel={`Effort: ${effortLevel}`}
                >
                  {effortLevel}
                </InlineBadge>
              </div>
            )}
            {dueLabel.urgency !== "none" && (
              <div className={styles.detailRow}>
                <Text size={200} className={styles.detailLabel}>
                  Due:
                </Text>
                <InlineBadge
                  style={DUE_BADGE_STYLE[dueLabel.urgency]}
                  ariaLabel={`Due: ${dueLabel.label}`}
                >
                  {dueLabel.label}
                </InlineBadge>
              </div>
            )}
            {todo.assignedToName && (
              <div className={styles.detailRow}>
                <Text size={200} className={styles.detailLabel}>
                  Assigned:
                </Text>
                <Text size={200}>{todo.assignedToName}</Text>
              </div>
            )}
          </div>

          <Divider />

          {/* To Do Score breakdown section */}
          <div className={styles.section}>
            <Text weight="semibold" size={300}>
              To Do Score
            </Text>
            <div className={styles.detailRow}>
              <Text size={200} className={styles.detailLabel}>
                Priority (50%):
              </Text>
              <Text size={200}>{breakdown.priorityComponent.toFixed(1)}</Text>
            </div>
            <div className={styles.detailRow}>
              <Text size={200} className={styles.detailLabel}>
                Effort (20%):
              </Text>
              <Text size={200}>{breakdown.effortComponent.toFixed(1)}</Text>
            </div>
            <div className={styles.detailRow}>
              <Text size={200} className={styles.detailLabel}>
                Urgency (30%):
              </Text>
              <Text size={200}>{breakdown.urgencyComponent.toFixed(1)}</Text>
            </div>
            <div className={styles.detailRow}>
              <Text size={200} className={styles.detailLabel}>
                <strong>Total:</strong>
              </Text>
              <Text size={200} weight="bold">
                {breakdown.todoScore}
              </Text>
            </div>
          </div>

          <Divider />

          {/* Actions section */}
          <div className={styles.section}>
            <Text weight="semibold" size={300}>
              Actions
            </Text>
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
                onClick={handleEmail}
              >
                Email
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<ChatRegular />}
                onClick={handleTeams}
              >
                Teams
              </Button>
              <Button
                appearance="subtle"
                size="small"
                icon={<SparkleRegular />}
                onClick={handleAiSummary}
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

TodoDetailPane.displayName = "TodoDetailPane";
