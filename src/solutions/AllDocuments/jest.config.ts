import type { Config } from 'jest';

/**
 * Jest configuration for AllDocuments solution unit tests.
 *
 * Wired by spaarkeai-compose-r6 task 051 (version-history affordance) — the
 * solution previously had NO test infrastructure. Modeled after
 * `src/solutions/WorkspaceLayoutWizard/jest.config.ts` (itself modeled on
 * SpaarkeAi's — the established pattern for Vite React Code Pages).
 *
 * Module mapping resolves @spaarke/* packages to their source trees so tests
 * don't require a dist build of the shared libraries. ESM-only transitive
 * imports of the @spaarke/ui-components barrel (marked, d3-force,
 * @spaarke/sdap-client) are mapped to tiny CJS stubs in src/__mocks__/ —
 * the same three stubs proven in SpaarkeAi + WorkspaceLayoutWizard.
 *
 * @see src/solutions/WorkspaceLayoutWizard/jest.config.ts — reference config
 */
const config: Config = {
  preset: 'ts-jest',
  testEnvironment: 'jest-environment-jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}', '**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    // Map workspace packages to source — avoids needing dist builds in CI
    '^@spaarke/ui-components$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/index.ts',
    '^@spaarke/ui-components/(.*)$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/$1',
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.Auth/src/index.ts',
    '^@spaarke/auth/(.*)$': '<rootDir>/../../client/shared/Spaarke.Auth/src/$1',
    // ESM-only packages ts-jest's CommonJS transform can't consume — stubs.
    '^d3-force$': '<rootDir>/src/__mocks__/d3-force.ts',
    '^marked$': '<rootDir>/src/__mocks__/marked.ts',
    '^@spaarke/sdap-client$': '<rootDir>/src/__mocks__/sdap-client.ts',
    // Dedupe React — force a single instance across workspace-linked libs.
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    '\\.css$': 'identity-obj-proxy',
  },
  transform: {
    '^.+\\.(ts|tsx)$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react-jsx',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          moduleResolution: 'node',
          allowImportingTsExtensions: false,
          noEmit: false,
        },
      },
    ],
  },
  testRunner: 'jest-circus/runner',
  collectCoverage: false,
};

export default config;
