/**
 * useAuthProbe.ts — bounded retry-with-backoff auth-ready probe.
 *
 * Extracted from `App.tsx`'s `AppWithAuth` (spaarkeai-assistant-enhancements-r2
 * Phase 0 FIX 3, 2026-08-07) so the retry logic is independently unit-testable
 * without pulling in `AppWithAuth`'s heavy dependency tree (ThreePaneShell,
 * LegalWorkspaceApp, etc.).
 *
 * Root cause this fixes: "the Assistant sometimes fails to load on the FIRST
 * mount after a hard reset (browser cache/cookies cleared); works after a
 * reload." `SpaarkeAuthProvider.getAccessToken()` never throws — on a cold MSAL
 * cache its first silent attempt (`acquireTokenSilent` -> `ssoSilent`, the
 * latter round-trips a hidden iframe to Entra) can simply take longer than
 * usual, and the provider swallows that outcome and returns an EMPTY string
 * rather than rejecting. The previous one-shot probe then permanently set
 * `isAuthenticated=false` with no retry — `useAuth()`/`isAuthenticated` is a
 * sync, non-reactive getter by design (see `@spaarke/auth`'s `useAuth.ts`), so
 * nothing re-checked it later and the shell stayed on `AuthLoadingState`
 * ("Initializing AI Chat... / Connecting to Dataverse...") until the user
 * manually reloaded the page.
 *
 * (This is a DIFFERENT gate than the notifications SignalR connection — that
 * connection is separately fire-and-forget / non-blocking, see
 * services/notificationsBootstrap.ts; the two were previously conflated
 * because both can log auth-timing warnings around the same cold-start
 * window.)
 *
 * @see App.tsx — AppWithAuth (sole consumer)
 * @see .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md §H-5 — the snapshot bug this file's
 *      sibling logic already fixed (this hook does NOT reintroduce a token
 *      snapshot — it only retries the boolean gate check).
 */
import * as React from "react";
import { getAuthProvider } from "@spaarke/auth";

/** Bounded retry delays (ms). ~10s total budget across 5 attempts. */
export const AUTH_PROBE_RETRY_DELAYS_MS = [500, 1500, 3000, 5000];

/**
 * Probes `getAuthProvider().getAccessToken()` once at mount, retrying with
 * backoff (see {@link AUTH_PROBE_RETRY_DELAYS_MS}) while it returns an empty
 * token (or throws). Returns `true` as soon as any attempt yields a non-empty
 * token; stays `false` forever once retries are exhausted (a subsequent
 * remount — e.g. a page reload — starts a fresh probe).
 *
 * UI-gating flag only — NEVER the token itself. Downstream BFF calls MUST
 * continue to acquire fresh tokens per-request via `authenticatedFetch` /
 * `useAuth()` (Spaarke Auth v2); this hook exists solely to decide when the
 * shell is ready to render auth-aware UI.
 */
export function useAuthProbe(): boolean {
  const [isAuthenticated, setIsAuthenticated] = React.useState<boolean>(false);

  React.useEffect(() => {
    let cancelled = false;

    const probeAuth = async (): Promise<void> => {
      const provider = getAuthProvider();

      for (let attempt = 0; attempt <= AUTH_PROBE_RETRY_DELAYS_MS.length; attempt++) {
        if (cancelled) return;
        try {
          const accessToken = await provider.getAccessToken();
          if (cancelled) return;
          if (accessToken) {
            setIsAuthenticated(true);
            return;
          }
        } catch (err) {
          if (cancelled) return;
          console.warn("[SpaarkeAi] Auth probe attempt failed:", err);
        }

        if (attempt < AUTH_PROBE_RETRY_DELAYS_MS.length) {
          await new Promise<void>(resolve => setTimeout(resolve, AUTH_PROBE_RETRY_DELAYS_MS[attempt]));
        }
      }

      if (!cancelled) {
        console.warn(
          "[SpaarkeAi] Auth probe exhausted all retries — no token acquired. isAuthenticated remains false."
        );
        setIsAuthenticated(false);
      }
    };

    void probeAuth();

    return () => {
      cancelled = true;
    };
  }, []);

  return isAuthenticated;
}
