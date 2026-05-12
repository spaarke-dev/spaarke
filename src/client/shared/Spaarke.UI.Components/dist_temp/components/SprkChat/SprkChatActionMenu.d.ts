/**
 * SprkChatActionMenu - Command palette / action menu for the SprkChat component
 *
 * Triggered by typing "/" in the chat input or clicking an action button.
 * Displays available actions organized by category (Playbooks, Actions, Search,
 * Settings) with keyboard navigation (arrow keys + Enter) and fuzzy text filtering.
 *
 * Features:
 * - Grouped actions by category with section headers
 * - Keyboard navigation: ArrowUp/Down to move, Enter to select, Escape to close
 * - Text filtering by label and description (case-insensitive)
 * - Highlight matching text during filtering
 * - Keyboard shortcut hints
 * - Accessible (ARIA roles, focus management)
 *
 * This is a pure UI component — data fetching and action handling are wired
 * externally by the consuming component.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 */
import * as React from 'react';
import { ISprkChatActionMenuProps, ISprkChatActionMenuHandle } from './types';
/**
 * SprkChatActionMenu - Command palette / action menu with keyboard navigation.
 *
 * Displays a categorized list of available actions that can be filtered by
 * typing. Supports full keyboard navigation with ArrowUp/Down, Enter, and
 * Escape keys.
 *
 * @example
 * ```tsx
 * <SprkChatActionMenu
 *   actions={[
 *     { id: "search-docs", label: "Search documents", category: "search" },
 *     { id: "run-playbook", label: "Run playbook", category: "playbooks", shortcut: "Ctrl+P" },
 *   ]}
 *   isOpen={showActionMenu}
 *   onSelect={(action) => handleAction(action)}
 *   onDismiss={() => setShowActionMenu(false)}
 *   filterText={slashFilter}
 * />
 * ```
 */
export declare const SprkChatActionMenu: React.ForwardRefExoticComponent<ISprkChatActionMenuProps & React.RefAttributes<ISprkChatActionMenuHandle>>;
export default SprkChatActionMenu;
//# sourceMappingURL=SprkChatActionMenu.d.ts.map