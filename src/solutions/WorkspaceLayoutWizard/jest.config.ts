import type { Config } from 'jest';

/**
 * Jest configuration for WorkspaceLayoutWizard solution unit tests.
 *
 * Wired by DEF-001 (spaarke-dataset-grid-framework-r2) — unblocks four
 * test files scaffolded by prior tasks (011 rowHeight, 013 sectionInstance
 * advanced, 015 widthPreference) + the pre-existing TemplateStep test.
 *
 * Modeled after `src/solutions/SpaarkeAi/jest.config.ts` (established pattern
 * for Vite React 18 Code Pages). Simplified because the wizard's dependency
 * graph is smaller — only @spaarke/ui-components + @spaarke/auth are used,
 * and no shared-lib mocks (marked, d3-force, sdap-client) are needed.
 *
 * Tests are placed alongside components in __tests__ directories.
 * Module mapping resolves @spaarke/* packages to their source trees so
 * tests don't require a dist build of the shared libraries.
 *
 * @see src/solutions/SpaarkeAi/jest.config.ts — reference config
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
    // @spaarke/auth is only imported by main.tsx (not the tests), but map it
    // defensively in case future tests need it. Points at source; if a test
    // needs to mock it, per-test `jest.mock('@spaarke/auth', ...)` still works.
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.Auth/src/index.ts',
    '^@spaarke/auth/(.*)$': '<rootDir>/../../client/shared/Spaarke.Auth/src/$1',
    // d3-force ships pure ESM — ts-jest's CommonJS transform can't consume it.
    // Map to a tiny CJS stub so transitive imports of useForceSimulation don't
    // crash jsdom tests. (Pattern proven in SpaarkeAi — R5 task 038.)
    '^d3-force$': '<rootDir>/src/__mocks__/d3-force.ts',
    // `marked` ships pure ESM that ts-jest's CommonJS transform can't consume.
    // Every test transitively importing @spaarke/ui-components/services/
    // renderMarkdown fails with "SyntaxError: Unexpected token 'export'" at
    // marked.esm.js parse time. Map to a pass-through stub. (Pattern proven
    // in SpaarkeAi — R6 Hotfix Wave B-G9c3.)
    '^marked$': '<rootDir>/src/__mocks__/marked.ts',
    // `@spaarke/sdap-client` ships from dist/ as ESM. Every test transitively
    // importing @spaarke/ui-components/services (barrel) hits
    // EntityCreationService.ts which imports `SdapApiClient` — parse fails at
    // dist/index.js `export { ... }`. Map to a tiny stub. (Pattern proven in
    // SpaarkeAi — R6 Wave C-G3.)
    '^@spaarke/sdap-client$': '<rootDir>/src/__mocks__/sdap-client.ts',
    // Dedupe React — the workspace-linked shared libraries each have their own
    // node_modules/react copy. Without forcing a single instance, hooks fail
    // with "Cannot read properties of null (reading 'useRef')" because the
    // dispatcher pointer lives in the test-runner's React, but a sub-component
    // imports a SECOND React instance from a nested node_modules.
    // (Pattern proven in SpaarkeAi jest.config.ts — R5 task 038.)
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    // CSS modules — return the imported name as an identity proxy so
    // `import styles from './x.module.css'` doesn't crash the parser.
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
          // Override module resolution for Jest (CommonJS-compatible)
          module: 'commonjs',
          moduleResolution: 'node',
          // Relax Vite-specific settings that break ts-jest
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
