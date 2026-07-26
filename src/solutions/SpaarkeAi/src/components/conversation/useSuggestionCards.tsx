/**
 * useSuggestionCards — controller for the proactive-suggestion renderer branch
 * (spaarke-notification-spine-r1 task 051 / FR-16). The hook analog of
 * `useConsumerChips.tsx`, for the Layer-C SPINE delivery path rather than the
 * chat-session SSE chip path.
 *
 * Lifecycle:
 *   1. Subscribe to `kind=suggestion` on the task-021 `@spaarke/notifications`
 *      client (via the injected `subscribe`). A live SignalR push is SIGNAL-ONLY
 *      (no envelope on the wire — NFR-02/03), so every signal (and the initial
 *      mount) re-grounds the full pending set from the BFF
 *      (`GET /api/notifications/pending`, task 022) — which is oid-scoped and
 *      read-time expiry-filtered SERVER-side (the access check).
 *   2. Pre-mount expiry filter (ADR-041 / spec): a card whose envelope
 *      `expiresAt` has passed is EXCLUDED from the rendered set entirely — not
 *      rendered-then-disabled. Testable independent of the click path.
 *   3. Click → re-fetch/re-ground via the BFF BEFORE acting (freshness + access
 *      re-check): confirm the outbox row is STILL pending; only then hand the
 *      fresh envelope to the host's `onSuggestionAction` (task 052's re-entry
 *      into the EXISTING `dispatchConsumer`/`launchSurface` — the client carries
 *      ZERO routing logic, ADR-039). A stale/revoked row (gone from the pending
 *      set) or any failure surfaces a stable local Assistant line (ADR-019) and
 *      dispatches NOTHING.
 *
 * This is a renderer BRANCH: it introduces no second dispatch pipeline, no
 * second confirmation gate, and no parallel suggestion library (spec Scope /
 * task escalation trigger). The actual dispatch is the host's — via
 * `onSuggestionAction`.
 *
 * Type note: the structural shapes below are declared inline rather than
 * imported from `@spaarke/notifications` because that package's built `dist`
 * type declarations lag its source for some event kinds — a value+type mixed
 * import breaks the surface `tsc` gate (same reason `notificationsBootstrap.ts`
 * declares its own `NotificationEventLite`). These are supersets of the real
 * wire types, so any real event/envelope is assignable to them.
 */

import * as React from "react";
import { makeStyles, shorthands, tokens, Button } from "@fluentui/react-components";
import { LightbulbFilamentRegular, ChevronDownRegular, ChevronRightRegular } from "@fluentui/react-icons";
import type { IChatMessage } from "@spaarke/ui-components";
import { SuggestionCard } from "./SuggestionCard";
import { makeLocalAssistantMessage } from "./summarizeRouting";

/** Local structural mirror of `@spaarke/notifications` `SuggestionEnvelope` (task 013). */
export interface SuggestionEnvelopeLite {
  readonly kind: string;
  readonly suggestionId: string;
  readonly source: string;
  readonly regardingRecordId: string;
  /** Dataverse logical name of the regarding record — pairs with regardingRecordId to open the record (task 052). */
  readonly regardingRecordType: string;
  readonly title: string;
  readonly snippet?: string;
  readonly actionHint: string;
  /** ISO 8601 datetime — when the suggestion expires. */
  readonly expiresAt: string;
}

/** Local structural mirror of `@spaarke/notifications` `NotificationEvent`. */
export interface SuggestionNotificationEvent {
  readonly outboxRowId: string;
  readonly kind: string;
  readonly envelope?: unknown;
  readonly source: string;
}

/** Local structural mirror of one `GET /api/notifications/pending` item (task 022). */
export interface PendingSuggestionItem {
  readonly outboxRowId: string;
  readonly kind: string;
  readonly envelope?: unknown;
  readonly expiresAt?: string | null;
}

/** A suggestion ready to render — the full envelope plus its outbox row id (the click-time re-check key). */
interface RenderableSuggestion {
  readonly outboxRowId: string;
  readonly envelope: SuggestionEnvelopeLite;
}

