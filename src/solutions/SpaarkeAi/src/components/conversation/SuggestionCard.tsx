/**
 * SuggestionCard — the proactive-suggestion renderer branch on the Assistant's
 * card surface (spaarke-notification-spine-r1 task 051 / FR-16).
 *
 * A `kind=suggestion` outbox row (task 050 producer) is delivered to the host
 * via the task-021 `@spaarke/notifications` subscriber, re-grounded through the
 * BFF (`GET /api/notifications/pending`, task 022), and rendered here as a
 * compact, bordered card. It is a SIBLING of `ConsumerChips.tsx`, NOT an
 * extension: a suggestion arrives asynchronously from the Layer-C spine,
 * independent of an active chat-session dispatch turn (which is why it cannot
 * ride `ConsumerChips`'s SSE-carried chip list). It reuses the SAME visual
 * language (the `chip` style-class token choices, verbatim in spirit) and the
 * SAME `dispatchConsumer`/`launchSurface` re-entry (via the host's
 * `onSuggestionAction` plug-point — task 052's contract). It is a renderer
 * BRANCH, not a new dispatch mechanism.
 *
 * ADR-021: Fluent UI v9 design tokens ONLY — no hardcoded colors. The card is
 * correct in both light and dark themes purely through semantic token
 * resolution, exactly as `ConsumerChips.tsx`'s `chip` class already demonstrates.
 * ADR-039: the client carries ZERO routing logic — `actionHint` is presentation
 * only; the host resolves it to a Binding (task 052).
 */

import * as React from "react";
import { Button, makeStyles, shorthands, tokens } from "@fluentui/react-components";
import { LightbulbFilamentRegular, ArrowRightRegular } from "@fluentui/react-icons";

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
  // Mirrors ConsumerChips.tsx's `chip` class token choices (task 043 SNS card):
  // a bordered, rounded surface — tokens only, so dark mode adapts automatically.
  card: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    columnGap: tokens.spacingHorizontalS,
    width: "100%",
    minWidth: 0,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground2,
    ...shorthands.border("1px", "solid", tokens.colorBrandStroke2),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    fontWeight: tokens.fontWeightRegular,
    boxShadow: tokens.shadow2,
    ":hover": {
      backgroundColor: tokens.colorBrandBackground2Hover,
      color: tokens.colorBrandForeground2Hover,
      boxShadow: tokens.shadow4,
    },
    ":hover:active": {
      backgroundColor: tokens.colorBrandBackground2Pressed,
      color: tokens.colorBrandForeground2Pressed,
    },
    ":disabled": {
      backgroundColor: tokens.colorNeutralBackgroundDisabled,
      color: tokens.colorNeutralForegroundDisabled,
      boxShadow: "none",
      ...shorthands.borderColor(tokens.colorNeutralStrokeDisabled),
    },
  },
  // Text column — title over an optional muted snippet. Ellipsize so a long
  // title never breaks the single-row card layout.
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
  /** Disables the card (e.g. while any suggestion's re-ground/dispatch is in flight). */
  readonly disabled?: boolean;
  /** Click → the host re-grounds via the BFF, then routes through the shared dispatch (task 052). */
  readonly onAction: () => void;
}

/**
 * A single proactive-suggestion card. Stateless — the host (`useSuggestionCards`)
 * owns the list, the pre-mount expiry filter, and the re-fetch-before-dispatch
 * click flow. This component ONLY renders + reports the click.
 */
export function SuggestionCard(props: SuggestionCardProps): React.JSX.Element {
  const { suggestion, disabled, onAction } = props;
  const styles = useStyles();

  return (
    <Button
      className={styles.card}
      appearance="secondary"
      size="small"
      shape="rounded"
      icon={<ArrowRightRegular />}
      iconPosition="after"
      disabled={disabled === true}
      onClick={onAction}
      aria-label={`Suggestion: ${suggestion.title}`}
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
  );
}
