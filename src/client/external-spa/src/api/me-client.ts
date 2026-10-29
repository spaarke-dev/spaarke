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
 * (`teamsHost` — a Teams tab is workforce SSO, a standalone browser is CIAM unless the workforce home plane is mounted; see
 * `src/host/TeamsHostAdapter.ts` + `main.tsx`) — only `entitlements` is mocked pending task 022.
 * A dev-only `?dv_persona=workforce|ciam|admin` override lets every persona's role-defaulted workspace be exercised
 * locally without a live entitlement backend. It runs only in dev-mock mode (`isDevMockEnabled()`, dev-mock.ts: the
 * dev server with `VITE_DEV_MOCK=true`), which a built bundle cannot enter, so it has no production surface (NFR-06:
 * the client is never the enforcement boundary regardless).
 *
 * STATUS(task-073): `fetchMeEntitlements` now calls the REAL BFF endpoint
 * `GET /api/v1/external/me/entitlements` (task 072; A8 / r3 task 011: default modules by
 * server-resolved principal kind, plus sprk_approlemodulemap extras). The BFF derives the plane from the caller's token.
 *
 * Failure handling (task 042, FR-18 / #1566):
 *   - 401 (after bffApiCall's one retry) or 403 → the promise REJECTS with the ApiError, so the workspace shows the
 *     sign-in or denied state — never a default workspace or a stand-in user.
 *   - a server failure (an ApiError with statusCode >= 500) → the rollout fallback: the plane's default modules
 *     (`PLANE_DEFAULTS`, aligned with the server's defaults) with an EMPTY identity, plus a console.warn (the UAT tell
 *     that it fired). NFR-06: the module-data endpoints enforce Tier-1/Tier-2 server-side regardless of what this
 *     returns.
 *   - EXCEPT a 5xx whose ProblemDetails `reasonCode` (or `errorCode`) is `sdap.access.registration_owner_unconfigured`
 *     (ISS-005, #1673): the workforce first sign-in was refused because the organization's customer business unit setting
 *     is missing and nothing was created. That is a configuration refusal, not an outage, so it REJECTS with
 *     `OwnerUnconfiguredError` and the workspace shows its fixed "not configured" message in the denied state.
 *   - anything else REJECTS: a token-acquisition failure (TeamsWorkforceAuthError, BackendSelectionError, the
 *     redirect-in-progress Error), a network fault, or a 4xx other than 401/403. The caller was never answered by the
 *     BFF, so showing a default workspace would present modules for a caller the server has not seen. The workspace
 *     renders the sign-in state for a failed Teams sign-in and its load-error state for the rest.
 */

import { bffApiCall } from '../auth/bff-client';
import { isWorkforceHomePlaneActive } from '../auth/msal-auth';
import { isDevMockEnabled } from '../dev-mock';
import { ApiError } from '../types';

/**
 * The BFF's reason/error code when a workforce first sign-in is refused because the customer business unit setting is
 * missing (ISS-005, #1673; the registration route answers with the same code).
 */
export const REGISTRATION_OWNER_UNCONFIGURED = 'sdap.access.registration_owner_unconfigured';

/** The fixed sentence shown for that refusal. The server `detail` is deliberately NOT shown; this string is the contract. */
export const OWNER_UNCONFIGURED_MESSAGE =
  'Sign-in is not configured for this organization. Contact your administrator.';

/** The title shown above it: the state is an unfinished set-up, not a permission problem ("Access denied" would contradict the sentence). */
export const OWNER_UNCONFIGURED_TITLE = 'Sign-in is not set up yet';

/** `/me` was refused with `registration_owner_unconfigured` (a 5xx that must NOT fall back to a default workspace). */
export class OwnerUnconfiguredError extends Error {
  constructor(public readonly statusCode: number) {
    super(OWNER_UNCONFIGURED_MESSAGE);
    this.name = 'OwnerUnconfiguredError';
  }
}

/** True when a 5xx ApiError's ProblemDetails body names the owner-unconfigured code in `reasonCode` or `errorCode`. */
function isOwnerUnconfigured(err: ApiError): boolean {
  try {
    const body = JSON.parse(err.message) as { reasonCode?: unknown; errorCode?: unknown } | null;
    return body?.reasonCode === REGISTRATION_OWNER_UNCONFIGURED || body?.errorCode === REGISTRATION_OWNER_UNCONFIGURED;
  } catch {
    return false; // not a ProblemDetails body: an ordinary server failure
  }
}

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
  /**
   * The Legal Front Door intake types (`sprk_requesttype` option values) the caller may submit - the rule the submit
   * route enforces (r3 task 052; `MeEntitlementsResponse.IntakeRequestTypes`). `[]` means none; `undefined` means the
   * server did not say (an older BFF, or the `PLANE_DEFAULTS` rollout fallback below) - NOT "none" and NOT "all": the
   * entry points then fall back to the plane/entitlement rule in `intake/intakeRegistry.ts` (`isIntakeTypeAvailable`).
   */
  intakeRequestTypes?: number[];
}

/**
 * Plane defaults for the production ROLLOUT fallback (a 5xx / network failure of the live call): the plane's default
 * modules and NO identity — the fallback never presents a user (task 042, FR-18: no mock or default user in a
 * production build). Entitlement ids match `widgetRegistry.ts`'s `requiredEntitlement` values and the server's
 * defaults (workforce = legal-front-door + policy-library; CIAM = assigned-work). `intakeRequestTypes` is deliberately
 * absent from the fallback: the client cannot know the per-customer partner switches, so it leaves the field
 * `undefined` and the intake entry points apply their documented fallback rule.
 */
const PLANE_DEFAULTS: Record<Plane, MeEntitlementsResponse> = {
  workforce: { displayName: '', email: '', plane: 'workforce', entitlements: ['legal-front-door', 'policy-library'] },
  ciam: { displayName: '', email: '', plane: 'ciam', entitlements: ['assigned-work'] },
  admin: { displayName: '', email: '', plane: 'admin', entitlements: ['admin'] },
};

/**
 * Dev-mock personas — read ONLY inside `isDevMockEnabled()` branches, so a built bundle (where that switch is the
 * literal `false`) carries neither the branch nor these names (task 042).
 */
function devPersona(plane: Plane): MeEntitlementsResponse {
  const names: Record<Plane, [string, string]> = {
    workforce: ['Sam Rivera', 'sam.rivera@contoso.example'],
    ciam: ['Dana Okafor', 'dana.okafor@partner-firm.example'],
    admin: ['Alex Reyes', 'alex.reyes@contoso.example'],
  };
  const [displayName, email] = names[plane];
  return { ...PLANE_DEFAULTS[plane], displayName, email };
}

const DEV_PERSONA_PARAM = 'dv_persona';

/** Dev-only persona override — active ONLY in dev-mock mode (see file header). */
function resolveDevPersonaOverride(): Plane | null {
  try {
    const value = new URLSearchParams(window.location.search).get(DEV_PERSONA_PARAM);
    if (value === 'workforce' || value === 'ciam' || value === 'admin') return value;
  } catch {
    // window/URLSearchParams unavailable — no override, fall through to the real signal.
  }
  return null;
}

/**
 * Resolve the caller's entitlements (task 073 — live). Calls the real BFF endpoint; a 401 or 403 REJECTS (the caller
 * renders the sign-in / denied state — task 042, FR-18); a server failure (5xx) degrades to the plane's default
 * modules with no identity, and every other failure rejects (see file header). `teamsHost` is the real bootstrap signal used for the dev-mock plane + the
 * rollout fallback plane; the live BFF derives the plane authoritatively from the caller's token.
 */
export async function fetchMeEntitlements(teamsHost: boolean): Promise<MeEntitlementsResponse> {
  // A Teams tab is workforce SSO; so is the browser workforce HOME plane (A9, task 058) - the signal for that is the
  // mounted plane, not `teamsHost`, which is false in a browser. Without it a home-plane user on the 5xx degrade would
  // get the CIAM default modules.
  const basePlane: Plane = teamsHost || isWorkforceHomePlaneActive() ? 'workforce' : 'ciam';

  // Local dev only (dev-mock.ts): short-circuit to the plane/persona mock, no BFF call. Preserves the
  // `?dv_persona=` override for exercising every persona's workspace without a live entitlement backend.
  if (isDevMockEnabled()) {
    const persona = devPersona(resolveDevPersonaOverride() ?? basePlane);
    return new Promise(resolve => window.setTimeout(() => resolve(persona), 300));
  }

  // The real Tier-1 entitlement endpoint (task 072). bffApiCall attaches the Bearer token + retries once on 401;
  // the BFF resolves the plane from the token and returns this exact shape.
  try {
    return await bffApiCall<MeEntitlementsResponse>('/api/v1/external/me/entitlements');
  } catch (err) {
    // Rollout fallback (NFR-06 — client is not the security boundary), for a SERVER failure only: the BFF answered
    // with a 5xx, so degrade to the plane's default modules rather than leave the workspace tab-less. The console
    // warning is the UAT tell that this fired.
    if (err instanceof ApiError && err.statusCode >= 500) {
      // A configuration refusal, not an outage: never a default workspace (ISS-005).
      if (isOwnerUnconfigured(err)) throw new OwnerUnconfiguredError(err.statusCode);
      console.warn('[me] live /api/v1/external/me/entitlements failed — falling back to plane-derived defaults.', err);
      return PLANE_DEFAULTS[basePlane];
    }
    // Not signed in (401 after the retry), not allowed (403), another 4xx, a failed token acquisition or a network
    // fault: never a default workspace (task 042). The caller maps it to a state of its own.
    throw err;
  }
}
