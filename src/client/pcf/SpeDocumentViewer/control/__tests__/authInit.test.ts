/**
 * SpeDocumentViewer auth bootstrap — task 124 (#1453).
 *
 * Pins the behaviour a B2B guest depends on: the control signs in with a
 * tenant-specific authority taken from per-environment configuration
 * (sprk_TenantId, else the Xrm organization tenant), never `/organizations` and
 * never the dev ids typed into the form, and refuses to start without a valid tenant,
 * client and BFF app id.
 */

const mockInitAuth = jest.fn().mockResolvedValue(undefined);
jest.mock('@spaarke/auth', () => ({
  initAuth: (...args: unknown[]) => mockInitAuth(...args),
}));

const mockGetTenantId = jest.fn();
const mockGetMsalClientId = jest.fn();
const mockGetBffApiAppId = jest.fn();
jest.mock('../../../shared/utils/environmentVariables', () => ({
  getTenantId: (...args: unknown[]) => mockGetTenantId(...args),
  getMsalClientId: (...args: unknown[]) => mockGetMsalClientId(...args),
  getBffApiAppId: (...args: unknown[]) => mockGetBffApiAppId(...args),
}));

import {
  asGuid,
  buildAuthConfig,
  initializeAuth,
  resolveViewerAuth,
  resolveXrmOrganizationTenantId,
} from '../authInit';

const webApi = {} as ComponentFramework.WebApi;

const ENV_TENANT = '0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0';
const XRM_TENANT = '7f6e5d4c-3b2a-1908-f7e6-d5c4b3a29180';
/** The dev tenant typed into the Document main form and shipped in SpaarkeMaster. */
const FORM_DEV_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const ENV_CLIENT = '170c98e1-d486-4355-bcbe-170454e0207c';
const ENV_BFF = '1e40baad-e065-4aea-a8d4-4b7ab273458c';

function setEnv(tenant: string | undefined, client: string | undefined, bff: string | undefined): void {
  mockGetTenantId.mockResolvedValue(tenant);
  mockGetMsalClientId.mockResolvedValue(client);
  mockGetBffApiAppId.mockResolvedValue(bff);
}

function setXrmTenant(tenantId: string | undefined): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm =
    tenantId === undefined
      ? undefined
      : { Utility: { getGlobalContext: () => ({ organizationSettings: { tenantId } }) } };
}

beforeEach(() => {
  jest.clearAllMocks();
  setXrmTenant(undefined);
});

afterAll(() => {
  setXrmTenant(undefined);
});

describe('asGuid', () => {
  it.each(['undefined', 'null', '', '   ', 'not-a-guid', 'a221a95e-6abc-4434-aecc'])('rejects %p', value => {
    expect(asGuid(value)).toBeUndefined();
  });

  it('rejects non-strings', () => {
    expect(asGuid(undefined)).toBeUndefined();
    expect(asGuid(null)).toBeUndefined();
    expect(asGuid(42)).toBeUndefined();
  });

  it('accepts and trims a GUID', () => {
    expect(asGuid(`  ${ENV_TENANT} `)).toBe(ENV_TENANT);
  });
});

describe('resolveXrmOrganizationTenantId', () => {
  it('returns the organization tenant when Xrm has one', () => {
    setXrmTenant(XRM_TENANT);
    expect(resolveXrmOrganizationTenantId()).toBe(XRM_TENANT);
  });

  it('returns undefined when Xrm is absent or its tenant is empty / malformed', () => {
    expect(resolveXrmOrganizationTenantId()).toBeUndefined();
    setXrmTenant('');
    expect(resolveXrmOrganizationTenantId()).toBeUndefined();
    setXrmTenant('undefined');
    expect(resolveXrmOrganizationTenantId()).toBeUndefined();
  });
});

