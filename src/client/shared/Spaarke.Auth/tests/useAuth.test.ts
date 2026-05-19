/**
 * Tests for useAuth() — the function-based React hook public API.
 *
 * useAuth is intentionally NOT a "real" React hook in Phase A — it has no
 * useState/useEffect, so it can be called as a plain function. Tests cover:
 *   - Returned shape (no token-string fields)
 *   - Delegation to the singleton provider
 *   - logout() stub behavior
 *
 * Task 015 will add a BroadcastChannel listener that requires React state;
 * at that point a React-renderer-based test will be added.
 */

const mockProvider = {
  isAuthenticated: jest.fn(() => true),
  getAccessToken: jest.fn(() => Promise.resolve('mock-access-token')),
  getCachedTenantId: jest.fn(() => 'mock-tenant-id'),
  clearAllCaches: jest.fn(),
  logout: jest.fn(() => Promise.resolve()),
};

jest.mock('../src/initAuth', () => ({
  getAuthProvider: jest.fn(() => mockProvider),
}));

import { useAuth } from '../src/useAuth';

describe('useAuth', () => {
  beforeEach(() => {
    mockProvider.isAuthenticated.mockClear();
    mockProvider.getAccessToken.mockClear();
    mockProvider.getCachedTenantId.mockClear();
    mockProvider.clearAllCaches.mockClear();
    mockProvider.logout.mockClear();
  });

  it('returns the expected shape', () => {
    const result = useAuth();

    expect(typeof result.isAuthenticated).toBe('boolean');
    expect(typeof result.getAccessToken).toBe('function');
    expect(typeof result.authenticatedFetch).toBe('function');
    expect(typeof result.tenantId).toBe('string');
    expect(typeof result.logout).toBe('function');
  });

  it('has NO token-string fields (function-based contract, AUDIT §4.1)', () => {
    const result = useAuth();
    const keys = Object.keys(result);

    expect(keys).not.toContain('token');
    expect(keys).not.toContain('accessToken');
  });

  it('isAuthenticated reflects the provider state at call time', () => {
    mockProvider.isAuthenticated.mockReturnValueOnce(false);
    const result = useAuth();
    expect(result.isAuthenticated).toBe(false);
  });

  it('tenantId returns the provider cached tenant id', () => {
    const result = useAuth();
    expect(result.tenantId).toBe('mock-tenant-id');
  });

  it('getAccessToken delegates to the provider', async () => {
    const result = useAuth();
    const token = await result.getAccessToken();

    expect(token).toBe('mock-access-token');
    expect(mockProvider.getAccessToken).toHaveBeenCalledTimes(1);
  });

  it('logout delegates to provider.logout()', async () => {
    const result = useAuth();
    await result.logout();

    expect(mockProvider.logout).toHaveBeenCalledTimes(1);
  });
});
