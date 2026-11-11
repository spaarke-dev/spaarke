/**
 * useLaneOpen - whether a worklist lane's card list is open, remembered per device (D-195).
 *
 * The list starts COLLAPSED: with no stored value, or when browser storage is unavailable, the lane is closed. The open state of
 * both lanes lives under ONE namespaced localStorage key as a JSON object (`{"Decide":true,"Do":false}`). Every read and write is
 * wrapped in try/catch: a storage failure never breaks the panel, the in-memory state still toggles, and only the persistence is lost.
 *
 * The shared library cannot import from the SpaarkeAi solution, so this copies the shape of that solution's `usePaneCollapse`
 * (localStorage, per device, try/catch around every access).
 *
 * Task: spaarke-ontology-platform-r1, task 169.
 */

import * as React from 'react';
import type { WorklistLane } from './types';

/** The one localStorage key that holds both lanes' open state. */
export const LANE_OPEN_STORAGE_KEY = 'spaarke.worklist.laneOpen';

function readAll(): Partial<Record<WorklistLane, boolean>> {
  try {
    const raw = window.localStorage.getItem(LANE_OPEN_STORAGE_KEY);
    if (raw === null) return {};
    const parsed: unknown = JSON.parse(raw);
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) return {};
    const out: Partial<Record<WorklistLane, boolean>> = {};
    for (const lane of ['Decide', 'Do'] as const) {
      const v = (parsed as Record<string, unknown>)[lane];
      if (typeof v === 'boolean') out[lane] = v;
    }
    return out;
  } catch {
    return {};
  }
}

function writeLane(lane: WorklistLane, open: boolean): void {
  try {
    // Read the stored value fresh so the other lane's state is kept, not overwritten with this hook's stale copy.
    window.localStorage.setItem(LANE_OPEN_STORAGE_KEY, JSON.stringify({ ...readAll(), [lane]: open }));
    // eslint-disable-next-line no-restricted-syntax -- D-195: persistence is best effort; a storage failure (private mode, quota, blocked site data) keeps the in-memory state and must not break the panel.
  } catch {
    // Best-effort persistence: silent no-op on a storage failure.
  }
}

export function useLaneOpen(lane: WorklistLane): readonly [boolean, (open: boolean) => void] {
  const [open, setOpenState] = React.useState<boolean>(() => readAll()[lane] === true);
  const setOpen = React.useCallback(
    (next: boolean) => {
      setOpenState(next);
      writeLane(lane, next);
    },
    [lane]
  );
  return [open, setOpen] as const;
}
