/**
 * WorkspaceHomeTab.tsx — Home tab content for the SpaarkeAi WorkspacePane.
 *
 * Task 069 (Option Z minimum scope, Bug 2 visual fix): SpaarkeAi's Workspace
 * Home tab now defaults to the **Daily Briefing** section. The shared
 * `createDailyBriefingRegistration` factory + `WorkspaceShell` + canonical
 * `buildDynamicWorkspaceConfig` are consumed from `@spaarke/ui-components`.
 *
 * Why Daily Briefing as the default Home content?
 *   - Operator 2026-05-20 direction (Option Z): SpaarkeAi has a DIFFERENT
 *     workspace surface than LegalWorkspace. After clarification, the minimum
 *     scope is: SpaarkeAi's Home tab defaults to Daily Briefing. Future Home
 *     content expansion (additional sections, AI session list, etc.) is
 *     deferred to follow-on review.
 *   - Daily Briefing is the LEAST domain-coupled of the LegalWorkspace
 *     section catalogue — it calls a shared BFF AI endpoint
 *     (`/api/ai/daily-briefing/narrate`) and is safe to consume from
 *     `@spaarke/ui-components` per ADR-012 (no Dataverse strings).
 *   - The other 5 legal-domain factories (getStarted, quickSummary,
 *     latestUpdates, todo, documents) remain solution-local per ADR-012.
 *
 * Auth (ADR-028):
 *   - `authenticatedFetch` + `bffBaseUrl` are obtained from `useAiSession()`
 *     (never snapshotted as props or token strings).
 *   - `tenantId` is read from the runtime config (used to scope the daily
 *     briefing TTL cache per ADR-014).
 *
 * Telemetry:
 *   - 429 failures route through `logTelemetryError(TELEMETRY_DAILY_BRIEFING_429, ...)`
 *     (SpaarkeAi-side helper from task 013).
 *
 * Standards:
 *   - ADR-012: WorkspaceShell + builder + Daily Briefing consumed from
 *     `@spaarke/ui-components` barrel
 *   - ADR-021: Fluent v9 tokens only (no hex / rgba)
 *   - ADR-022: React 19 functional component
 *   - ADR-028: All BFF calls via `authenticatedFetch`; no `accessToken` snapshots
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  WorkspaceShell,
  buildDynamicWorkspaceConfig,
  createDailyBriefingRegistration,
} from "@spaarke/ui-components";
import type {
  WorkspaceConfig,
  SectionRegistration,
  SectionFactoryContext,
  LayoutJson,
} from "@spaarke/ui-components";
import { useAiSession } from "@spaarke/ai-widgets";
import { getTenantId } from "../../config/runtimeConfig";
import {
  logTelemetryError,
  TELEMETRY_DAILY_BRIEFING_429,
} from "../../telemetry/errorTelemetry";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    flex: 1,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground2,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Default layout — single row, single Daily Briefing section
//
// Minimum-scope Home tab (task 069): one row with the Daily Briefing section.
// Future expansions (multi-section Home, recent AI sessions, etc.) will
// extend this layout.
// ---------------------------------------------------------------------------

const HOME_LAYOUT_JSON: LayoutJson = {
  schemaVersion: 1,
  rows: [
    {
      sections: ["daily-briefing"],
    },
  ],
};

// ---------------------------------------------------------------------------
// Build a SectionFactoryContext for SpaarkeAi.
//
// Daily Briefing doesn't use webApi / service / onNavigate / onOpenWizard /
// onBadgeCountChange / onRefetchReady — it self-contains its data layer via
// the supplied `authenticatedFetch`. We supply no-op stubs for the unused
// fields so the SectionFactoryContext contract is satisfied without forcing
// SpaarkeAi to provide Dataverse handles it doesn't have.
// ---------------------------------------------------------------------------

function buildSpaarkeAiContext(bffBaseUrl: string): SectionFactoryContext {
  return {
    webApi: undefined as unknown,
    userId: "",
    service: undefined as unknown,
    bffBaseUrl,
    onNavigate: () => {
      /* SpaarkeAi Home tab does not navigate from Daily Briefing */
    },
    onOpenWizard: () => {
      /* Daily Briefing never opens wizards */
    },
    onBadgeCountChange: () => {
      /* Daily Briefing does not report badge counts */
    },
    onRefetchReady: () => {
      /* Daily Briefing's refetch is owned internally by the section */
    },
    onExpandSection: undefined,
    onOpenDocumentsDialog: undefined,
    scope: "my",
    businessUnitId: undefined,
  };
}

// ---------------------------------------------------------------------------
// WorkspaceHomeTab component
// ---------------------------------------------------------------------------

/**
 * WorkspaceHomeTab — content for the WorkspacePane Home tab.
 *
 * Renders the Daily Briefing section as the default Home content per Option Z
 * (task 069). Uses the canonical `buildDynamicWorkspaceConfig` + `WorkspaceShell`
 * from `@spaarke/ui-components` so future Home-content expansion is purely a
 * matter of extending `HOME_LAYOUT_JSON` and registering more sections.
 *
 * Auth is per-request via `useAiSession().authenticatedFetch` (ADR-028).
 * 429 failures route through `logTelemetryError` (FR-24 error-only telemetry).
 */
export const WorkspaceHomeTab: React.FC = () => {
  const styles = useStyles();

  const { bffBaseUrl, authenticatedFetch, isAuthenticated } = useAiSession();

  // Resolve tenant ID for cache scoping (ADR-014). Read from runtime config —
  // it's a synchronous getter that's already wired during app startup.
  const tenantId = React.useMemo<string | undefined>(() => {
    try {
      return getTenantId();
    } catch {
      // Runtime config not initialized — fall back to anonymous cache key.
      return undefined;
    }
  }, []);

  // 429 telemetry callback — routes through SpaarkeAi's error-only helper.
  const handleRateLimitError = React.useCallback(
    (properties: Record<string, unknown>) => {
      logTelemetryError(TELEMETRY_DAILY_BRIEFING_429, properties);
    },
    [],
  );

  // Build the Daily Briefing registration via the shared factory, closing
  // over auth deps + telemetry routing. Stable across renders (deps
  // shouldn't change after initial auth).
  const dailyBriefingRegistration = React.useMemo<SectionRegistration>(
    () =>
      createDailyBriefingRegistration({
        authenticatedFetch,
        tenantId,
        onRateLimitError: handleRateLimitError,
      }),
    [authenticatedFetch, tenantId, handleRateLimitError],
  );

  // Build the WorkspaceConfig via the canonical builder.
  const config = React.useMemo<WorkspaceConfig>(() => {
    const factoryContext = buildSpaarkeAiContext(bffBaseUrl);
    return buildDynamicWorkspaceConfig(
      HOME_LAYOUT_JSON,
      [dailyBriefingRegistration],
      factoryContext,
    );
  }, [bffBaseUrl, dailyBriefingRegistration]);

  // Don't render the section until auth is ready — the Daily Briefing fetch
  // would fail with no token, surfacing a confusing error state instead of
  // a smooth first-paint experience.
  if (!isAuthenticated) {
    return <div className={styles.root} data-testid="home-tab-root" />;
  }

  return (
    <div className={styles.root} data-testid="home-tab-root">
      <WorkspaceShell config={config} />
    </div>
  );
};

WorkspaceHomeTab.displayName = "WorkspaceHomeTab";
