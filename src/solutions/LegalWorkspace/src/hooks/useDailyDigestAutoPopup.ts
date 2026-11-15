/**
 * useDailyDigestAutoPopup — Auto-opens the Daily Digest Code Page dialog
 * on workspace launch. Shows once per session (sessionStorage guard).
 *
 * Opens sprk_dailyupdate via Xrm.Navigation.navigateTo as a 60% × 80% dialog.
 */

import { useEffect, useRef } from 'react';

const SESSION_KEY = 'spaarke_dailyDigestShown';

export function useDailyDigestAutoPopup(): void {
  const hasRunRef = useRef(false);

  useEffect(() => {
    if (hasRunRef.current) return;
    hasRunRef.current = true;

    // Already shown this session
    if (sessionStorage.getItem(SESSION_KEY) === 'true') {
      console.debug('[DailyDigest] Already shown this session — skipping');
      return;
    }

    // Mark as shown before opening
    sessionStorage.setItem(SESSION_KEY, 'true');

    // Small delay to let the workspace finish rendering
    const timer = setTimeout(() => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const w = window as any;
        const xrm = w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;

        if (!xrm?.Navigation?.navigateTo) {
          console.warn('[DailyDigest] Xrm.Navigation not available');
          return;
        }

        console.info('[DailyDigest] Opening Daily Briefing dialog...');
        xrm.Navigation.navigateTo(
          {
            pageType: 'webresource',
            webresourceName: 'sprk_dailyupdate',
          },
          {
            target: 2,
            width: { value: 60, unit: '%' },
            height: { value: 80, unit: '%' },
            title: 'Daily Briefing',
          }
        ).catch(() => {
          // User closed dialog or navigation error — ignore
        });
      } catch (err) {
        console.warn('[DailyDigest] Failed to open dialog:', err);
      }
    }, 1500);

    return () => clearTimeout(timer);
  }, []);
}
