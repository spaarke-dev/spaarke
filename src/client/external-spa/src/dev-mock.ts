/**
 * The ONE switch for local mock mode (spaarke-SPA-external-access-platform-r3 task 042, FR-18, #1566).
 *
 * Mock mode (a fake signed-in user, canned BFF responses, persona overrides) exists for local UI work only. It is on
 * when BOTH hold:
 *   - `import.meta.env.DEV` — Vite's dev server (and the test runner). NOT a build-time guarantee: Vite derives DEV
 *     from NODE_ENV, so `NODE_ENV=development vite build` leaves it `true`;
 *   - `import.meta.env.VITE_DEV_MOCK === 'true'` — an exact comparison, the same rule vite.config.ts applies when it
 *     refuses the build, and one the bundler can fold to `false` once the build has pinned the value.
 *
 * What keeps mock mode out of a built bundle is vite.config.ts, not DEV: any `vite build` with the flag set fails
 * (`refuseDevMockInBuild`), and every build `define`s `import.meta.env.VITE_DEV_MOCK` as the literal "false", so this
 * function is constant-false there and the bundler drops every mock branch, the canned data and the mock identity.
 * `npm run check:dist` scans the output for any that survived. Every mock path in `src/` reads this function — never
 * `VITE_DEV_MOCK` directly (tests/out-of-plane-routes.test.ts scans for that).
 */
export function isDevMockEnabled(): boolean {
  return import.meta.env.DEV && import.meta.env.VITE_DEV_MOCK === 'true';
}
