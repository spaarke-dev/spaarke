import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchReferenceList, type ReferenceChoice, type ReferenceListName } from '../services/referenceListService';
import { fetchDefaultAssignee, type DefaultAssignee } from '../services/quickCreateDefaultsService';

/**
 * useCreateRecordFormData — the data the "+ New" create form needs (task 100, owner UAT round 5 item 3):
 * the three reference lists (matter types, practice areas, project types) and the Assigned To prefill.
 *
 * Loaded LAZILY, the first time `enabled` becomes true (the user opened "+ New"), then kept for the pane's lifetime —
 * a user who only files to existing records costs no extra requests. Each list is load-once and cached by
 * `referenceListService` (~24 h, non-empty results only), so after the first open the lists are instant.
 *
 * A failed list load and "loaded, zero active rows" are different states (task 038's coordinator fix, kept for every
 * list): a failure sets `error` (announced, NFR-11) and offers `retry`; the form shows it and never leaves a required
 * field silently empty. A failed prefill is NOT an error the user must act on — Assigned To is optional — so it
 * resolves to "no prefill".
 */

/** One reference list, as the form renders it. */
export interface ReferenceListState {
  options: ReferenceChoice[];
  loading: boolean;
  /** A readable message when the list failed to load (distinct from an empty list). */
  error: string | null;
  /** Re-fetches the list (the Retry action). */
  retry: () => void;
}

export interface CreateRecordFormData {
  matterTypes: ReferenceListState;
  practiceAreas: ReferenceListState;
  projectTypes: ReferenceListState;
  /** The user's own linked contact — the Assigned To prefill — or null (none linked, or unavailable). */
  defaultAssignee: DefaultAssignee | null;
}

export interface UseCreateRecordFormDataOptions {
  apiBaseUrl?: string;
  getAccessToken?: () => Promise<string>;
  /** Load once this first becomes true (the "+ New" form opened). */
  enabled: boolean;
  /** The browser test harness: serve the demo lists and no prefill, never the BFF. */
  demo?: boolean;
  /** Announces a load failure (NFR-11). */
  announce?: (message: string, politeness: 'polite' | 'assertive') => void;
}

/** Plural noun per list, for the loading/empty/error copy. */
export const REFERENCE_LIST_PLURAL: Record<ReferenceListName, string> = {
  'matter-types': 'matter types',
  'practice-areas': 'practice areas',
  'project-types': 'project types',
};

/** The failure message for a list — task 038's matter-type copy, generalized. */
export function referenceListErrorMessage(list: ReferenceListName): string {
  return `Couldn't load ${REFERENCE_LIST_PLURAL[list]}. Try again, or search for an existing record instead.`;
}

/**
 * Demo rows for the browser test harness ONLY — mirror the dev lists (matter types: task 038's five rows; practice
 * areas and project types: live dev rows, 2026-10-05) so the form's UX is iterable without the BFF.
 */
const DEMO_LISTS: Record<ReferenceListName, ReferenceChoice[]> = {
  'matter-types': [
    { id: '6cedd99b-30da-f011-8406-7ced8d1dc988', name: 'Commercial', code: 'CMRCL' },
    { id: 'cdbf53b0-30da-f011-8406-7ced8d1dc988', name: 'Employment', code: 'EMPL' },
    { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
    { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
    { id: '60c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Trademark', code: 'TMRK' },
  ],
  'practice-areas': [
    { id: 'b41377db-690e-f111-8342-7c1e520aa4df', name: 'Appellate', code: 'APPL' },
    { id: 'b371e9c1-690e-f111-8342-7ced8d1dc988', name: 'Banking & Finance', code: 'BNKF' },
    { id: 'f6523f00-6a0e-f111-8342-7ced8d1dc988', name: 'Commercial Transactions', code: 'CTRNS' },
  ],
  'project-types': [
    { id: '3f71fcb2-b018-f111-8343-7ced8d1dc988', name: 'Intellectual Property - Patent' },
    { id: '0ed9d8ac-b018-f111-8343-7ced8d1dc988', name: 'Litigation' },
  ],
};

function useReferenceList(
  list: ReferenceListName,
  { apiBaseUrl, getAccessToken, enabled, demo = false, announce }: UseCreateRecordFormDataOptions
): ReferenceListState {
  const [options, setOptions] = useState<ReferenceChoice[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const mountedRef = useRef(true);
  const startedRef = useRef(false);
  useEffect(
    () => () => {
      mountedRef.current = false;
    },
    []
  );

  const load = useCallback(async () => {
    if (demo) {
      setOptions(DEMO_LISTS[list]);
      setError(null);
      return;
    }
    if (!apiBaseUrl || !getAccessToken) return;
    setLoading(true);
    setError(null);
    try {
      const token = await getAccessToken();
      const items = await fetchReferenceList(list, apiBaseUrl, token, getAccessToken);
      if (!mountedRef.current) return;
      setOptions(items);
    } catch {
      if (!mountedRef.current) return;
      const message = referenceListErrorMessage(list);
      setError(message);
      // NFR-11: the one state change here a screen-reader user could otherwise miss (the field just stays a
      // disabled, empty-looking dropdown).
      announce?.(message, 'assertive');
    } finally {
      if (mountedRef.current) setLoading(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- apiBaseUrl/getAccessToken are stable for the pane's lifetime; announce is stable per useAnnounce
  }, [list, demo, apiBaseUrl, getAccessToken]);

  useEffect(() => {
    if (!enabled || startedRef.current) return;
    startedRef.current = true;
    void load();
  }, [enabled, load]);

  const retry = useCallback(() => {
    void load();
  }, [load]);

  return { options, loading, error, retry };
}

export function useCreateRecordFormData(options: UseCreateRecordFormDataOptions): CreateRecordFormData {
  const matterTypes = useReferenceList('matter-types', options);
  const practiceAreas = useReferenceList('practice-areas', options);
  const projectTypes = useReferenceList('project-types', options);

  const { apiBaseUrl, getAccessToken, enabled, demo = false } = options;
  const [defaultAssignee, setDefaultAssignee] = useState<DefaultAssignee | null>(null);
  const assigneeStartedRef = useRef(false);
  const mountedRef = useRef(true);
  useEffect(
    () => () => {
      mountedRef.current = false;
    },
    []
  );
  useEffect(() => {
    if (!enabled || assigneeStartedRef.current || demo || !apiBaseUrl || !getAccessToken) return;
    assigneeStartedRef.current = true;
    // Load-once, guarded by the MOUNT, not by this effect's cleanup: the host's `getAccessToken` may be a new function
    // on every render (App passes an inline arrow), and a cleanup-scoped flag would discard the one request made.
    void (async () => {
      try {
        const token = await getAccessToken();
        const assignee = await fetchDefaultAssignee(apiBaseUrl, token, getAccessToken);
        if (mountedRef.current) setDefaultAssignee(assignee);
      } catch {
        // Optional field: an unavailable prefill leaves Assigned To empty and fully usable — nothing to announce.
      }
    })();
  }, [enabled, demo, apiBaseUrl, getAccessToken]);

  return { matterTypes, practiceAreas, projectTypes, defaultAssignee };
}
