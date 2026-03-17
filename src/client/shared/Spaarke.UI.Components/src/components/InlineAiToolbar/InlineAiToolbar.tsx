/**
 * InlineAiToolbar - Floating absolutely-positioned AI action toolbar.
 *
 * Renders as an absolutely-positioned overlay above user text selections in
 * the Analysis Workspace editor. The toolbar stays in the DOM when hidden
 * (`visible=false`) using `display: 'none'` to avoid layout thrash from
 * repeated mount/unmount cycles.
 *
 * Positioning is driven by `props.position` ({top, left} in pixels relative
 * to the nearest positioned ancestor), computed by the `useInlineAiToolbar`
 * hook (task 012).
 *
 * Renders `InlineAiActions` as its content, passing through the `actions`
 * and `onAction` props.
 *
 * @see InlineAiActions - renders the action button row
 * @see inlineAiToolbar.types.ts - shared type definitions
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-012 - Shared Component Library (no Xrm/ComponentFramework imports)
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import { InlineAiToolbarProps } from './inlineAiToolbar.types';
import { InlineAiActions } from './InlineAiActions';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  '@keyframes toolbarFadeIn': {
    from: { opacity: 0, transform: 'translateY(-4px)' },
    to: { opacity: 1, transform: 'translateY(0)' },
  },
  toolbar: {
    // Absolute positioning — parent element must be position:relative
    position: 'absolute',

    // Visual appearance: card-style surface with border and shadow
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    boxShadow: tokens.shadow8,

    // Compact padding around the button row
    ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalXXS),

    // Sit above editor content (Lexical editor layers are typically < 100)
    zIndex: 1000,

    // Allow toolbar width to size to its content (not stretch to parent width)
    width: 'max-content',
    maxWidth: '480px',

    // Smooth appear animation
    animationName: 'toolbarFadeIn',
    animationDuration: '120ms',
    animationTimingFunction: 'ease-out',
    animationFillMode: 'forwards',
  },
  hidden: {
    // Keep in DOM to avoid layout recalculation on every selection change
    display: 'none',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * InlineAiToolbar is a floating absolutely-positioned container that renders
 * an AI action bar near a text selection. It stays mounted in the DOM when
 * hidden (controlled via the `visible` prop) to prevent flicker and layout
 * recalculation.
 *
 * @example
 * ```tsx
 * <div style={{ position: 'relative' }}>
 *   <LexicalEditor ... />
 *   <InlineAiToolbar
 *     visible={toolbarVisible}
 *     position={{ top: selectionBottom, left: selectionLeft }}
 *     actions={DEFAULT_INLINE_ACTIONS}
 *     onAction={(action, selectedText) => dispatchInlineAction(action, selectedText)}
 *   />
 * </div>
 * ```
 */
export const InlineAiToolbar: React.FC<InlineAiToolbarProps> = ({
  visible,
  position,
  actions,
  onAction,
}) => {
  const styles = useStyles();

  return (
    <div
      className={mergeClasses(styles.toolbar, !visible && styles.hidden)}
      style={{
        top: position.top,
        left: position.left,
      }}
      role="toolbar"
      aria-label="AI inline actions"
      aria-hidden={!visible}
      data-testid="inline-ai-toolbar"
    >
      <InlineAiActions actions={actions} onAction={onAction} />
    </div>
  );
};
