/**
 * Jest configuration for SemanticSearch code-page.
 *
 * Scope: unit + integration tests under `src/__tests__/` covering
 *  - FR-CP-01: parseUrlParams envelope parsing (`utils/`)
 *  - FR-CP-02..04: hook + service behavior (`hooks/`, `services/`)
 *  - FR-PARITY-02: request-body shape parity with PCF (`integration/`)
 *
 * Tests use React 18/19 + @testing-library/react with the jsdom environment.
 * Pattern adapted from the AnalysisWorkspace code-page (sibling code-page).
 *
 * Run locally: `npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    // Map @spaarke/auth path alias (mirrors tsconfig.json) so jest can resolve
    // imports without going through the webpack alias.
    '^@spaarke/auth$': '<rootDir>/../../shared/Spaarke.Auth/src/index',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        diagnostics: false,
        tsconfig: {
          // Override the code-page tsconfig for tests — production tsconfig
          // sets `rootDir: ./src` which excludes the @spaarke/auth alias source
          // and `module: ESNext` which ts-jest cannot consume.
          target: 'ES2020',
          module: 'commonjs',
          moduleResolution: 'node',
          jsx: 'react-jsx',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          skipLibCheck: true,
          strict: false,
          noImplicitAny: false,
        },
      },
    ],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