describe('resolveViewerAuth', () => {
  it('uses sprk_TenantId when it is set, even if Xrm has a tenant', async () => {
    setEnv(ENV_TENANT, ENV_CLIENT, ENV_BFF);
    setXrmTenant(XRM_TENANT);

    const resolved = await resolveViewerAuth(webApi);

    expect(resolved).toEqual({
      tenantId: ENV_TENANT,
      clientId: ENV_CLIENT,
      bffAppId: ENV_BFF,
      tenantSource: 'environment-variable',
    });
  });

  it('uses the Xrm organization tenant when sprk_TenantId is empty', async () => {
    setEnv('', ENV_CLIENT, ENV_BFF);
    setXrmTenant(XRM_TENANT);

    const resolved = await resolveViewerAuth(webApi);

    expect(resolved.tenantId).toBe(XRM_TENANT);
    expect(resolved.tenantSource).toBe('xrm-organization');
  });

  it('treats a malformed sprk_TenantId ("undefined") as unset and uses the Xrm tenant', async () => {
    setEnv('undefined', ENV_CLIENT, ENV_BFF);
    setXrmTenant(XRM_TENANT);

    expect((await resolveViewerAuth(webApi)).tenantId).toBe(XRM_TENANT);
  });

  it('never uses the form property tenant when the env var read fails (getEnvironmentVariable swallows errors)', async () => {
    // A failed read surfaces as undefined/''. The form's dev tenant is not an input at all:
    // the resolver's only inputs are the env vars and Xrm.
    setEnv(undefined, ENV_CLIENT, ENV_BFF);
    setXrmTenant(XRM_TENANT);

    const resolved = await resolveViewerAuth(webApi);

    expect(resolved.tenantId).toBe(XRM_TENANT);
    expect(resolved.tenantId).not.toBe(FORM_DEV_TENANT);
    expect(resolveViewerAuth.length).toBe(1); // (webApi) only — no form-property parameter
  });

  it('fails closed when neither sprk_TenantId nor Xrm yields a valid tenant', async () => {
    setEnv(undefined, ENV_CLIENT, ENV_BFF);
    setXrmTenant('');

    await expect(resolveViewerAuth(webApi)).rejects.toThrow(/no valid tenant ID/);
    expect(mockInitAuth).not.toHaveBeenCalled();
  });

  it('fails closed when sprk_MsalClientId is missing (no form-property fallback)', async () => {
    setEnv(ENV_TENANT, '', ENV_BFF);

    await expect(resolveViewerAuth(webApi)).rejects.toThrow(/sprk_MsalClientId/);
  });

  it('fails closed when sprk_BffApiAppId is missing (no form-property fallback)', async () => {
    setEnv(ENV_TENANT, ENV_CLIENT, undefined);

    await expect(resolveViewerAuth(webApi)).rejects.toThrow(/sprk_BffApiAppId/);
  });

  it('produces configuration errors the host shows, not ones it treats as a blocked popup', async () => {
    // SpeDocumentViewerHost lets errors mentioning popup / blocked / interaction_required fall
    // through to the viewer; a configuration error must never match, or it would not fail closed.
    const cases: Array<[string | undefined, string | undefined, string | undefined]> = [
      [undefined, ENV_CLIENT, ENV_BFF],
      [ENV_TENANT, undefined, ENV_BFF],
      [ENV_TENANT, ENV_CLIENT, undefined],
    ];
    for (const [tenant, client, bff] of cases) {
      setEnv(tenant, client, bff);
      const error = await resolveViewerAuth(webApi).catch((e: Error) => e);
      expect(error).toBeInstanceOf(Error);
      expect((error as Error).message.toLowerCase()).not.toMatch(/popup|blocked|interaction_required/);
    }
  });
});

describe('buildAuthConfig / initializeAuth', () => {
  const resolved = {
    tenantId: ENV_TENANT,
    clientId: ENV_CLIENT,
    bffAppId: ENV_BFF,
    tenantSource: 'environment-variable' as const,
  };

  it('passes the tenant explicitly and uses the user_impersonation scope', () => {
    const config = buildAuthConfig(resolved, 'https://bff.example.net');

    expect(config.tenantId).toBe(ENV_TENANT);
    expect(config.clientId).toBe(ENV_CLIENT);
    expect(config.bffApiScope).toBe(`api://${ENV_BFF}/user_impersonation`);
    expect(config.bffBaseUrl).toBe('https://bff.example.net');
    // The library builds the authority from tenantId; a hand-built authority is not passed.
    expect(config.authority).toBeUndefined();
  });

  it('never configures SDAP.Access or a multi-tenant authority', () => {
    const serialized = JSON.stringify(buildAuthConfig(resolved, 'https://bff.example.net'));

    expect(serialized).not.toMatch(/SDAP\.Access/);
    expect(serialized).not.toMatch(/organizations|\/common/);
  });

  it('initializes @spaarke/auth with the resolved tenant', async () => {
    await initializeAuth(resolved, 'https://bff.example.net');

    expect(mockInitAuth).toHaveBeenCalledTimes(1);
    expect(mockInitAuth.mock.calls[0][0]).toMatchObject({ tenantId: ENV_TENANT, clientId: ENV_CLIENT });
  });
});
