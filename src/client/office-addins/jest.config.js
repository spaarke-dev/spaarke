/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  // `word` added by task 037 (FR-17): `word/commands/__tests__/commands.test.ts` is the FIRST test
  // file outside `shared/` in this package, so it was invisible to jest until this widened. Verified
  // safe: no `.test.ts(x)` file existed anywhere outside `shared/` before this change (checked via a
  // repo-wide find), so this newly discovers ONLY task 037's new suite — no other dormant suite is
  // surfaced. `outlook` is included for symmetry (its `commands/` has no test file yet, but the same
  // command-context testing pattern belongs there too, and the roots list should not need touching
  // again for that follow-up).
  // `packaging` added by task 078 (FR-05): the unified app-package merge (`packaging/mergeUnifiedManifest.js`)
  // is build tooling with its own suite. Verified safe the same way: `packaging/` is a new folder whose ONLY
  // test file is task 078's, so widening surfaces no dormant suite. NOT named `build/`: the repo-root
  // .gitignore ignores every `build/` folder, so a module there is never committed and a fresh checkout
  // (CI included) cannot resolve webpack's require of it.
  roots: ['<rootDir>/shared', '<rootDir>/word', '<rootDir>/outlook', '<rootDir>/packaging'],
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
    // Task 096: the Email tab renders the REAL shared compose engine through its `SendEmailPane` wrapper (same
    // exact alias as webpack.config.js) — tests exercise the shared component itself, never a copy. Its bare
    // imports (react, Fluent, lexical) resolve from this package's node_modules via `modulePaths` below.
    // Task 099: the shared `cleanGuid` (ADR-044), exact alias — same as webpack.config.js; replaces the local copy.
    '^@spaarke/ui-components/guid$': '<rootDir>/../shared/Spaarke.UI.Components/src/utils/guid.ts',
    '^@spaarke/ui-components/send-email-pane$':
      '<rootDir>/../shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailPane.tsx',
    // Task 100: the "+ New" form's Assigned To picker — the REAL shared `LookupField`, same exact alias as webpack.
    '^@spaarke/ui-components/lookup-field$':
      '<rootDir>/../shared/Spaarke.UI.Components/src/components/LookupField/LookupField.tsx',
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
  },
  // Task 096: shared sources aliased above live outside this package and have no node_modules of their own in
  // CI — their bare imports fall back to this package's node_modules (one React, one Fluent, one lexical).
  modulePaths: ['<rootDir>/node_modules'],
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
