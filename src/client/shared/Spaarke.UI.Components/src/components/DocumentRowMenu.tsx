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
 * Since C-9 (spaarke-ontology-platform-r1 task 052) the menu scaffold, group + divider logic and item rendering live in
 * the shared `RowActionMenu`; this file keeps only the document action table (labels, icons, ordering) and the
 * `disabledActions` mapping, so the rendered menu is unchanged.
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
import { RowActionMenu } from './RowActionMenu';
import type { RowActionDescriptor } from './RowActionMenu';
import {
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
  // `disabledActions` HIDES the listed actions (see the prop doc); RowActionMenu drops hidden items and places
  // dividers only between groups that still have a visible item.
  const disabled = disabledActions ?? [];
  const groups: RowActionDescriptor<DocumentRowAction>[][] = [GROUP_A, GROUP_B, GROUP_C].map(group =>
    group.map(a => ({ key: a.key, label: a.label, icon: a.icon, hidden: disabled.includes(a.key) }))
  );

  // Trigger stopPropagation: required by spec FR-SC-02 / FR-DOC-01. We stop the click here so the row's `onClick`
  // (which opens preview) does not also fire when the trigger is clicked.
  return (
    <RowActionMenu<DocumentRowAction>
      groups={groups}
      onAction={onAction}
      ariaLabel={`More actions for ${document.name}`}
      triggerClassName={className}
      stopTriggerPropagation
    />
  );
};

DocumentRowMenu.displayName = 'DocumentRowMenu';
