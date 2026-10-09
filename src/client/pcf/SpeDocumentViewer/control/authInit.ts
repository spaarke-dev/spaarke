/**
 * Authentication initialization for the SpeDocumentViewer PCF control.
 *
 * Uses @spaarke/auth (ADR-028) for MSAL token management; BFF calls go through
 * authenticatedFetch() in BffClient.ts.
 *
 * WHY THIS FILE CHANGED (v1.0.28, spaarkeai-word-add-in-r1 task 124, issue #1453)
 * -----------------------------------------------------------------------------
 * v1.0.27 ignored its tenant (`void tenantId`) and let @spaarke/auth fall back to
 * `/organizations` whenever `Xrm…organizationSettings.tenantId` was empty. Entra then
 * signs the user in to their HOME tenant. For a member that is Spaarke's tenant, so
 * the defect stayed hidden; for a B2B guest it is their own tenant, where Spaarke's
 * single-tenant apps do not exist → AADSTS700016 → 401 on /view-url.
 *
 * The 2026-05-13 popup regression that motivated `void tenantId` (ef57fc3f35) was a
 * MALFORMED authority (`…microsoftonline.com/undefined`), not tenant-specific
 * authorities in general. This file therefore passes the tenant explicitly and
 * validates it as a GUID first, so the malformed shape cannot return.
 *
 * Resolution — per-environment sources ONLY:
 *   tenant   : sprk_TenantId → Xrm organizationSettings.tenantId → fail closed
 *   client   : sprk_MsalClientId                                → fail closed
 *   BFF app  : sprk_BffApiAppId                                 → fail closed
 *
 * The form's `tenantId` / `clientAppId` / `bffAppId` properties are deliberately
 * IGNORED. They are static values typed into the Document main form in dev
 * (tenant a221a95e…, app b36e9b91…, BFF 1e40baad…) and shipped inside SpaarkeMaster,
 * so in any other environment they are the WRONG tenant / apps: falling back to them
 * when an environment variable is empty or unreadable (getEnvironmentVariable swallows
 * errors) would sign every user — members included — in to the dev tenant. The Xrm
 * organization tenant is the environment's own tenant, so it is a safe second source
 * for the tenant; there is no equivalent per-environment source for the two app ids.
 * The manifest keeps the properties (optional, ignored) so existing forms stay valid.
 *
 * Fail closed rather than omit `tenantId` and defer to @spaarke/auth's own discovery:
 * that discovery is being changed in flight (task 122) and, in the library version this
 * control is built against today, still ends at `/organizations` — the exact defect.
 * An explicit error is deterministic and independent of the library version.
 *
 * Using sprk_MsalClientId also puts this control on the SAME MSAL client as
 * RelatedDocumentCount and the Document ribbon on the same form (INV-7: one client,
 * one token cache, one consent).
 *
 * Scope: `api://{bffAppId}/user_impersonation` (constraints/auth.md), not SDAP.Access.
 */

import { initAuth } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';
import { getBffApiAppId, getMsalClientId, getTenantId } from '../../shared/utils/environmentVariables';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The auth configuration this control signs in with. */
export interface IResolvedViewerAuth {
  tenantId: string;
  clientId: string;
  bffAppId: string;
  /** Which source supplied the tenant — logged so a wrong environment variable is diagnosable. */
  tenantSource: TenantSource;
}

export type TenantSource = 'environment-variable' | 'xrm-organization';

/** Return the value trimmed when it is a GUID, otherwise undefined ("undefined", "null", "" and junk are rejected). */
export function asGuid(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined;
  const trimmed = value.trim();
  return GUID_PATTERN.test(trimmed) ? trimmed : undefined;
}

/**
 * The Dataverse organization's tenant from Xrm (this frame, then parent, then top).
 * This is the environment's tenant, not the user's home tenant, so it is correct for
 * B2B guests too. Returns undefined when absent (it can be empty on first load).
 */
export function resolveXrmOrganizationTenantId(): string | undefined {
  if (typeof window === 'undefined') return undefined;
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin */
  }
  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const tid = (frame as any).Xrm?.Utility?.getGlobalContext?.()?.organizationSettings?.tenantId;
      const valid = asGuid(tid);
      if (valid) return valid;
    } catch {
      /* cross-origin or Xrm not ready */
    }
  }
  return undefined;
}

function missing(name: string, source: string): Error {
  // Fail closed: never let @spaarke/auth fall back to /organizations (INV-3).
  return new Error(`SpeDocumentViewer cannot sign in: no valid ${name}. Set the ${source} to a GUID.`);
}

/**
 * Resolve tenant, MSAL client and BFF app id from per-environment sources. Throws when
 * any is missing or not a GUID — the caller shows an error instead of signing in to
 * the wrong tenant.
 */
export async function resolveViewerAuth(webApi: ComponentFramework.WebApi): Promise<IResolvedViewerAuth> {
  const [envTenantId, envClientId, envBffAppId] = await Promise.all([
    getTenantId(webApi),
    getMsalClientId(webApi),
    getBffApiAppId(webApi),
  ]);

  let tenantId = asGuid(envTenantId);
  let tenantSource: TenantSource = 'environment-variable';
  if (!tenantId) {
    tenantId = resolveXrmOrganizationTenantId();
    tenantSource = 'xrm-organization';
  }
  if (!tenantId) {
    throw missing('tenant ID', 'sprk_TenantId Dataverse environment variable (Xrm organizationSettings had none)');
  }

  const clientId = asGuid(envClientId);
  if (!clientId) throw missing('MSAL client ID', 'sprk_MsalClientId Dataverse environment variable');

  const bffAppId = asGuid(envBffAppId);
  if (!bffAppId) throw missing('BFF application ID', 'sprk_BffApiAppId Dataverse environment variable');

  return { tenantId, clientId, bffAppId, tenantSource };
}

/** Dataverse org URL for the MSAL redirect URI (same rule as RelatedDocumentCount). */
function resolveRedirectUri(): string {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = (window as any).Xrm as
      | { Utility?: { getGlobalContext?: () => { getClientUrl?: () => string } } }
      | undefined;
    const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
    if (url) return url.replace(/\/+$/, '');
  } catch {
    /* Xrm unavailable (test harness) — fall through */
  }
  return window.location.origin;
}

/** Build the @spaarke/auth configuration. Exported for tests. */
export function buildAuthConfig(resolved: IResolvedViewerAuth, bffApiUrl: string): IAuthConfig {
  return {
    clientId: resolved.clientId,
    // tenantId (not a hand-built authority): the library builds
    // https://login.microsoftonline.com/{tenantId} (INV-3, INV-6).
    tenantId: resolved.tenantId,
    redirectUri: resolveRedirectUri(),
    bffApiScope: `api://${resolved.bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };
}

/** Initialize @spaarke/auth for this control with a tenant-specific authority. */
export async function initializeAuth(resolved: IResolvedViewerAuth, bffApiUrl: string): Promise<void> {
  console.info(
    `[authInit] SpeDocumentViewer: tenant ${resolved.tenantId} (${resolved.tenantSource}), ` +
      `client ${resolved.clientId.substring(0, 8)}…, BFF app ${resolved.bffAppId.substring(0, 8)}…`
  );
  await initAuth(buildAuthConfig(resolved, bffApiUrl));
}
