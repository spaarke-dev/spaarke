/**
 * TodoSection - Related to-do display/edit for Event Detail Side Pane
 *
 * Fixed section (not config-driven) shown at the bottom of every side pane.
 * Queries sprk_eventtodo by _sprk_regardingevent_value.
 * - If to-do exists: shows card with checkbox, name, assigned to, due date
 * - If no to-do: shows "+ Add To Do" button
 *
 * Entity: sprk_eventtodo
 * Fields: sprk_name, sprk_regardingevent, sprk_assignedto, sprk_duedate,
 *         statecode, statuscode, sprk_graphtaskid, sprk_graphsyncedat
 *
 * @see approach-a-dynamic-form-renderer.md
 */

import * as React from "react";
import {
  Checkbox,
  Input,
  Button,
  Text,
  Persona,
  Spinner,
  makeStyles,
  shorthands,
  tokens,
} from "@fluentui/react-components";
import { DatePicker } from "@fluentui/react-datepicker-compat";
import {
  CheckmarkCircleRegular,
  AddRegular,
  PersonRegular,
  CalendarMonthRegular,
} from "@fluentui/react-icons";
import { CollapsibleSection } from "./CollapsibleSection";
import { useRelatedRecord } from "../hooks/useRelatedRecord";
import { getXrm } from "../utils/xrmAccess";
import type { ILookupValue } from "../types/FormConfig";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
  },
  todoCard: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap("8px"),
    ...shorthands.padding("12px"),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
  },
  headerRow: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("8px"),
  },
  nameInput: {
    flexGrow: 1,
  },
  detailRow: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap("6px"),
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  detailIcon: {
    fontSize: "14px",
    color: tokens.colorNeutralForeground3,
  },
  completedText: {
    textDecoration: "line-through",
    color: tokens.colorNeutralForeground3,
  },
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap("8px"),
    ...shorthands.padding("8px", "0"),
  },
  emptyText: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface TodoSectionProps {
  /** Event record ID */
  eventId: string | null;
  /** Whether editing is disabled */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function parseISODate(value: string | null | undefined): Date | null {
  if (!value) return null;
  try {
    const d = new Date(value);
    return isNaN(d.getTime()) ? null : d;
  } catch {
    return null;
  }
}

function formatDateShort(date?: Date): string {
  if (!date) return "";
  return date.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const TodoSection: React.FC<TodoSectionProps> = ({
  eventId,
  disabled = false,
}) => {
  const styles = useStyles();
  const [isSaving, setIsSaving] = React.useState(false);

  const todo = useRelatedRecord({
    entityName: "sprk_eventtodo",
    parentLookupField: "sprk_regardingevent",
    parentId: eventId,
    selectFields:
      "sprk_eventtodoid,sprk_name,sprk_duedate,statecode,statuscode," +
      "_sprk_assignedto_value,sprk_graphtaskid",
  });

  // Extract values from record
  const todoName = (todo.record?.["sprk_name"] as string) ?? "";
  const todoDueDate = (todo.record?.["sprk_duedate"] as string) ?? null;
  const todoStatecode = (todo.record?.["statecode"] as number) ?? 0;
  const isCompleted = todoStatecode === 1;
  const assignedToName =
    (todo.record?.[
      "_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue"
    ] as string) ?? null;

  // ─────────────────────────────────────────────────────────────────────────
  // Handlers
  // ─────────────────────────────────────────────────────────────────────────

  const handleToggleComplete = React.useCallback(async () => {
    if (!todo.recordId) return;
    setIsSaving(true);
    try {
      const newState = isCompleted ? 0 : 1; // Toggle Active/Inactive
      await todo.updateRecord({ statecode: newState });
    } finally {
      setIsSaving(false);
    }
  }, [todo, isCompleted]);

  const handleNameChange = React.useCallback(
    (_ev: unknown, data: { value: string }) => {
      // Debounced save on blur
      if (todo.record) {
        todo.record["sprk_name"] = data.value;
      }
    },
    [todo.record]
  );

  const handleNameBlur = React.useCallback(
    async (ev: React.FocusEvent<HTMLInputElement>) => {
      const newName = ev.target.value;
      if (todo.recordId && newName !== todoName) {
        await todo.updateRecord({ sprk_name: newName });
      }
    },
    [todo, todoName]
  );

  const handleDueDateChange = React.useCallback(
    async (date: Date | null | undefined) => {
      if (!todo.recordId) return;
      await todo.updateRecord({
        sprk_duedate: date ? date.toISOString() : null,
      });
    },
    [todo]
  );

  const handleAssignedToLookup = React.useCallback(async () => {
    if (!todo.recordId) return;

    const xrm = getXrm();
    if (!xrm?.Utility?.lookupObjects) return;

    try {
      const result = await xrm.Utility.lookupObjects({
        defaultEntityType: "contact",
        entityTypes: ["contact"],
        allowMultiSelect: false,
      });

      if (result && result.length > 0) {
        const selected: ILookupValue = {
          id: result[0].id.replace(/[{}]/g, "").toLowerCase(),
          name: result[0].name,
          entityType: result[0].entityType,
        };
        await todo.updateRecord({
          "sprk_assignedto@odata.bind": `/contacts(${selected.id})`,
        });
        todo.refresh();
      }
    } catch (err) {
      console.error("[TodoSection] Lookup error:", err);
    }
  }, [todo]);

  const handleAddTodo = React.useCallback(async () => {
    setIsSaving(true);
    try {
      await todo.createRecord({
        sprk_name: "New To Do",
      });
    } finally {
      setIsSaving(false);
    }
  }, [todo]);

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <CollapsibleSection
      title="To Do"
      icon={<CheckmarkCircleRegular />}
      defaultExpanded={true}
    >
      <div className={styles.container}>
        {/* Loading */}
        {todo.isLoading && (
          <Spinner size="tiny" label="Loading to-do..." />
        )}

        {/* To-do exists */}
        {!todo.isLoading && todo.record && (
          <div className={styles.todoCard}>
            {/* Checkbox + Name */}
            <div className={styles.headerRow}>
              <Checkbox
                checked={isCompleted}
                onChange={handleToggleComplete}
                disabled={disabled || isSaving}
                aria-label={isCompleted ? "Mark incomplete" : "Mark complete"}
              />
              <Input
                className={styles.nameInput}
                defaultValue={todoName}
                onBlur={handleNameBlur}
                onChange={handleNameChange}
                disabled={disabled || isSaving}
                appearance="underline"
                aria-label="To-do name"
                style={isCompleted ? { textDecoration: "line-through" } : undefined}
              />
            </div>

            {/* Due Date */}
            <div className={styles.detailRow}>
              <CalendarMonthRegular className={styles.detailIcon} />
              <DatePicker
                value={parseISODate(todoDueDate)}
                onSelectDate={handleDueDateChange}
                disabled={disabled || isSaving}
                placeholder="Set due date..."
                formatDate={formatDateShort}
                aria-label="To-do due date"
                style={{ flexGrow: 1 }}
              />
            </div>

            {/* Assigned To */}
            <div className={styles.detailRow}>
              <PersonRegular className={styles.detailIcon} />
              {assignedToName ? (
                <Persona
                  name={assignedToName}
                  size="extra-small"
                  avatar={{ color: "colorful" }}
                />
              ) : (
                <Button
                  appearance="subtle"
                  size="small"
                  onClick={handleAssignedToLookup}
                  disabled={disabled || isSaving}
                >
                  Assign
                </Button>
              )}
            </div>
          </div>
        )}

        {/* No to-do */}
        {!todo.isLoading && !todo.record && (
          <div className={styles.emptyState}>
            <Text className={styles.emptyText}>No to-do for this event</Text>
            <Button
              appearance="secondary"
              icon={<AddRegular />}
              onClick={handleAddTodo}
              disabled={disabled || isSaving}
              size="small"
            >
              Add To Do
            </Button>
          </div>
        )}
      </div>
    </CollapsibleSection>
  );
};