// Stable, non-raw local lines (ADR-019) — never a server error string.
const STALE_MESSAGE = "That suggestion is no longer available.";
const FAIL_MESSAGE = "Sorry — I couldn't open that suggestion. Please try again.";

const useStyles = makeStyles({
  // Proactive-suggestion stack — its own region at the top of the pane (a
  // suggestion arrives independent of a dispatch turn, so it is NOT the
  // transcript-footer chip slot). Tokens only (ADR-021).
  stack: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  // Collapsed-by-default disclosure banner ("You have N new notifications") so a
  // stack of proactive suggestions never dominates the conversation space (UAT
  // 2026-07-22). Click toggles the card list. Tokens only (ADR-021) — brand-accented
  // to match SuggestionCard, but full-width + subtle so it reads as a header, not a card.
  banner: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalS,
    justifyContent: "flex-start",
    width: "100%",
    minWidth: 0,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground2,
    ...shorthands.border("1px", "solid", tokens.colorBrandStroke2),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    fontWeight: tokens.fontWeightSemibold,
    boxShadow: tokens.shadow2,
    cursor: "pointer",
    // No hover highlight (UAT 2026-07-22): the banner is a disclosure toggle, not a
    // clickable record — only the suggestion cards (clickable rows) get hover. Explicitly
    // pin the hover/active states to the resting appearance so the Fluent Button's built-in
    // subtle-hover does not fire on the banner.
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1,
      color: tokens.colorBrandForeground2,
    },
    ":hover:active": {
      backgroundColor: tokens.colorNeutralBackground1,
      color: tokens.colorBrandForeground2,
    },
  },
  bannerLabel: {
    flexGrow: 1,
    textAlign: "left",
    minWidth: 0,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  // The expanded suggestions render as light rows inside ONE bordered panel (UAT
  // 2026-07-24) — not 5 stacked boxes. A hairline divider separates rows; the panel
  // owns the border/radius so each SuggestionCard stays borderless. Tokens only (ADR-021).
  cardList: {
    display: "flex",
    flexDirection: "column",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border("1px", "solid", tokens.colorNeutralStroke2),
    ...shorthands.overflow("hidden"),
    "> *:not(:first-child)": {
      ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke2),
    },
  },
});

/**
 * Strip a redundant leading verb ("Review "/"Review: ") from a suggestion title for
 * DISPLAY — every Daily-Briefing suggestion currently arrives titled "Review X", so the
 * verb repeated down the stack is noise (UAT 2026-07-24). The action (open) is implied by
 * clicking. This is a presentation-only trim; the fuller fix is at the producer
 * (DailyBriefingSuggestionProducer) so the envelope carries the clean subject.
 */
function displaySuggestionTitle(title: string): string {
  return title.replace(/^\s*review[:\-–—]?\s+/i, "").trim() || title;
}

export interface SuggestionCardsDeps {
  /**
   * Subscribe to a notification kind; returns an unregister function. Wire to
   * `getNotificationsClient().registerHandler(kind, handler)` — the ONE
   * host-wide client (never a second client instance).
   */
  readonly subscribe: (
    kind: "suggestion",
    handler: (event: SuggestionNotificationEvent) => void
  ) => () => void;
  /**
   * The re-fetch/re-ground BFF call: `GET /api/notifications/pending` → the
   * caller's current pending rows (server access + expiry filtered). Used both
   * to obtain envelopes for live signal-only pushes AND to re-check freshness
   * at click time.
   */
  readonly fetchPending: () => Promise<ReadonlyArray<PendingSuggestionItem>>;
  /**
   * The task-052 dispatch plug-point: route the re-validated suggestion's action
   * through the EXISTING `dispatchConsumer`/`launchSurface` mechanism. 051
   * carries ZERO routing logic (ADR-039) — it hands the host the FRESH envelope;
   * the host resolves `actionHint` → Binding.
   */
  readonly onSuggestionAction: (envelope: SuggestionEnvelopeLite) => void | Promise<void>;
  /**
   * Dismiss an outbox row server-side (`POST /api/notifications/{outboxRowId}/dismiss`) so it no
   * longer appears in `/pending` (UAT 2026-07-22). Called by the card's dismiss 'x' AND after a
   * successful action, so an acted-on / dismissed suggestion does not reappear on the next poll.
   * Fail-soft: the hook removes the card locally regardless (a failed server dismiss just means it
   * may reappear on a later poll — never an error to the user).
   */
  readonly dismiss: (outboxRowId: string) => Promise<void>;
  /** Stable local failure line injector (ADR-019) — wire to the injection queue's `inject`. */
  readonly inject: (message: IChatMessage) => void;
  /** Injected clock for deterministic expiry filtering in tests. Defaults to `Date.now`. */
  readonly now?: () => number;
}

