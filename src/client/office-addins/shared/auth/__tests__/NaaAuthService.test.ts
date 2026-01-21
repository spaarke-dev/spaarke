/**
 * Unit tests for NaaAuthService
 *
 * Tests the NAA (Nested App Authentication) service for Office Add-ins.
 */

import type { AccountInfo, AuthenticationResult } from '@azure/msal-browser';

// Mock MSAL before importing the service
jest.mock('@azure/msal-browser', () => ({
  createNestablePublicClientApplication: jest.fn(),
  PublicClientApplication: jest.fn().mockImplementation(() => ({
    initialize: jest.fn().mockResolvedValue(undefined),
    handleRedirectPromise: jest.fn().mockResolvedValue(null),
    getAllAccounts: jest.fn().mockReturnValue([]),
    acquireTokenSilent: jest.fn(),
    acquireTokenPopup: jest.fn(),
    logoutPopup: jest.fn(),
  })),
  InteractionRequiredAuthError: class InteractionRequiredAuthError extends Error {
    errorCode = 'interaction_required';
  },
  BrowserAuthError: class BrowserAuthError extends Error {
    constructor(public errorCode: string, message?: string) {
      super(message);
    }
  },
  AuthError: class AuthError extends Error {
    constructor(public errorCode: string, public errorMessage: string) {
      super(errorMessage);
    }
  },
  LogLevel: {
    Error: 0,
    Warning: 1,
    Info: 2,
    Verbose: 3,
    Trace: 4,
  },
}));

// Mock Office.js
const mockOffice = {
  context: {
    diagnostics: {
      platform: 'OfficeOnline',
      version: '16.0.0.0',
    },
    ui: {
      displayDialogAsync: jest.fn(),
    },
  },
  onReady: jest.fn((callback: () => void) => callback()),
  PlatformType: {
    PC: 'PC',
    Mac: 'Mac',
    OfficeOnline: 'OfficeOnline',
  },
};

(global as unknown as { Office: typeof mockOffice }).Office = mockOffice;

import {
  NaaAuthServiceImpl,
  NaaAuthError,
  NaaAuthErrorCode,
  type INaaAuthService,
  type NaaAuthState,
} from '../NaaAuthService';
import { createNestablePublicClientApplication } from '@azure/msal-browser';

