import type { Config } from 'jest';

const config: Config = {
  preset: 'ts-jest',
  testEnvironment: 'jest-environment-jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}', '**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    // Map workspace packages to source until dist is built in CI.
    '^@spaarke/ai-outputs$': '<rootDir>/../Spaarke.AI.Outputs/src/index.ts',
    '^@spaarke/ui-components$': '<rootDir>/../Spaarke.UI.Components/src/index.ts',
    '^@spaarke/ai-context$': '<rootDir>/../Spaarke.AI.Context/src/index.ts',
    // @spaarke/communication-components — new shared lib (messaging-communication-app-r2
    // task 030); source-mode only, mirrors the pattern above (no dist build).
    '^@spaarke/communication-components$': '<rootDir>/../Spaarke.Communication.Components/src/index.ts',
    // @spaarke/auth — stub for tests; real implementation uses MSAL/browser APIs
    // that are unavailable in jsdom. Tests that need auth functions mock them via jest.mock().
    '^@spaarke/auth$': '<rootDir>/src/__mocks__/@spaarke/auth.ts',
    // d3-force ships pure ESM — ts-jest's CommonJS transform can't consume it.
    // Map to a tiny CJS stub so transitive imports of useForceSimulation (via
    // the @spaarke/ui-components barrel, pulled in by register-context-widgets
    // / register-workspace-widgets / the widget registries) don't crash jsdom
    // tests. Mirrors src/solutions/SpaarkeAi/jest.config.ts (R5 task 038).
    // Added by ai-architecture-redesign-r2 task 021 (test-repair).
    '^d3-force$': '<rootDir>/src/__mocks__/d3-force.ts',
    // `@spaarke/sdap-client` is not resolvable from this package's workspace
    // (it lives on the PCF / Office Add-in module-resolution paths). Every
    // test transitively importing @spaarke/ui-components/services/index fails
    // because EntityCreationService.ts has a top-level
    // `import { SdapApiClient } from '@spaarke/sdap-client'`. Map to a tiny
    // stub. Mirrors src/solutions/SpaarkeAi/jest.config.ts (R6 Wave C-G3).
    // Added by ai-architecture-redesign-r2 task 021 (test-repair).
    '^@spaarke/sdap-client$': '<rootDir>/src/__mocks__/sdap-client.ts',
    // `marked` ships pure ESM that ts-jest's CommonJS transform can't consume.
    // Every test transitively importing @spaarke/ui-components/services/
    // renderMarkdown fails with "SyntaxError: Unexpected token 'export'" at
    // marked.esm.js parse time. Map to a pass-through stub. Mirrors
    // src/solutions/SpaarkeAi/jest.config.ts (R6 Hotfix Wave B-G9c3).
    // Added by ai-architecture-redesign-r2 task 021 (test-repair).
    '^marked$': '<rootDir>/src/__mocks__/marked.ts',
    // Dedupe React — Spaarke.AI.Outputs (and other sibling shared libs) each
    // have their own node_modules/react copy installed independently. Without
    // forcing a single instance, hooks fail with "Invalid hook call" /
    // "Cannot read properties of null (reading 'useRef')" because the
    // dispatcher pointer lives in the test-runner's React, but a sub-component
    // (e.g. Spaarke.AI.Outputs' DocumentViewerWidget) imports a SECOND React
    // instance from its own nested node_modules. Mirrors
    // src/solutions/SpaarkeAi/jest.config.ts (R5 task 038).
    // Added by ai-architecture-redesign-r2 task 021 (test-repair).
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    // CSS modules
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
        },
      },
    ],
  },
  // jest-circus is the default runner in Jest 27+
  testRunner: 'jest-circus/runner',
  collectCoverage: false,
};

export default config;
