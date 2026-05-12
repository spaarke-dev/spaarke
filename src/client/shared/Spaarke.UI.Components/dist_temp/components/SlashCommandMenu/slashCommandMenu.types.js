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
import { SparkleRegular, SearchRegular, ChatRegular, QuestionCircleRegular, BrainCircuitRegular, } from '@fluentui/react-icons';
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
export const DEFAULT_SLASH_COMMANDS = [
    {
        id: 'summarize',
        label: 'Summarize',
        description: 'Summarize the current analysis document',
        trigger: '/summarize',
        icon: React.createElement(SparkleRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'analyze',
        label: 'Analyze',
        description: 'Run a general analysis on the current document',
        trigger: '/analyze',
        icon: React.createElement(BrainCircuitRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'ask',
        label: 'Ask',
        description: 'Ask a question about the current document',
        trigger: '/ask',
        icon: React.createElement(ChatRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'explain',
        label: 'Explain',
        description: 'Explain the selected content or current document',
        trigger: '/explain',
        icon: React.createElement(SearchRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'plan',
        label: 'Plan',
        description: 'Generate a structured plan from the document content',
        trigger: '/plan',
        icon: React.createElement(SparkleRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'rewrite',
        label: 'Rewrite',
        description: 'Rewrite the selected content with improvements',
        trigger: '/rewrite',
        icon: React.createElement(SparkleRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'translate',
        label: 'Translate',
        description: 'Translate selected content to a target language',
        trigger: '/translate',
        icon: React.createElement(SparkleRegular),
        category: 'system',
        source: 'system',
    },
    {
        id: 'help',
        label: 'Help',
        description: 'Show available commands and usage',
        trigger: '/help',
        icon: React.createElement(QuestionCircleRegular),
        category: 'system',
        source: 'system',
    },
];
//# sourceMappingURL=slashCommandMenu.types.js.map