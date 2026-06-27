/**
 * Jest config for DocumentUploadWizard code-page.
 *
 * Scope (minimal): unit-tests for pure-TS helpers like
 * `src/components/searchIndexResolver.ts` (FR-WIZ-06).
 * No DOM / JSX tests configured — those belong to the shared lib
 * `@spaarke/ui-components` jest suite.
 *
 * Run locally: `npm install && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/*.test.ts'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        // Override the wizard's tsconfig for tests — production tsconfig has
        // `allowImportingTsExtensions: true` + `noEmit: true` which ts-jest
        // doesn't honor. Tests need plain TS compilation.
        target: 'ES2020',
        module: 'CommonJS',
        moduleResolution: 'node',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true,
        strict: true,
        jsx: 'react-jsx',
        skipLibCheck: true,
      },
    }],
  },
};
