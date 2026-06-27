/**
 * Jest configuration for @spaarke/daily-briefing-components.
 *
 * R2 task 019 / NFR-05: Adds Jest test infrastructure for the 3 split hooks
 * (`useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`)
 * + smoke test mounting `DailyBriefingApp` with mocked `Xrm` and asserting the
 * BFF `/narrate` call fires with a non-empty payload.
 *
 * Mirrors `@spaarke/auth` Jest setup (the canonical Jest pattern in
 * `src/client/shared/`). Smart Todo's __tests__ folder is intentionally
 * dependency-free smoke functions and is NOT the pattern here.
 *
 * Setup:
 *   - `testEnvironment: 'jsdom'` so React Testing Library can mount components.
 *   - `ts-jest` to compile `.ts` / `.tsx` test files using the package tsconfig.
 *   - `testMatch` picks up `test/**\/*.test.{ts,tsx}` (mirrors task POML's
 *     `test/` directory choice).
 *   - `@spaarke/auth` is module-mapped to a local mock so smoke tests don't
 *     need MSAL/window-globals; per-test calls override with `jest.mock(...)`.
 */
/** @type {import('jest').Config} */
module.exports = {
  testEnvironment: "jsdom",
  transform: {
    "^.+\\.tsx?$": [
      "ts-jest",
      {
        tsconfig: "tsconfig.test.json",
        diagnostics: { ignoreCodes: [151001] },
      },
    ],
  },
  testMatch: ["<rootDir>/test/**/*.test.ts", "<rootDir>/test/**/*.test.tsx"],
  moduleFileExtensions: ["ts", "tsx", "js", "jsx", "json"],
  moduleNameMapper: {
    // Route the `@spaarke/auth` peer dep to a local test stub so smoke
    // tests don't need MSAL/window-globals to construct the package.
    "^@spaarke/auth$": "<rootDir>/test/__mocks__/spaarke-auth.ts",
    "^@spaarke/ui-components/services$":
      "<rootDir>/test/__mocks__/spaarke-ui-components-services.ts",
    "^@spaarke/ui-components$":
      "<rootDir>/test/__mocks__/spaarke-ui-components.tsx",
  },
  setupFilesAfterEnv: ["<rootDir>/test/jest.setup.ts"],
  // Coverage thresholds left empty in the initial 0.1.0 release — NFR-05
  // requires test existence + a measurable report, not a hard floor.
  collectCoverageFrom: [
    "src/hooks/useBriefingNotifications.ts",
    "src/hooks/useBriefingPreferences.ts",
    "src/hooks/useBriefingActions.ts",
    "src/components/DailyBriefingApp.tsx",
  ],
  // jsdom polyfills (window.matchMedia is referenced by Fluent v9 in some paths).
  testPathIgnorePatterns: ["/node_modules/", "/dist/"],
};
