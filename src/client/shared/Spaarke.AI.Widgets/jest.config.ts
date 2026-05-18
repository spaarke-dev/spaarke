import type { Config } from 'jest';

const config: Config = {
  preset: 'ts-jest',
  testEnvironment: 'jest-environment-jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}', '**/*.test.{ts,tsx}'],
  moduleNameMapper: {
    // Map workspace packages to source until dist is built in CI.
    '^@spaarke/ui-components$': '<rootDir>/../Spaarke.UI.Components/src/index.ts',
    '^@spaarke/ai-context$': '<rootDir>/../Spaarke.AI.Context/src/index.ts',
    // @spaarke/auth — stub for tests; real implementation uses MSAL/browser APIs
    // that are unavailable in jsdom. Tests that need auth functions mock them via jest.mock().
    '^@spaarke/auth$': '<rootDir>/src/__mocks__/@spaarke/auth.ts',
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
