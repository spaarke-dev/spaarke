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
    '^@spaarke/ui-components$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/index.ts',
    '^@spaarke/auth$': '<rootDir>/../../client/shared/Spaarke.AI.Widgets/src/__mocks__/@spaarke/auth.ts',
    '^@spaarke/ai-context$': '<rootDir>/../../client/shared/Spaarke.AI.Context/src/index.ts',
    '^@spaarke/ai-outputs$': '<rootDir>/../../client/shared/Spaarke.AI.Outputs/src/index.ts',
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
