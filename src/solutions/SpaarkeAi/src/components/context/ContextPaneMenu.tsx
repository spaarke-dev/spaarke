/**
 * ContextPaneMenu.tsx — Context pane tools menu rendered in the
 * ContextPaneController's PaneHeader rightSlot.
 *
 * # What changed (UAT 2026-07-20)
 *
 * Operator feedback: "replace the Tools drop down with the vertical three dots;
 * remove the 'pin' for the tools and add icons (so style matches the Assistant
 * drop down)." So this menu now mirrors `AssistantToolMenu` exactly:
 *
 *   - Trigger: icon-only vertical three-dots (⋮) `MoreVerticalRegular` button
 *     (was a "Tools ▾" text + chevron button).
 *   - Rows: each tool renders with a LEADING identity icon and its label —
 *     matching the Assistant menu's `[icon] Label` rows.
 *   - The per-tool PIN toggle (the "default on load" affordance backed by
 *     `contextToolPin.ts`) was REMOVED along with all pin state. Selecting a
 *     tool still loads it; there is simply no pinned-default concept anymore.
 *
 * Selection is owned by the parent (ContextPaneController) via the
 * `useContextTool` hook — this component is purely presentational + dispatch.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local component (depends on solution-local tool ids).
 *   - ADR-021: Fluent v9 tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *
 * @see AssistantToolMenu.tsx — the ⋮ trigger + icon-row idiom this mirrors.
 */

import * as React from 'react';
import {
  makeStyles,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuGroupHeader,
  Button,
  Tooltip,
} from '@fluentui/react-components';
import {
  MoreVerticalRegular,
  TextBulletListSquareRegular,
  SearchRegular,
  PinRegular,
} from '@fluentui/react-icons';
import type { ContextToolId } from '../../hooks/useContextTool';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ContextPaneMenuProps {
  /**
   * The currently-selected Context tool. Retained as a prop because the parent
   * controller owns selection state; this menu no longer renders an active
   * marker for it (task 106 removed the checkmark per operator feedback).
   */
  selectedTool: ContextToolId;
  /** Called when the user selects a tool from the menu. */
  onSelectTool: (id: ContextToolId) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: 'auto',
  },
});

// ---------------------------------------------------------------------------
// Tool catalog — keep in sync with ContextToolId in useContextTool.ts
// ---------------------------------------------------------------------------

interface ToolDescriptor {
  id: ContextToolId;
  label: string;
  icon: React.ReactElement;
}

const CONTEXT_TOOLS: readonly ToolDescriptor[] = [
  {
    // R6 Pillar 6c / task 095 — Claude-Code-like trace of the agent's tool calls,
    // knowledge retrievals, playbook node executions, and routing decisions.
    id: 'execution-trace',
    label: 'Execution Trace',
    icon: <TextBulletListSquareRegular />,
  },
  {
    id: 'semantic-search',
    label: 'Semantic Search',
    icon: <SearchRegular />,
  },
  {
    // R6 Pillar 7 / task 096 — inspectable voice-trigger pins.
    id: 'pinned-memory',
    label: 'Pinned Memory',
    icon: <PinRegular />,
  },
];

// ---------------------------------------------------------------------------
// ContextPaneMenu component
// ---------------------------------------------------------------------------

/**
 * ContextPaneMenu — Fluent v9 Menu rendered in <PaneHeader rightSlot> of the
 * ContextPaneController. Mirrors AssistantToolMenu. See file header.
 */
export const ContextPaneMenu: React.FC<ContextPaneMenuProps> = ({
  selectedTool: _selectedTool,
  onSelectTool,
}) => {
  const styles = useStyles();
  const [menuOpen, setMenuOpen] = React.useState(false);

  const handleSelect = React.useCallback(
    (id: ContextToolId) => {
      onSelectTool(id);
      setMenuOpen(false);
    },
    [onSelectTool],
  );

  const handleOpenChange = React.useCallback(
    (_e: unknown, data: { open: boolean }) => {
      setMenuOpen(data.open);
    },
    [],
  );

  return (
    <Menu open={menuOpen} onOpenChange={handleOpenChange} positioning="below-end">
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Context tools" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<MoreVerticalRegular />}
            aria-label="Context tools"
            className={styles.trigger}
            data-testid="context-pane-menu-trigger"
          />
        </Tooltip>
      </MenuTrigger>

      <MenuPopover data-testid="context-pane-menu-popover">
        <MenuList>
          <MenuGroupHeader>Context Tools</MenuGroupHeader>
          {CONTEXT_TOOLS.map((tool) => (
            <MenuItem
              key={tool.id}
              icon={tool.icon}
              onClick={() => handleSelect(tool.id)}
              data-testid={`context-tool-${tool.id}`}
            >
              {tool.label}
            </MenuItem>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

ContextPaneMenu.displayName = 'ContextPaneMenu';
