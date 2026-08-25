/**
 * DEV-ONLY replacement for `src/services/authInit.ts` — nine-screen render review.
 *
 * ⚠️ NEVER SHIPPED. Swapped in only by `vite.review.config.ts`, which is never used by
 * `npm run build`. The production `authInit.ts` is untouched.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * WHY THIS EXISTS
 *
 * design.md §9's first acceptance criterion is "all nine screens either work against the Spaarke Dev
 * tenant, or are deliberately removed". Every fix in this project has been verified at the API,
 * mapping or schema layer — WireMock proves the mapping, Graph's $metadata proves the field names,
 * live calls prove the data exists. None of that proves a screen renders.
 *
 * `authInit.ts` is the single choke point every screen's data passes through, so replacing just this
 * file runs the REAL app — real routing, real AppShell, real nine screens — against fixed payloads.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * WHAT THIS CANNOT TELL YOU
 *
 * - It does NOT prove the BFF produces these payloads. Only a live call does.
 * - It does NOT exercise the `authenticatedFetch` → `ApiError` → `describeApiError` path from task
 *   001 in its real form, because there is no real HTTP here. Error rendering is exercised only for
 *   the routes deliberately failed below.
 * - Payload provenance is mixed. Each route below is labelled `live`, `partial`, or `synthetic`.
 *   A screen that looks right against a `synthetic` payload has proven only that it renders a shape.
 */

import { ApiError } from "@spaarke/auth";
import { FIXTURES } from "./fixtures";

export function ensureAuthInitialized(): Promise<void> {
  return Promise.resolve();
}

/**
 * Match a request URL against the fixture table by PATH SEGMENTS.
 *
 * 🔴 An earlier version used `pathOnly.includes(route)`, which is subtly wrong and produced a white
 * screen: `/spe/containers/{id}/items` CONTAINS `/spe/containers`, so the File Browser received the
 * containers-list envelope where it expected `DriveItem[]`, and died calling array methods on an
 * object. Sorting longest-first did not save it, because the longer key simply was not present.
 *
 * Segment matching with `:param` placeholders makes the distinction explicit: a fixture key matches
 * only if it has the SAME NUMBER of segments and every literal segment is equal.
 */
function matchRoute(
  url: string,
  method: string,
): { body: unknown; status: number; segments: string[] } | undefined {
  const path = url.split("?")[0];
  // Normalise to the part from `/spe/` onward, so an absolute BFF base URL matches too.
  const idx = path.indexOf("/spe/");
  const target = (idx >= 0 ? path.slice(idx) : path).split("/").filter(Boolean);

  const segmentsMatch = (route: string): boolean => {
    const parts = route.split("/").filter(Boolean);
    if (parts.length !== target.length) return false;
    return parts.every((p, i) => p.startsWith(":") || p === target[i]);
  };

  /*
   * METHOD-QUALIFIED FIRST, then bare path.
   *
   * A GET list and a POST create share a path but NOT a response shape: `GET /spe/containers`
   * returns `{ items, count }` while `POST /spe/containers` returns a single `Container`. Matching
   * on path alone handed the create dialog a list envelope — which would have looked like a working
   * "+ New" that quietly produced nonsense.
   */
  for (const [key, value] of Object.entries(FIXTURES)) {
    const [maybeMethod, ...rest] = key.split(" ");
    if (rest.length === 0) continue; // bare-path key; handled below
    if (maybeMethod.toUpperCase() !== method) continue;
    if (segmentsMatch(rest.join(" "))) return { ...value, segments: target };
  }

  for (const [key, value] of Object.entries(FIXTURES)) {
    if (key.includes(" ")) continue; // method-qualified; already tried
    if (segmentsMatch(key)) return { ...value, segments: target };
  }
  return undefined;
}

export async function authenticatedFetch(
  url: string,
  init?: RequestInit,
): Promise<Response> {
  const method = (init?.method ?? "GET").toUpperCase();
  const hit = matchRoute(url, method);

  if (!hit) {
    // Loudly, not silently: an unmocked route is a gap in the review, not a working screen.
    console.warn(`[review-mock] NO FIXTURE for ${method} ${url} — returning 404`);
    throw new ApiError(
      `No fixture is defined for ${method} ${url}. This is a gap in the review harness, not ` +
        `necessarily a product defect.`,
      404,
      { status: 404, title: "Not Found" },
    );
  }

  console.info(`[review-mock] ${method} ${url} → ${hit.status}`);

  /*
   * A fixture body may be a RESOLVER, so a per-id route can answer for the id that was asked for
   * rather than always answering with row 0. Returning row 0 for every id is how the harness came to
   * show "the same files and dates in every container" — plausible, uniform, and wrong.
   */
  const resolved =
    typeof hit.body === "function"
      ? (hit.body as (segments: string[]) => unknown)(hit.segments)
      : hit.body;

  if (resolved === undefined && hit.status < 400) {
    // The resolver found nothing. Say so, rather than falling back to something that looks right.
    console.warn(`[review-mock] resolver returned nothing for ${method} ${url} — returning 404`);
    throw new ApiError(
      `No fixture row matches ${method} ${url}.`,
      404,
      { status: 404, title: "Not Found" } as never,
    );
  }

  // Writes are accepted but NOT persisted — the review is about rendering, and a fake persistence
  // layer would let a screen appear to save when the real PATCH currently 400s in the live tenant.
  if (method !== "GET" && hit.status < 400) {
    console.warn(
      `[review-mock] ${method} accepted WITHOUT persisting. Note the live container-type PATCH ` +
        `returns 400 today — see notes/live-verification-2026-08-24.md §2.`,
    );
  }

  if (hit.status >= 400) {
    const problem = resolved as { detail?: string; title?: string };
    throw new ApiError(
      problem.detail ?? problem.title ?? "Request failed",
      hit.status,
      resolved as never,
    );
  }

  return new Response(JSON.stringify(resolved), {
    status: hit.status,
    headers: { "Content-Type": "application/json" },
  });
}
