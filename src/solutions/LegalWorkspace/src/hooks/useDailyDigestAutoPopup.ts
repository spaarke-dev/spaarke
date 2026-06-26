/**
 * useDailyDigestAutoPopup — Opens the Daily Briefing (sprk_dailyupdate) Code
 * Page dialog automatically on first workspace launch per browser session,
 * controlled by the user's `autoPopup` preference from the canonical R4
 * Daily Briefing preferences schema (sprk_userpreference type 100000002).
 *
 * R4 task 043 / FR-17d (2026-06-26) — REWIRED:
 *   - Pre-R4 (R1 era): read preference type 100000001 (DailyDigestAutoPopup)
 *     with JSON shape `{ enabled: boolean }`.
 *   - R4: read preference type 100000002 (DailyDigest — shared by the
 *     widget's preferences dropdown) with JSON shape including
 *     `{ autoPopup: boolean, ... }`. This unifies "auto-popup on workspace
 *     launch" with the in-widget preferences toggle the user already
 *     manipulates — they are now the SAME preference, not two parallel ones.
 *   - Default behavior (opt-out model) preserved: when no preference record
 *     exists, auto-popup IS enabled (matches `DEFAULT_DAILY_DIGEST_PREFERENCES.autoPopup === true`).
 *
 * Behavior (unchanged from R1):
 *   1. Queries sprk_userpreference for the DailyDigest preference type.
 *   2. If no preference record exists, auto-popup is enabled by default
 *      (opt-out model — matches the R4 widget defaults).
 *   3. Checks sessionStorage for "spaarke_dailyDigestShown" — only fires
 *      once per session.
 *   4. Opens sprk_dailyupdate (the standalone Daily Briefing Code Page
 *      hosting `DailyBriefingApp`) via Xrm.Navigation.navigateTo
 *      (60% × 80% dialog).
 *   5. Sets the sessionStorage flag so subsequent navigations don't
 *      re-trigger.
 *
 * SpaarkeAi-embedding suppression (unchanged):
 *   SpaarkeAi's `main.tsx` writes the sessionStorage sentinel BEFORE any
 *   React tree mounts (see `suppressLegalWorkspaceDailyDigestAutoPopup`)
 *   so embedded LegalWorkspace instances never auto-popup the modal — the
 *   daily-briefing widget renders INLINE in SpaarkeAi via the workspace
 *   section registry instead.
 *
 * Usage:
 *   useDailyDigestAutoPopup({ webApi, userId });
 */

import { useEffect, useRef } from "react";
import { DataverseService } from "../services/DataverseService";
import type { IWebApi } from "../types/xrm";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Dataverse choice value for the canonical R4 DailyDigest preference type.
 *
 * Mirrors `PREFERENCE_TYPE_DAILY_DIGEST` in
 * `@spaarke/daily-briefing-components/src/types/notifications.ts`. We
 * intentionally inline the constant rather than import to keep
 * LegalWorkspace's dependency graph stable (no new NPM dependencies per
 * R4 task 043 constraints). Schema contract under test in
 * `Spaarke.DailyBriefing.Components/test/autoPopupPreferenceContract.test.ts`.
 *
 * sprk_preferencetype option set values:
 *   100000000 = TodoKanbanThresholds
 *   100000001 = (legacy) DailyDigestAutoPopup — DEPRECATED in R4
 *   100000002 = DailyDigest (autoPopup, disabledChannels, dueWithinDays, timeWindow)
 */
const PREFERENCE_TYPE_DAILY_DIGEST = 100000002;

/** SessionStorage key — prevents re-opening the digest within the same browser session. */
const SESSION_KEY = "spaarke_dailyDigestShown";

/** Web resource name for the Daily Briefing standalone Code Page. */
const DAILY_DIGEST_WEB_RESOURCE = "sprk_dailyupdate";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IUseDailyDigestAutoPopupOptions {
  /** Xrm.WebApi reference from the framework context. */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId). */
  userId: string;
}

// ---------------------------------------------------------------------------
// Pure parsing helper (exported for unit testing)
// ---------------------------------------------------------------------------

