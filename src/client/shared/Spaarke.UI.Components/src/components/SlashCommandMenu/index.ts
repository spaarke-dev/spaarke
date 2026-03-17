/**
 * SlashCommandMenu - Barrel Exports
 *
 * Re-exports all public surface for the SlashCommandMenu component group:
 *   - SlashCommandMenu        (floating Fluent v9 Popover command palette)
 *   - Types and interfaces   (SlashCommand, SlashCommandMenuProps, SlashCommandCategory)
 *   - DEFAULT_SLASH_COMMANDS (built-in system commands: /summarize, /analyze, etc.)
 *
 * Consumers import from '@spaarke/ui-components':
 *   import { SlashCommandMenu, DEFAULT_SLASH_COMMANDS } from '@spaarke/ui-components';
 *
 * The useSlashCommands hook is exported separately from the hooks index:
 *   import { useSlashCommands } from '@spaarke/ui-components';
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-021 - Fluent UI v9 tokens; dark mode required
 */

export { SlashCommandMenu } from './SlashCommandMenu';
export type {
  SlashCommandCategory,
  SlashCommand,
  SlashCommandMenuProps,
} from './slashCommandMenu.types';
export { DEFAULT_SLASH_COMMANDS } from './slashCommandMenu.types';