describe('NaaAuthService', () => {
  let authService: INaaAuthService;
  let mockMsalInstance: {
    handleRedirectPromise: jest.Mock;
    getAllAccounts: jest.Mock;
    acquireTokenSilent: jest.Mock;
    acquireTokenPopup: jest.Mock;
    logoutPopup: jest.Mock;
  };

  const mockAccount: AccountInfo = {
    homeAccountId: 'test-home-id',
    environment: 'login.microsoftonline.com',
    tenantId: 'test-tenant-id',
    username: 'test@example.com',
    localAccountId: 'test-local-id',
    name: 'Test User',
  };

  const mockAuthResult: AuthenticationResult = {
    accessToken: 'mock-access-token',
    account: mockAccount,
    expiresOn: new Date(Date.now() + 3600 * 1000),
    scopes: ['api://test/user_impersonation'],
    idToken: 'mock-id-token',
    idTokenClaims: {},
    tenantId: 'test-tenant-id',
    uniqueId: 'test-unique-id',
    authority: 'https://login.microsoftonline.com/test-tenant-id',
    tokenType: 'Bearer',
    correlationId: 'test-correlation-id',
    fromCache: false,
  };

  beforeEach(() => {
    // Reset singleton
    NaaAuthServiceImpl.resetInstance();

    // Create mock MSAL instance
    mockMsalInstance = {
      handleRedirectPromise: jest.fn().mockResolvedValue(null),
      getAllAccounts: jest.fn().mockReturnValue([]),
      acquireTokenSilent: jest.fn(),
      acquireTokenPopup: jest.fn(),
      logoutPopup: jest.fn().mockResolvedValue(undefined),
    };

    // Configure createNestablePublicClientApplication mock
    (createNestablePublicClientApplication as jest.Mock).mockResolvedValue(mockMsalInstance);

    // Get fresh instance
    authService = NaaAuthServiceImpl.getInstance();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('getInstance', () => {
    it('should return the same instance on multiple calls', () => {
      const instance1 = NaaAuthServiceImpl.getInstance();
      const instance2 = NaaAuthServiceImpl.getInstance();

      expect(instance1).toBe(instance2);
    });

    it('should return new instance after reset', () => {
      const instance1 = NaaAuthServiceImpl.getInstance();
      NaaAuthServiceImpl.resetInstance();
      const instance2 = NaaAuthServiceImpl.getInstance();

      expect(instance1).not.toBe(instance2);
    });
  });

  describe('initialize', () => {
    it('should initialize with NAA when supported', async () => {
      await authService.initialize();

      expect(createNestablePublicClientApplication).toHaveBeenCalled();
      expect(authService.isInitialized()).toBe(true);
      expect(authService.isNaaSupported()).toBe(true);
    });

    it('should not reinitialize if already initialized', async () => {
      await authService.initialize();
      await authService.initialize();

      expect(createNestablePublicClientApplication).toHaveBeenCalledTimes(1);
    });

    it('should restore account from cache on init', async () => {
      mockMsalInstance.getAllAccounts.mockReturnValue([mockAccount]);

      await authService.initialize();

      expect(authService.getAccount()).toEqual(mockAccount);
      expect(authService.isAuthenticated()).toBe(true);
    });

    it('should handle redirect response if present', async () => {
      mockMsalInstance.handleRedirectPromise.mockResolvedValue(mockAuthResult);

      await authService.initialize();

      expect(authService.getAccount()).toEqual(mockAccount);
    });
  });

  describe('isNaaSupported', () => {
    it('should return true for Office Online', async () => {
      await authService.initialize();

      expect(authService.isNaaSupported()).toBe(true);
    });

    it('should return true for Windows PC with sufficient version', async () => {
      mockOffice.context.diagnostics.platform = 'PC';
      mockOffice.context.diagnostics.version = '16.0.14000.12345';

      NaaAuthServiceImpl.resetInstance();
      authService = NaaAuthServiceImpl.getInstance();
      await authService.initialize();

      expect(authService.isNaaSupported()).toBe(true);
    });

    it('should return false for old Windows version', async () => {
      mockOffice.context.diagnostics.platform = 'PC';
      mockOffice.context.diagnostics.version = '16.0.12000.12345';

      NaaAuthServiceImpl.resetInstance();
      authService = NaaAuthServiceImpl.getInstance();
      await authService.initialize();

      // Note: The fallback path will be used, but initialization succeeds
      expect(authService.isNaaSupported()).toBe(false);
    });

    afterAll(() => {
      // Reset to default
      mockOffice.context.diagnostics.platform = 'OfficeOnline';
      mockOffice.context.diagnostics.version = '16.0.0.0';
    });
  });

  describe('signIn', () => {
    beforeEach(async () => {
      await authService.initialize();
    });

    it('should try silent acquisition first', async () => {
      mockMsalInstance.acquireTokenSilent.mockResolvedValue(mockAuthResult);
      mockMsalInstance.getAllAccounts.mockReturnValue([mockAccount]);

      // Need to reinitialize to pick up the cached account
      NaaAuthServiceImpl.resetInstance();
      (createNestablePublicClientApplication as jest.Mock).mockResolvedValue(mockMsalInstance);
      authService = NaaAuthServiceImpl.getInstance();
      await authService.initialize();

      const result = await authService.signIn();

      expect(mockMsalInstance.acquireTokenSilent).toHaveBeenCalled();
      expect(result.accessToken).toBe('mock-access-token');
    });

    it('should fall back to popup when silent fails', async () => {
      mockMsalInstance.acquireTokenSilent.mockRejectedValue(
        new (jest.requireMock('@azure/msal-browser').InteractionRequiredAuthError)()
      );
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);

      const result = await authService.signIn();

      expect(mockMsalInstance.acquireTokenPopup).toHaveBeenCalled();
      expect(result.accessToken).toBe('mock-access-token');
    });

    it('should throw NaaAuthError on failure', async () => {
      mockMsalInstance.acquireTokenPopup.mockRejectedValue(
        new (jest.requireMock('@azure/msal-browser').BrowserAuthError)('user_cancelled')
      );

      await expect(authService.signIn()).rejects.toThrow(NaaAuthError);
    });

    it('should update auth state on successful sign in', async () => {
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);

      await authService.signIn();

      expect(authService.isAuthenticated()).toBe(true);
      expect(authService.getAccount()).toEqual(mockAccount);
    });
  });

  describe('signOut', () => {
    beforeEach(async () => {
      mockMsalInstance.getAllAccounts.mockReturnValue([mockAccount]);
      await authService.initialize();
    });

    it('should clear account on sign out', async () => {
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);
      await authService.signIn();

      await authService.signOut();

      expect(authService.isAuthenticated()).toBe(false);
      expect(authService.getAccount()).toBeNull();
    });

    it('should call MSAL logout', async () => {
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);
      await authService.signIn();

      await authService.signOut();

      expect(mockMsalInstance.logoutPopup).toHaveBeenCalled();
    });

    it('should handle logout error gracefully', async () => {
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);
      await authService.signIn();

      mockMsalInstance.logoutPopup.mockRejectedValue(new Error('Logout failed'));

      // Should not throw, just clear local state
      await expect(authService.signOut()).resolves.not.toThrow();
      expect(authService.isAuthenticated()).toBe(false);
    });
  });

  describe('getAccessToken', () => {
    beforeEach(async () => {
      mockMsalInstance.getAllAccounts.mockReturnValue([mockAccount]);
      await authService.initialize();
    });

    it('should return token from silent acquisition', async () => {
      mockMsalInstance.acquireTokenSilent.mockResolvedValue(mockAuthResult);

      const result = await authService.getAccessToken();

      expect(result.accessToken).toBe('mock-access-token');
      expect(result.fromCache).toBe(true);
    });

    it('should fall back to popup when silent fails', async () => {
      mockMsalInstance.acquireTokenSilent.mockRejectedValue(
        new (jest.requireMock('@azure/msal-browser').InteractionRequiredAuthError)()
      );
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);

      const result = await authService.getAccessToken();

      expect(result.accessToken).toBe('mock-access-token');
      expect(result.fromCache).toBe(false);
    });

    it('should force refresh if token is about to expire', async () => {
      const almostExpiredResult = {
        ...mockAuthResult,
        expiresOn: new Date(Date.now() + 60 * 1000), // Expires in 60 seconds
      };
      const refreshedResult = {
        ...mockAuthResult,
        accessToken: 'refreshed-token',
      };

      mockMsalInstance.acquireTokenSilent
        .mockResolvedValueOnce(almostExpiredResult)
        .mockResolvedValueOnce(refreshedResult);

      const result = await authService.getAccessToken();

      // Should have called acquireTokenSilent twice (first check, then force refresh)
      expect(mockMsalInstance.acquireTokenSilent).toHaveBeenCalledTimes(2);
      expect(result.accessToken).toBe('refreshed-token');
    });
  });

  describe('getAuthState', () => {
    it('should return initial unauthenticated state', () => {
      const state = authService.getAuthState();

      expect(state.isAuthenticated).toBe(false);
      expect(state.isAuthenticating).toBe(false);
      expect(state.account).toBeNull();
      expect(state.error).toBeNull();
    });

    it('should return authenticated state after sign in', async () => {
      await authService.initialize();
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);

      await authService.signIn();

      const state = authService.getAuthState();
      expect(state.isAuthenticated).toBe(true);
      expect(state.account).toEqual(mockAccount);
    });
  });

  describe('onAuthStateChange', () => {
    it('should call listener immediately with current state', async () => {
      await authService.initialize();
      const listener = jest.fn();

      authService.onAuthStateChange(listener);

      expect(listener).toHaveBeenCalledWith(expect.objectContaining({
        isAuthenticated: false,
      }));
    });

    it('should call listener on state changes', async () => {
      await authService.initialize();
      const listener = jest.fn();
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);

      authService.onAuthStateChange(listener);
      listener.mockClear();

      await authService.signIn();

      // Should have been called for authenticating state and authenticated state
      expect(listener).toHaveBeenCalled();
      const lastCall = listener.mock.calls[listener.mock.calls.length - 1][0] as NaaAuthState;
      expect(lastCall.isAuthenticated).toBe(true);
    });

    it('should stop calling listener after unsubscribe', async () => {
      await authService.initialize();
      const listener = jest.fn();
      mockMsalInstance.acquireTokenPopup.mockResolvedValue(mockAuthResult);

      const unsubscribe = authService.onAuthStateChange(listener);
      listener.mockClear();

      unsubscribe();

      await authService.signIn();

      expect(listener).not.toHaveBeenCalled();
    });
  });

  describe('error handling', () => {
    beforeEach(async () => {
      await authService.initialize();
    });

    it('should throw NOT_INITIALIZED when not initialized', () => {
      NaaAuthServiceImpl.resetInstance();
      const uninitializedService = NaaAuthServiceImpl.getInstance();

      expect(() => uninitializedService.getAccessToken()).rejects.toThrow(NaaAuthError);
    });

    it('should map user cancelled error correctly', async () => {
      mockMsalInstance.acquireTokenPopup.mockRejectedValue(
        new (jest.requireMock('@azure/msal-browser').BrowserAuthError)('user_cancelled')
      );

      try {
        await authService.signIn();
        fail('Should have thrown');
      } catch (error) {
        expect(error).toBeInstanceOf(NaaAuthError);
        expect((error as NaaAuthError).code).toBe(NaaAuthErrorCode.USER_CANCELLED);
      }
    });

    it('should map popup blocked error correctly', async () => {
      mockMsalInstance.acquireTokenPopup.mockRejectedValue(
        new (jest.requireMock('@azure/msal-browser').BrowserAuthError)('popup_window_error')
      );

      try {
        await authService.signIn();
        fail('Should have thrown');
      } catch (error) {
        expect(error).toBeInstanceOf(NaaAuthError);
        expect((error as NaaAuthError).code).toBe(NaaAuthErrorCode.POPUP_BLOCKED);
      }
    });

    it('should map network error correctly', async () => {
      mockMsalInstance.acquireTokenPopup.mockRejectedValue(
        new (jest.requireMock('@azure/msal-browser').BrowserAuthError)('network_error')
      );

      try {
        await authService.signIn();
        fail('Should have thrown');
      } catch (error) {
        expect(error).toBeInstanceOf(NaaAuthError);
        expect((error as NaaAuthError).code).toBe(NaaAuthErrorCode.NETWORK_ERROR);
      }
    });
  });
});

describe('NaaAuthError', () => {
  it('should create error with code and message', () => {
    const error = new NaaAuthError(
      NaaAuthErrorCode.NOT_INITIALIZED,
      'Service not initialized'
    );

    expect(error.code).toBe(NaaAuthErrorCode.NOT_INITIALIZED);
    expect(error.userMessage).toBe('Service not initialized');
    expect(error.message).toBe('Service not initialized');
    expect(error.name).toBe('NaaAuthError');
  });

  it('should include original error when provided', () => {
    const originalError = new Error('Original error');
    const error = new NaaAuthError(
      NaaAuthErrorCode.UNKNOWN,
      'Wrapped error',
      originalError
    );

    expect(error.originalError).toBe(originalError);
  });
});
