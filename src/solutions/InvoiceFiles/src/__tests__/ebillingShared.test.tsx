/**
 * The e-billing pages' shared scaffolding (`src/solutions/_ebilling-shared/`, #2965): the auth + theme bootstrap every e-billing code page
 * mounts through, and the one `toFailure`. Lives here because this page has the jsdom + Testing Library setup; the shared folder has no
 * toolchain of its own.
 *
 * `@spaarke/ui-components` is mocked (virtual: the shared folder has no `node_modules`, and the barrel is never loaded by a test); the
 * `@spaarke/auth` entry points the bootstrap calls are mocked so the SEQUENCE is what is asserted (ADR-028: the page's own
 * `ensureAuthInitialized` runs before the page renders).
 */

import { renderHook, waitFor, act } from '@testing-library/react';

const mockEnsureNavigator = jest.fn();
const mockInitAppInsights = jest.fn();
jest.mock(
  '@spaarke/ui-components',
  () => ({
    resolveCodePageTheme: jest.fn(() => ({ name: 'light' })),
    setupCodePageThemeListener: jest.fn(() => () => undefined),
    AppInsightsService: { initializeFromRuntime: (...a: unknown[]) => mockInitAppInsights(...a) },
    ensureNavigatorSidePane: () => mockEnsureNavigator(),
  }),
  { virtual: true }
);

const mockResolveRuntimeConfig = jest.fn();
const mockGetTenantId = jest.fn();
jest.mock('@spaarke/auth', () => {
  const actual = jest.requireActual('@spaarke/auth');
  return {
    ...actual,
    resolveRuntimeConfig: () => mockResolveRuntimeConfig(),
    getAuthProvider: () => ({ getTenantId: () => mockGetTenantId() }),
    getTelemetryConnectionString: jest.fn(),
  };
});

import { ApiError, AuthError } from '@spaarke/auth';
import { bootstrapAuth, useCodePageBootstrap } from '../../../_ebilling-shared/codePageBootstrap';
import { toFailure as sharedToFailure } from '../../../_ebilling-shared/apiFailure';
import { toFailure as invoiceFilesToFailure } from '../services/invoiceFilesApi';

const config = { bffBaseUrl: 'https://bff.example', tenantId: 'tenant-1' } as never;

beforeEach(() => {
  jest.clearAllMocks();
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});

describe('bootstrapAuth', () => {
  it('sets the runtime config BEFORE it initialises auth, and starts Application Insights', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    const order: string[] = [];
    const setRuntimeConfig = jest.fn(() => void order.push('setRuntimeConfig'));
    const ensureAuthInitialized = jest.fn(async () => void order.push('ensureAuthInitialized'));

    await bootstrapAuth({ setRuntimeConfig, ensureAuthInitialized });

    expect(order).toEqual(['setRuntimeConfig', 'ensureAuthInitialized']);
    expect(mockInitAppInsights).toHaveBeenCalledTimes(1);
    expect(mockGetTenantId).not.toHaveBeenCalled(); // the config already names the tenant
  });

  it('falls back to the signed-in user\'s tenant when the config has none', async () => {
    mockResolveRuntimeConfig.mockResolvedValue({ bffBaseUrl: 'https://bff.example', tenantId: '' });
    mockGetTenantId.mockResolvedValue('tenant-from-token');
    const setRuntimeConfig = jest.fn();

    await bootstrapAuth({ setRuntimeConfig, ensureAuthInitialized: async () => undefined });

    expect(setRuntimeConfig).toHaveBeenCalledTimes(2);
    expect(setRuntimeConfig).toHaveBeenLastCalledWith({ bffBaseUrl: 'https://bff.example', tenantId: 'tenant-from-token' });
  });

  it('rejects when auth cannot be initialised (the page fails closed)', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    await expect(
      bootstrapAuth({ setRuntimeConfig: jest.fn(), ensureAuthInitialized: async () => Promise.reject(new Error('no sso')) })
    ).rejects.toThrow('no sso');
  });
});

describe('useCodePageBootstrap', () => {
  const deps = () => ({ setRuntimeConfig: jest.fn(), ensureAuthInitialized: jest.fn(async () => undefined) });

  it('becomes ready once auth is initialised', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    const d = deps();
    const { result } = renderHook(() => useCodePageBootstrap({ label: 'Test', ...d }));
    expect(result.current.ready).toBe(false);
    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(result.current.bootstrapError).toBeNull();
    expect(d.ensureAuthInitialized).toHaveBeenCalledTimes(1);
  });

  it('shows the failure and retries on request, never becoming ready while auth fails', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    const d = deps();
    d.ensureAuthInitialized.mockRejectedValueOnce(new Error('no sso'));
    const { result } = renderHook(() => useCodePageBootstrap({ label: 'Test', ...d }));
    await waitFor(() => expect(result.current.bootstrapError).toBeInstanceOf(Error));
    expect(result.current.ready).toBe(false);

    act(() => result.current.retry());
    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(result.current.bootstrapError).toBeNull();
    expect(d.ensureAuthInitialized).toHaveBeenCalledTimes(2);
  });

  it('does not re-run the bootstrap when the caller passes inline lambdas that change identity on every render', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    const ensure = jest.fn(async () => undefined);
    const { result, rerender } = renderHook(() =>
      useCodePageBootstrap({ label: 'Test', setRuntimeConfig: () => undefined, ensureAuthInitialized: () => ensure() })
    );
    await waitFor(() => expect(result.current.ready).toBe(true));
    rerender();
    rerender();
    await act(async () => undefined);
    expect(ensure).toHaveBeenCalledTimes(1);
    expect(mockResolveRuntimeConfig).toHaveBeenCalledTimes(1);
    expect(result.current.ready).toBe(true);
  });

  it('does not start auth when the page is not enabled (an unsaved or foreign record)', async () => {
    const d = deps();
    const { result } = renderHook(() => useCodePageBootstrap({ label: 'Test', enabled: false, ...d }));
    await act(async () => undefined);
    expect(result.current.ready).toBe(false);
    expect(mockResolveRuntimeConfig).not.toHaveBeenCalled();
    expect(d.ensureAuthInitialized).not.toHaveBeenCalled();
  });

  it('does not register the Navigator side pane unless the page asks for it', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    const { result } = renderHook(() => useCodePageBootstrap({ label: 'Test', ...deps() }));
    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(mockEnsureNavigator).not.toHaveBeenCalled();
  });

  it('registers the Navigator side pane when the page asks for it', async () => {
    mockResolveRuntimeConfig.mockResolvedValue(config);
    const { result } = renderHook(() => useCodePageBootstrap({ label: 'Test', registerNavigator: true, ...deps() }));
    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(mockEnsureNavigator).toHaveBeenCalledTimes(1);
  });
});

describe('toFailure (the one shared copy)', () => {
  it('is the very function this page exports', () => {
    expect(invoiceFilesToFailure).toBe(sharedToFailure);
  });

  it('maps a non-Error rejection to status 0 with no detail', () => {
    expect(sharedToFailure('boom')).toEqual({ status: 0, errorCode: null, detail: null, signInFailed: false });
  });

  it('keeps the ProblemDetails errorCode and detail of an ApiError, and flags an exhausted sign-in', () => {
    const f = sharedToFailure(new ApiError('HTTP 409', 409, { errorCode: 'file_scan_pending', detail: 'still scanning' } as never));
    expect(f).toMatchObject({ status: 409, errorCode: 'file_scan_pending', detail: 'still scanning', signInFailed: false });
    expect(sharedToFailure(new AuthError('no token'))).toMatchObject({ status: 0, signInFailed: true });
  });
});
