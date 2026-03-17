/**
 * SlashCommandMenu Types
 *
 * Type definitions for the SlashCommandMenu component and useSlashCommands hook.
 * The slash command menu opens in SprkChat when the user types '/' as the first
 * character in the input bar, showing a filterable, keyboard-navigable list of
 * AI commands.
 *
 * System commands are always present; playbook capabilities from the context
 * mapping endpoint are appended as dynamic commands via props.
 *
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 * @see ADR-021 - Fluent UI v9 tokens; dark mode required
 * @see spec-FR-09 - Slash command menu with type-ahead filtering and keyboard nav
 */

import React from 'react';
import {
  SparkleRegular,
  SearchRegular,
  ChatRegular,
  QuestionCircleRegular,
  BrainCircuitRegular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Core Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Category discriminator for grouping slash commands in the menu.
 * - 'system'   → Built-in SprkChat commands always available
 * - 'playbook' → Dynamic commands from context mapping (playbook capabilities)
 */
export type SlashCommandCategory = 'system' | 'playbook';

/**
 * A single slash command entry shown in the SlashCommandMenu.
 * Commands are identified by their trigger string (e.g., '/summarize').
 */
export interface SlashCommand {
  /** Unique stable identifier for the command. */
  id: string;

  /** Display label shown in the menu item (e.g., 'Summarize'). */
  label: string;

  /** Short description of what the command does, shown below the label. */
  description: string;

  /**
   * The trigger string the user types to invoke this command (e.g., '/summarize').
   * Must start with '/'.
   */
  trigger: string;

  /** Optional Fluent UI icon element rendered alongside the command label. */
  icon?: React.ReactElement;

  /**
   * Grouping category for the command.
   * 'system' commands are always visible; 'playbook' commands are context-specific.
   */
  category: SlashCommandCategory;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component Props
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for the SlashCommandMenu floating popover component.
 *
 * The parent component (SprkChatInput) is responsible for:
 * - Detecting '/' input to set open=true
 * - Extracting the filter text after '/' to pass as filterText
 * - Providing an anchorRef that the popover positions itself relative to
 */
export interface SlashCommandMenuProps {
  /** Whether the menu popover is visible. */
  visible: boolean;

  /**
   * The ordered list of commands to display (static + dynamic combined).
   * The parent or useSlashCommands hook provides the merged, filtered set.
   */
  commands: SlashCommand[];

  /**
   * The current filter text (text after the '/' character in the input).
   * Used for highlighting matches in the command label.
   * Filtering itself is done by the useSlashCommands hook; this prop
   * is used purely for highlighting the matched substring.
   */
  filterText: string;

  /** Callback fired when the user selects a command (click or Enter). */
  onSelect: (command: SlashCommand) => void;

  /** Callback fired when the menu should close (Esc pressed or click outside). */
  onDismiss: () => void;

  /**
   * Ref to the anchor element (typically the chat input container).
   * The popover positions itself above this element.
   */
  anchorRef: React.RefObject<HTMLElement>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Default System Commands
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Default set of built-in SprkChat slash commands.
 * These are always available regardless of analysis context.
 *
 * Commands:
 * - /summarize → Summarizes the current analysis document
 * - /analyze   → Runs a general analysis on the current document
 * - /ask       → Opens the chat pre-filled for a free-form question
 * - /explain   → Explains the selected content or current document
 * - /plan      → Generates a structured plan from the document content
 * - /rewrite   → Rewrites the selected content with improvements
 * - /translate → Translates selected content to a target language
 * - /help      → Shows available commands and usage
 */
export const DEFAULT_SLASH_COMMANDS: SlashCommand[] = [
  {
    id: 'summarize',
    label: 'Summarize',
    description: 'Summarize the current analysis document',
    trigger: '/summarize',
    icon: React.createElement(SparkleRegular),
    category: 'system',
  },
  {
    id: 'analyze',
    label: 'Analyze',
    description: 'Run a general analysis on the current document',
    trigger: '/analyze',
    icon: React.createElement(BrainCircuitRegular),
    category: 'system',
  },
  {
    id: 'ask',
    label: 'Ask',
    description: 'Ask a question about the current document',
    trigger: '/ask',
    icon: React.createElement(ChatRegular),
    category: 'system',
  },
  {
    id: 'explain',
    label: 'Explain',
    description: 'Explain the selected content or current document',
    trigger: '/explain',
    icon: React.createElement(SearchRegular),
    category: 'system',
  },
  {
    id: 'plan',
    label: 'Plan',
    description: 'Generate a structured plan from the document content',
    trigger: '/plan',
    icon: React.createElement(SparkleRegular),
    category: 'system',
  },
  {
    id: 'rewrite',
    label: 'Rewrite',
    description: 'Rewrite the selected content with improvements',
    trigger: '/rewrite',
    icon: React.createElement(SparkleRegular),
    category: 'system',
  },
  {
    id: 'translate',
    label: 'Translate',
    description: 'Translate selected content to a target language',
    trigger: '/translate',
    icon: React.createElement(SparkleRegular),
    category: 'system',
  },
  {
    id: 'help',
    label: 'Help',
    description: 'Show available commands and usage',
    trigger: '/help',
    icon: React.createElement(QuestionCircleRegular),
    category: 'system',
  },
];
