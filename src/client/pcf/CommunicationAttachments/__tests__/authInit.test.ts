/**
 * authInit — the environment tenant reaches @spaarke/auth (#1453).
 *
 * A B2B guest signed in against `/organizations` lands in their HOME tenant, where Spaarke's
 * single-tenant app does not exist (AADSTS700016). The control therefore passes the tenant it
 * loaded, and passes nothing (never "undefined") when it has no usable one.
 */
import { initAuth } from '@spaarke/auth';
import { initializeAuth } from '../CommunicationAttachments/authInit';

const TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const CLIENT = '170c98e1-d486-4355-bcbe-170454e0207c';
const BFF = '1e40baad-e065-4aea-a8d4-4b7ab273458c';

const initAuthMock = initAuth as unknown as jest.Mock;

function lastConfig(): Record<string, unknown> {
  return initAuthMock.mock.calls[initAuthMock.mock.calls.length - 1][0] as Record<string, unknown>;
}

describe('CommunicationAttachments initializeAuth', () => {
  beforeEach(() => initAuthMock.mockClear());

  it('passes a valid tenant as tenantId and never builds an authority itself', async () => {
    await initializeAuth(TENANT, CLIENT, BFF, 'https://api.example.com', 'https://org.crm.dynamics.com');
    const config = lastConfig();
    expect(config.tenantId).toBe(TENANT);
    expect(config.authority).toBeUndefined();
    expect(config.bffApiScope).toBe(`api://${BFF}/user_impersonation`);
  });

  it.each(['', 'undefined', 'null', 'organizations', 'common', 'not a tenant'])(
    'passes no tenantId for the invalid value %p',
    async bad => {
      await initializeAuth(bad, CLIENT, BFF, 'https://api.example.com', 'https://org.crm.dynamics.com');
      expect(lastConfig()).not.toHaveProperty('tenantId');
    }
  );
});
