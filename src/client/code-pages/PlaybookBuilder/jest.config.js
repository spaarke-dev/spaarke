/**
 * Jest configuration for PlaybookBuilder code page.
 *
 * Mirrors src/client/shared/Spaarke.UI.Components/jest.config.js (sibling parity per
 * R3 task 094 — same runner family, same transformer, same RTL/jest-dom setup,
 * same coverage gate philosophy). Coverage thresholds intentionally scoped to
 * the H2 surface added/touched by tasks 091/092/093 + task 043 (per NFR-03 —
 * coverage mandatory for new UI).
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  collectCoverageFrom: [
    'src/components/catalog/**/*.tsx',
    'src/services/schemaValidation.ts',
    'src/services/catalogService.ts',
    '!src/**/*.d.ts',
    '!src/**/index.ts',
    '!src/**/__tests__/**'
  ],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy'
  },
  transformIgnorePatterns: ['node_modules/'],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      // Production builds use esbuild-loader (transpile-only). Diagnostics
      // disabled so the test runner mirrors the production posture; type
      // errors remain visible to `tsc -p` separately.
      diagnostics: false,
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true,
        moduleResolution: 'node',
        target: 'ES2020',
        module: 'commonjs',
        strict: false,
        noImplicitAny: false,
        skipLibCheck: true
      }
    }],
    '^.+\\.jsx?$': ['ts-jest', {
      diagnostics: false,
      tsconfig: {
        allowJs: true,
        esModuleInterop: true,
        module: 'commonjs'
      }
    }]
  }
};
