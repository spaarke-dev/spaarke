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
 * STATUS(task-073): `fetchMeEntitlements` now calls the REAL BFF endpoint
 * `GET /api/v1/external/me/entitlements` (task 072, Option B: workforce from sprk_approlemodulemap
 * App-Role→module; CIAM blanket outside-counsel set). The BFF derives the plane from the caller's token.
 *
 * Rollout safety: on ANY failure of the live call, `fetchMeEntitlements` degrades to the plane-derived
 * `MOCK_BY_PLANE` defaults (a console.warn is emitted) so a transient live-auth hiccup never leaves the
 * workspace tab-less. NFR-06: the client is NEVER the security boundary — the module-data endpoints
 * enforce Tier-1/Tier-2 server-side regardless of what this returns. The mock values are deliberately
 * aligned with the server (workforce = ['legal-front-door','policy-library'] from the seeded map; ciam =
 * ['assigned-work']), so the fallback is behavior-identical to a correct live response for the R2 personas.
 * During UAT, the mock `displayName` ('Sam Rivera' / 'Dana Okafor') is the tell that the fallback fired —
 * a real live response carries the actual signed-in user's name.
 *
 * `VITE_DEV_MOCK=true` (local dev only) still short-circuits to the plane/persona mock without any BFF
 * call, preserving the `?dv_persona=` override for local exercise of every persona's workspace.
 */

import { bffApiCall } from '../auth/bff-client';

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
 * Resolve the caller's entitlements (task 073 — live). Calls the real BFF endpoint in production; on any
 * failure degrades to plane-derived `MOCK_BY_PLANE` defaults (see file header). `teamsHost` is the real
 * bootstrap signal used for the dev-mock plane + the rollout fallback plane; the live BFF derives the
 * plane authoritatively from the caller's token.
 */
export async function fetchMeEntitlements(teamsHost: boolean): Promise<MeEntitlementsResponse> {
  const basePlane: Plane = teamsHost ? 'workforce' : 'ciam';

  // Local dev (VITE_DEV_MOCK=true): short-circuit to the plane/persona mock, no BFF call. Preserves the
  // `?dv_persona=` override for exercising every persona's workspace without a live entitlement backend.
  if (import.meta.env.VITE_DEV_MOCK === 'true') {
    const plane = resolveDevPersonaOverride() ?? basePlane;
    return new Promise(resolve => window.setTimeout(() => resolve(MOCK_BY_PLANE[plane]), 300));
  }

  // Production: the real Tier-1 entitlement endpoint (task 072). bffApiCall attaches the Bearer token +
  // retries once on 401; the BFF resolves the plane from the token and returns this exact shape.
  try {
    return await bffApiCall<MeEntitlementsResponse>('/api/v1/external/me/entitlements');
  } catch (err) {
    // Rollout fallback (NFR-06 — client is not the security boundary): degrade to plane-derived defaults
    // so a transient live-auth failure never leaves the workspace tab-less. The mock displayName is the
    // UAT tell that this fired.
    // eslint-disable-next-line no-console
    console.warn('[me] live /api/v1/external/me/entitlements failed — falling back to plane-derived defaults.', err);
    return MOCK_BY_PLANE[basePlane];
  }
}
