/**
 * useWorklistData - loads the worklist's sets from the Signal read route (task 038) and keeps them.
 *
 * `lanes` and `closedToday` load on mount (the lanes are the list; closed today feeds each lane's "n of m done today"). The
 * `suppressed` and `closedEarlier` sets load the first time their disclosure opens: each is a full route read, and nobody
 * needs them until they look. A reload keeps the data on screen until the new answer arrives, and an answer that is no longer the
 * latest request for its set is ignored. Nothing is persisted: a new mount starts from `lanes` with no filter (HANDOFF section 1.4 L123).
 *
 * Task: spaarke-ontology-platform-r1, task 059.
 */

import * as React from 'react';
import { WorklistRequestError } from './worklistService';
import type { WorklistData, WorklistService, WorklistSetName } from './worklistService';

export interface WorklistSetState {
  status: 'idle' | 'loading' | 'ready' | 'error';
  /** The last good answer, kept while a reload is in flight or after a reload failed. */
  data?: WorklistData;
  error?: string;
  /** The HTTP status of the failed read, when an answer arrived (null otherwise). */
  httpStatus?: number | null;
}

export type WorklistSets = Readonly<Record<WorklistSetName, WorklistSetState>>;

const IDLE: WorklistSetState = { status: 'idle' };
const INITIAL: WorklistSets = { lanes: IDLE, closedToday: IDLE, suppressed: IDLE, closedEarlier: IDLE };

const messageOf = (err: unknown): string =>
  err instanceof Error && err.message ? err.message : 'The worklist could not be read.';

export interface UseWorklistData {
  sets: WorklistSets;
  /** The latest sets, readable from an async path without waiting for a render. */
  latest: React.MutableRefObject<WorklistSets>;
  /** Load a secondary set if it has not been asked for yet. */
  ensure: (set: WorklistSetName) => void;
  /** Read a set again (the "Try again" button). */
  retry: (set: WorklistSetName) => void;
  /** Read every set that has been asked for again (after the wizard recorded something). */
  reload: () => void;
}

export function useWorklistData(service: WorklistService): UseWorklistData {
  const [sets, setSets] = React.useState<WorklistSets>(INITIAL);
  const latest = React.useRef<WorklistSets>(INITIAL);
  const requests = React.useRef<Record<WorklistSetName, number>>({
    lanes: 0,
    closedToday: 0,
    suppressed: 0,
    closedEarlier: 0,
  });
  const alive = React.useRef(true);

  const commit = React.useCallback((set: WorklistSetName, next: WorklistSetState) => {
    latest.current = { ...latest.current, [set]: next };
    setSets(latest.current);
  }, []);

  const load = React.useCallback(
    (set: WorklistSetName) => {
      requests.current[set] += 1;
      const mine = requests.current[set];
      commit(set, { status: 'loading', data: latest.current[set].data });
      service.read(set).then(
        data => {
          if (alive.current && requests.current[set] === mine) commit(set, { status: 'ready', data });
        },
        err => {
          if (alive.current && requests.current[set] === mine) {
            commit(set, {
              status: 'error',
              data: latest.current[set].data,
              error: messageOf(err),
              httpStatus: err instanceof WorklistRequestError ? err.httpStatus : null,
            });
          }
        }
      );
    },
    [service, commit]
  );

  React.useEffect(() => {
    alive.current = true;
    load('lanes');
    load('closedToday');
    return () => {
      alive.current = false;
    };
  }, [load]);

  const ensure = React.useCallback(
    (set: WorklistSetName) => {
      if (latest.current[set].status === 'idle') load(set);
    },
    [load]
  );

  const reload = React.useCallback(() => {
    for (const set of ['lanes', 'closedToday', 'suppressed', 'closedEarlier'] as const) {
      if (set === 'lanes' || set === 'closedToday' || latest.current[set].status !== 'idle') load(set);
    }
  }, [load]);

  return { sets, latest, ensure, retry: load, reload };
}
