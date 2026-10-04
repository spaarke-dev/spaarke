// Test runner for the external SPA (unified-access-control-r2 task 140). It reuses the app's own Vite config — the same
// shared-library alias and dependency resolution the bundle is built with — and adds a DOM.
import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      include: ['tests/**/*.test.{ts,tsx}'],
      setupFiles: ['tests/setup.ts'],
      // src/config.ts refuses to load without these (it fails fast on a build that skipped token substitution). Test
      // values only — no test reaches the network: every BFF call goes through a mocked seam.
      env: {
        VITE_BFF_API_URL: 'https://bff.test.invalid',
        VITE_MSAL_CLIENT_ID: '00000000-0000-0000-0000-000000000140',
        VITE_MSAL_AUTHORITY: 'https://test.ciamlogin.com/test.onmicrosoft.com',
        VITE_MSAL_TENANT_ID: '00000000-0000-0000-0000-000000000141',
        VITE_MSAL_BFF_SCOPE: 'api://bff.test/user_impersonation',
      },
    },
  })
);
