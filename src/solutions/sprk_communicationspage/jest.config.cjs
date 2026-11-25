/**
 * Jest config for the communications grid page (ISS-008, #2948). Patterned after src/solutions/ApExport/jest.config.cjs:
 * a node environment, because the suites test the page's logic (record scoping and the New Email / New Message
 * commands), not rendering.
 *
 * `@spaarke/ui-components` maps to a small stub that re-exports the real, dependency-free helpers this page's logic
 * uses (see `src/__tests__/uiComponentsStub.ts`), so locally the suites need neither the package's dist build nor
 * its own install. CI's client-tests job (nightly, advisory) installs and builds it anyway, which is harmless.
 *
 * Run: `npm install --legacy-peer-deps --no-audit --no-fund && npm test` from this folder.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.test.ts'],
  moduleNameMapper: {
    '^@spaarke/ui-components$': '<rootDir>/src/__tests__/uiComponentsStub.ts',
  },
  transform: {
    '^.+\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react',
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          module: 'commonjs',
          moduleResolution: 'node',
          strict: true,
          isolatedModules: true,
          skipLibCheck: true,
          types: ['jest'],
        },
      },
    ],
  },
};
