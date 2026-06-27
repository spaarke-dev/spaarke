/**
 * HelpAffordance.tsx — R6 task 085 / D-D-06 (Pillar 8 `/help` UI affordance).
 *
 * Discoverable Fluent v9 icon-button + Tooltip that opens the `CommandHelpPanel`
 * (built by task 081). Lives in the chat input bar area so users who don't know
 * slash syntax can still discover the closed Pillar 8 vocabulary (6 hard +
 * 4 soft slashes + 3 reference shapes).
 *
 * ## Wiring
 *
 * The component is intentionally minimal — it's a controlled affordance with
 * a single `onClick` prop. The host (`ConversationPane`) owns the
 * `helpPanelOpen` state added in Wave D-G1 by task 081 and toggles it via
 * the callback. The same drawer surface is shared with the `/help` hard
 * slash so users see one consistent help surface regardless of how they
 * opened it.
 *
 * ## Placement (per task 085 evidence: Option A)
 *
 * Rendered as an absolutely-positioned overlay button anchored to the bottom-
 * right of the chat region — near where SprkChat's own send button sits.
 * Absolute positioning was chosen over wrapping `<SprkChat>` in a custom
 * container because SprkChat owns its input bar internally and wrapping
 * would force a refactor (NFR-11: existing input bar behavior must be
 * unchanged; additive UX only).
 *
 * ## Accessibility
 *
 *   - `aria-label`: "Show available commands (/help)"
 *   - Tooltip via Fluent v9 `Tooltip` with `relationship="label"` — the
 *     tooltip text is the accessible name so screen readers announce it.
 *   - Keyboard-focusable (`Button` is button by default; tab navigation
 *     works without extra wiring).
 *   - Screen-reader-only visually hidden text duplicates the action so
 *     compact icon-only buttons still announce clearly even without
 *     tooltip activation.
 *
 * ## ADR compliance
 *
 *   - ADR-012 Fluent v9 only — no Fluent v8 imports.
 *   - ADR-021 semantic tokens only — no hardcoded colors. The `subtle`
 *     appearance + transparent default background defer to the theme.
 *   - ADR-022 functional component + hooks.
 *   - ADR-029 frontend-only — zero BFF surface; publish-size delta = 0 MB.
 *
 * @see CommandHelpPanel.tsx — the drawer this button opens (task 081)
 * @see ConversationPane.tsx — owns `helpPanelOpen` state + renders this button
 */

import * as React from 'react';
import {
  Button,
  Tooltip,
  makeStyles,
  tokens,
  mergeClasses,
} from '@fluentui/react-components';
import { QuestionCircleRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Anchored overlay container. Sits at the top-right of its parent (the
   * chat region) so it's discoverable without interrupting SprkChat's
   * input bar layout. The parent must be `position: relative` for this to
   * anchor correctly — `ConversationPane.sprkChatFlex` is a flex container,
   * but the wrapper around `<SprkChat>` is `position: relative`-ready.
   */
  root: {
    position: 'absolute',
    top: tokens.spacingVerticalS,
    right: tokens.spacingHorizontalS,
    zIndex: 1,
  },
  /**
   * Screen-reader-only visually hidden text. Used to duplicate the
   * action text for assistive tech even when the tooltip is not
   * activated (some screen readers may not pick up Tooltip content
   * synchronously).
   */
  srOnly: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    padding: 0,
    margin: '-1px',
    overflow: 'hidden',
    clip: 'rect(0, 0, 0, 0)',
    whiteSpace: 'nowrap',
    border: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface HelpAffordanceProps {
  /**
   * Called when the user activates the affordance (click, Enter, or Space).
   * The host wires this to `setHelpPanelOpen(true)` so the
   * `CommandHelpPanel` drawer (task 081) opens.
   */
  onClick: () => void;

  /**
   * Optional className for the root container. Allows the host to override
   * positioning if a layout deviates from the default top-right anchor
   * (e.g. embedding in a toolbar row instead of overlaying the chat).
   */
  className?: string;

  /**
   * Optional disabled flag. Rarely used — the affordance should always be
   * available — but supported for completeness (e.g. while help content
   * is loading).
   */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ARIA_LABEL = 'Show available commands (/help)';
const TOOLTIP_CONTENT = 'Show available commands (/help)';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Renders the Pillar 8 `/help` discovery button. Fluent v9 subtle Button
 * with a question-circle icon + Tooltip. Dark-mode safe per ADR-021.
 */
export function HelpAffordance(
  props: HelpAffordanceProps,
): React.JSX.Element {
  const { onClick, className, disabled } = props;
  const styles = useStyles();

  const handleClick = React.useCallback((): void => {
    if (disabled === true) return;
    onClick();
  }, [disabled, onClick]);

  return (
    <div className={mergeClasses(styles.root, className)}>
      <Tooltip content={TOOLTIP_CONTENT} relationship="label">
        <Button
          appearance="subtle"
          icon={<QuestionCircleRegular />}
          aria-label={ARIA_LABEL}
          disabled={disabled}
          onClick={handleClick}
          data-testid="help-affordance"
        />
      </Tooltip>
      <span className={styles.srOnly}>{ARIA_LABEL}</span>
    </div>
  );
}
