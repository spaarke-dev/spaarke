/**
 * useTodoKanbanPreferences — the user's Smart To Do Kanban preferences (thresholds, orientation, column order), one
 * copy for every host (#2972).
 *
 * All Smart To Do surfaces read and write ONE `sprk_userpreference` record per user (preference type 100000000,
 * JSON in `sprk_preferencevalue`). Before #2972 each host had its own copy of this hook, and the workspace copy knew
 * only the thresholds: saving a threshold there wrote `{ todayThreshold, tomorrowThreshold }` and dropped the
 * orientation and column order the code page had stored. This hook:
 *   - parses every known field, with defaults for missing or invalid ones;
 *   - keeps keys it does not know, so a newer client's fields survive an older surface's save;
 *   - merges each update into the LATEST state (a ref), not the render-time closure, so two quick updates
 *     (e.g. a column reorder then a threshold change) cannot overwrite each other.
 * Run one instance per page and pass its result down; two instances hold independent state.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { ITodoKanbanPreferences, SmartTodoOrientation, SmartTodoViewMode } from '../types/entities';
import type { TodoDataverseService } from '../services/TodoDataverseService';
import { DEFAULT_TODAY_THRESHOLD, DEFAULT_TOMORROW_THRESHOLD } from './useKanbanColumns';

/** sprk_preferencetype value of the Smart To Do Kanban preferences. */
export const PREFERENCE_TYPE_TODO_KANBAN = 100000000;

/** First-visit orientation (R4 FR-28). */
export const DEFAULT_SMART_TODO_ORIENTATION: SmartTodoOrientation = 'horizontal';

/** First-visit view mode (R4-104). `card` renders the Kanban. */
export const DEFAULT_SMART_TODO_VIEW_MODE: SmartTodoViewMode = 'card';

/** Preferences used until the record loads, and when there is none. */
export const DEFAULT_TODO_KANBAN_PREFERENCES: Readonly<Required<ITodoKanbanPreferences>> = {
  todayThreshold: DEFAULT_TODAY_THRESHOLD,
  tomorrowThreshold: DEFAULT_TOMORROW_THRESHOLD,
  viewMode: DEFAULT_SMART_TODO_VIEW_MODE,
  orientation: DEFAULT_SMART_TODO_ORIENTATION,
  columnOrders: {},
};

function isOrientation(value: unknown): value is SmartTodoOrientation {
  return value === 'horizontal' || value === 'vertical';
}

function isViewMode(value: unknown): value is SmartTodoViewMode {
  return value === 'card' || value === 'list';
}

function isColumnOrders(value: unknown): value is Record<string, string[]> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  return Object.values(value as Record<string, unknown>).every(
    v => Array.isArray(v) && v.every(id => typeof id === 'string')
  );
}

/**
 * Parses a stored `sprk_preferencevalue`. Known fields are validated (invalid → default); unknown keys are returned
 * in `extra` so a save can write them back. Corrupt JSON yields the defaults.
 */
export function parseTodoKanbanPreferences(json: string | null | undefined): {
  preferences: Required<ITodoKanbanPreferences>;
  extra: Record<string, unknown>;
} {
  let parsed: Record<string, unknown> = {};
  try {
    const value: unknown = json ? JSON.parse(json) : {};
    if (value && typeof value === 'object' && !Array.isArray(value)) parsed = value as Record<string, unknown>;
  } catch {
    parsed = {};
  }
  const { todayThreshold, tomorrowThreshold, viewMode, orientation, columnOrders, ...extra } = parsed;
  return {
    preferences: {
      todayThreshold: typeof todayThreshold === 'number' ? todayThreshold : DEFAULT_TODAY_THRESHOLD,
      tomorrowThreshold: typeof tomorrowThreshold === 'number' ? tomorrowThreshold : DEFAULT_TOMORROW_THRESHOLD,
      viewMode: isViewMode(viewMode) ? viewMode : DEFAULT_SMART_TODO_VIEW_MODE,
      orientation: isOrientation(orientation) ? orientation : DEFAULT_SMART_TODO_ORIENTATION,
      columnOrders: isColumnOrders(columnOrders) ? columnOrders : {},
    },
    extra,
  };
}

export interface IUseTodoKanbanPreferencesOptions {
  /** The data service (host-constructed). The latest one is used. */
  service: Pick<TodoDataverseService, 'getUserPreferences' | 'saveUserPreferences'>;
  /** The current user's systemuser GUID. Empty: defaults, nothing loaded or saved. */
  userId: string;
}

export interface IUseTodoKanbanPreferencesResult {
  /** Current preferences (defaults until loaded). */
  preferences: Required<ITodoKanbanPreferences>;
  /** Merges `update` into the current preferences, shows it at once, and saves it. */
  updatePreferences: (update: Partial<ITodoKanbanPreferences>) => Promise<void>;
  /** True while the record is loading. */
  isLoading: boolean;
}

