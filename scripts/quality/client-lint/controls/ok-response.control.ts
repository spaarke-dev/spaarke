/**
 * Lint CONTROLS for `client-lint.mjs controls` -- never imported, never executed.
 *
 * The line AFTER a `@control must-fire` marker MUST be reported by `@typescript-eslint/no-unnecessary-condition`;
 * the line AFTER a `@control must-not-fire` marker MUST NOT be reported. The runner asserts exactly this, so
 * a toolchain or type regression that silently stops catching dead `!res.ok` checks turns the build red.
 */
import type { AuthenticatedFetchFn, ResponseFetchFn } from "@spaarke/auth";

declare const throwingFetch: AuthenticatedFetchFn;
declare const returningFetch: ResponseFetchFn;
declare const rawFetch: (url: string) => Promise<Response>;

export async function deadIfNotOk(): Promise<unknown> {
  const res = await throwingFetch("/api/x");
  // @control must-fire
  if (!res.ok) return null;
  return res.json();
}

export async function deadTernaryNotOk(): Promise<string> {
  const res = await throwingFetch("/api/x");
  // @control must-fire
  return !res.ok ? "failed" : "fine";
}

export async function liveIfNotOkReturningFetch(): Promise<unknown> {
  const res = await returningFetch("/api/x");
  // @control must-not-fire
  if (!res.ok) return null;
  return res.json();
}

export async function liveIfNotOkRawFetch(): Promise<unknown> {
  const res = await rawFetch("/api/x");
  // @control must-not-fire
  if (!res.ok) return null;
  return res.json();
}
