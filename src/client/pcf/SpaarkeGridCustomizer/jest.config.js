/**
 * Jest configuration for the SpaarkeGridCustomizer PCF (unified-access-control-r2 task 168).
 *
 * The tests drive the real override functions the grid receives (customizers/*) and the control's
 * init through its real entry module.
 *
 * `generated/ManifestTypes` (imported by index.ts) is emitted by the PCF toolchain and is gitignored.
 * ts-jest type-checks through TypeScript's own module resolution, which `moduleNameMapper` does not
 * reach, so a mapper alone cannot make a clean checkout compile (task 168 v1, verifier item 2: TS2307,
 * 0 tests ran). `globalSetup` therefore runs `pcf-scripts refreshTypes` before any test file is
 * compiled: the suite runs on a clean checkout (after `npm install`), with no prior build, against the
 * real manifest types. index.ts imports only interfaces from it, which TypeScript erases, so nothing
 * resolves the module at run time.
 *
 * The lock reads the ONE list of the regarding filing columns, config/regarding-filing-columns.json at
 * the repository root (owner round 38), hence `resolveJsonModule`.
 */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.{ts,tsx}'],
  globalSetup: '<rootDir>/jest.globalSetup.js',
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': '<rootDir>/__tests__/__mocks__/styleMock.ts',
  },
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          jsx: 'react',
          esModuleInterop: true,
          resolveJsonModule: true,
          strict: true,
          target: 'ES2017',
          module: 'commonjs',
          skipLibCheck: true,
          types: ['jest', 'node', 'powerapps-component-framework'],
        },
      },
    ],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
};
