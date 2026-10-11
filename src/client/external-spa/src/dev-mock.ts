/**
 * The ONE switch for local mock mode (spaarke-SPA-external-access-platform-r3 task 042, FR-18, #1566).
 *
 * Mock mode (a fake signed-in user, canned BFF responses, persona overrides) exists for local UI work only. It is on
 * when BOTH hold:
 *   - `import.meta.env.DEV` — Vite's dev server (and the test runner). Every `vite build` replaces it with the literal
 *     `false`, so this function returns `false` in a built bundle and the bundler drops every mock branch, the canned
 *     data and the mock identity with it (verified by a `dist/` grep — task-042 notes);
 *   - `VITE_DEV_MOCK === 'true'`.
 *
 * A production build with `VITE_DEV_MOCK=true` does not get that far: vite.config.ts refuses it
 * (`refuseDevMockInProductionBuild`). Every mock path in `src/` reads this function — never `VITE_DEV_MOCK` directly
 * (tests/out-of-plane-routes.test.ts scans for that).
 */
export function isDevMockEnabled(): boolean {
  return import.meta.env.DEV && import.meta.env.VITE_DEV_MOCK === 'true';
}
