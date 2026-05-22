/**
 * ContextPaneMenu.tsx — Dropdown menu rendered in the ContextPaneController's
 * PaneHeader rightSlot (Task 095; pin column added Task 099).
 *
 * Mirrors the WorkspacePaneMenu pattern: Fluent v9 `<Menu>` with a subtle
 * Button trigger using `ChevronDownRegular`, MenuPopover + MenuList +
 * MenuGroupHeader + MenuItems for each available Context tool. The active
 * tool gets a `CheckmarkRegular` icon (same `activeMarker` treatment as
 * WorkspacePaneMenu line 530-540) on the RIGHT side of the row.
 *
 * Task 099 adds a clickable PIN COLUMN on the LEFT of each tool's name,
 * mirroring the workspace-dropdown pin pattern (task 098). The operator
 * wanted identical UX across both dropdowns. Pin semantics are SINGLE-PIN
 * ("default on load"):
 *
 *   - PinRegular (outline, neutral foreground 3) = NOT pinned
 *   - PinFilled (solid, brand foreground 1)      = PINNED
 *   - Clicking the pin TOGGLES — pinning a new tool implicitly unpins the
 *     previous one (storage layer enforces single-pin).
 *   - Pin click does NOT change the active selectedTool; it only updates the
 *     pinned-default which takes effect on next cold load (or after a
 *     selected-tool localStorage clear).
 *   - `stopPropagation()` prevents the MenuItem's onClick (which fires
 *     `onSelectTool`) from triggering on a pin click.
 *
 * Tools surfaced:
 *   - Quick Start (default) — GetStartedCardsWidget (the existing FR-18 / FR-19
 *     7-card grid).
 *   - Semantic Search — SemanticSearchCriteriaTool (Task 095 — in-pane
 *     search criteria + Search button → launches sprk_semanticsearch modal).
 *
 * Selection is owned by the parent (ContextPaneController) via the
 * `useContextTool` hook — this component is purely presentational + dispatch.
 * Persistence of the active tool happens in the hook (`selected-tool` key);
 * persistence of the pinned default happens in this component via the
 * `contextToolPin.ts` utility (`pinned-tool` key).
 *
 * Composition rationale:
 *   - The PaneHeader's rightSlot wrapper applies `stopPropagation` on clicks
 *     when `onCollapse` is wired (task 094) — so the menu trigger button
 *     never accidentally collapses the pane. No defensive belt needed here.
 *
 *   - Styling reuses the same `trigger` / `activeMarker` / `tabLabel` shapes
 *     as WorkspacePaneMenu for visual parity across panes; pin styling
 *     (`layoutRow` / `pinButton` / `pinButtonActive`) mirrors task 098 on the
 *     Workspace pane verbatim so the operator sees a single coherent UX.
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
  mergeClasses,
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
  PinRegular,
  PinFilled,
} from '@fluentui/react-icons';
import type { ContextToolId } from '../../hooks/useContextTool';
import {
  getPinnedContextTool,
  pinContextTool,
  unpinContextTool,
} from '../../services/contextToolPin';

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

  // Task 099 — pin column (mirrors task 098's WorkspacePaneMenu styles).
  // The pin icon appears on the LEFT of the tool name (BEFORE the active
  // checkmark marker) and is a clickable affordance independent of the
  // MenuItem's main onClick handler. Small (20×20) so it sits comfortably
  // inside a MenuItem row without inflating row height.
  toolRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    width: '100%',
    minWidth: 0,
  },
  pinButton: {
    minWidth: 'unset',
    height: '20px',
    width: '20px',
    padding: '0',
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    ':hover': {
      color: tokens.colorNeutralForeground1,
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
  },
  pinButtonActive: {
    color: tokens.colorBrandForeground1,
    ':hover': {
      color: tokens.colorBrandForeground1,
    },
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

  // Task 099 — pinned tool state. Single-pin: at most one tool id, or null
  // when nothing is pinned. We hydrate from localStorage on mount (lazy
  // initializer) and re-read it whenever the menu opens (cheap; covers the
  // edge case where another tab/window changed the pin while this one was
  // open). Toggling the pin updates BOTH local state (for immediate icon
  // flip) AND localStorage (so next cold load sees the new pin).
  const [pinnedToolId, setPinnedToolId] = React.useState<ContextToolId | null>(
    () => getPinnedContextTool(),
  );

  const handleSelect = React.useCallback(
    (id: ContextToolId) => {
      onSelectTool(id);
      setMenuOpen(false);
    },
    [onSelectTool],
  );

  // Pin-toggle handler. We accept the MouseEvent so we can stop propagation
  // BEFORE the MenuItem's onClick fires — otherwise the click would also
  // change the active tool (which is NOT what pin should do).
  const handleTogglePin = React.useCallback(
    (id: ContextToolId, ev: React.MouseEvent<HTMLButtonElement>) => {
      ev.stopPropagation();
      ev.preventDefault();
      setPinnedToolId((prev) => {
        if (prev === id) {
          unpinContextTool();
          return null;
        }
        pinContextTool(id);
        return id;
      });
    },
    [],
  );

  // Re-read pinned state when the menu opens so we always reflect the latest
  // localStorage value (covers other-tab pin changes).
  const handleMenuOpenChange = React.useCallback(
    (_e: unknown, data: { open: boolean }) => {
      if (data.open) {
        setPinnedToolId(getPinnedContextTool());
      }
      setMenuOpen(data.open);
    },
    [],
  );

  return (
    <Menu
      open={menuOpen}
      onOpenChange={handleMenuOpenChange}
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
            const isPinned = pinnedToolId === tool.id;
            const pinLabel = isPinned
              ? `Unpin ${tool.label}`
              : `Pin ${tool.label} as default`;
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
                <span className={styles.toolRow}>
                  <Tooltip content={pinLabel} relationship="label">
                    <Button
                      appearance="transparent"
                      size="small"
                      aria-label={pinLabel}
                      aria-pressed={isPinned}
                      icon={isPinned ? <PinFilled /> : <PinRegular />}
                      onClick={(ev) => handleTogglePin(tool.id, ev)}
                      className={mergeClasses(
                        styles.pinButton,
                        isPinned && styles.pinButtonActive,
                      )}
                      data-testid={`context-tool-pin-${tool.id}`}
                    />
                  </Tooltip>
                  <span className={styles.tabLabel}>{tool.label}</span>
                </span>
              </MenuItem>
            );
          })}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

ContextPaneMenu.displayName = 'ContextPaneMenu';
