/**
 * KanbanHeader — Header bar above the Kanban board (Task 016).
 *
 * Layout (flexbox row, left-to-right):
 *   [ToDoIcon] Smart To Do [42]  │  [Add input... ] [Add]  │  [↻] [⚙]
 *
 * Sections:
 *   - Left group:   MicrosoftToDoIcon + title + count badge
 *   - Center group: AddTodoBar (flex-grow, takes remaining space)
 *   - Right group:  Recalculate button + Settings button
 *
 * Behaviour:
 *   - Returns null when `embedded === true` (matches SmartToDo embedded pattern)
 *   - Recalculate button shows Spinner while isRecalculating is true
 *   - All event handlers are delegated to the parent via props
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Button,
  Spinner,
} from "@fluentui/react-components";
import {
  ArrowClockwiseRegular,
  SettingsRegular,
} from "@fluentui/react-icons";
import { MicrosoftToDoIcon } from "../../icons/MicrosoftToDoIcon";
import { AddTodoBar } from "./AddTodoBar";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
    flexWrap: "wrap",
  },

  leftGroup: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },

  centerGroup: {
    flex: "1 1 0",
    minWidth: 0,
  },

  rightGroup: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IKanbanHeaderProps {
  /** Total number of items across all columns. */
  totalCount: number;
  /** Called when recalculate button is clicked. */
  onRecalculate: () => void;
  /** True while recalculate is in progress (shows spinner on button). */
  isRecalculating: boolean;
  /** Called by AddTodoBar when a new item is submitted. */
  onAdd: (title: string) => Promise<void>;
  /** True while a new item is being created. */
  isAdding: boolean;
  /** Called when settings gear is clicked. */
  onSettingsOpen: () => void;
  /** When true, hides the header (matching existing SmartToDo embedded pattern). */
  embedded?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const KanbanHeader: React.FC<IKanbanHeaderProps> = React.memo(
  ({
    totalCount,
    onRecalculate,
    isRecalculating,
    onAdd,
    isAdding,
    onSettingsOpen,
    embedded = false,
  }) => {
    const styles = useStyles();

    if (embedded) {
      return null;
    }

    return (
      <div className={styles.header} role="banner">
        {/* Left group: icon + title + count */}
        <div className={styles.leftGroup}>
          <MicrosoftToDoIcon size={20} active />
          <Text weight="semibold" size={400}>
            Smart To Do
          </Text>
          <Badge appearance="filled" color="informative">
            {totalCount}
          </Badge>
        </div>

        {/* Center group: add bar (flex-grow) */}
        <div className={styles.centerGroup}>
          <AddTodoBar onAdd={onAdd} isAdding={isAdding} />
        </div>

        {/* Right group: recalculate + settings */}
        <div className={styles.rightGroup}>
          <Button
            appearance="subtle"
            size="small"
            icon={
              isRecalculating ? (
                <Spinner size="tiny" />
              ) : (
                <ArrowClockwiseRegular />
              )
            }
            disabled={isRecalculating}
            onClick={onRecalculate}
            aria-label="Recalculate column assignments"
            title="Recalculate"
          />
          <Button
            appearance="subtle"
            size="small"
            icon={<SettingsRegular />}
            onClick={onSettingsOpen}
            aria-label="Threshold settings"
            title="Settings"
          />
        </div>
      </div>
    );
  }
);

KanbanHeader.displayName = "KanbanHeader";