/** A promise plus its resolver: settles when the current preference load finishes (or is abandoned). */
interface ILoadGate {
  promise: Promise<void>;
  open: () => void;
}

function createLoadGate(): ILoadGate {
  let open: () => void = () => undefined;
  const promise = new Promise<void>(resolve => {
    open = resolve;
  });
  return { promise, open };
}

const OPEN_GATE: ILoadGate = { promise: Promise.resolve(), open: () => undefined };

export function useTodoKanbanPreferences(options: IUseTodoKanbanPreferencesOptions): IUseTodoKanbanPreferencesResult {
  const { service, userId } = options;

  const [preferences, setPreferences] = useState<Required<ITodoKanbanPreferences>>(DEFAULT_TODO_KANBAN_PREFERENCES);
  const [isLoading, setIsLoading] = useState<boolean>(!!userId);

  const preferencesRef = useRef<Required<ITodoKanbanPreferences>>(DEFAULT_TODO_KANBAN_PREFERENCES);
  const extraRef = useRef<Record<string, unknown>>({});
  const recordIdRef = useRef<string | undefined>(undefined);
  const serviceRef = useRef(service);
  serviceRef.current = service;

  // Updates made while the record is still loading are kept here and applied on top of the loaded values, so an
  // early click (e.g. the Header's Layout toggle) neither saves defaults over the stored record nor gets reset when
  // the load lands. Saves wait for the gate.
  const pendingRef = useRef<Partial<ITodoKanbanPreferences> | null>(userId ? {} : null);
  const loadGateRef = useRef<ILoadGate>(userId ? createLoadGate() : OPEN_GATE);
  // Saves run one at a time, each writing the latest state: two quick updates by a user with no record yet would
  // otherwise both find no record and create two.
  const saveChainRef = useRef<Promise<void>>(Promise.resolve());

  useEffect(() => {
    if (!userId) {
      pendingRef.current = null;
      loadGateRef.current.open();
      loadGateRef.current = OPEN_GATE;
      setIsLoading(false);
      return undefined;
    }
    let cancelled = false;
    const gate = loadGateRef.current === OPEN_GATE ? createLoadGate() : loadGateRef.current;
    loadGateRef.current = gate;
    pendingRef.current = pendingRef.current ?? {};
    setIsLoading(true);

    const finish = (loaded?: { preferences: Required<ITodoKanbanPreferences>; extra: Record<string, unknown> }) => {
      if (loaded) {
        extraRef.current = loaded.extra;
        preferencesRef.current = loaded.preferences;
      }
      if (pendingRef.current) {
        preferencesRef.current = {
          ...preferencesRef.current,
          ...pendingRef.current,
        } as Required<ITodoKanbanPreferences>;
        pendingRef.current = null;
      }
      setPreferences(preferencesRef.current);
      setIsLoading(false);
      gate.open();
    };

    serviceRef.current
      .getUserPreferences(userId, PREFERENCE_TYPE_TODO_KANBAN)
      .then(result => {
        if (cancelled) return;
        if (result.success && result.data.length > 0) {
          const record = result.data[0];
          recordIdRef.current = record.sprk_userpreferenceid;
          finish(parseTodoKanbanPreferences(record.sprk_preferencevalue));
        } else {
          finish();
        }
      })
      .catch(() => {
        if (cancelled) return;
        console.warn('[useTodoKanbanPreferences] Failed to fetch preferences, using defaults');
        finish();
      });
    return () => {
      cancelled = true;
      // A save waiting on this load proceeds with what it has; the next load opens a fresh gate.
      gate.open();
      loadGateRef.current = OPEN_GATE;
    };
  }, [userId]);

  const persist = useCallback((): Promise<void> => {
    const run = async (): Promise<void> => {
      try {
        const result = await serviceRef.current.saveUserPreferences(
          userId,
          PREFERENCE_TYPE_TODO_KANBAN,
          JSON.stringify({ ...extraRef.current, ...preferencesRef.current }),
          recordIdRef.current
        );
        if (result.success) {
          recordIdRef.current = result.data;
        } else {
          // The local state stays; the next save retries.
          console.error('[useTodoKanbanPreferences] Failed to save preferences:', result.error);
        }
      } catch (err) {
        console.error('[useTodoKanbanPreferences] Error saving preferences:', err);
      }
    };
    const next = saveChainRef.current.then(run, run);
    saveChainRef.current = next;
    return next;
  }, [userId]);

  const updatePreferences = useCallback(
    async (update: Partial<ITodoKanbanPreferences>) => {
      preferencesRef.current = { ...preferencesRef.current, ...update } as Required<ITodoKanbanPreferences>;
      setPreferences(preferencesRef.current);
      if (!userId) return;
      if (pendingRef.current) pendingRef.current = { ...pendingRef.current, ...update };
      await loadGateRef.current.promise;
      await persist();
    },
    [userId, persist]
  );

  return { preferences, updatePreferences, isLoading };
}
