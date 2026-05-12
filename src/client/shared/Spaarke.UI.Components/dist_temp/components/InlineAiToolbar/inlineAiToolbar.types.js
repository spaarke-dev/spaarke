/**
 * InlineAiToolbar Types
 *
 * Type definitions for the InlineAiToolbar component and its sub-components.
 * The toolbar floats near user text selections in the Analysis Workspace editor,
 * offering quick AI actions that either send to SprkChat ('chat') or open the
 * DiffReviewPanel ('diff').
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 16 APIs only
 */
import React from 'react';
import { SparkleRegular, TextEditStyle20Regular, ArrowExpandRegular, CheckmarkCircleRegular, Chat20Regular, } from '@fluentui/react-icons';
// ─────────────────────────────────────────────────────────────────────────────
// Default Actions
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Default set of inline AI actions shown in the toolbar.
 *
 * Action types:
 * - summarize  → chat  (streams summary into SprkChat)
 * - simplify   → diff  (rewrites selection; shows before/after in DiffReviewPanel)
 * - expand     → diff  (expands selection; shows before/after in DiffReviewPanel)
 * - fact-check → chat  (streams fact-check analysis into SprkChat)
 * - ask        → chat  (opens SprkChat pre-filled with selection as context)
 */
export const DEFAULT_INLINE_ACTIONS = [
    {
        id: 'summarize',
        label: 'Summarize',
        icon: React.createElement(SparkleRegular),
        actionType: 'chat',
        description: 'Summarize the selected text',
    },
    {
        id: 'simplify',
        label: 'Simplify',
        icon: React.createElement(TextEditStyle20Regular),
        actionType: 'diff',
        description: 'Simplify the selected text and show changes',
    },
    {
        id: 'expand',
        label: 'Expand',
        icon: React.createElement(ArrowExpandRegular),
        actionType: 'diff',
        description: 'Expand the selected text and show changes',
    },
    {
        id: 'fact-check',
        label: 'Fact-Check',
        icon: React.createElement(CheckmarkCircleRegular),
        actionType: 'chat',
        description: 'Fact-check the selected text',
    },
    {
        id: 'ask',
        label: 'Ask',
        icon: React.createElement(Chat20Regular),
        actionType: 'chat',
        description: 'Ask a question about the selected text',
    },
];
//# sourceMappingURL=inlineAiToolbar.types.js.map