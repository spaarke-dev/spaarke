/**
 * Jest config for DocumentUploadWizard code-page.
 *
 * Scope (minimal): unit-tests for pure-TS helpers like `src/services/uploadOrchestrator.ts`, and `*.test.tsx` for a step that is rendered
 * with `react-dom/server` (node environment, no DOM; `DocumentEmailStep.richText.test.tsx`, task 197).
 * Interactive DOM tests belong to the shared lib `@spaarke/ui-components` jest suite.
 *
 * Run locally: `npm install && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/*.test.{ts,tsx}'],
  // The wizard's own tsconfig `paths`, for Jest: the shared library is read from source (no dist build needed).
  moduleNameMapper: {
    '^@spaarke/ui-components$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src',
    '^@spaarke/ui-components/(.*)$': '<rootDir>/../../client/shared/Spaarke.UI.Components/src/$1',
  },
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
        baseUrl: '.',
        paths: {
          '@spaarke/ui-components': ['../../client/shared/Spaarke.UI.Components/src'],
          '@spaarke/ui-components/*': ['../../client/shared/Spaarke.UI.Components/src/*'],
        },
      },
    }],
  },
};
