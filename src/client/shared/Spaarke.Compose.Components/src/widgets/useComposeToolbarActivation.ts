/**
 * useComposeToolbarActivation.ts — Compose AI toolbar ACTIVATION (task 048, closes
 * e2e-gap-register gap 2.2).
 *
 * Project: spaarkeai-compose-r2, task 048 (Phase 4 Catalog — the AI-activation gate).
 *
 * WHY THIS EXISTS
 * ---------------
 * `ComposeAiToolbar`'s five actions ship with `bindingId: ''` (the honest Phase-4
 * stub — see that file's "PHASE-4 STUB BOUNDARY"), so every AI button renders
 * DISABLED. `registerComposeAiToolbarAction()` is the only thing that swaps a
 * stub's empty bindingId for a real deployed GUID, and until this hook it was
 * called ONLY in tests — so the toolbar was inert in the running app. This hook
 * is the missing user-facing TRIGGER: on Compose mount it reads the EXISTING
 * capability-discovery endpoint and registers each returned compose capability's
 * real `bindingId` onto the matching toolbar action.
 *
 * HOW IT MAPS
 * -----------
 * `GET /api/ai/capabilities?surface=compose` returns the closed-catalog Bindings
 * declared on the `compose` surface (ADR-039). Each `CapabilityDto.consumerType`
 * equals the toolbar action `id` VERBATIM (the five DEFAULT_ACTIONS ids
 * `compose-explain-clause` / `compose-compare-to-playbook` /
 * `compose-draft-alternative` / `compose-summarize-word-changes` /
 * `compose-defined-terms`). So the map is: register onto the DEFAULT action whose
 * `id === capability.consumerType`, filling in `bindingId` and preserving the
 * authored label / tooltip / placement. A capability with NO matching DEFAULT
 * action (e.g. the whole-document `compose-summarize` binding, which is not a
 * toolbar action) is SKIPPED — never appended as a new action.
 *
 * COMPONENT JUSTIFICATION (CLAUDE.md §11)
 * ---------------------------------------
 *   Existing: SpaarkeAi's `useCapabilityDiscovery` fetches the SAME endpoint —
 *     but it lives in `src/solutions/SpaarkeAi/` and renders a soft-slash LAUNCHER
 *     list; a shared-lib mount host (LegalWorkspace's compose section factory)
 *     CANNOT import it (that reverses the unidirectional shared-lib ← solution
 *     dependency), and its concern (render a launcher menu) differs from this one
 *     (register toolbar bindingIds). No new BFF endpoint or consumer→GUID resolver
 *     is introduced (CLAUDE.md §10/§11) — this reuses the EXISTING read.
 *   Extension: not applicable across the package boundary; this hook is the
 *     compose-package-local activation, co-located with `registerComposeAiToolbarAction`.
 *   Cost-of-doing-nothing: without it the compose AI toolbar stays inert in the
 *     running app (every button disabled forever) even after the catalog deploy
 *     (047) — gap 2.2 stays open.
 *
 * CONSTRAINTS HONORED (BINDING)
 * -----------------------------
 *   - ADR-028: the fetch goes through `@spaarke/auth` `authenticatedFetch` — no
 *     `accessToken` prop, no bespoke `Bearer` header assembled here.
 *   - ADR-039: bindingId comes ONLY from the response; no hardcoded GUID, no
 *     client-side consumer→GUID resolver, no new route/discriminant.
 *   - ADR-015: logs COUNTS only — never the capability list contents.
 *   - Graceful: a fetch failure or 0 capabilities (pre-047 deploy) leaves the
 *     buttons DISABLED — no error UI, no throw; debug-log only.
 *
 * @see ./ComposeAiToolbar.tsx — `registerComposeAiToolbarAction` / DEFAULT_ACTIONS / the re-render subscription
 * @see src/server/api/Sprk.Bff.Api/Api/Ai/CapabilityDiscoveryEndpoints.cs — the GET this consumes
 * @see src/solutions/SpaarkeAi/src/components/conversation/useCapabilityDiscovery.ts — the sibling launcher hook (different surface + concern)
 */

