/**
 * Test-local mock for `@spaarke/auth`.
 *
 * R2 task 019 / NFR-05:
 *   The smoke test for `DailyBriefingApp` mounts the component with mocked
 *   Xrm, so it transitively imports `briefingService.ts` which imports
 *   `authenticatedFetch` from `@spaarke/auth`. The Jest `moduleNameMapper`
 *   routes that import here so we don't need MSAL/window-globals.
 *
 *   Individual tests can override the implementation with
 *   `jest.spyOn(spaarkeAuth, "authenticatedFetch")` after import.
 */

export const authenticatedFetch: jest.Mock = jest.fn(() =>
  Promise.resolve(
    new Response(
      JSON.stringify({
        tldr: { highlights: [], confidence: 0 },
        channelNarratives: [],
        generatedAtUtc: new Date().toISOString(),
      }),
      { status: 200, headers: { "Content-Type": "application/json" } }
    )
  )
);
