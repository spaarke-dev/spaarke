/**
 * DailyBriefingSection.tsx — LegalWorkspace re-export shim.
 *
 * Hoisted to `@spaarke/ui-components` in task 069. The shared component now
 * accepts `authenticatedFetch`, `tenantId`, and `onRateLimitError` as props
 * (context-agnostic per ADR-012 + ADR-028). This shim closes over the
 * LegalWorkspace-local `authenticatedFetch`, lazy-resolves tenant ID, and
 * routes 429 telemetry through the local `trackEvent` helper — preserving
 * the pre-069 standalone behavior EXACTLY (FR-25 / NFR-10).
 *
 * Existing callers can keep their imports unchanged:
 *   ```tsx
 *   import { DailyBriefingSection } from "./DailyBriefingSection";
 *   <DailyBriefingSection />            // standalone case (no props)
 *   <DailyBriefingSection onRateLimitError={...} />  // embed-time override
 *   ```
 *
 * The optional `onRateLimitError` prop is preserved for embed callers that
 * want to override the local telemetry routing. When not supplied, the shim
 * falls back to `trackEvent` (the standalone LegalWorkspace behavior).
 *
 * See task 067/069 hoist precedent and:
 *   - `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/DailyBriefingSection.tsx`
 *   - ADR-012 (shared components), ADR-021 (Fluent v9 tokens), ADR-028 (auth contract).
 */

import * as React from "react";
import {
  DailyBriefingSection as DailyBriefingSectionShared,
  TELEMETRY_EVENT_DAILY_BRIEFING_429,
  type DailyBriefingSectionProps as DailyBriefingSectionSharedProps,
} from "@spaarke/ui-components";
import { authenticatedFetch, getTenantId } from "../../services/authInit";
import { trackEvent } from "../../services/telemetry";

// Re-export types so any callers using `import type { DailyBriefingSectionProps } ...` continue working.
// We expose the same prop shape but with `authenticatedFetch` made optional (the
// shim provides it). This preserves the pre-069 contract where callers could
// instantiate the section with no props at all.
export interface DailyBriefingSectionProps {
  /**
   * Optional callback fired exactly once per 429 failure transition. When
   * NOT provided, the shim emits via LegalWorkspace's local `trackEvent`
   * (preserving the pre-069 standalone behavior).
   */
  onRateLimitError?: DailyBriefingSectionSharedProps["onRateLimitError"];
}

/**
 * LegalWorkspace-bound `DailyBriefingSection` wrapper. Preserves the pre-069
 * contract: callable with no props (standalone) or with `onRateLimitError`
 * for embed-time telemetry overrides.
 */
export const DailyBriefingSection: React.FC<DailyBriefingSectionProps> = ({
  onRateLimitError,
}) => {
  const [tenantId, setTenantId] = React.useState<string | undefined>(undefined);

  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const id = await getTenantId();
        if (!cancelled) setTenantId(id);
      } catch {
        // Tenant ID resolution failed — shared hook will use the anonymous cache key.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Standalone fallback: route 429 telemetry through LegalWorkspace's
  // `trackEvent` using the SAME event name string as the shared lib constant.
  // This preserves FR-25 / NFR-10 — standalone LegalWorkspace emits the same
  // App Insights event the pre-069 implementation emitted.
  const effectiveCallback = React.useMemo(
    () => {
      if (onRateLimitError) return onRateLimitError;
      return (properties: Record<string, unknown>) => {
        const stringProps: Record<string, string> = {};
        for (const [k, v] of Object.entries(properties)) {
          stringProps[k] = String(v);
        }
        trackEvent(TELEMETRY_EVENT_DAILY_BRIEFING_429, stringProps);
      };
    },
    [onRateLimitError],
  );

  return (
    <DailyBriefingSectionShared
      authenticatedFetch={authenticatedFetch}
      tenantId={tenantId}
      onRateLimitError={effectiveCallback}
    />
  );
};

export default DailyBriefingSection;
