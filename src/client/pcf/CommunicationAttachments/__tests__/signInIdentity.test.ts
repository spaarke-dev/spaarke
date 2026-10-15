/**
 * #1453 — a PCF's sign-in identity comes from the environment's own variables FIRST. Shipped forms carry static
 * dev values (the Matter main form binds the dev tenant), so a form value must only ever be a fallback.
 * Tests the shared helper (`src/client/pcf/shared/utils/environmentVariables.ts`) used by SemanticSearchControl,
 * CommunicationAttachments and DocumentRelationshipViewer.
 */
import { clearEnvironmentVariableCache, resolveSignInIdentity } from '../../shared/utils/environmentVariables';

const ENV_TENANT = '11111111-1111-1111-1111-111111111111';
const FORM_TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';

/** A fake PCF WebApi serving environment variables: name → value (undefined = not defined in Dataverse). */
function webApiWith(values: Record<string, string | undefined>, failing = false): ComponentFramework.WebApi {
  return {
    retrieveMultipleRecords: jest.fn(async (entity: string, query: string) => {
      if (failing) throw new Error('network');
      if (entity === 'environmentvariabledefinition') {
        const name = /schemaname eq '([^']+)'/.exec(query)?.[1] ?? '';
        return { entities: name in values ? [{ environmentvariabledefinitionid: name, defaultvalue: '' }] : [] };
      }
      const id = /_value eq '([^']+)'/.exec(query)?.[1] ?? '';
      const value = values[id];
      return { entities: value !== undefined ? [{ value }] : [] };
    }),
  } as unknown as ComponentFramework.WebApi;
}

const FORM = { tenantId: FORM_TENANT, clientAppId: 'form-client', bffAppId: 'form-bff' };

describe('resolveSignInIdentity (#1453)', () => {
  beforeEach(() => clearEnvironmentVariableCache());

  it('the environment variables win over the form values', async () => {
    const identity = await resolveSignInIdentity(
      webApiWith({ sprk_TenantId: ENV_TENANT, sprk_MsalClientId: 'env-client', sprk_BffApiAppId: 'env-bff' }),
      FORM
    );
    expect(identity).toEqual({ tenantId: ENV_TENANT, clientAppId: 'env-client', bffAppId: 'env-bff' });
  });

  it('falls back to a form value only for a variable the environment does not define', async () => {
    const identity = await resolveSignInIdentity(webApiWith({ sprk_TenantId: ENV_TENANT }), FORM);
    expect(identity).toEqual({ tenantId: ENV_TENANT, clientAppId: 'form-client', bffAppId: 'form-bff' });
  });

  it('treats an empty variable value as not configured', async () => {
    const identity = await resolveSignInIdentity(webApiWith({ sprk_TenantId: '  ' }), FORM);
    expect(identity.tenantId).toBe(FORM_TENANT);
  });

  it('returns empty strings when neither source has a value (the library then discovers the tenant)', async () => {
    const identity = await resolveSignInIdentity(webApiWith({}));
    expect(identity).toEqual({ tenantId: '', clientAppId: '', bffAppId: '' });
  });

  it('a failed variable read falls back to the form value rather than throwing', async () => {
    const identity = await resolveSignInIdentity(webApiWith({}, true), { tenantId: FORM_TENANT });
    expect(identity.tenantId).toBe(FORM_TENANT);
  });
});
