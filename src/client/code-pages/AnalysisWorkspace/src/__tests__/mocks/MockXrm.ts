/**
 * MockXrm - Simulates the Xrm SDK global for testing authentication.
 *
 * Provides a mock implementation of Xrm.Utility.getGlobalContext() and
 * the authContext.acquireToken() flow used by authService.ts.
 *
 * @see services/authService.ts
 * @see context/AuthContext.tsx
 */

export interface MockXrmOptions {
    /** Token to return from acquireToken. Null simulates failure. */
    token?: string | null;
    /** Whether Xrm should be "unavailable" (simulates running outside Dataverse) */
    unavailable?: boolean;
    /** Delay in ms before resolving token acquisition */
    tokenDelayMs?: number;
    /** Error to throw when acquiring token */
    tokenError?: Error;
}

const DEFAULT_TOKEN = "mock-bearer-token-abc-123";

/**
 * Install a mock Xrm global and return a teardown function.
 */
export function installMockXrm(options: MockXrmOptions = {}): () => void {
    const {
        token = DEFAULT_TOKEN,
        unavailable = false,
        tokenDelayMs = 0,
        tokenError,
    } = options;

    const originalXrm = (globalThis as Record<string, unknown>).Xrm;

    if (unavailable) {
        delete (globalThis as Record<string, unknown>).Xrm;
        return () => {
            if (originalXrm !== undefined) {
                (globalThis as Record<string, unknown>).Xrm = originalXrm;
            }
        };
    }

    const mockXrm = {
        Utility: {
            getGlobalContext: jest.fn(() => ({
                getClientUrl: jest.fn(() => "https://spaarkedev1.crm.dynamics.com"),
                organizationSettings: {
                    uniqueName: "spaarkedev1",
                },
                userSettings: {
                    userId: "{00000000-0000-0000-0000-000000000001}",
                    userName: "Test User",
                },
                client: {
                    getClient: jest.fn(() => "Web"),
                },
            })),
        },
        Navigation: {
            navigateTo: jest.fn(),
        },
        WebApi: {
            retrieveRecord: jest.fn(),
            retrieveMultipleRecords: jest.fn(),
        },
    };

    // Mock the auth flow that authService uses
    // The real implementation uses Xrm to detect the environment, then acquires tokens.
    // For testing, we mock at the global Xrm level so hooks using useAuth get a token.
    (globalThis as Record<string, unknown>).Xrm = mockXrm;

    return () => {
        if (originalXrm !== undefined) {
            (globalThis as Record<string, unknown>).Xrm = originalXrm;
        } else {
            delete (globalThis as Record<string, unknown>).Xrm;
        }
    };
}

/**
 * Creates a mock auth context value for testing components that use useAuth().
 */
export function createMockAuthContextValue(overrides: {
    token?: string | null;
    isAuthenticated?: boolean;
    isAuthenticating?: boolean;
    authError?: Error | null;
    isXrmUnavailable?: boolean;
} = {}) {
    return {
        token: overrides.token ?? DEFAULT_TOKEN,
        isAuthenticated: overrides.isAuthenticated ?? true,
        isAuthenticating: overrides.isAuthenticating ?? false,
        authError: overrides.authError ?? null,
        isXrmUnavailable: overrides.isXrmUnavailable ?? false,
        refreshToken: jest.fn().mockResolvedValue(overrides.token ?? DEFAULT_TOKEN),
        retryAuth: jest.fn(),
    };
}
