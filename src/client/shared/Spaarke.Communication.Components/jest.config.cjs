/**
 * Jest configuration for @spaarke/communication-components.
 *
 * Added by messaging-communication-app-r3 task 031 (FR-14a) — this package's
 * only prior test file (`CommunicationsWorkspaceWidget.test.ts`) had no test
 * runner wired ("checked in for the future test runner setup"). This config
 * is that setup, mirroring the canonical Jest pattern already used by
 * `@spaarke/ai-widgets` (which faces the identical `@spaarke/ui-components`
 * barrel problem below) and `@spaarke/daily-briefing-components`.
 *
 * Setup:
 *   - `testEnvironment: 'jsdom'` so React Testing Library can mount
 *     `CommunicationsWorkspaceWidget` (which now renders the REAL shared
 *     `<ConversationWorkspace>`/`<ConversationView>` — tasks 011/012).
 *   - `ts-jest` compiles `.ts`/`.tsx` test + source files against
 *     `tsconfig.test.json` (the base config's `emitDeclarationOnly` build
 *     settings don't apply to tests).
 *   - `@spaarke/ui-components` maps to SOURCE (not its built `dist/`, which
 *     ships pure ESM `export` syntax that ts-jest's CommonJS transform can't
 *     consume when loaded from `node_modules` — jest ignores node_modules
 *     for transforms by default). Mapping to source means the barrel's own
 *     transitive deps (`d3-force`, `marked`, `@spaarke/sdap-client`) must
 *     ALSO be stubbed — mirrors `@spaarke/ai-widgets/jest.config.ts`
 *     verbatim (same barrel, same problem, already solved there).
 *   - React/ReactDOM are pinned to THIS package's own `node_modules` copy so
 *     a component mounted from `@spaarke/ui-components` source shares the
 *     SAME React instance (hooks fail with "Invalid hook call" otherwise).
 *   - `@fluentui/react-components` / `@fluentui/react-tabster` / `tabster` are
 *     ALSO pinned to this package's own copy (task 040 discovery): this
 *     package's own devDependency (`@fluentui/react-components` ^9.46.2) and
 *     `@spaarke/ui-components`'s (^9.73.2) resolve to two DIFFERENT installed
 *     versions with different transitive `tabster` versions (8.8.0 vs 8.7.0).
 *     Rendering a component built against one copy (e.g. this package's own
 *     `<Toolbar>`) alongside one built against the source-mapped other copy
 *     (e.g. `DataGridViewSelector` from `@spaarke/ui-components` source) in
 *     the SAME test creates two independent tabster "core" instances that do
 *     not share internal state, crashing deep in `@fluentui/react-tabster`'s
 *     `useTabster` hook (`getMover`: "Cannot read properties of undefined
 *     (reading 'set')") the first time both mount within one commit — first
 *     hit by `EmailWorkspace.test.tsx` (task 040), which is the first test in
 *     this package to combine a real `<Toolbar>`-family component together
 *     with the real `DataGridViewSelector` in one tree. Same "one instance"
 *     fix as the existing React/ReactDOM dedup above, just extended to the
 *     Fluent/tabster trio.
 *   - `@spaarke/auth` is intentionally NOT mapped here — this package never
 *     initializes `@spaarke/auth` itself, so every test file that needs it
 *     supplies its own `jest.mock('@spaarke/auth', () => ({ ... }))`
 *     (see `CommunicationsWorkspaceWidget.test.ts`).
 */
/** @type {import('jest').Config} */
module.exports = {
  testEnvironment: 'jsdom',
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: 'tsconfig.test.json',
        diagnostics: { ignoreCodes: [151001] },
      },
    ],
  },
  testMatch: ['<rootDir>/src/**/*.test.ts', '<rootDir>/src/**/*.test.tsx'],
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
  setupFilesAfterEnv: ['<rootDir>/src/__mocks__/jest.setup.ts'],
  moduleNameMapper: {
    // Map to source until @spaarke/ui-components ships a Jest-consumable
    // (CJS) dist build — mirrors @spaarke/ai-widgets/jest.config.ts.
    '^@spaarke/ui-components$': '<rootDir>/../Spaarke.UI.Components/src/index.ts',
    // Transitive-dependency stubs for the @spaarke/ui-components barrel
    // (copied verbatim from @spaarke/ai-widgets/src/__mocks__ — same barrel,
    // same problem, already solved there).
    '^d3-force$': '<rootDir>/src/__mocks__/d3-force.ts',
    '^marked$': '<rootDir>/src/__mocks__/marked.ts',
    '^@spaarke/sdap-client$': '<rootDir>/src/__mocks__/sdap-client.ts',
    // Dedupe React so a component mounted from @spaarke/ui-components SOURCE
    // shares this package's React instance (avoids "Invalid hook call").
    '^react$': '<rootDir>/node_modules/react',
    '^react/(.*)$': '<rootDir>/node_modules/react/$1',
    '^react-dom$': '<rootDir>/node_modules/react-dom',
    '^react-dom/(.*)$': '<rootDir>/node_modules/react-dom/$1',
    // Dedupe Fluent v9 + tabster so a component mounted from @spaarke/ui-components
    // SOURCE shares this package's SAME tabster core (see note above — avoids a
    // "Cannot read properties of undefined (reading 'set')" crash deep in
    // @fluentui/react-tabster's useTabster hook when two independent copies mount
    // together, e.g. a real `<Toolbar>` alongside `DataGridViewSelector`).
    '^@fluentui/react-components$': '<rootDir>/node_modules/@fluentui/react-components',
    '^@fluentui/react-tabster$': '<rootDir>/node_modules/@fluentui/react-tabster',
    '^@fluentui/react-tabster/(.*)$': '<rootDir>/node_modules/@fluentui/react-tabster/$1',
    '^tabster$': '<rootDir>/node_modules/tabster',
  },
  testPathIgnorePatterns: ['/node_modules/', '/dist/'],
};
