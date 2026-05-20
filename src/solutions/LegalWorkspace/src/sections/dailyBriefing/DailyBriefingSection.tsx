/**
 * DailyBriefingSection.tsx — Fluent v9 section body rendering AI-curated bullets
 * fetched via `useDailyBriefing()` (POST /api/ai/daily-briefing/narrate).
 *
 * Implements FR-15. Consumed by both standalone LegalWorkspace and SpaarkeAi's
 * embedded WorkspacePane per FR-25 — there are no SpaarkeAi-specific deps in
 * this component.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only. NO hex literals; NO rgba() literals.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *   - ADR-028: Data flows through `useDailyBriefing()`, which uses
 *     `authenticatedFetch`. This component never touches tokens directly.
 *
 * Task 035 (Wave 3d) extension points (intentionally simple here):
 *   - The error branch renders a placeholder MessageBar that task 035 will
 *     refine into a 429 graceful degraded state with retry CTA (NFR-11).
 *   - The empty-data branch (no error + no bullets after loading) renders a
 *     minimal message that task 035 will refine into the OC-08 friendly state.
 */

import * as React from "react";
import {
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { useDailyBriefing } from "./useDailyBriefing";

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
  emptyState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    minHeight: "120px",
    color: tokens.colorNeutralForeground2,
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
// Component
// ---------------------------------------------------------------------------

/**
 * DailyBriefingSection — renders the AI-curated daily briefing bullets.
 *
 * No props — this component is intentionally context-free. The hook handles
 * auth, fetching, and caching internally.
 */
export const DailyBriefingSection: React.FC = () => {
  const styles = useStyles();
  const { bullets, isLoading, error } = useDailyBriefing();

  // 1. Loading state — Spinner while the first fetch is in flight.
  if (isLoading) {
    return (
      <div className={styles.root}>
        <div className={styles.loading}>
          <Spinner size="small" label="Loading daily briefing..." />
        </div>
      </div>
    );
  }

  // 2. Error state — placeholder MessageBar.
  //    Task 035 will refine this with a 429-specific graceful degraded UI + retry.
  if (error) {
    const intent = error.kind === "rate-limit" ? "warning" : "error";
    const message =
      error.kind === "rate-limit"
        ? "Daily briefing is temporarily rate-limited."
        : "Unable to load daily briefing.";
    return (
      <div className={styles.root}>
        <MessageBar intent={intent}>
          <MessageBarBody>{message}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  // 3. Empty-data state — task 035 refines this into the OC-08 friendly state.
  if (bullets.length === 0) {
    return (
      <div className={styles.root}>
        <div className={styles.emptyState}>
          <Text size={300}>No briefing items right now.</Text>
        </div>
      </div>
    );
  }

  // 4. Success state — render bullets.
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
