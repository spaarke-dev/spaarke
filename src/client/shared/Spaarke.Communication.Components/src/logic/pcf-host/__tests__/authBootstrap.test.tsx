/**
 * PCF auth bootstrap (#2967): config resolution, the "not configured" path, tenant pass-through, and the hook's
 * ready / not-configured / error states. If config resolution or the "not configured" message breaks, these fail.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { initAuth } from '@spaarke/auth';
import {
  bootstrapPcfAuth,
  initializeAuth,
  notConfiguredMessage,
  resolveDataverseUrl,
  usePcfAuthBootstrap,
  type IPcfEnvironmentReaders,
} from '../index';

jest.mock('@spaarke/auth', () => ({
  initAuth: jest.fn().mockResolvedValue(undefined),
  isValidTenant: (v: unknown) => typeof v === 'string' && /^[0-9a-f-]{36}$/i.test(v.trim()),
}));

const initAuthMock = initAuth as unknown as jest.Mock;
const TENANT = 'a221a95e-6abc-4434-aecc-e48338a1b2f2';
const webApi = {};
const getXrm = () => ({
  Utility: { getGlobalContext: () => ({ getClientUrl: () => 'https://org.crm.dynamics.com' }) },
});

function env(over: Partial<Record<keyof IPcfEnvironmentReaders<object>, string>> = {}): IPcfEnvironmentReaders<object> {
  const v = {
    getMsalClientId: 'client',
    getBffApiAppId: 'bff',
    getApiBaseUrl: 'https://bff.example',
    getTenantId: TENANT,
    ...over,
  };
  return {
    getMsalClientId: jest.fn().mockResolvedValue(v.getMsalClientId),
    getBffApiAppId: jest.fn().mockResolvedValue(v.getBffApiAppId),
    getApiBaseUrl: jest.fn().mockResolvedValue(v.getApiBaseUrl),
    getTenantId: jest.fn().mockResolvedValue(v.getTenantId),
  };
}

beforeEach(() => initAuthMock.mockClear());

describe('bootstrapPcfAuth', () => {
  it('resolves from the Dataverse environment variables and initialises auth', async () => {
    const result = await bootstrapPcfAuth({ controlLabel: 'X is', webApi, env: env(), getXrm });
    expect(result).toEqual({ status: 'ready', baseUrl: 'https://bff.example' });
    expect(initAuthMock).toHaveBeenCalledTimes(1);
    expect(initAuthMock.mock.calls[0][0]).toMatchObject({
      clientId: 'client',
      tenantId: TENANT,
      redirectUri: 'https://org.crm.dynamics.com',
      bffApiScope: 'api://bff/user_impersonation',
      bffBaseUrl: 'https://bff.example',
      proactiveRefresh: true,
    });
    expect(initAuthMock.mock.calls[0][0].authority).toBeUndefined();
  });

  it('prefers manifest inputs and does not read the environment variables for them', async () => {
    const e = env();
    await bootstrapPcfAuth({
      controlLabel: 'X is',
      webApi,
      manifestClientAppId: 'm-client',
      manifestBffAppId: 'm-bff',
      manifestApiBaseUrl: 'https://m.example',
      env: e,
      getXrm,
    });
    expect(e.getMsalClientId).not.toHaveBeenCalled();
    expect(e.getBffApiAppId).not.toHaveBeenCalled();
    expect(e.getApiBaseUrl).not.toHaveBeenCalled();
    expect(initAuthMock.mock.calls[0][0]).toMatchObject({ clientId: 'm-client', bffBaseUrl: 'https://m.example' });
  });

  it.each(['getMsalClientId', 'getBffApiAppId', 'getApiBaseUrl'] as const)(
    'reports "not configured" with the control label and never calls initAuth when %s is empty',
    async missing => {
      const result = await bootstrapPcfAuth({
        controlLabel: 'Message actions are',
        webApi,
        env: env({ [missing]: '' }),
        getXrm,
      });
      expect(result).toEqual({ status: 'not-configured', message: notConfiguredMessage('Message actions are') });
      expect(notConfiguredMessage('Message actions are')).toMatch(
        /^Message actions are not configured\. Set the sprk_MsalClientId/
      );
      expect(initAuthMock).not.toHaveBeenCalled();
    }
  );
});

describe('initializeAuth', () => {
  it.each(['', 'undefined', 'organizations', 'common', 'not a tenant'])('passes no tenantId for %p', async bad => {
    await initializeAuth('c', 'b', 'https://api', 'https://org', bad);
    expect(initAuthMock.mock.calls[0][0]).not.toHaveProperty('tenantId');
  });
});

describe('resolveDataverseUrl', () => {
  it('falls back to the window origin when Xrm gives no client URL or throws', () => {
    expect(resolveDataverseUrl(() => undefined)).toBe(window.location.origin);
    expect(
      resolveDataverseUrl(() => {
        throw new Error('cross-frame');
      })
    ).toBe(window.location.origin);
  });
});

describe('usePcfAuthBootstrap', () => {
  function Probe(props: { e: IPcfEnvironmentReaders<object> }): React.ReactElement {
    const s = usePcfAuthBootstrap({ controlLabel: 'The panel is', webApi, env: props.e, getXrm });
    return (
      <div>
        <span data-testid="ready">{String(s.authReady)}</span>
        <span data-testid="error">{s.authError ?? ''}</span>
        <span data-testid="base">{s.bffBaseUrl}</span>
      </div>
    );
  }

  it('becomes ready with the host-only BFF base URL', async () => {
    render(<Probe e={env()} />);
    await waitFor(() => expect(screen.getByTestId('ready').textContent).toBe('true'));
    expect(screen.getByTestId('base').textContent).toBe('https://bff.example');
    expect(screen.getByTestId('error').textContent).toBe('');
  });

  it('shows the not-configured message and stays not ready', async () => {
    render(<Probe e={env({ getBffApiAppId: '' })} />);
    await waitFor(() => expect(screen.getByTestId('error').textContent).toBe(notConfiguredMessage('The panel is')));
    expect(screen.getByTestId('ready').textContent).toBe('false');
    expect(initAuthMock).not.toHaveBeenCalled();
  });

  it('surfaces an initAuth failure as the error', async () => {
    initAuthMock.mockRejectedValueOnce(new Error('popup blocked'));
    render(<Probe e={env()} />);
    await waitFor(() => expect(screen.getByTestId('error').textContent).toBe('popup blocked'));
    expect(screen.getByTestId('ready').textContent).toBe('false');
  });
});
