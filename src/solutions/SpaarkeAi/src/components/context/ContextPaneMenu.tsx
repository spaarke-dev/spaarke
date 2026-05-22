/**
 * ContextPaneMenu.tsx — Dropdown menu rendered in the ContextPaneController's
 * PaneHeader rightSlot (Task 095).
 *
 * Mirrors the WorkspacePaneMenu pattern: Fluent v9 `<Menu>` with a subtle
 * Button trigger using `ChevronDownRegular`, MenuPopover + MenuList +
 * MenuGroupHeader + MenuItems for each available Context tool. The active
 * tool gets a `CheckmarkRegular` icon (same `activeMarker` treatment as
 * WorkspacePaneMenu line 530-540).
 *
 * Tools surfaced:
 *   - Quick Start (default) — GetStartedCardsWidget (the existing FR-18 / FR-19
 *     7-card grid).
 *   - Semantic Search — SemanticSearchCriteriaTool (Task 095 new — in-pane
 *     search criteria + Search button → launches sprk_semanticsearch modal).
 *
 * Selection is owned by the parent (ContextPaneController) via the
 * `useContextTool` hook — this component is purely presentational + dispatch.
 * Persistence to localStorage happens in the hook, not here.
 *
 * Composition rationale:
 *   - The PaneHeader's rightSlot wrapper applies `stopPropagation` on clicks
 *     when `onCollapse` is wired (task 094) — so the menu trigger button
 *     never accidentally collapses the pane. No defensive belt needed here.
 *
 *   - Styling reuses the same `trigger` / `activeMarker` / `tabLabel` shapes
 *     as WorkspacePaneMenu for visual parity across panes.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local component (depends on solution-local tool ids).
 *   - ADR-021: Fluent v9 tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
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
  ChevronDownRegular,
  AppsListRegular,
  SearchRegular,
  CheckmarkRegular,
} from '@fluentui/react-icons';
import type { ContextToolId } from '../../hooks/useContextTool';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ContextPaneMenuProps {
  /** The currently-selected Context tool (drives the active checkmark). */
  selectedTool: ContextToolId;
  /** Called when the user selects a tool from the dropdown. */
  onSelectTool: (id: ContextToolId) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  trigger: {
    minWidth: 'auto',
  },
  tabLabel: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    flex: '1 1 auto',
    minWidth: 0,
  },
  activeMarker: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
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
    id: 'quick-start',
    label: 'Quick Start',
    icon: <AppsListRegular />,
  },
  {
    id: 'semantic-search',
    label: 'Semantic Search',
    icon: <SearchRegular />,
  },
];

// ---------------------------------------------------------------------------
// ContextPaneMenu component
// ---------------------------------------------------------------------------

/**
 * ContextPaneMenu — Fluent v9 Menu rendered in <PaneHeader rightSlot> of the
 * ContextPaneController. See file header for full design rationale.
 */
export const ContextPaneMenu: React.FC<ContextPaneMenuProps> = ({
  selectedTool,
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

  return (
    <Menu
      open={menuOpen}
      onOpenChange={(_, data) => setMenuOpen(data.open)}
      positioning="below-end"
    >
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content="Open context tools menu" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<ChevronDownRegular />}
            iconPosition="after"
            aria-label="Open context tools menu"
            className={styles.trigger}
            data-testid="context-pane-menu-trigger"
          >
            Tools
          </Button>
        </Tooltip>
      </MenuTrigger>

      <MenuPopover data-testid="context-pane-menu-popover">
        <MenuList>
          <MenuGroupHeader>Context Tools</MenuGroupHeader>
          {CONTEXT_TOOLS.map((tool) => {
            const isActive = tool.id === selectedTool;
            return (
              <MenuItem
                key={tool.id}
                onClick={() => handleSelect(tool.id)}
                data-testid={`context-tool-${tool.id}`}
                icon={
                  isActive ? (
                    <CheckmarkRegular className={styles.activeMarker} />
                  ) : (
                    tool.icon
                  )
                }
              >
                <span className={styles.tabLabel}>{tool.label}</span>
              </MenuItem>
            );
          })}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

ContextPaneMenu.displayName = 'ContextPaneMenu';
