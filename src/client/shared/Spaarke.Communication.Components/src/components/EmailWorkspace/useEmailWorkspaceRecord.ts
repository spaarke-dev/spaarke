/**
 * useEmailWorkspaceRecord.ts
 *
 * Per-selection host data hook for `<EmailWorkspace />` (task 040). Fills the
 * gap between the reading-pane shell's `renderConnections`/`renderTracking`
 * slots (task 032 — presentational-only, "values in, callbacks out") and the
 * actual `sprk_communication` record: ONE `retrieveRecord` call (no `$select`,
 * mirroring `CommunicationConnectionsApp.tsx`) resolves the envelope/
 * association/tracking state those two sub-views need, plus the `.eml`
 * archive-document id the body slot (033) needs.
 *
 * Read/write field-name knowledge lives in `EmailWorkspace.mapping.ts` only —
 * this hook is a thin data-fetch/write orchestrator over it.
 */
import * as React from 'react';
import type { IDataService } from '@spaarke/ui-components';
import {
  COMMUNICATION_ENTITY,
  EMAIL_TRACKING_FIELDS,
  resolveEmlArchiveDocumentId,
  toWorkspaceRecordState,
  type EmailWorkspaceRecordState,
} from './EmailWorkspace.mapping';

export interface UseEmailWorkspaceRecordResult {
  /** Envelope/association/tracking state for the selected record; `null` while loading or on load failure. */
  recordState: EmailWorkspaceRecordState | null;
  /** Resolved `.eml` archive document id, or `null` when none exists (a normal state — task 033). */
  emlDocumentId: string | null;
  /** True while the initial read for this selection is in flight. */
  isLoading: boolean;
  /** True when the record read itself failed (drives `EmailBodyView`'s `recordLoadError` state). */
  recordLoadError: boolean;
  /** Re-read the record — called after any successful associations write (`onAssociationsChanged`). */
  reload: () => void;
  /** Retry the whole read after a failure. */
  retry: () => void;
  updateMonitor: (value: boolean) => Promise<void>;
  updateHighPriority: (value: boolean) => Promise<void>;
  updateAccessPermission: (value: number) => Promise<void>;
}

/**
 * @param dataService Host-injected structural data adapter (ADR-012).
 * @param communicationId The shell's currently selected id, or `undefined` when nothing is selected.
 */
export function useEmailWorkspaceRecord(
  dataService: IDataService,
  communicationId: string | undefined
): UseEmailWorkspaceRecordResult {
  const [recordState, setRecordState] = React.useState<EmailWorkspaceRecordState | null>(null);
  const [emlDocumentId, setEmlDocumentId] = React.useState<string | null>(null);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [recordLoadError, setRecordLoadError] = React.useState<boolean>(false);
  const [reloadKey, setReloadKey] = React.useState(0);

  React.useEffect(() => {
    if (!communicationId) {
      setRecordState(null);
      setEmlDocumentId(null);
      setIsLoading(false);
      setRecordLoadError(false);
      return;
    }
    let cancelled = false;
    setIsLoading(true);
    setRecordLoadError(false);

    void (async () => {
      try {
        const [raw, docId] = await Promise.all([
          dataService.retrieveRecord(COMMUNICATION_ENTITY, communicationId),
          resolveEmlArchiveDocumentId(dataService, communicationId),
        ]);
        if (cancelled) return;
        setRecordState(toWorkspaceRecordState(raw));
        setEmlDocumentId(docId);
      } catch (err) {
        if (cancelled) return;
        console.error('[EmailWorkspace] record read failed:', err);
        setRecordState(null);
        setEmlDocumentId(null);
        setRecordLoadError(true);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- dataService is a stable host-supplied adapter
  }, [communicationId, reloadKey]);

  const reload = React.useCallback(() => setReloadKey(k => k + 1), []);
  const retry = reload;

  const updateField = React.useCallback(
    async (field: string, value: boolean | number) => {
      if (!communicationId) return;
      await dataService.updateRecord(COMMUNICATION_ENTITY, communicationId, { [field]: value });
      reload();
    },
    [communicationId, dataService, reload]
  );

  const updateMonitor = React.useCallback(
    (value: boolean) => updateField(EMAIL_TRACKING_FIELDS.monitor, value),
    [updateField]
  );
  const updateHighPriority = React.useCallback(
    (value: boolean) => updateField(EMAIL_TRACKING_FIELDS.highPriority, value),
    [updateField]
  );
  const updateAccessPermission = React.useCallback(
    (value: number) => updateField(EMAIL_TRACKING_FIELDS.accessPermission, value),
    [updateField]
  );

  return {
    recordState,
    emlDocumentId,
    isLoading,
    recordLoadError,
    reload,
    retry,
    updateMonitor,
    updateHighPriority,
    updateAccessPermission,
  };
}
