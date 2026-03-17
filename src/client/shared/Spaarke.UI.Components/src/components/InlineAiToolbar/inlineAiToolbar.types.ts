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
import {
  SparkleRegular,
  TextEditStyle20Regular,
  ArrowExpandRegular,
  CheckmarkCircleRegular,
  Chat20Regular,
} from '@fluentui/react-icons';

// ─────────────────────────────────────────────────────────────────────────────
// Core Action Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Discriminates how an inline action result is delivered:
 * - 'chat'  → dispatches to SprkChat session (streaming response in side pane)
 * - 'diff'  → opens DiffReviewPanel showing before/after comparison
 */
export type InlineAiActionType = 'chat' | 'diff';

/** A single action entry in the inline toolbar. */
export interface InlineAiAction {
  /** Unique stable identifier for the action. */
  id: string;
  /** Display label shown on the toolbar button. */
  label: string;
  /** Fluent UI icon element rendered on the button. */
  icon: React.ReactElement;
  /** Delivery mechanism for the action result. */
  actionType: InlineAiActionType;
  /** Optional tooltip description surfaced to screen readers and hover tooltips. */
  description?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component Props
// ─────────────────────────────────────────────────────────────────────────────

/** Props for the InlineAiToolbar floating container component. */
export interface InlineAiToolbarProps {
  /** Ordered list of action buttons to render. */
  actions: InlineAiAction[];
  /** Pixel coordinates for absolute positioning relative to the host container. */
  position: { top: number; left: number };
  /** Whether the toolbar is visible. Controls render/display state. */
  visible: boolean;
  /**
   * Callback fired when the user clicks an action button.
   * Receives the action definition and the currently selected text at the time of click.
   */
  onAction: (action: InlineAiAction, selectedText: string) => void;
}

/** Props for the InlineAiActions inner list component (renders the button row). */
export interface InlineAiActionsProps {
  /** Ordered list of action buttons to render. */
  actions: InlineAiAction[];
  /**
   * Callback fired when the user clicks an action button.
   * Receives the action definition and the currently selected text at the time of click.
   */
  onAction: (action: InlineAiAction, selectedText: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// BroadcastChannel Event Type
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BroadcastChannel payload for cross-pane inline action dispatch.
 * Emitted by InlineAiToolbar; consumed by SprkChat side pane and
 * DiffReviewPanel to receive context about which action was triggered.
 *
 * Channel name: 'sprk-inline-action' (matches SprkChatBridge conventions)
 */
export interface InlineActionBroadcastEvent {
  /** Event discriminator. Always 'inline_action'. */
  type: 'inline_action';
  /** The action id that was triggered (e.g., 'summarize', 'simplify'). */
  actionId: string;
  /** Delivery mechanism — determines which pane handles the event. */
  actionType: InlineAiActionType;
  /** Human-readable action label (for logging and UI display in receiving pane). */
  label: string;
  /** The text that was selected when the action was triggered. */
  selectedText: string;
  /** Optional SprkChat session ID to route the action to a specific session. */
  sessionId?: string;
}

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
export const DEFAULT_INLINE_ACTIONS: InlineAiAction[] = [
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