export interface SuggestionCardsController {
  /** Memoized render node for the proactive-suggestion region (null when nothing to show). */
  readonly suggestionSlot: React.ReactNode;
  /** Count of currently-rendered (non-expired) suggestions. */
  readonly count: number;
}

/** True when the envelope is a well-formed `kind=suggestion` shape (grounding-by-construction guard). */
function isSuggestionEnvelope(envelope: unknown): envelope is SuggestionEnvelopeLite {
  if (envelope === null || typeof envelope !== "object") {
    return false;
  }
  const e = envelope as Record<string, unknown>;
  return (
    e.kind === "suggestion" &&
    typeof e.suggestionId === "string" &&
    typeof e.title === "string" &&
    typeof e.actionHint === "string" &&
    typeof e.expiresAt === "string" &&
    // A suggestion is only actionable if it can be OPENED — both the record type
    // and id must be present (task 052). An envelope missing either does not render.
    typeof e.regardingRecordId === "string" &&
    e.regardingRecordId.length > 0 &&
    typeof e.regardingRecordType === "string" &&
    e.regardingRecordType.length > 0
  );
}

/** True when `expiresAt` (ISO 8601) is at or before `nowMs`. Malformed dates are treated as expired (fail-safe). */
function isExpired(expiresAt: string, nowMs: number): boolean {
  const t = Date.parse(expiresAt);
  return Number.isNaN(t) || t <= nowMs;
}

