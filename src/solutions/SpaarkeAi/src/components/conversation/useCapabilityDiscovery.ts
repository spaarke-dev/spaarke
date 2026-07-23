/**
 * useCapabilityDiscovery — client consumption of the capability-discovery READ
 * endpoint (spec FR-A1-12, R1 — the gate-038 deferral, BFF task 041).
 *
 * Fetches `GET /api/ai/capabilities` and exposes the launchable-capability list
 * so a soft-slash launcher can render deterministically from CATALOG DATA
 * instead of asking the model to name what exists. The BFF endpoint already
 * projects the closed catalog (ADR-039) deterministically (stable ordering,
 * no per-request timestamps/ids) — this hook does not re-sort or supplement
 * the list; it renders exactly what the server returned.
 *
 * ADR-015: log counts only. ADR-028: `authenticatedFetch` only (no bespoke
 * headers/props — see `@spaarke/auth` client contract).
 */

import * as React from "react";

/** One launchable capability's launch metadata — mirrors the BFF `CapabilityDto` shape. */
export interface CapabilityDiscoveryItem {
  bindingId: string;
  consumerType: string;
  consumerCode: string;
  displayLabel: string;
  surfaces: string[];
  launchArgsSchemaJson: string | null;
}

interface CapabilityDiscoveryResponseShape {
  capabilities: CapabilityDiscoveryItem[];
}

export interface CapabilityDiscoveryDeps {
  bffBaseUrl: string;
  authenticatedFetch: (input: string, init?: RequestInit) => Promise<Response>;
  /** Placement surface to request (default `"assistant"` — the soft-slash launcher's surface). */
  surface?: string;
  /**
   * When false, the hook stays inert — it performs NO fetch and returns an empty list. Lets a host
   * defer the catalog read until a capability is actually needed (e.g. the first time a revise flow
   * fires), so a mounted-but-idle host does not issue an eager background request. Defaults to true
   * (eager) for backward compatibility with the soft-slash launcher.
   */
  enabled?: boolean;
}

export interface CapabilityDiscoveryState {
  capabilities: CapabilityDiscoveryItem[];
  loading: boolean;
  /** True only on a fetch failure; the launcher degrades to an empty list (never invented entries). */
  error: boolean;
  refresh: () => void;
}

export function useCapabilityDiscovery(deps: CapabilityDiscoveryDeps): CapabilityDiscoveryState {
  const { bffBaseUrl, authenticatedFetch, surface, enabled = true } = deps;

  const [capabilities, setCapabilities] = React.useState<CapabilityDiscoveryItem[]>([]);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<boolean>(false);
  const [refreshToken, setRefreshToken] = React.useState<number>(0);

  React.useEffect(() => {
    // Inert until enabled — no fetch, no state churn (deferred-read hosts gate on their own signal).
    if (!enabled) return;

    let cancelled = false;

    void (async () => {
      setLoading(true);
      setError(false);
      try {
        const url = new URL(
          `${bffBaseUrl.replace(/\/$/, "")}/api/ai/capabilities`
        );
        if (surface) {
          url.searchParams.set("surface", surface);
        }

        const response = await authenticatedFetch(url.toString());
        if (!response.ok) {
          throw new Error(`capability-discovery fetch failed: ${response.status}`);
        }

        const payload = (await response.json()) as CapabilityDiscoveryResponseShape;
        if (!cancelled) {
          // ADR-015: counts only — never log the capability list contents.
          console.log(
            "[useCapabilityDiscovery] loaded %d launchable capabilities",
            payload.capabilities.length
          );
          setCapabilities(payload.capabilities);
        }
      } catch (err) {
        if (!cancelled) {
          console.error(
            "[useCapabilityDiscovery] fetch threw:",
            err instanceof Error ? err.name : "unknown"
          );
          setError(true);
          // Fail closed: an unreachable catalog means an EMPTY launcher, never
          // a fallback list of invented capability names (ADR-039).
          setCapabilities([]);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [bffBaseUrl, authenticatedFetch, surface, refreshToken, enabled]);

  const refresh = React.useCallback(() => {
    setRefreshToken((t) => t + 1);
  }, []);

  return React.useMemo(
    () => ({ capabilities, loading, error, refresh }),
    [capabilities, loading, error, refresh]
  );
}
