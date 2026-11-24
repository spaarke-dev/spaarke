/**
 * PCF auth bootstrap — ONE copy for the Communication* PCFs (#2967; previously copied into five `authInit.ts`
 * files and five `useEffect` blocks that differed only in the control name).
 *
 * Plain TypeScript, no React: `usePcfAuthBootstrap` wraps it for the host component. Behaviour is unchanged
 * (ADR-028): the same `@spaarke/auth` `initAuth` call, the same manifest-then-Dataverse-environment-variable
 * resolution, the same tenant-specific authority input. `@spaarke/ui-components` is NOT imported here (its bare
 * barrel breaks a PCF bundle, and a deep `dist/` path breaks code pages that alias it to `src/`), so the caller
 * passes in what it already imports: `getXrm` and the environment-variable readers.
 */
import { initAuth, isValidTenant } from '@spaarke/auth';
import type { IAuthConfig } from '@spaarke/auth';

/** The slice of the PCF `shared/utils/environmentVariables` readers the bootstrap needs. */
export interface IPcfEnvironmentReaders<TWebApi> {
  getMsalClientId(webApi: TWebApi): Promise<string>;
  getBffApiAppId(webApi: TWebApi): Promise<string>;
  /** Host-only BFF URL (strips `/api`; `@spaarke/auth` re-adds it). */
  getApiBaseUrl(webApi: TWebApi): Promise<string>;
  getTenantId(webApi: TWebApi): Promise<string>;
}

/**
 * Reads the Dataverse client URL. Each PCF passes `() => getXrm('clientUrl')?.Utility?.getGlobalContext().getClientUrl()`,
 * so `getXrm` itself is never passed around (the Xrm capability guard follows every `getXrm` value to its uses).
 */
export type GetClientUrl = () => unknown;

/** Initialise `@spaarke/auth` for a PCF. `tenantId` is optional: absent or invalid, `@spaarke/auth` discovery decides. */
export async function initializeAuth(
  clientAppId: string,
  bffAppId: string,
  bffApiUrl: string,
  dataverseUrl: string,
  tenantId?: string
): Promise<void> {
  const config: IAuthConfig = {
    clientId: clientAppId,
    // The environment's tenant makes the authority tenant-specific (a B2B guest signed in against /organizations
    // lands in their home tenant — #1453). A malformed value is rejected by isValidTenant(), never passed on.
    ...(isValidTenant(tenantId) ? { tenantId: tenantId.trim() } : {}),
    redirectUri: dataverseUrl,
    bffApiScope: `api://${bffAppId}/user_impersonation`,
    bffBaseUrl: bffApiUrl,
    proactiveRefresh: true,
  };
  await initAuth(config);
}

/** The Dataverse org URL for the MSAL redirect URI (frame-walk to Xrm), else this window's origin. */
export function resolveDataverseUrl(getClientUrl: GetClientUrl): string {
  try {
    const url = getClientUrl();
    if (typeof url === 'string' && url.length > 0) return url;
    // eslint-disable-next-line no-restricted-syntax -- no Xrm frame, or a throwing probe: fall back to the window origin
  } catch {
    /* fall through to window.location.origin */
  }
  return window.location.origin;
}

export interface IPcfAuthBootstrapOptions<TWebApi> {
  /** Subject of the "not configured" message, e.g. "Message actions" (rendered as "<label> are not configured…"). */
  controlLabel: string;
  webApi: TWebApi;
  manifestClientAppId?: string;
  manifestBffAppId?: string;
  manifestApiBaseUrl?: string;
  env: IPcfEnvironmentReaders<TWebApi>;
  getClientUrl: GetClientUrl;
}

export type PcfAuthBootstrapResult =
  | { status: 'ready'; baseUrl: string }
  | { status: 'not-configured'; message: string };

/** The message shown when the three settings cannot be resolved. `controlLabel` includes its verb: "X are" / "X is". */
export function notConfiguredMessage(controlLabel: string): string {
  return (
    `${controlLabel} not configured. Set the sprk_MsalClientId / sprk_BffApiAppId / ` +
    'sprk_BffApiBaseUrl Dataverse environment variables (or the PCF Client App ID / BFF App ID / API URL inputs).'
  );
}

/**
 * Resolve the three settings (manifest input first, then Dataverse environment variable), then initialise auth.
 * Throws if a reader or `initAuth` throws; the hook turns that into the error message.
 */
export async function bootstrapPcfAuth<TWebApi>(
  opts: IPcfAuthBootstrapOptions<TWebApi>
): Promise<PcfAuthBootstrapResult> {
  const { webApi, env } = opts;
  const clientAppId = opts.manifestClientAppId || (await env.getMsalClientId(webApi));
  const bffAppId = opts.manifestBffAppId || (await env.getBffApiAppId(webApi));
  const baseUrl = opts.manifestApiBaseUrl || (await env.getApiBaseUrl(webApi));
  if (!clientAppId || !bffAppId || !baseUrl) {
    return { status: 'not-configured', message: notConfiguredMessage(opts.controlLabel) };
  }
  await initializeAuth(
    clientAppId,
    bffAppId,
    baseUrl,
    resolveDataverseUrl(opts.getClientUrl),
    await env.getTenantId(webApi)
  );
  return { status: 'ready', baseUrl };
}
