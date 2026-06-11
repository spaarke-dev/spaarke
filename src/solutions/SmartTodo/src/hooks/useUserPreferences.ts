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

/**
 * SmartTodo view-mode values (R4 FR-09 — task 033).
 *
 * card = card-grid render (default on first visit per FR-09 acceptance)
 * list = dense list render
 *
 * Persisted in the same JSON payload as the kanban thresholds
 * (preference-type 100000000) to avoid adding a new optionset value.
 * The conceptual "SmartTodoView" preference referenced in spec FR-09 is
 * round-tripped via the `viewMode` field in the shared JSON envelope.
 */
export type SmartTodoViewMode = 'card' | 'list';

/** Default view mode shown on first visit (FR-09). */
export const DEFAULT_SMART_TODO_VIEW_MODE: SmartTodoViewMode = 'card';

const VALID_VIEW_MODES: ReadonlyArray<SmartTodoViewMode> = ['card', 'list'];

function isValidViewMode(value: unknown): value is SmartTodoViewMode {
  return typeof value === 'string' && (VALID_VIEW_MODES as readonly string[]).includes(value);
}

/**
 * SmartTodo Kanban orientation values (R4 FR-28 / FR-29 / NFR-08 — task 071).
 *
 * horizontal = columns laid out side-by-side (default on first visit)
 * vertical   = columns stacked top-to-bottom
 *
 * Mirrors `KanbanOrientation` from `@spaarke/ui-components/Kanban` and
 * `Orientation` from `@spaarke/ui-components/OrientationToggle`. Persisted
 * in the SAME JSON payload as the kanban thresholds + viewMode
 * (preference-type 100000000) — the spec FR-30 "SmartTodoOrientation"
 * preference is round-tripped via the `orientation` field in this
 * envelope, NOT via a new preferencetype optionset value.
 */
export type SmartTodoOrientation = 'horizontal' | 'vertical';

/** Default orientation shown on first visit (FR-28). */
export const DEFAULT_SMART_TODO_ORIENTATION: SmartTodoOrientation = 'horizontal';

const VALID_ORIENTATIONS: ReadonlyArray<SmartTodoOrientation> = ['horizontal', 'vertical'];

function isValidOrientation(value: unknown): value is SmartTodoOrientation {
  return typeof value === 'string' && (VALID_ORIENTATIONS as readonly string[]).includes(value);
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Kanban threshold preferences shape (stored as JSON in sprk_preferencevalue).
 *
 * R4 task 031 / FR-07 / OD-2 — the R3 `myTasksFilterMode` field is removed
 * because "Assigned to Me" is now the SOLE filter mode (no user selection
 * to persist). The R3 JSON payload remains backwards-compatible on read: if
 * an older record still contains `myTasksFilterMode`, the parser ignores it.
 */
export interface ITodoKanbanPreferences {
  todayThreshold: number;
  tomorrowThreshold: number;
  /**
   * R4 FR-09 (task 033) — persisted list/card view selection for the
   * SmartTodo Code Page. Defaults to "card" on first visit.
   */
  viewMode: SmartTodoViewMode;
  /**
   * R4 FR-28 / FR-29 / FR-30 (task 071) — persisted Kanban board
   * orientation. Defaults to "horizontal" on first visit. Round-tripped
   * via this shared envelope (same preferencetype as thresholds +
   * viewMode) rather than a separate preferencetype optionset value.
   */
  orientation: SmartTodoOrientation;
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
  viewMode: DEFAULT_SMART_TODO_VIEW_MODE,
  orientation: DEFAULT_SMART_TODO_ORIENTATION,
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
            // Older records may still carry a legacy `myTasksFilterMode` key
            // (R3 FR-12). It is silently ignored on read per R4 OD-2.
            const parsed = JSON.parse(record.sprk_preferencevalue) as Partial<ITodoKanbanPreferences>;
            setPreferences({
              todayThreshold: typeof parsed.todayThreshold === 'number'
                ? parsed.todayThreshold
                : DEFAULT_TODAY_THRESHOLD,
              tomorrowThreshold: typeof parsed.tomorrowThreshold === 'number'
                ? parsed.tomorrowThreshold
                : DEFAULT_TOMORROW_THRESHOLD,
              viewMode: isValidViewMode(parsed.viewMode)
                ? parsed.viewMode
                : DEFAULT_SMART_TODO_VIEW_MODE,
              orientation: isValidOrientation(parsed.orientation)
                ? parsed.orientation
                : DEFAULT_SMART_TODO_ORIENTATION,
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
