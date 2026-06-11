import type { Config } from 'jest';

/**
 * Jest configuration for SpaarkeAi solution unit tests.
 *
 * Tests are placed alongside components in __tests__ directories.
 * Module mapping resolves @spaarke/* packages to their source trees
 * so tests don't require a dist build of the shared libraries.
 *
 * @see src/client/shared/Spaarke.AI.Widgets/jest.config.ts — reference config
 */
const config: Config = {
  preset: 'ts-jest',
  testEnvironment: 'jest-environment-jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}', '**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    // Map workspace packages to source — avoids needing dist builds in CI
    '^@spaarke/ai-widgets$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/index.ts',
    // Subpath imports — match deep-import patterns like
    // `@spaarke/ai-widgets/hooks/useWorkspaceLayouts` used by SpaarkeAi adapters
    // that skip the barrel's side-effect widget registration.
    // (R6 Hotfix Wave B-G9c3 unblocks ConversationPane.r5.test.tsx +
    // ConversationPane.slash-nl-rewire.test.tsx — both blocked on this mapping
    // gap pre-fix.)
    '^@spaarke/ai-widgets/(.*)$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/$1',
    '^@spaarke/ui-components$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/index.ts',
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/__mocks__/@spaarke/auth.ts',
    '^@spaarke/ai-context$': '<rootDir>/../../client/shared/Spaarke.AI.Context/src/index.ts',
    '^@spaarke/ai-outputs$': '<rootDir>/../../client/shared/Spaarke.AI.Outputs/src/index.ts',
    // d3-force ships pure ESM — ts-jest's CommonJS transform can't consume it.
    // Map to a tiny CJS stub so transitive imports of useForceSimulation don't
    // crash jsdom tests. (R5 task 038.) The stub returns the chainable
    // simulation surface the hook expects.
    '^d3-force$': '<rootDir>/src/__mocks__/d3-force.ts',
    // `marked` ships pure ESM that ts-jest's CommonJS transform can't consume.
    // Every test transitively importing @spaarke/ui-components/services/
    // renderMarkdown fails with "SyntaxError: Unexpected token 'export'" at
    // marked.esm.js parse time. Map to a pass-through stub so tests don't need
    // a Markdown render. (R6 Hotfix Wave B-G9c3, 2026-06-10.)
    '^marked$': '<rootDir>/src/__mocks__/marked.ts',
    // Dedupe React — the workspace-linked shared libraries each have their own
    // node_modules/react copy. Without forcing a single instance, hooks fail
    // with "Cannot read properties of null (reading 'useRef')" because the
    // dispatcher pointer lives in the test-runner's React, but a sub-component
    // imports a SECOND React instance from a nested node_modules. (R5 task 038.)
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