export function useSuggestionCards(deps: SuggestionCardsDeps): SuggestionCardsController {
  const { subscribe, fetchPending, onSuggestionAction, dismiss, inject, now } = deps;
  const styles = useStyles();

  const [suggestions, setSuggestions] = React.useState<ReadonlyArray<RenderableSuggestion>>([]);
  // While any suggestion's re-ground/dispatch is in flight, disable the whole stack (single decision per turn).
  const [acting, setActing] = React.useState(false);
  // Collapsed by default — the banner shows a count; the card list drops down on click
  // (UAT 2026-07-22: a stack of cards was consuming the conversation space).
  const [expanded, setExpanded] = React.useState(false);

  // Re-ground the full pending suggestion set from the BFF. Non-fatal: a failed
  // refresh leaves the prior set intact (the next signal / poll tick retries).
  const refresh = React.useCallback(async (): Promise<void> => {
    try {
      const pending = await fetchPending();
      const next: RenderableSuggestion[] = [];
      for (const item of pending) {
        if (isSuggestionEnvelope(item.envelope)) {
          next.push({ outboxRowId: item.outboxRowId, envelope: item.envelope });
        }
      }
      setSuggestions(next);
    } catch {
      // swallow — degraded refresh must never throw into React render/effect.
    }
  }, [fetchPending]);

  // Subscribe once; re-ground on every 'suggestion' signal (a live push is
  // signal-only, so we must fetch the envelope from the BFF). No mount-time
  // fetch: the task-021 client's poll-fallback fires an immediate first tick on
  // start (task 022 FR-06), which delivers any already-pending suggestion as an
  // event through this same handler — so cold-load discovery is covered without
  // an extra uncontrolled fetch on every ConversationPane mount. (A pre-existing
  // pending suggestion surfaces on the first live ping or the first poll tick.)
  React.useEffect(() => {
    const unsubscribe = subscribe("suggestion", () => {
      void refresh();
    });
    return unsubscribe;
  }, [subscribe, refresh]);

  // Pre-mount expiry filter (spec / ADR-041): an expired envelope is excluded
  // from the rendered set entirely — never rendered-then-disabled.
  const nowMs = now ? now() : Date.now();
  const rendered = React.useMemo(
    () => suggestions.filter((s) => !isExpired(s.envelope.expiresAt, nowMs)),
    [suggestions, nowMs]
  );

  const handleAction = React.useCallback(
    async (item: RenderableSuggestion): Promise<void> => {
      setActing(true);
      try {
        // Re-fetch/re-ground BEFORE acting (freshness + access re-check, NFR-02):
        // the row must still be in the caller's server-scoped pending set.
        const pending = await fetchPending();
        const stillPending = pending.some((p) => p.outboxRowId === item.outboxRowId);
        if (!stillPending) {
          inject(makeLocalAssistantMessage(STALE_MESSAGE));
          setSuggestions((prev) => prev.filter((s) => s.outboxRowId !== item.outboxRowId));
          return;
        }
        // Route through the host's EXISTING dispatch (task 052) — never a new path.
        await onSuggestionAction(item.envelope);
        // Consume the suggestion once handed off — locally AND server-side (dismiss the outbox row)
        // so an acted-on suggestion does not reappear on the next poll. Dismiss is best-effort.
        setSuggestions((prev) => prev.filter((s) => s.outboxRowId !== item.outboxRowId));
        void dismiss(item.outboxRowId).catch(() => {
          /* best-effort: a failed server dismiss only risks a later re-appearance, never an error */
        });
      } catch {
        inject(makeLocalAssistantMessage(FAIL_MESSAGE));
      } finally {
        setActing(false);
      }
    },
    [fetchPending, onSuggestionAction, dismiss, inject]
  );

  // Explicit dismiss ('x') — remove the card locally and dismiss the outbox row server-side so it
  // never reappears. Best-effort on the server call (local removal always happens).
  const handleDismiss = React.useCallback(
    (item: RenderableSuggestion): void => {
      setSuggestions((prev) => prev.filter((s) => s.outboxRowId !== item.outboxRowId));
      void dismiss(item.outboxRowId).catch(() => {
        /* best-effort — a failed dismiss only risks the card reappearing on a later poll */
      });
    },
    [dismiss]
  );

  const suggestionSlot = React.useMemo<React.ReactNode>(() => {
    if (rendered.length === 0) {
      return null;
    }
    const count = rendered.length;
    const label = count === 1 ? "You have 1 new notification" : `You have ${count} new notifications`;
    return (
      <div
        className={styles.stack}
        role="group"
        aria-label="Suggestions"
        data-testid="suggestion-cards"
      >
        <Button
          className={styles.banner}
          appearance="subtle"
          onClick={() => setExpanded((e) => !e)}
          aria-expanded={expanded}
          aria-controls="suggestion-card-list"
          data-testid="suggestion-banner"
        >
          <LightbulbFilamentRegular aria-hidden />
          <span className={styles.bannerLabel}>{label}</span>
          {expanded ? <ChevronDownRegular aria-hidden /> : <ChevronRightRegular aria-hidden />}
        </Button>
        {expanded ? (
          <div id="suggestion-card-list" className={styles.cardList}>
            {rendered.map((s) => (
              <SuggestionCard
                key={s.outboxRowId}
                suggestion={{
                  suggestionId: s.envelope.suggestionId,
                  title: displaySuggestionTitle(s.envelope.title),
                  snippet: s.envelope.snippet,
                  actionHint: s.envelope.actionHint,
                }}
                disabled={acting}
                onAction={() => void handleAction(s)}
                onDismiss={() => handleDismiss(s)}
              />
            ))}
          </div>
        ) : null}
      </div>
    );
  }, [rendered, acting, handleAction, handleDismiss, expanded, styles.stack, styles.banner, styles.bannerLabel, styles.cardList]);

  return React.useMemo(
    () => ({ suggestionSlot, count: rendered.length }),
    [suggestionSlot, rendered.length]
  );
}
