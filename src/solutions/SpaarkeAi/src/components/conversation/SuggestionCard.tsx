/**
 * SuggestionCard — the proactive-suggestion renderer branch on the Assistant's
 * card surface (spaarke-notification-spine-r1 task 051 / FR-16).
 *
 * A `kind=suggestion` outbox row (task 050 producer) is delivered to the host
 * via the task-021 `@spaarke/notifications` subscriber, re-grounded through the
 * BFF (`GET /api/notifications/pending`, task 022), and rendered here as a
 * lightweight ROW inside the host's suggestion panel. It is a SIBLING of
 * `ConsumerChips.tsx`, NOT an extension: a suggestion arrives asynchronously from
 * the Layer-C spine, independent of an active chat-session dispatch turn.
 *
 * Layout (UAT 2026-07-24): the row is borderless — its visual container is the
 * panel in `useSuggestionCards` (one bordered panel, hairline row dividers) rather
 * than 5 stacked boxes. Two controls: a main clickable region (click → open the
 * regarding record) and a small dismiss 'x' that reveals on row hover/focus (like a
 * tab close). They are SIBLINGS, never nested buttons. The former per-row lightbulb
 * icon + the always-on brand-blue box/shadow were removed (they made the stack read
 * as a cluttered wall); the single suggestion glyph now lives once on the panel header.
 * The hover highlight is a SOFT NEUTRAL on the clickable main region only.
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors. Correct in both
 * light and dark themes purely through semantic token resolution.
 * ADR-039: the client carries ZERO routing logic — `actionHint` is presentation
 * only; the host resolves it to a Binding (task 052).
 */

import * as React from "react";
import { Button, makeStyles, shorthands, tokens } from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";

/**
 * The DISPLAY fields a suggestion card renders. A structural subset of the
 * task-013 `SuggestionEnvelope` (mirrored locally — the hook owns the full
 * envelope + the outbox row id used for the click-time re-ground/re-check).
 */
export interface SuggestionCardModel {
  /** Stable identity — drives the test id and the React key. */
  readonly suggestionId: string;
  /** Short display title (e.g. "Acme v. Beta"). The host strips any redundant leading verb. */
  readonly title: string;
  /** OPTIONAL access-checked excerpt (usually absent — the producer omits it conservatively). */
  readonly snippet?: string;
  /** Presentation only (ADR-039). NEVER used to route — the host resolves the action (task 052). */
  readonly actionHint: string;
}

const useStyles = makeStyles({
  // Borderless row. The visual container (border + radius) is the panel in
  // useSuggestionCards; each suggestion is a light row, not a boxed card (UAT
  // 2026-07-24). The dismiss 'x' is hidden until the row is hovered/focused.
  card: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalXS,
    width: "100%",
    minWidth: 0,
    ":hover [data-suggestion-dismiss]": { opacity: 1 },
    ":focus-within [data-suggestion-dismiss]": { opacity: 1 },
  },
  // The clickable "open the record" region — fills the row. Neutral text with a
  // SOFT NEUTRAL hover (not the old brand-blue wall). The hover/pressed highlight
  // lives here (this is the affordance the user clicks).
  main: {
    flexGrow: 1,
    minWidth: 0,
    justifyContent: "flex-start",
    color: tokens.colorNeutralForeground1,
    backgroundColor: "transparent",
    borderRadius: tokens.borderRadiusMedium,
    ...shorthands.border("none"),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      color: tokens.colorNeutralForeground1,
    },
    ":hover:active": {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
    ":disabled": {
      backgroundColor: "transparent",
      color: tokens.colorNeutralForegroundDisabled,
    },
  },
  // The small dismiss 'x' — quiet by default (revealed on row hover/focus via the
  // container rule above), muted neutral, colors up on its own hover. Mirrors a tab
  // close, not a primary action.
  dismiss: {
    minWidth: "auto",
    opacity: 0,
    color: tokens.colorNeutralForeground3,
    ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalXS),
    ...shorthands.margin("0", tokens.spacingHorizontalXS, "0", "0"),
    ":hover": { color: tokens.colorNeutralForeground1 },
    ":focus": { opacity: 1 },
    ":disabled": { color: tokens.colorNeutralForegroundDisabled },
  },
  // Text column — title over an optional muted snippet. Ellipsize so a long
  // title never breaks the single-row layout.
  text: {
    display: "flex",
    flexDirection: "column",
    alignItems: "flex-start",
    minWidth: 0,
    rowGap: tokens.spacingVerticalXXS,
  },
  title: {
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "100%",
  },
  snippet: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    maxWidth: "100%",
  },
});

export interface SuggestionCardProps {
  /** The suggestion to render. */
  readonly suggestion: SuggestionCardModel;
  /** Disables both controls (e.g. while any suggestion's re-ground/dispatch is in flight). */
  readonly disabled?: boolean;
  /** Click the main region → the host re-grounds via the BFF, then routes through the shared dispatch (task 052). */
  readonly onAction: () => void;
  /** Click the dismiss 'x' → the host dismisses the outbox row (server-persisted) and removes the card. */
  readonly onDismiss: () => void;
}

/**
 * A single proactive-suggestion row. Stateless — the host (`useSuggestionCards`)
 * owns the list, the panel chrome, the pre-mount expiry filter, and both the
 * re-fetch-before-dispatch click flow and the dismiss flow. This component ONLY
 * renders + reports the clicks.
 */
export function SuggestionCard(props: SuggestionCardProps): React.JSX.Element {
  const { suggestion, disabled, onAction, onDismiss } = props;
  const styles = useStyles();

  return (
    <div className={styles.card}>
      <Button
        className={styles.main}
        appearance="transparent"
        size="small"
        shape="rounded"
        disabled={disabled === true}
        onClick={onAction}
        aria-label={`Open suggestion: ${suggestion.title}`}
        data-testid={`suggestion-card-${suggestion.suggestionId}`}
        data-suggestion-id={suggestion.suggestionId}
      >
        <span className={styles.text}>
          <span className={styles.title}>{suggestion.title}</span>
          {suggestion.snippet ? (
            <span className={styles.snippet}>{suggestion.snippet}</span>
          ) : null}
        </span>
      </Button>
      <Button
        className={styles.dismiss}
        appearance="transparent"
        size="small"
        icon={<DismissRegular />}
        disabled={disabled === true}
        onClick={onDismiss}
        aria-label={`Dismiss suggestion: ${suggestion.title}`}
        data-testid={`suggestion-dismiss-${suggestion.suggestionId}`}
        data-suggestion-dismiss=""
      />
    </div>
  );
}
