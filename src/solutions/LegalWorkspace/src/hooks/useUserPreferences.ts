/**
 * useUserPreferences — React hook for reading and writing user preferences.
 *
 * Fetches the TodoKanbanThresholds preference from sprk_userpreference on mount,
 * parses the JSON value, and provides an update function to save new thresholds.
 *
 * Falls back to default thresholds (60, 30) when no preference record exists
 * or when JSON parsing fails.
 *
 * Usage:
 *   const { preferences, updatePreferences, isLoading } = useUserPreferences({ webApi, userId });
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { DataverseService } from '../services/DataverseService';
import type { IWebApi } from '../types/xrm';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Default "Today" column threshold (items with score >= this go to Today). */
export const DEFAULT_TODAY_THRESHOLD = 60;

/** Default "Tomorrow" column threshold (items with score >= this go to Tomorrow). */
export const DEFAULT_TOMORROW_THRESHOLD = 30;

/**
 * Dataverse choice value for the TodoKanbanThresholds preference type.
 * Must match the sprk_preferencetype option set value configured in Dataverse.
 */
export const PREFERENCE_TYPE_TODO_KANBAN = 100000000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Kanban threshold preferences shape (stored as JSON in sprk_preferencevalue). */
export interface ITodoKanbanPreferences {
  todayThreshold: number;
  tomorrowThreshold: number;
}

export interface IUseUserPreferencesOptions {
  /** Xrm.WebApi reference from the framework context. */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId). */
  userId: string;
}

export interface IUseUserPreferencesResult {
  /** Current threshold preferences (defaults if not loaded yet). */
  preferences: ITodoKanbanPreferences;
  /** Save new threshold preferences to Dataverse. */
  updatePreferences: (prefs: Partial<ITodoKanbanPreferences>) => Promise<void>;
  /** True while the initial fetch is in progress. */
  isLoading: boolean;
}

// ---------------------------------------------------------------------------
// Default preferences
// ---------------------------------------------------------------------------

const DEFAULT_PREFERENCES: ITodoKanbanPreferences = {
  todayThreshold: DEFAULT_TODAY_THRESHOLD,
  tomorrowThreshold: DEFAULT_TOMORROW_THRESHOLD,
};

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useUserPreferences(
  options: IUseUserPreferencesOptions
): IUseUserPreferencesResult {
  const { webApi, userId } = options;

  const [preferences, setPreferences] = useState<ITodoKanbanPreferences>(DEFAULT_PREFERENCES);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  // Track the Dataverse record ID so we can update without re-querying
  const recordIdRef = useRef<string | undefined>(undefined);

  // Stable service reference — only recreates when webApi changes
  const serviceRef = useRef<DataverseService>(new DataverseService(webApi));
  useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // -------------------------------------------------------------------------
  // Fetch on mount
  // -------------------------------------------------------------------------

  useEffect(() => {
    if (!userId) {
      setIsLoading(false);
      return;
    }

    let cancelled = false;
    setIsLoading(true);

    serviceRef.current
      .getUserPreferences(userId, PREFERENCE_TYPE_TODO_KANBAN)
      .then((result) => {
        if (cancelled) return;

        if (result.success && result.data.length > 0) {
          const record = result.data[0];
          recordIdRef.current = record.sprk_userpreferenceid;

          // Parse JSON value with fallback to defaults
          try {
            const parsed = JSON.parse(record.sprk_preferencevalue) as Partial<ITodoKanbanPreferences>;
            setPreferences({
              todayThreshold: typeof parsed.todayThreshold === 'number'
                ? parsed.todayThreshold
                : DEFAULT_TODAY_THRESHOLD,
              tomorrowThreshold: typeof parsed.tomorrowThreshold === 'number'
                ? parsed.tomorrowThreshold
                : DEFAULT_TOMORROW_THRESHOLD,
            });
          } catch {
            console.warn('[useUserPreferences] Failed to parse preference JSON, using defaults');
            setPreferences(DEFAULT_PREFERENCES);
          }
        }
        // If no record found, keep defaults (already set)

        setIsLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        console.warn('[useUserPreferences] Failed to fetch preferences, using defaults');
        setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [userId]);

  // -------------------------------------------------------------------------
  // Update callback
  // -------------------------------------------------------------------------

  const updatePreferences = useCallback(
    async (update: Partial<ITodoKanbanPreferences>) => {
      const merged: ITodoKanbanPreferences = {
        ...preferences,
        ...update,
      };

      // Optimistic update
      setPreferences(merged);

      const jsonValue = JSON.stringify(merged);

      try {
        const result = await serviceRef.current.saveUserPreferences(
          userId,
          PREFERENCE_TYPE_TODO_KANBAN,
          jsonValue,
          recordIdRef.current
        );

        if (result.success) {
          // Store the record ID for future updates
          recordIdRef.current = result.data;
        } else {
          console.error('[useUserPreferences] Failed to save preferences:', result.error);
          // Note: we don't rollback the optimistic update — the local state
          // is still valid, just not persisted. It will retry on next save.
        }
      } catch (err) {
        console.error('[useUserPreferences] Error saving preferences:', err);
      }
    },
    [preferences, userId]
  );

  return { preferences, updatePreferences, isLoading };
}
