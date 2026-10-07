/**
 * Jest config for the EventDetailSidePane code page.
 *
 * unified-access-control-r2 task 147 r1c-v1 (verifier item 6): the pane had no test harness, so the ORDER of its two
 * writes on save (the filing through the BFF first, then every other field as the caller's own Xrm.WebApi update) was
 * unpinned. Scope (minimal, the DocumentUploadWizard pattern): unit tests of the pane's services, in node.
 *
 * `@spaarke/ui-components` is mapped to the ONE module the services import from it — the BFF child-write seam
 * (`splitFilingPayload`, the shared filing rule) — at its source, so a test needs neither the built library nor Fluent.
 * The BFF transport (`childRecordWrites.ts`, MSAL) is mocked in each test.
 *
 * Run locally: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts'],
  moduleNameMapper: {
    // The seam + master's ONE cleanGuid (#1121), re-exported from their sources (sweep integration, 147 x master).
    '^@spaarke/ui-components$': '<rootDir>/src/__tests__/uiComponentsTestSeam.ts',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          // The pane's tsconfig is bundler-mode (allowImportingTsExtensions + noEmit), which ts-jest does not honour.
          target: 'ES2020',
          module: 'CommonJS',
          moduleResolution: 'node',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          strict: true,
          isolatedModules: true,
          skipLibCheck: true,
          types: ['jest'],
        },
      },
    ],
  },
};
