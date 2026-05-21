/**
 * DailyBriefingSection.tsx — Fluent v9 section body rendering AI-curated bullets
 * fetched via `useDailyBriefing()` (POST /api/ai/daily-briefing/narrate).
 *
 * Implements FR-15. Consumed by both standalone LegalWorkspace and SpaarkeAi's
 * embedded WorkspacePane per FR-25 — there are no SpaarkeAi-specific deps in
 * this component.
 *
 * Three-state contract (FR-16 / NFR-11 / OC-08):
 *   - SUCCESS: AI bullets in a Fluent v9 layout (unchanged from task 034).
 *   - 429:     Degraded card with explanatory message + Retry button. Section
 *              stays visible at the same dimensions (does NOT collapse).
 *              Emits `spaarke-ai-error.daily-briefing.rate-limited` telemetry
 *              ONCE per failure transition (FR-24 — error-only telemetry).
 *   - EMPTY:   Friendly empty state — small icon + "Nothing to see right now —
 *              enjoy your day". Section stays visible. NO telemetry (OC-08 is
 *              happy-path; FR-24 is strict error-only).
 *
 * Telemetry boundary (FR-25 backwards-compat):
 *   - This component lives in LegalWorkspace and MUST NOT import from
 *     SpaarkeAi (cross-solution coupling would break the standalone-
 *     LegalWorkspace deployment path).
 *   - Standalone LegalWorkspace: emits via the local `trackEvent` helper
 *     (`src/services/telemetry.ts`) using the same event name string.
 *   - SpaarkeAi embed: may inject an `onRateLimitError` callback that wraps
 *     `logTelemetryError` from SpaarkeAi's `telemetry/errorTelemetry.ts`. When
 *     a callback is provided, it WINS over the local emission.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only. NO hex literals; NO rgba() literals.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *   - ADR-028: Data flows through `useDailyBriefing()`, which uses
 *     `authenticatedFetch`. This component never touches tokens directly.
 */

import * as React from "react";
import {
  Spinner,
  Text,
  Button,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  EmojiSparkleRegular,
  ArrowClockwiseRegular,
  WarningRegular,
} from "@fluentui/react-icons";
import { useDailyBriefing } from "./useDailyBriefing";
import { trackEvent } from "../../services/telemetry";

// ---------------------------------------------------------------------------
// Telemetry event-name constants (mirrors SpaarkeAi/src/telemetry/errorTelemetry.ts)
//
// Kept as a local string constant so this LegalWorkspace section does NOT
// cross-import from the SpaarkeAi solution (FR-25 backwards-compat).
// The string value is identical to TELEMETRY_DAILY_BRIEFING_429 in the
// SpaarkeAi helper so App Insights KQL queries match across both surfaces.
// ---------------------------------------------------------------------------

const TELEMETRY_EVENT_DAILY_BRIEFING_429 =
  "spaarke-ai-error.daily-briefing.rate-limited";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 0",
    minHeight: 0,
    padding: "8px 12px 12px 12px",
    gap: "8px",
  },
  loading: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "120px",
  },
  // Empty state (OC-08 — friendly happy-path "nothing to see") and 429 degraded
  // state share a centered, vertical layout so the section preserves its
  // dimensions across all three states (FR-16 / NFR-11).
  centeredState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "120px",
    gap: "8px",
    textAlign: "center",
    color: tokens.colorNeutralForeground2,
  },
  emptyIcon: {
    fontSize: "32px",
    color: tokens.colorNeutralForeground3,
    // currentColor inheritance keeps dark-mode parity (ADR-021).
  },
  degradedIcon: {
    fontSize: "32px",
    color: tokens.colorPaletteYellowForeground1,
  },
  degradedTitle: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  degradedBody: {
    color: tokens.colorNeutralForeground2,
    maxWidth: "320px",
  },
  retryButtonRow: {
    display: "flex",
    justifyContent: "center",
    marginTop: "4px",
  },
  bulletList: {
    display: "flex",
    flexDirection: "column",
    gap: "8px",
    listStyle: "none",
    margin: 0,
    padding: 0,
    overflowY: "auto",
  },
  bulletItem: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: "8px",
    color: tokens.colorNeutralForeground1,
  },
  bulletMarker: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0,
    minWidth: "12px",
  },
  bulletText: {
    flex: "1 1 auto",
    lineHeight: tokens.lineHeightBase300,
  },
});

// ---------------------------------------------------------------------------
// Component props
// ---------------------------------------------------------------------------

/**
 * Optional props for `DailyBriefingSection`. All props are optional so the
 * default `React.createElement(DailyBriefingSection)` factory call from
 * `dailyBriefing.registration.ts` continues to work unchanged (FR-25).
 */
