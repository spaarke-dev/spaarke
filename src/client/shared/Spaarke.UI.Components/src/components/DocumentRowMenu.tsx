/**
 * DocumentRowMenu.tsx
 *
 * Shared Fluent v9 row-action menu rendered as a 3-dot `MenuButton`.
 *
 * Spec: FR-SC-02 (shared component), FR-DOC-01 (canonical action ordering).
 *
 * Behavior:
 *  - Trigger is a Fluent v9 `MenuButton` (`appearance="subtle"`, `size="small"`,
 *    icon `MoreVertical20Regular`).
 *  - The trigger's `onClick` calls `e.stopPropagation()` BEFORE the menu opens
 *    so a parent row's `onClick` (which typically opens the document
 *    preview Dialog) does NOT fire.
 *  - 12 leaf actions in the FR-DOC-01 order, separated by 2 `MenuDivider`s
 *    after `findSimilar` and after `openRecord`.
 *  - `disabledActions` hides the listed actions from the rendered menu;
 *    dividers are emitted only between groups that have at least one
 *    visible action (no orphaned/double dividers, no empty groups).
 *
 * Fluent v9 portal gotcha (see `.claude/patterns/ui/fluent-v9-portal-gotcha.md`):
 *  - `Menu` renders its popover through a React portal that escapes the
 *    `FluentProvider` subtree. Spaarke's project convention is for the
 *    consuming surface (PCF/Code Page) to mount a single root `FluentProvider`
 *    and rely on its default `applyStylesToPortals={true}` (per the project's
 *    PCF reference theme provider). This component therefore does NOT mount
 *    its own provider — doing so would shadow the customer-tenant theme
 *    propagated by `context.fluentDesignLanguage?.tokenTheme`.
 *
 * Standards:
 *  - ADR-012 (shared component, generic — no Semantic-Search-specific logic)
 *  - ADR-021 (Fluent v9 tokens only — no hardcoded hex/rgb)
 *  - ADR-022 (React 16/17 compatible — no React 18-only APIs)
 */

import * as React from 'react';
import {
  Menu,
  MenuTrigger,
  MenuButton,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
} from '@fluentui/react-components';
import {
  MoreVertical20Regular,
  Eye20Regular,
  Sparkle20Regular,
  Open20Regular,
  Search20Regular,
  ArrowDownload20Regular,
  Link20Regular,
  Mail20Regular,
  DocumentText20Regular,
  PanelRightExpand20Regular,
  Pin20Regular,
  Rename20Regular,
  Delete20Regular,
} from '@fluentui/react-icons';

import type { DocumentRowAction, IDocumentRowMenuTarget } from '../types/DocumentRowMenu';

