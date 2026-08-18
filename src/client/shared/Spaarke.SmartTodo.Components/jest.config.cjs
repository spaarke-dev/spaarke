/**
 * Jest configuration for @spaarke/smart-todo-components (smart-todo-r5 task 040).
 *
 * Patterned after src/client/shared/Spaarke.UI.Components/jest.config.js,
 * path-adjusted, with three deliberate deviations (documented here + in
 * projects/smart-todo-r5/notes/task-040-test-infra.md):
 *
 *   1. `.cjs` extension, not `.js`. This package's package.json declares
 *      `"type": "module"`, so a plain `jest.config.js` would be loaded as an
 *      ES module and `module.exports = {...}` would throw
 *      "module is not defined in ES module scope". `.cjs` forces CommonJS
 *      regardless of the package's `"type"` field — the exact same fix
 *      `src/solutions/SmartTodo/jest.config.cjs` already applies for the
 *      identical reason (see that file's header comment).
 *
 *   2. `roots: ['<rootDir>']`, not `['<rootDir>/src']`. This package's three
 *      pre-existing test files (`SmartTodoWidget.test.tsx`,
 *      `priorityEffortCardUi.test.ts`, `useKanbanColumns.test.ts`) live in a
 *      top-level `__tests__/` directory, sibling to `src/` — not nested inside
 *      `src` like Spaarke.UI.Components' convention (that package's tests
 *      live under src, in per-folder __tests__ directories). Scoping roots
 *      to `src/` here would silently discover zero tests.
 *
 *   3. No `collectCoverageFrom` / `coverageThreshold`. ADR-038 treats test
 *      coverage as an observation, never a gate (root CLAUDE.md constraint,
 *      smart-todo-r5 task 040 constraints). This package is brand-new to
 *      Jest as of this task — an arbitrary threshold here would be a new gate
 *      being invented, not an existing one being preserved verbatim.
 */

module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>'],
  testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx'],
  setupFilesAfterEnv: ['<rootDir>/jest.setup.cjs'],
  moduleNameMapper: {
    '\\.(css|less|scss|sass)$': 'identity-obj-proxy',
  },
  transformIgnorePatterns: [
    'node_modules/(?!(d3-force|d3-dispatch|d3-quadtree|d3-timer|marked)/)',
  ],
  transform: {
    '^.+\\.tsx?$': ['ts-jest', {
      tsconfig: {
        jsx: 'react',
        esModuleInterop: true,
        allowSyntheticDefaultImports: true,
        module: 'commonjs',
      },
    }],
    '^.+\\.jsx?$': ['ts-jest', {
      tsconfig: {
        allowJs: true,
        esModuleInterop: true,
        module: 'commonjs',
      },
    }],
  },
};