export interface DailyBriefingSectionProps {
  /**
   * Optional callback fired exactly once per 429 failure transition. SpaarkeAi
   * embeds inject this to route the event through their dedicated
   * `errorTelemetry.ts` helper. When NOT provided, the section falls back to
   * LegalWorkspace's local `trackEvent` so standalone deployments still emit
   * the event under the same name string.
   *
   * Receives a properties payload suitable for App Insights `trackEvent`.
   */
  onRateLimitError?: (properties: Record<string, unknown>) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * DailyBriefingSection — renders the AI-curated daily briefing bullets.
 *
 * The component owns its data fetching via `useDailyBriefing()`. Props are
 * optional and only used to inject SpaarkeAi-side telemetry routing when
 * embedded — standalone LegalWorkspace works without props.
 */
export const DailyBriefingSection: React.FC<DailyBriefingSectionProps> = ({
  onRateLimitError,
}) => {
  const styles = useStyles();
  const { bullets, isLoading, error, refetch } = useDailyBriefing();

  // ---------------------------------------------------------------------
  // 429 telemetry — dedupe via ref so the event fires ONCE per failure
  // transition, not on every render. The ref tracks the in-flight failure
  // identity (status code + message hash is good enough for an error path).
  // When `error` clears (successful refetch), we reset the ref so a subsequent
  // 429 emits again.
  // ---------------------------------------------------------------------
  const emittedFailureRef = React.useRef<string | null>(null);

  React.useEffect(() => {
    if (error?.kind === "rate-limit") {
      const failureKey = `${error.status}:${error.message}`;
      if (emittedFailureRef.current !== failureKey) {
        emittedFailureRef.current = failureKey;

        const properties: Record<string, unknown> = {
          endpoint: "/api/ai/daily-briefing/narrate",
          statusCode: error.status,
          message: error.message,
          timestamp: new Date().toISOString(),
        };

        if (onRateLimitError) {
          // Embed-time injection (e.g., SpaarkeAi → logTelemetryError)
          try {
            onRateLimitError(properties);
          } catch {
            // Never let a telemetry callback failure break the UI.
          }
        } else {
          // Standalone LegalWorkspace fallback — App Insights via local helper.
          // Properties values are coerced to strings by trackEvent's signature.
          const stringProps: Record<string, string> = {};
          for (const [k, v] of Object.entries(properties)) {
            stringProps[k] = String(v);
          }
          trackEvent(TELEMETRY_EVENT_DAILY_BRIEFING_429, stringProps);
        }
      }
    } else if (!error) {
      // Clearing the error (e.g., successful retry) resets the dedupe key so
      // the next 429 fires telemetry again.
      emittedFailureRef.current = null;
    }
  }, [error, onRateLimitError]);

  // ---------------------------------------------------------------------
  // 1. Loading state — Spinner while the first fetch is in flight.
  // ---------------------------------------------------------------------
  if (isLoading) {
    return (
      <div className={styles.root}>
        <div className={styles.loading}>
          <Spinner size="small" label="Loading daily briefing..." />
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------
  // 2. 429 graceful degraded state (FR-16 / NFR-11).
  //    Stays visible at the same dimensions as success/empty; retry CTA
  //    re-invokes the hook bypassing the TTL cache.
  // ---------------------------------------------------------------------
  if (error && error.kind === "rate-limit") {
    return (
      <div className={styles.root}>
        <div
          className={styles.centeredState}
          role="status"
          aria-live="polite"
        >
          <WarningRegular className={styles.degradedIcon} aria-hidden="true" />
          <Text size={400} className={styles.degradedTitle}>
            Daily Briefing temporarily unavailable
          </Text>
          <Text size={200} className={styles.degradedBody}>
            We hit a rate limit. Try again in a moment.
          </Text>
          <div className={styles.retryButtonRow}>
            <Button
              appearance="secondary"
              icon={<ArrowClockwiseRegular />}
              onClick={() => {
                void refetch();
              }}
            >
              Retry
            </Button>
          </div>
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------
  // 3. Other error states (auth/unavailable/error). Treated like 429 visually
  //    but without telemetry emission per FR-24 (only the 429 event is wired
  //    for this task). Section stays visible.
  // ---------------------------------------------------------------------
  if (error) {
    return (
      <div className={styles.root}>
        <div
          className={styles.centeredState}
          role="status"
          aria-live="polite"
        >
          <WarningRegular className={styles.degradedIcon} aria-hidden="true" />
          <Text size={400} className={styles.degradedTitle}>
            Daily Briefing unavailable
          </Text>
          <Text size={200} className={styles.degradedBody}>
            Something went wrong loading your briefing.
          </Text>
          <div className={styles.retryButtonRow}>
            <Button
              appearance="secondary"
              icon={<ArrowClockwiseRegular />}
              onClick={() => {
                void refetch();
              }}
            >
              Retry
            </Button>
          </div>
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------
  // 4. Empty-data state (FR-16 / OC-08). Happy path — NO telemetry.
  //    Section remains visible at normal dimensions.
  // ---------------------------------------------------------------------
  if (bullets.length === 0) {
    return (
      <div className={styles.root}>
        <div className={styles.centeredState}>
          <EmojiSparkleRegular
            className={styles.emptyIcon}
            aria-hidden="true"
          />
          <Text size={300}>
            Nothing to see right now — enjoy your day
          </Text>
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------
  // 5. Success state — render bullets (unchanged from task 034).
  // ---------------------------------------------------------------------
  return (
    <div className={styles.root}>
      <ul className={styles.bulletList} aria-label="Daily briefing items">
        {bullets.map((text, idx) => (
          <li key={idx} className={styles.bulletItem}>
            <span className={styles.bulletMarker} aria-hidden="true">
              •
            </span>
            <Text className={styles.bulletText} size={300}>
              {text}
            </Text>
          </li>
        ))}
      </ul>
    </div>
  );
};

export default DailyBriefingSection;
