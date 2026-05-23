/**
 * useDailyBriefing.ts — LegalWorkspace re-export shim.
 *
 * Hoisted to `@spaarke/ui-components` in task 069. This shim preserves the
 * pre-069 hook signature `useDailyBriefing(): DailyBriefingState` for any
 * LegalWorkspace caller by closing over the LegalWorkspace-local
 * `authenticatedFetch` (from `services/authInit.ts`) and tenant ID resolver.
 *
 * Consumers in this solution can keep their existing imports unchanged.
 *
 * See task 067/069 hoist precedent and:
 *   - `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/useDailyBriefing.ts`
 *   - ADR-012 (shared components), ADR-028 (auth contract).
 */

import * as React from "react";
import {
  useDailyBriefing as useDailyBriefingShared,
  type DailyBriefingState,
  type DailyBriefingError,
} from "@spaarke/ui-components";
import { authenticatedFetch, getTenantId } from "../../services/authInit";

// Re-export types so existing imports `import type { DailyBriefingState, ... } from "./useDailyBriefing"` continue working.
export type { DailyBriefingState, DailyBriefingError };

/**
 * LegalWorkspace-bound `useDailyBriefing` wrapper.
 *
 * Closes over the local `authenticatedFetch` and lazy-resolves the tenant ID
 * from the local auth wrapper. Same behavioral contract as the pre-069 hook.
 */
export function useDailyBriefing(): DailyBriefingState {
  // Lazy-resolve tenant ID so the hook stays synchronous-shaped.
  const [tenantId, setTenantId] = React.useState<string | undefined>(undefined);

  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const id = await getTenantId();
        if (!cancelled) setTenantId(id);
      } catch {
        // Tenant ID resolution failed — hook will use the anonymous cache key.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return useDailyBriefingShared({
    authenticatedFetch,
    tenantId,
  });
}
