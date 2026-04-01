/**
 * useDailyDigestAutoPopup — Opens the Daily Digest (sprk_dailyupdate) Code Page
 * dialog automatically on first workspace launch per browser session, if the
 * user's autoPopup preference is enabled.
 *
 * Behavior:
 *   1. Queries sprk_userpreference for the DailyDigestAutoPopup preference type.
 *   2. If no preference record exists, auto-popup is enabled by default (opt-out model).
 *   3. Checks sessionStorage for "spaarke_dailyDigestShown" — only fires once per session.
 *   4. Opens sprk_dailyupdate via Xrm.Navigation.navigateTo (60% × 80% dialog).
 *   5. Sets the sessionStorage flag so subsequent navigations don't re-trigger.
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
 * Dataverse choice value for the DailyDigestAutoPopup preference type.
 * Must match the sprk_preferencetype option set value in Dataverse.
 * (100000000 = TodoKanbanThresholds, 100000001 = DailyDigestAutoPopup)
 */
const PREFERENCE_TYPE_DAILY_DIGEST_AUTO_POPUP = 100000001;

/** SessionStorage key — prevents re-opening the digest within the same browser session. */
const SESSION_KEY = "spaarke_dailyDigestShown";

/** Web resource name for the Daily Digest Code Page. */
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
        // Check user preference — opt-out model: enabled by default
        const service = new DataverseService(webApi);
        const result = await service.getUserPreferences(
          userId,
          PREFERENCE_TYPE_DAILY_DIGEST_AUTO_POPUP
        );

        if (result.success && result.data.length > 0) {
          // Preference record exists — check if user explicitly disabled auto-popup
          try {
            const parsed = JSON.parse(result.data[0].sprk_preferencevalue) as {
              enabled?: boolean;
            };
            if (parsed.enabled === false) {
              // User opted out — mark session so we don't re-query
              sessionStorage.setItem(SESSION_KEY, "opted-out");
              return;
            }
          } catch {
            // Malformed JSON — treat as enabled (default)
          }
        }
        // No preference record OR enabled: proceed to open dialog

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
