/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/shared'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: '<rootDir>/tsconfig.json',
        // Disable type checking in tests for faster execution
        isolatedModules: true,
      },
    ],
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
  collectCoverageFrom: [
    'shared/**/*.{ts,tsx}',
    '!shared/**/*.d.ts',
    '!shared/**/index.ts',
    '!shared/**/__tests__/**',
  ],
  coverageDirectory: '<rootDir>/coverage',
  coverageReporters: ['text', 'lcov', 'html'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  // Combined: TypeScript path aliases + CSS mocking.
  // (Prior version had two `moduleNameMapper` keys — the second overrode the
  // first, wiping out path aliases. Fixed for smart-todo-decoupling-r3 task 071.)
  moduleNameMapper: {
    '^@shared/(.*)$': '<rootDir>/shared/$1',
    '^@outlook/(.*)$': '<rootDir>/outlook/$1',
    '^@word/(.*)$': '<rootDir>/word/$1',
    // `@spaarke/auth` (task 072 / FR-25) resolves via `file:` to a sibling
    // package whose `dist/` is compiled ESM (`export {...}` — tsconfig
    // `module: ESNext`) with no CJS entry point; Jest's CommonJS runtime can't
    // `require()` that directly (`SyntaxError: Unexpected token 'export'`).
    // Map straight to the TypeScript source instead — ts-jest already
    // transforms `.ts` (see `transform` below), so this sidesteps the ESM/CJS
    // mismatch without widening the transform to arbitrary `.js` in node_modules.
    '^@spaarke/auth$': '<rootDir>/../shared/Spaarke.Auth/src/index.ts',
    // Task 042 (FR-B2 / ADR-045): reuse the code page's EXACT `derivePrimaryReview`
    // candidate model — map straight to the pure `provenance.ts` source (ts-jest
    // transforms it) so the add-in shares the identical function without forking it
    // or pulling the React-bearing components barrel.
    '^@spaarke/communication-components/logic/connections/provenance$':
      '<rootDir>/../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts',
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
  },
  // Ignore transforming node_modules except for specific packages
  transformIgnorePatterns: [
    'node_modules/(?!(@azure|@fluentui)/)',
  ],
  // Global test timeout
  testTimeout: 10000,
  // Clear mocks between tests
  clearMocks: true,
  // Restore mocks between tests
  restoreMocks: true,
};
