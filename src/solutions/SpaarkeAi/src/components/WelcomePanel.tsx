/**
 * WelcomePanel.tsx — Welcome heading shell for Spaarke AI (no-context launch)
 *
 * **R3 task 068 (smoke remediation — Bug 1 + UX-A)**:
 *
 * Reduced to a minimal heading-only shell. Previously this component owned BOTH
 * the central "How can I help you today?" prompt AND a "Recent Conversations"
 * list of the user's last 5 sessions. That structure had two operator-reported
 * problems:
 *
 *   1. (Bug 1) WelcomePanel and SprkChat were mutually exclusive in
 *      ConversationPane's render path — so on cold load the chat input was
 *      never mounted and the user had nowhere to type. SprkChat is now
 *      ALWAYS rendered by ConversationPane; this component contributes the
 *      welcome heading ABOVE it when in the welcome stage.
 *
 *   2. (UX-A) The "Recent Conversations" section duplicated the History
 *      overlay (HistoryRegular icon in PaneHeader → `<HistoryOverlay>` from
 *      task 022). The agreed pattern is "start blank; use History overlay to
 *      resume past sessions". The Recent Conversations section and its
 *      `useRecentSessions` hook have been removed from this component.
 *
 * The component is intentionally tiny. It no longer fetches data, does not
 * call useAiSession(), and has no auth surface. Callers (ConversationPane)
 * render it as the welcome heading above the always-mounted SprkChat.
 *
 * Design constraints:
 * - ADR-012: No new shared components (in-solution component).
 * - ADR-021: Fluent v9 semantic tokens only — no hardcoded colors / no rgba literals.
 * - ADR-021: Dark mode works without additional CSS (tokens adapt automatically).
 * - ADR-028: No auth surface — this component does not touch the BFF.
 *
 * @see ADR-021 — Fluent v9 design system, dark mode, semantic tokens
 * @see ConversationPane.tsx — renders this component above SprkChat when showWelcomePanel === true
 * @see HistoryOverlay — owns the "resume past session" flow (task 022)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  header: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    flexShrink: 0,
  },
  headerSubtitle: {
    color: tokens.colorNeutralForeground1,
    textAlign: "center",
  },
});

// ---------------------------------------------------------------------------
// WelcomePanel
// ---------------------------------------------------------------------------

/**
 * WelcomePanel — heading-only shell shown ABOVE SprkChat when Spaarke AI opens
 * with no entity context and no session.
 *
 * R3 task 068: stripped to the central prompt. The chat input lives below this
 * component, rendered unconditionally by ConversationPane (Bug 1 fix). Session
 * resume is handled by the HistoryOverlay reached via the PaneHeader history
 * icon (task 022) — no in-page resume cards remain here.
 *
 * Has no props. The component performs no I/O.
 */
export function WelcomePanel(): React.JSX.Element {
  const styles = useStyles();

  return (
    <div className={styles.header} role="region" aria-label="Spaarke AI welcome">
      <Text as="h2" size={400} weight="semibold" className={styles.headerSubtitle}>
        How can I help you today?
      </Text>
    </div>
  );
}