// Re-export the types so consumers can `import { ... } from '@spaarke/ui-components'`
// once the barrel is updated by task 012.
export type { DocumentRowAction, IDocumentRowMenuTarget };

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** Props for {@link DocumentRowMenu}. */
export interface IDocumentRowMenuProps {
  /** Document the menu acts on. Used for the accessible trigger label. */
  document: IDocumentRowMenuTarget;
  /** Invoked when the user clicks an action item. */
  onAction: (action: DocumentRowAction) => void;
  /**
   * Optional list of actions to hide from the rendered menu. Useful for
   * per-document permission scoping (e.g., omit `delete` for users who
   * lack delete permission, or omit `email` for non-emailable types).
   * Hidden items are removed from the menu entirely; dividers between
   * groups are still placed correctly between any remaining visible items.
   */
  disabledActions?: DocumentRowAction[];
  /** Optional extra className applied to the trigger button. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Action descriptor table (single source of truth for ordering + labels)
// ---------------------------------------------------------------------------

interface IActionDescriptor {
  readonly key: DocumentRowAction;
  readonly label: string;
  readonly icon: React.ReactElement;
}

/**
 * Group A — content actions (Preview · AI summary · Open file · Find similar).
 * Renders first; followed by a divider when group B has at least one visible item.
 */
const GROUP_A: ReadonlyArray<IActionDescriptor> = [
  { key: 'preview', label: 'Preview', icon: <Eye20Regular /> },
  { key: 'aiSummary', label: 'AI summary', icon: <Sparkle20Regular /> },
  { key: 'openFile', label: 'Open file', icon: <Open20Regular /> },
  { key: 'findSimilar', label: 'Find similar', icon: <Search20Regular /> },
];

/**
 * Group B — share / collaboration actions
 * (Download · Copy link · Email · Open record).
 * Email here is the single-document convenience action (multi-select email
 * lives on the toolbar).
 */
const GROUP_B: ReadonlyArray<IActionDescriptor> = [
  { key: 'download', label: 'Download', icon: <ArrowDownload20Regular /> },
  { key: 'copyLink', label: 'Copy link', icon: <Link20Regular /> },
  { key: 'email', label: 'Email', icon: <Mail20Regular /> },
  { key: 'openRecord', label: 'Open record', icon: <DocumentText20Regular /> },
];

/**
 * Group C — record-management actions
 * (Toggle workspace · Pin to top · Rename · Delete).
 */
const GROUP_C: ReadonlyArray<IActionDescriptor> = [
  { key: 'toggleWorkspace', label: 'Toggle workspace', icon: <PanelRightExpand20Regular /> },
  { key: 'pinToTop', label: 'Pin to top', icon: <Pin20Regular /> },
  { key: 'rename', label: 'Rename', icon: <Rename20Regular /> },
  { key: 'delete', label: 'Delete', icon: <Delete20Regular /> },
];

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Reusable 3-dot row-action menu for document grids.
 *
 * @example
 * ```tsx
 * <DocumentRowMenu
 *   document={{ id: row.id, name: row.name }}
 *   onAction={(action) => handleRowAction(action, row)}
 *   disabledActions={!canDelete(row) ? ['delete'] : undefined}
 * />
 * ```
 */
export const DocumentRowMenu: React.FC<IDocumentRowMenuProps> = ({
  document,
  onAction,
  disabledActions,
  className,
}) => {
  // Build the filtered groups once per render. We intentionally allocate
  // small arrays here (≤4 items each) — cheaper than memoization overhead
  // for this size and avoids React 18-only hooks.
  const disabled = disabledActions ?? [];
  const isVisible = (a: IActionDescriptor): boolean => !disabled.includes(a.key);

  const visibleA = GROUP_A.filter(isVisible);
  const visibleB = GROUP_B.filter(isVisible);
  const visibleC = GROUP_C.filter(isVisible);

  // A divider is rendered only when there is at least one visible item BEFORE
  // and at least one visible item AFTER it — prevents orphaned dividers when
  // a whole group is hidden via `disabledActions`.
  const dividerAfterA = visibleA.length > 0 && (visibleB.length > 0 || visibleC.length > 0);
  const dividerAfterB = visibleB.length > 0 && visibleC.length > 0;

  // Trigger stopPropagation: required by spec FR-SC-02 / FR-DOC-01.
  // We stop the click here so the row's `onClick` (which opens preview) does
  // not also fire when the trigger is clicked.
  const handleTriggerClick = React.useCallback((e: React.MouseEvent<HTMLButtonElement>) => {
    e.stopPropagation();
  }, []);

  const renderItem = (a: IActionDescriptor): React.ReactElement => (
    <MenuItem key={a.key} icon={a.icon} onClick={() => onAction(a.key)}>
      {a.label}
    </MenuItem>
  );

  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <MenuButton
          appearance="subtle"
          size="small"
          icon={<MoreVertical20Regular />}
          aria-label={`More actions for ${document.name}`}
          className={className}
          onClick={handleTriggerClick}
        />
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {visibleA.map(renderItem)}
          {dividerAfterA && <MenuDivider />}
          {visibleB.map(renderItem)}
          {dividerAfterB && <MenuDivider />}
          {visibleC.map(renderItem)}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

DocumentRowMenu.displayName = 'DocumentRowMenu';
