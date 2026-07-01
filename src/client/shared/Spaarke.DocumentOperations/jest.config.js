/**
 * Jest configuration for @spaarke/document-operations.
 *
 * Scope: unit tests under `__tests__/` covering the canonical
 * `useDocumentActions` hook moved from SemanticSearch by task 031
 * of `spaarkeai-compose-r1`.
 *
 * Tests use React 19 + @testing-library/react with the jsdom environment.
 * Pattern adapted from the SemanticSearch code-page (consumer of this lib).
 *
 * Run locally: `npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/__tests__'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    // Map @spaarke/auth to source so jest resolves the peer package without
    // requiring a prebuilt `dist/` — mirrors the SemanticSearch consumer
    // pattern in src/client/code-pages/SemanticSearch/jest.config.js.
    '^@spaarke/auth$': '<rootDir>/../Spaarke.Auth/src/index',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        diagnostics: false,
        tsconfig: {
          // Override the lib tsconfig for tests — production tsconfig sets
          // `rootDir: ./src` (excludes __tests__ dir) and `module: ESNext`
          // which ts-jest cannot consume.
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