/**
 * Parse the `autoPopup` field from a raw `sprk_preferencevalue` JSON string.
 *
 * Contract (FR-17d):
 *   - Returns `true` if the JSON parses and contains `autoPopup === true`.
 *   - Returns `false` if the JSON parses and contains `autoPopup === false`.
 *   - Returns `true` if the JSON is missing the `autoPopup` field (opt-out
 *     default — matches `DEFAULT_DAILY_DIGEST_PREFERENCES.autoPopup`).
 *   - Returns `true` if the JSON is malformed/empty (defensive — opt-out
 *     model, don't silently disable the popup on parse failure).
 *
 * Mirrors the merge-with-defaults semantics in the canonical
 * `mergeWithDefaults` helper at
 * `@spaarke/daily-briefing-components/src/services/preferencesService.ts:155`.
 * Schema parity is asserted in
 * `Spaarke.DailyBriefing.Components/test/autoPopupPreferenceContract.test.ts`.
 */
export function parseAutoPopupFromPreferenceJson(rawValue: string | undefined | null): boolean {
  if (!rawValue) return true;
  try {
    const parsed = JSON.parse(rawValue) as { autoPopup?: unknown };
    if (typeof parsed.autoPopup === "boolean") return parsed.autoPopup;
    return true; // missing field → default (opt-out)
  } catch {
    return true; // malformed → default (opt-out)
  }
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useDailyDigestAutoPopup(
  options: IUseDailyDigestAutoPopupOptions
): void {
  const { webApi, userId } = options;
  const hasRunRef = useRef(false);

  useEffect(() => {
    // Guard: only run once per component lifecycle
    if (hasRunRef.current) return;
    if (!userId || !webApi) return;

    // Guard: already shown this session
    try {
      if (sessionStorage.getItem(SESSION_KEY)) return;
    } catch {
      // sessionStorage unavailable (e.g. iframe sandbox) — skip silently
      return;
    }

    hasRunRef.current = true;

    (async () => {
      try {
        // Check user preference — opt-out model: enabled by default.
        // R4 task 043 (2026-06-26): now reads PREFERENCE_TYPE_DAILY_DIGEST
        // (100000002, the canonical R4 widget preferences row) and parses
        // the `autoPopup` field per `DailyDigestPreferences` schema.
        const service = new DataverseService(webApi);
        const result = await service.getUserPreferences(
          userId,
          PREFERENCE_TYPE_DAILY_DIGEST
        );

        if (result.success && result.data.length > 0) {
          // Preference record exists — check the R4 `autoPopup` field
          const rawValue = result.data[0].sprk_preferencevalue;
          const autoPopup = parseAutoPopupFromPreferenceJson(rawValue);
          if (!autoPopup) {
            // User opted out — mark session so we don't re-query next render
            sessionStorage.setItem(SESSION_KEY, "opted-out");
            return;
          }
        }
        // No preference record OR autoPopup=true (default): proceed to open dialog

        // Mark session BEFORE opening to prevent race conditions with re-renders
        sessionStorage.setItem(SESSION_KEY, "shown");

        // Resolve Xrm from parent frames (PCF runs in iframe)
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm: any =
          (window as any)?.Xrm ??
          (window.parent as any)?.Xrm ??
          (window.top as any)?.Xrm;

        if (!xrm?.Navigation?.navigateTo) {
          console.warn(
            "[useDailyDigestAutoPopup] Xrm.Navigation not available — skipping auto-popup"
          );
          return;
        }

        await xrm.Navigation.navigateTo(
          {
            pageType: "webresource",
            webresourceName: DAILY_DIGEST_WEB_RESOURCE,
          },
          {
            target: 2,
            width: { value: 60, unit: "%" },
            height: { value: 80, unit: "%" },
            title: "Daily Briefing",
          }
        );
      } catch (err) {
        // User cancelled dialog or navigation error — not actionable
        console.debug("[useDailyDigestAutoPopup] Dialog closed or failed:", err);
      }
    })();
  }, [webApi, userId]);
}
