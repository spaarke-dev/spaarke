/**
 * SlashCommandMenu - Floating Fluent v9 Popover with filterable command list.
 *
 * Opens above the SprkChat input bar when the user types '/' to trigger the
 * slash command mode. Renders a filtered, keyboard-navigable list of SprkChat
 * commands grouped by source category: System, Playbook, and Scope.
 *
 * Category grouping:
 * - System Commands   → Built-in commands always available (neutral icon)
 * - Playbook Commands → Dynamic commands from the active playbook (brand/purple accent)
 * - Scope Commands    → Dynamic commands from analysis scopes (teal/secondary accent)
 *
 * Each non-system command item optionally shows a source subtitle indicating
 * which playbook or scope contributed the command (e.g., "From: Legal Research").
 *
 * Positioning: The popover is anchored to `anchorRef` using an absolutely-
 * positioned container rendered in the same stacking context as the anchor.
 * The parent component is responsible for positioning the anchor correctly
 * (typically the chat input container).
 *
 * Keyboard navigation:
 * - ArrowDown / ArrowUp → move focused item (across category boundaries)
 * - Enter               → select focused item
 * - Escape              → dismiss menu (fires onDismiss)
 *
 * Category headers are visual-only (not selectable, skipped by keyboard nav).
 *
 * Accessibility:
 * - role="listbox" on the list container
 * - role="option" on each item
 * - aria-selected on the focused item
 * - aria-activedescendant on the listbox
 * - Category headers use role="presentation" and aria-hidden
 *
 * @see slashCommandMenu.types.ts - Type definitions and DEFAULT_SLASH_COMMANDS
 * @see useSlashCommands.ts       - Hook for managing state and filtering
 * @see ADR-021                   - Fluent v9 tokens; no hard-coded colors; dark mode
 * @see ADR-012                   - Shared Component Library (no Xrm imports)
 */
import * as React from 'react';
import { type SlashCommandMenuProps } from './slashCommandMenu.types';
/**
 * SlashCommandMenu renders a floating command palette above the SprkChat input
 * bar. Commands are grouped by source category (System, Playbook, Scope) with
 * distinct visual headers and optional source subtitles. Supports keyboard
 * navigation across category boundaries and dark mode via Fluent v9 design tokens.
 *
 * @example
 * ```tsx
 * const inputRef = useRef<HTMLTextAreaElement>(null);
 * const anchorRef = useRef<HTMLDivElement>(null);
 * const { menuVisible, filterText, filteredCommands, handleCommandSelect, dismissMenu } =
 *   useSlashCommands({ inputRef });
 *
 * return (
 *   <div ref={anchorRef} style={{ position: 'relative' }}>
 *     <SlashCommandMenu
 *       visible={menuVisible}
 *       commands={filteredCommands}
 *       filterText={filterText}
 *       onSelect={handleCommandSelect}
 *       onDismiss={dismissMenu}
 *       anchorRef={anchorRef}
 *     />
 *     <textarea ref={inputRef} ... />
 *   </div>
 * );
 * ```
 */
export declare const SlashCommandMenu: React.FC<SlashCommandMenuProps>;
//# sourceMappingURL=SlashCommandMenu.d.ts.map