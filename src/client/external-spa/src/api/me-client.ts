/**
 * me-client — the entitlement `/me` contract for the workspace widget registry (task 012).
 *
 * Defines the SHAPE the future BFF entitlement endpoint will return (spec.md FR-01/"module-
 * entitlement layer" — `/me` returns the caller's modules; task 022 builds the server side) and
 * returns a client-side mock TODAY so the entitlement-gated widget registry + role-defaulted
 * tabs can be built and reviewed ahead of that endpoint (task 012 POML: "consume the real /me
 * contract; mock the payload where the endpoint is not yet deployed").
 *
 * `plane` is derived from the REAL host-detection signal already established at bootstrap
 * (`teamsHost` — a Teams tab is workforce SSO, a standalone browser is CIAM; see
 * `src/host/TeamsHostAdapter.ts` + `main.tsx`) — only `entitlements` is mocked pending task 022.
 * A dev-only `?dv_persona=workforce|ciam|admin` override (gated behind the existing
 * `VITE_DEV_MOCK` flag, same convention as `mocks/mock-service.ts`) lets every persona's
 * role-defaulted workspace be exercised locally without a live entitlement backend — it never
 * activates outside `VITE_DEV_MOCK=true`, so it has no production security surface (NFR-06: the
 * client is never the enforcement boundary regardless).
 *
 * STATUS(task-072): the real BFF entitlement endpoint now EXISTS —
 * `GET /api/v1/external/me/entitlements` (Option B: workforce from sprk_approlemodulemap App-Role→module;
 * CIAM blanket outside-counsel set), returning this exact `MeEntitlementsResponse` shape. The mock values
 * below are DELIBERATELY aligned with what that endpoint returns per plane (workforce =
 * ['legal-front-door','policy-library'] from the seeded map; ciam = ['assigned-work']), so the swap is a
 * one-line change with no consumer impact.
 *
 * TODO(task-073): flip `fetchMeEntitlements` to
 * `bffApiCall<MeEntitlementsResponse>('/api/v1/external/me/entitlements')`, co-located with the
 * deploy + dual-plane (workforce `roles` claim + CIAM) UAT that a live auth call requires. Until then the
 * mock keeps the working SPA independent of an unverified live auth path (owner decision, 2026-08-11).
 */

/** Identity plane — matches the caller's auth authority (ADR-028 dual-plane). */
export type Plane = 'workforce' | 'ciam' | 'admin';

/**
 * The `/me` entitlement contract. `entitlements` is a flat list of module ids the caller is
 * entitled to (Tier-1, server-enforced in production per NFR-06) — the widget registry gates
 * widget visibility/routability against this list.
 */
export interface MeEntitlementsResponse {
  displayName: string;
  email: string;
  plane: Plane;
  entitlements: string[];
}

/** Mock `/me` payloads, one per plane — entitlement ids match `widgetRegistry.ts`'s `requiredEntitlement` values. */
const MOCK_BY_PLANE: Record<Plane, MeEntitlementsResponse> = {
  workforce: {
    displayName: 'Sam Rivera',
    email: 'sam.rivera@contoso.example',
    plane: 'workforce',
    entitlements: ['legal-front-door', 'policy-library'],
  },
  ciam: {
    displayName: 'Dana Okafor',
    email: 'dana.okafor@partner-firm.example',
    plane: 'ciam',
    entitlements: ['assigned-work'],
  },
  admin: {
    displayName: 'Alex Reyes',
    email: 'alex.reyes@contoso.example',
    plane: 'admin',
    entitlements: ['admin'],
  },
};

const DEV_PERSONA_PARAM = 'dv_persona';

/** Dev-only persona override — active ONLY when VITE_DEV_MOCK=true (see file header). */
function resolveDevPersonaOverride(): Plane | null {
  if (import.meta.env.VITE_DEV_MOCK !== 'true') return null;
  try {
    const value = new URLSearchParams(window.location.search).get(DEV_PERSONA_PARAM);
    if (value === 'workforce' || value === 'ciam' || value === 'admin') return value;
  } catch {
    // window/URLSearchParams unavailable — no override, fall through to the real signal.
  }
  return null;
}

/**
 * Resolve the caller's entitlements. `teamsHost` is the real bootstrap signal (see file header);
 * `entitlements` are mocked pending task 022. Never rejects for the mock path — a future real
 * implementation may reject on network failure, so callers (WorkspaceHomePage) still handle it.
 */
export async function fetchMeEntitlements(teamsHost: boolean): Promise<MeEntitlementsResponse> {
  const basePlane: Plane = teamsHost ? 'workforce' : 'ciam';
  const plane = resolveDevPersonaOverride() ?? basePlane;
  const response = MOCK_BY_PLANE[plane];
  // Simulate latency so the workspace's loading state is exercised — mirrors the delay()
  // convention in mocks/mock-service.ts used elsewhere in this SPA.
  return new Promise(resolve => window.setTimeout(() => resolve(response), 300));
}
