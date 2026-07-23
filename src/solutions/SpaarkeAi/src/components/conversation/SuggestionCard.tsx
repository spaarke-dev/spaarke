/**
 * SuggestionCard — the proactive-suggestion renderer branch on the Assistant's
 * card surface (spaarke-notification-spine-r1 task 051 / FR-16).
 *
 * A `kind=suggestion` outbox row (task 050 producer) is delivered to the host
 * via the task-021 `@spaarke/notifications` subscriber, re-grounded through the
 * BFF (`GET /api/notifications/pending`, task 022), and rendered here as a
 * compact, bordered card. It is a SIBLING of `ConsumerChips.tsx`, NOT an
 * extension: a suggestion arrives asynchronously from the Layer-C spine,
 * independent of an active chat-session dispatch turn.
 *
 * Layout (UAT 2026-07-22): the card is a container with TWO controls — a main
 * clickable region (click → open the regarding record) and a small dismiss 'x'
 * (like a tab close). They are SIBLINGS, never nested buttons. The former "open"
 * arrow (→) affordance was removed — the whole message is clickable, so the arrow
 * was redundant. The hover highlight lives ONLY on the clickable main region.
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors. Correct in both
 * light and dark themes purely through semantic token resolution.
 * ADR-039: the client carries ZERO routing logic — `actionHint` is presentation
 * only; the host resolves it to a Binding (task 052).
 */

import * as React from "react";
import { Button, makeStyles, shorthands, tokens } from "@fluentui/react-components";
import { LightbulbFilamentRegular, DismissRegular } from "@fluentui/react-icons";

/**
 * The DISPLAY fields a suggestion card renders. A structural subset of the
 * task-013 `SuggestionEnvelope` (mirrored locally — the hook owns the full
 * envelope + the outbox row id used for the click-time re-ground/re-check).
 */
export interface SuggestionCardModel {
  /** Stable identity — drives the test id and the React key. */
  readonly suggestionId: string;
  /** Short display title (e.g. "Review Acme v. Beta"). */
  readonly title: string;
  /** OPTIONAL access-checked excerpt (usually absent — the producer omits it conservatively). */
  readonly snippet?: string;
  /** Presentation only (ADR-039). NEVER used to route — the host resolves the action (task 052). */
  readonly actionHint: string;
}

const useStyles = makeStyles({
  // Bordered card container — a flex row holding the clickable main region + the
  // dismiss 'x'. Tokens only, so dark mode adapts automatically. NO hover on the
  // container itself (UAT 2026-07-22): the hover highlight belongs to the clickable
  // main region, not the whole card / not the dismiss control.
  card: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalXS,
    width: "100%",
    minWidth: 0,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border("1px", "solid", tokens.colorBrandStroke2),
    boxShadow: tokens.shadow2,
  },
  // The clickable "open the record" region — fills the row; brand-accented; the
  // hover/pressed highlight lives here (this is the affordance the user clicks).
  main: {
    flexGrow: 1,
    minWidth: 0,
    justifyContent: "flex-start",
    color: tokens.colorBrandForeground2,
    backgroundColor: "transparent",
    ...shorthands.border("none"),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
      color: tokens.colorBrandForeground2Hover,
    },
    ":hover:active": {
      backgroundColor: tokens.colorBrandBackground2Pressed,
      color: tokens.colorBrandForeground2Pressed,
    },
    ":disabled": {
      backgroundColor: "transparent",
      color: tokens.colorNeutralForegroundDisabled,
    },
  },
  // The small dismiss 'x' — a subtle brand-colored icon button (mirrors the tab
  // close affordance). Not the primary action, so it stays quiet until hovered.
  dismiss: {
    minWidth: "auto",
    color: tokens.colorBrandForeground2,
    ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalXS),
    ...shorthands.margin("0", tokens.spacingHorizontalXS, "0", "0"),
    ":hover": { color: tokens.colorBrandForeground2Hover },
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
 * A single proactive-suggestion card. Stateless — the host (`useSuggestionCards`)
 * owns the list, the pre-mount expiry filter, and both the re-fetch-before-dispatch
 * click flow and the dismiss flow. This component ONLY renders + reports the clicks.
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
          <span className={styles.title}>
            <LightbulbFilamentRegular aria-hidden />
            {" "}
            {suggestion.title}
          </span>
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
      />
    </div>
  );
}
