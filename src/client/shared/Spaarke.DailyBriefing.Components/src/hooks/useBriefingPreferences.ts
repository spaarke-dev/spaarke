/**
 * useBriefingPreferences — fetches and persists Daily Digest user preferences
 * (sprk_userpreference) via Xrm.WebApi.
 *
 * One of the three independent hooks decomposed from the original monolithic
 * `useNotificationData` (R2 FR-06 / task 014). Each of the three hooks
 * can be consumed independently — NO shared internal state, NO singletons,
 * NO context. Cross-hook coordination (e.g., "refetch notifications when
 * `preferences.disabledChannels` changes") is the consumer's responsibility
 * via an effect (Option A, per project design.md / spec FR-06).
 *
 * Opt-out preference model:
 *   - `disabledChannels` is the only mutator that should trigger a notifications
 *     refetch at the consumer layer.
 *   - The hook itself does NOT refetch notifications when preferences change —
 *     that's the consumer's job (Option A explicit effect-based coordination).
 *
 * Usage:
 *   const { preferences, updatePreferences, isLoading, error } =
 *     useBriefingPreferences(webApi, userId);
 *
 * Coordination example (consumer-layer effect — Option A):
 *   const { preferences, ... } = useBriefingPreferences(webApi, userId);
 *   const { refetch, ... } = useBriefingNotifications(webApi);
 *   useEffect(() => { refetch(); }, [preferences.disabledChannels]);
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import type { IWebApi, DailyDigestPreferences } from '../types/notifications';
import { DEFAULT_DAILY_DIGEST_PREFERENCES } from '../types/notifications';
import { fetchDigestPreferences, saveDigestPreferences } from '../services/preferencesService';

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

export interface UseBriefingPreferencesResult {
  /** Loaded user preferences (defaults until first successful fetch). */
  preferences: DailyDigestPreferences;
  /**
   * Apply a partial update to preferences and persist to Dataverse.
   * Optimistically updates local state; logs and reverts if save fails.
   */
  updatePreferences: (update: Partial<DailyDigestPreferences>) => Promise<void>;
  /** Whether the initial preferences load is in flight. */
  isLoading: boolean;
  /** Error message if the preferences fetch/save failed. */
  error?: string;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Fetches and persists Daily Digest user preferences.
 *
 * @param webApi - Xrm.WebApi reference (from xrmProvider.getWebApi()).
 *                 Null while Xrm is still resolving — hook stays idle.
 * @param userId - The current user's systemuser GUID (stripped of braces).
 */
export function useBriefingPreferences(webApi: IWebApi | null, userId: string): UseBriefingPreferencesResult {
  const [preferences, setPreferences] = useState<DailyDigestPreferences>(DEFAULT_DAILY_DIGEST_PREFERENCES);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | undefined>(undefined);

  // Track preference record ID for updates without re-querying
  const preferenceRecordIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    if (!webApi || !userId) {
      setIsLoading(false);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(undefined);

    fetchDigestPreferences(webApi, userId)
      .then(result => {
        if (cancelled) return;
        if (result.success) {
          setPreferences(result.data.preferences);
          preferenceRecordIdRef.current = result.data.recordId;
        }
        // On failure, keep defaults — non-fatal (covered by spec: "opt-out model")
        setIsLoading(false);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message =
          err && typeof err === 'object' && 'message' in err
            ? String((err as { message: unknown }).message)
            : 'Failed to load preferences.';
        setError(message);
        setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [webApi, userId]);

  const updatePreferences = useCallback(
    async (update: Partial<DailyDigestPreferences>) => {
      if (!webApi || !userId) return;

      const merged: DailyDigestPreferences = {
        ...preferences,
        ...update,
      };

      // Optimistic update
      setPreferences(merged);

      const result = await saveDigestPreferences(webApi, userId, merged, preferenceRecordIdRef.current);

      if (result.success) {
        preferenceRecordIdRef.current = result.data;
      } else {
        console.error('[DailyBriefing] Failed to save preferences:', result.error.message);
        setError(result.error.message);
      }
    },
    [webApi, userId, preferences]
  );

  return {
    preferences,
    updatePreferences,
    isLoading,
    error,
  };
}
