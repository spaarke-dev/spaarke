/**
 * MyTasksFilter — Three-mode filter for the Smart To Do Kanban board.
 *
 * Renders a Fluent v9 `RadioGroup` in horizontal layout inside the
 * `KanbanHeader`. Three modes per R3 FR-12 / A-6:
 *
 *   - **My Tasks** (default): owner OR assignee = current user.
 *       (A-6 also calls for "OR owner is a team the user is a member of".
 *        That third clause is deferred to a follow-up — see TODO in
 *        `services/queryHelpers.ts buildTodoItemsQuery`.)
 *
 *   - **Assigned to me**: assignee = current user.
 *
 *   - **All**: no ownership filter (active-statuscode filter still applies).
 *
 * The selected mode persists in user preferences alongside the kanban-threshold
 * JSON value (no new sprk_preferencetype optionset value is required) — see
 * `hooks/useUserPreferences.ts`.
 *
 * Accessibility (NFR-10):
 *   - RadioGroup is keyboard-navigable: Tab to focus, Left/Right + Up/Down to
 *     move between options, Space to select.
 *   - aria-labelledby ties the group to a visible "Filter:" label.
 *   - Each Radio has an aria-label so screen-reader users hear the mode name
 *     independent of visual context.
 *   - layout="horizontal" keeps the control compact next to the title.
 *
 * Design constraints (NFR-01):
 *   - ALL colours from Fluent UI v9 semantic tokens.
 *   - Griffel `makeStyles` only — no inline styles, no CSS files.
 *   - Light / dark / high-contrast modes supported automatically.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Radio,
  RadioGroup,
} from "@fluentui/react-components";
import type {
  RadioGroupOnChangeData,
} from "@fluentui/react-components";
import type { MyTasksFilterMode } from "../hooks/useUserPreferences";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  label: {
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
  },
  group: {
    // RadioGroup defaults to vertical; we render horizontally via prop, but
    // also tighten the spacing for the header context.
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
});

// ---------------------------------------------------------------------------
// Mode definitions (single source of truth for label + aria + order)
// ---------------------------------------------------------------------------

interface IModeOption {
  value: MyTasksFilterMode;
  label: string;
  ariaLabel: string;
}

/**
 * Mode options in display order. Order matches FR-12: My Tasks (default) →
 * Assigned to me → All.
 */
const MODE_OPTIONS: ReadonlyArray<IModeOption> = [
  {
    value: "MyTasks",
    label: "My Tasks",
    ariaLabel: "Show tasks I own or that are assigned to me",
  },
  {
    value: "AssignedToMe",
    label: "Assigned to me",
    ariaLabel: "Show only tasks assigned to me",
  },
  {
    value: "All",
    label: "All",
    ariaLabel: "Show all tasks regardless of ownership",
  },
];

// Stable id for the visible label — RadioGroup ties to it via aria-labelledby.
const FILTER_LABEL_ID = "smart-todo-my-tasks-filter-label";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IMyTasksFilterProps {
  /** Currently-selected mode. */
  value: MyTasksFilterMode;
  /** Called when the user selects a new mode. */
  onChange: (mode: MyTasksFilterMode) => void;
  /** Disable the control (e.g. while preferences are still loading). */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const MyTasksFilterImpl: React.FC<IMyTasksFilterProps> = ({
  value,
  onChange,
  disabled = false,
}) => {
  const styles = useStyles();

  const handleChange = React.useCallback(
    (_ev: React.FormEvent<HTMLDivElement>, data: RadioGroupOnChangeData) => {
      // RadioGroup emits `data.value` as a plain string; cast to the typed
      // discriminated union and guard against unexpected inputs.
      const next = data.value as MyTasksFilterMode;
      if (
        next === "MyTasks" ||
        next === "AssignedToMe" ||
        next === "All"
      ) {
        onChange(next);
      }
    },
    [onChange]
  );

  return (
    <div className={styles.root}>
      <Text
        id={FILTER_LABEL_ID}
        size={200}
        weight="regular"
        className={styles.label}
      >
        Filter:
      </Text>
      <RadioGroup
        value={value}
        onChange={handleChange}
        layout="horizontal"
        disabled={disabled}
        aria-labelledby={FILTER_LABEL_ID}
        className={styles.group}
      >
        {MODE_OPTIONS.map((opt) => (
          <Radio
            key={opt.value}
            value={opt.value}
            label={opt.label}
            aria-label={opt.ariaLabel}
          />
        ))}
      </RadioGroup>
    </div>
  );
};

MyTasksFilterImpl.displayName = "MyTasksFilter";

export const MyTasksFilter = React.memo(MyTasksFilterImpl);
MyTasksFilter.displayName = "MyTasksFilter";

// Re-export the mode option metadata so tests (and any future consumers) can
// assert against the canonical list without duplicating string literals.
export { MODE_OPTIONS as MY_TASKS_FILTER_MODES };