import * as React from 'react';
import { useAuth } from '@spaarke/auth';
import { getComposeAiToolbarActions, registerComposeAiToolbarAction } from './ComposeAiToolbar';

/** Placement surface requested by the compose toolbar (matches the deployed compose bindings). */
const COMPOSE_SURFACE = 'compose';

/**
 * Minimal client mirror of the BFF `CapabilityDto` — only the two fields this
 * hook reads (the rest of the projection is irrelevant to registration). JSON is
 * camelCase (System.Text.Json default). See `CapabilityDiscoveryEndpoints.cs`.
 */
interface CapabilityDiscoveryEntry {
  bindingId: string;
  consumerType: string;
}

interface CapabilityDiscoveryResponseShape {
  capabilities?: CapabilityDiscoveryEntry[];
}

export interface UseComposeToolbarActivationOptions {
  /** BFF base URL (may be empty in a non-Dataverse host — then activation is a no-op). */
  bffBaseUrl: string;
  /**
   * Placement surface to request. Defaults to `"compose"` — override only in a test
   * or an alternate placement.
   */
  surface?: string;
  /**
   * Test/injection escape hatch — bypasses `useAuth().authenticatedFetch` so a test
   * can drive the hook without an MSAL bootstrap. Production hosts must NOT set this.
   */
  fetchOverride?: typeof fetch;
}

/**
 * On mount (and whenever `bffBaseUrl`/`surface`/auth change), fetch the compose
 * capability list and register each match onto the toolbar. Returns nothing — its
 * effect is the module-level registration that the mounted `ComposeAiToolbar`
 * re-reads via `subscribeComposeAiToolbarActions`.
 */
export function useComposeToolbarActivation(options: UseComposeToolbarActivationOptions): void {
  const { bffBaseUrl, surface = COMPOSE_SURFACE, fetchOverride } = options;
  const { authenticatedFetch } = useAuth();
  const doFetch = fetchOverride ?? authenticatedFetch;

  React.useEffect(() => {
    // No BFF configured (e.g. non-Dataverse host / early paint) → nothing to
    // activate; buttons stay disabled (graceful).
    if (!bffBaseUrl) return;

    let cancelled = false;

    void (async () => {
      try {
        const url = new URL(`${bffBaseUrl.replace(/\/$/, '')}/api/ai/capabilities`);
        url.searchParams.set('surface', surface);

        const response = await doFetch(url.toString(), { method: 'GET' });
        if (!response.ok) {
          throw new Error(`capability-discovery fetch failed: ${response.status}`);
        }

        const payload = (await response.json()) as CapabilityDiscoveryResponseShape;
        if (cancelled) return;

        const capabilities = payload.capabilities ?? [];
        // Snapshot the current action set ONCE before registering (defaults +
        // any prior registrations); `find` uses this stable snapshot so mid-loop
        // registrations can't perturb subsequent lookups.
        const knownActions = getComposeAiToolbarActions();

        let registered = 0;
        for (const capability of capabilities) {
          const match = knownActions.find(a => a.id === capability.consumerType);
          // Skip capabilities with no matching DEFAULT toolbar action (e.g. the
          // whole-document `compose-summarize` binding) — never append them.
          if (!match) continue;
          // Defensive: a capability with an empty bindingId (shouldn't happen
          // post-deploy) would leave the button disabled anyway — skip it so we
          // never "register" a still-inert stub.
          if (!capability.bindingId) continue;
          // Preserve the authored label / tooltip / placement; fill only bindingId.
          registerComposeAiToolbarAction({ ...match, bindingId: capability.bindingId });
          registered += 1;
        }

        // ADR-015: counts only — never log capability contents.
        // eslint-disable-next-line no-console
        console.debug(
          '[useComposeToolbarActivation] registered %d of %d compose capabilities',
          registered,
          capabilities.length
        );
      } catch (err) {
        if (!cancelled) {
          // Graceful: buttons stay disabled, no error UI, no throw. Debug-log only.
          // eslint-disable-next-line no-console
          console.debug(
            '[useComposeToolbarActivation] activation skipped:',
            err instanceof Error ? err.name : 'unknown'
          );
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [bffBaseUrl, surface, doFetch]);
}
