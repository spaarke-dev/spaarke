/**
 * Loads a `sprk_communication` record when an `id` is present. For `compose`
 * mode (no id) there is no record — the hook resolves immediately with a null
 * record so the layout switch defaults to the interactive email slot.
 */

import { useEffect, useState } from 'react';
import type { ICommunicationRecord } from '../types/communication';
import { readCommunicationRecord } from '../services/communicationRecord';

export type RecordLoadState =
  | { status: 'idle'; record: null; error: null }
  | { status: 'loading'; record: null; error: null }
  | { status: 'loaded'; record: ICommunicationRecord | null; error: null }
  | { status: 'error'; record: null; error: string };

export function useCommunicationRecord(id: string | undefined): RecordLoadState {
  const [state, setState] = useState<RecordLoadState>(
    id ? { status: 'loading', record: null, error: null } : { status: 'loaded', record: null, error: null }
  );

  useEffect(() => {
    if (!id) {
      setState({ status: 'loaded', record: null, error: null });
      return;
    }

    let cancelled = false;
    setState({ status: 'loading', record: null, error: null });

    readCommunicationRecord(id)
      .then(record => {
        if (!cancelled) setState({ status: 'loaded', record, error: null });
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          const message = err instanceof Error ? err.message : 'Failed to load the communication record.';
          setState({ status: 'error', record: null, error: message });
        }
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  return state;
}
