/**
 * Test stub for `@spaarke/auth` (v1.7.0, UAC-r2 task 147 r1). The control bootstraps MSAL lazily, only when a regarding
 * write is re-filed through the BFF; the handler suites inject `refileThroughBff`, so nothing here is reached by them.
 * Mapped in jest.config.js.
 */
export const authenticatedFetch = jest.fn(() =>
  Promise.reject(new Error('authenticatedFetch is not available in tests'))
);
export const initAuth = jest.fn(() => Promise.resolve({}));
export const resolveRuntimeConfig = jest.fn(() =>
  Promise.resolve({
    bffBaseUrl: 'https://bff.test',
    bffOAuthScope: 'api://test/.default',
    msalClientId: 'client',
    tenantId: '',
  })
);
