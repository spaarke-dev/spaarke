/**
 * RowActionsCell — the DataGrid's host row-actions column (`rowActions` prop).
 *
 * Generic: the host supplies the actions and owns what they do (through the grid's `onRecordAction`); the grid only
 * places and renders them. No entity names, no Xrm, no data access (ADR-012).
 *
 * Rules:
 *  - Hide, don't disable: an action whose `isVisible(record)` returns false is not rendered for that row.
 *  - Each action is an icon-only subtle button; its tooltip equals its aria-label (the action's `label`).
 *  - Up to {@link ROW_ACTIONS_INLINE_LIMIT} visible actions render inline. Past that, the first
 *    `ROW_ACTIONS_INLINE_LIMIT - 1` stay inline and the rest fold into a "More actions" menu, so the column keeps a
 *    fixed width.
 *  - A click, or Space / Enter on an action or a "More actions" menu item, does not toggle the row's selection.
 *
 * **ADR**: ADR-012, ADR-021 (Fluent v9, tokens), ADR-022 (no React-18-only hooks).
 */

import * as React from 'react';
import {
  Button,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { MoreHorizontal20Regular } from '@fluentui/react-icons';

/** A per-row action the HOST adds to the grid as its last column. */
export interface DataGridRowAction {
  /** Stable id, passed to `onRecordAction` as `actionId`. */
  id: string;
  /** Accessible name and tooltip of the button (and the menu item text when folded). */
  label: string;
  /** Icon element (for example a Fluent icon). */
  icon: React.ReactElement;
  /** Return false to HIDE the action for that row (never rendered disabled). Absent: always shown. */
  isVisible?: (record: Record<string, unknown>) => boolean;
}

/** Visible actions shown inline before the rest fold into the "More actions" menu. */
export const ROW_ACTIONS_INLINE_LIMIT = 3;

/** Column id of the actions column; not a Dataverse logical name, so it cannot collide with a configured column. */
export const ROW_ACTIONS_COLUMN_ID = '__spaarke_row_actions';

const BUTTON_WIDTH = 32; // Fluent v9 medium icon-only button
const BUTTON_GAP = 4; // tokens.spacingHorizontalXS
const CELL_PADDING = 24; // spacingHorizontalM on each side (DataGrid `cell` style)

/** Fixed width of the actions column for the given action list (enough for the widest row). */
export function rowActionsColumnWidth(actions: ReadonlyArray<DataGridRowAction>): number {
  const slots = Math.max(1, Math.min(actions.length, ROW_ACTIONS_INLINE_LIMIT));
  return slots * BUTTON_WIDTH + (slots - 1) * BUTTON_GAP + CELL_PADDING;
}

/** The actions of `actions` that are visible for `record` (isVisible absent or not false). */
export function visibleRowActions(
  actions: ReadonlyArray<DataGridRowAction>,
  record: Record<string, unknown>
): DataGridRowAction[] {
  return actions.filter(a => (a.isVisible ? a.isVisible(record) !== false : true));
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
});

export interface RowActionsCellProps {
  actions: ReadonlyArray<DataGridRowAction>;
  record: Record<string, unknown>;
  onInvoke: (actionId: string) => void;
}

export const RowActionsCell: React.FC<RowActionsCellProps> = ({ actions, record, onInvoke }) => {
  const styles = useStyles();
  const visible = visibleRowActions(actions, record);
  if (visible.length === 0) return null;

  const fold = visible.length > ROW_ACTIONS_INLINE_LIMIT;
  const inline = fold ? visible.slice(0, ROW_ACTIONS_INLINE_LIMIT - 1) : visible;
  const folded = fold ? visible.slice(ROW_ACTIONS_INLINE_LIMIT - 1) : [];

  // Row click toggles selection in Fluent's DataGridRow; an action is not a selection gesture.
  const stop = (e: React.SyntheticEvent) => e.stopPropagation();
  // Space also toggles the row (DataGridRow.onKeyDown, for a non-interactive target such as a menu item) and a
  // keydown in the "More actions" popover reaches the row through the React portal. Space / Enter on an action is an
  // activation, never a selection gesture. Other keys (Tab, arrows, Esc) must still reach the menu / grid navigation.
  const stopActivationKey = (e: React.KeyboardEvent) => {
    if (e.key === ' ' || e.key === 'Spacebar' || e.key === 'Enter') e.stopPropagation();
  };

  return (
    <div className={styles.root} onClick={stop} onKeyDown={stopActivationKey} data-testid="datagrid-row-actions">
      {inline.map(action => (
        <Tooltip key={action.id} content={action.label} relationship="label">
          <Button
            appearance="subtle"
            icon={action.icon}
            aria-label={action.label}
            onClick={e => {
              e.stopPropagation();
              onInvoke(action.id);
            }}
          />
        </Tooltip>
      ))}
      {folded.length > 0 && (
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Tooltip content="More actions" relationship="label">
              <Button appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
            </Tooltip>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {folded.map(action => (
                <MenuItem key={action.id} icon={action.icon} onClick={() => onInvoke(action.id)}>
                  {action.label}
                </MenuItem>
              ))}
            </MenuList>
          </MenuPopover>
        </Menu>
      )}
    </div>
  );
};
