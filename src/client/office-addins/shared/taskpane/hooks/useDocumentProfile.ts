import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '@spaarke/ui-components/guid';
import { summaryStatusFromCode, type DocumentSummaryStatusName } from '../services/documentProfileChoices';

/**
 * useDocumentProfile.ts — spaarkeai-word-add-in-r1 task 021 (FR-07) + task 022 (FR-08) + task 033
 * (FR-16b, `searchIndexed`/`searchIndexName`).
 *
 * Reads the four AI profile fields (`sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`,
 * `sprk_documenttype`) plus `sprk_filesummarystatus` for the `sprk_document` resolved by task 013,
 * via the EXISTING `GET /api/v1/documents/{id}` read (task 021 — no new BFF route for the read).
 *
 * Deliberately does NOT call the BFF at all when `documentId` is absent (task 013 resolved no
 * identity, or hasn't resolved yet) — acceptance criterion "makes no profile read call" for the
 * no-identity case.
 *
 * Task 022 adds `generateProfile()`: POSTs `/api/office/documents/{id}/generate-profile` (202 Accepted,
 * unconditional overwrite, no confirmation). Since task 068 the 202 means a profile job is on the job
 * queue, where it survives a server restart, and that job records `sprk_filesummarystatus` Pending →
 * Completed or Failed. On a 202 the displayed status moves to Pending immediately and the hook then
 * re-reads the record every PROFILE_POLL_INTERVAL_MS, per task 021's SAME status mapping — no second
 * status table — until the status leaves Pending or PROFILE_POLL_CAP_MS passes (task 137, #1090). The
 * server writes Pending before it answers 202, so no read in that window shows the status from before
 * the click. The wait ends on unmount or a document change; at the cap the last status stays, never a
 * made-up Completed.
 *
 * Task 033 (§11 decision, recorded in `notes/033-find-index-gating-decisions.md`): the Find tab's
 * three-state gate needs `sprk_searchindexed` (and, for display, `sprk_searchindexname`) for the
 * SAME resolved document this hook already reads. `DataverseServiceClientImpl.GetDocumentAsync`
 * already selects `sprk_searchindexed`/`sprk_searchindexname` in its ColumnSet, and
 * `GET /api/v1/documents/{id}` already serializes the full `DocumentEntity` (not a profile-only
 * projection) — so both fields were ALREADY present on every response this hook receives; only the
 * TypeScript envelope and the returned result were missing them. No BFF change was needed. `FindView`
 * reads `searchIndexed` from this hook rather than adding a second call to the same route. `refetch()`
 * is new too — `FindView` calls it after Run Index completes, to re-poll without waiting for
 * `documentId` to change (it doesn't).
 */

/** @internal Response shape for `GET /api/v1/documents/{id}` (only the fields this hook reads). */
interface BffDocumentProfileEnvelope {
  data?: {
    summary?: string | null;
    tldr?: string | null;
    keywords?: string | null;
    documentType?: string | null;
    summaryStatus?: number | null;
    /** task 033: `sprk_searchindexed` — a Dataverse BIT (true / false / null). */
    searchIndexed?: boolean | null;
    /** task 033: `sprk_searchindexname`, for display only — never written by the add-in. */
    searchIndexName?: string | null;
  } | null;
}

/** How often the pane re-reads the document while a Generate Profile job runs (task 137, #1090). */
export const PROFILE_POLL_INTERVAL_MS = 3_000;

/** How long it keeps re-reading: a profile takes 8–12 s (p90 12.3 s), so this is several times that. */
export const PROFILE_POLL_CAP_MS = 60_000;

/** @internal Response shape for `POST /api/office/documents/{id}/generate-profile`. */
interface GenerateProfileResponse {
  documentId?: string;
  correlationId?: string;
}

export interface DocumentProfileFields {
  summary: string;
  tldr: string;
  keywords: string;
  documentType: string;
}

/**
 * Outcome of a document-profile read. Every branch is a defined, honest state — never blank
 * fields with no explanation (spec FR-07's acceptance criterion).
 */
export type DocumentProfileOutcome =
  | { kind: 'no-identity' }
  | { kind: 'loading' }
  | ({ kind: 'completed' } & DocumentProfileFields)
  | { kind: 'status'; status: Exclude<DocumentSummaryStatusName, 'Completed'> }
  | { kind: 'error'; message: string };

export interface UseDocumentProfileResult {
  /** The current read outcome — unchanged shape/semantics from task 021. */
  outcome: DocumentProfileOutcome;
  /**
   * Dispatches the FR-08 Generate Profile trigger. No-op (resolves immediately, issues no network
   * request) when `documentId` is absent — the caller (DocumentProfileSection) also disables the
   * button in that state, so this is defense-in-depth, not the only guard.
   *
   * Resolves `true` when the server ACCEPTED the request (202 — the profile job is queued), `false` when it
   * was refused or never sent (task 088: the Save tab's saved state re-enables "Save" only on `true`).
   */
  generateProfile: () => Promise<boolean>;
  /** True while the POST is in flight (button busy state). */
  isGenerating: boolean;
  /** Set when the trigger POST itself fails (network/auth/validation) — distinct from a profile
   * that ran and failed (that is `outcome.status === 'Failed'`, a normal read state). */
  generateError: string | null;
  /**
   * task 033 (FR-16b): the tri-state `sprk_searchindexed` BIT for the resolved document — `true`,
   * `false`, or `null` (never indexed). `undefined` while `documentId` is absent or the read is still
   * in flight — callers MUST NOT treat `undefined` the same as `null`; `FindView` gates on this
   * distinction so it never flashes the "not indexed" state before the read has actually returned.
   */
  searchIndexed: boolean | null | undefined;
  /** task 033: `sprk_searchindexname`, for display only. Same availability rule as `searchIndexed`. */
  searchIndexName: string | null | undefined;
  /**
   * task 033: re-runs the SAME `GET /api/v1/documents/{id}` read this hook already performs, without
   * requiring `documentId` to change. `FindView` calls this after Run Index completes to re-poll
   * `sprk_searchindexed` — the add-in never writes that field itself; it reads and re-polls.
   */
  refetch: () => void;
}

/**
 * Reads the AI profile for the given resolved document id, and exposes the FR-08 Generate Profile
 * trigger. Re-fetches whenever `documentId` changes, or after a successful `generateProfile()` call.
 */
export function useDocumentProfile(documentId: string | undefined): UseDocumentProfileResult {
  const [outcome, setOutcome] = useState<DocumentProfileOutcome>(
    documentId ? { kind: 'loading' } : { kind: 'no-identity' }
  );
  const [refreshToken, setRefreshToken] = useState(0);
  // The document whose profile job the pane is following after a 202; `run` counts the clicks so a second Generate
  // Profile restarts the wait. Undefined until a request is accepted.
  const [followed, setFollowed] = useState<{ documentId: string; run: number } | undefined>(undefined);
  // The document the pane shows right now, for a Generate Profile request that returns after the user moved on.
  const shownDocumentId = useRef(documentId);
  const [isGenerating, setIsGenerating] = useState(false);
  const [generateError, setGenerateError] = useState<string | null>(null);
  // task 033: undefined until the FIRST successful read returns (or documentId is absent) — kept
  // separate from `outcome` because searchIndexed is meaningful regardless of the profile's own
  // summary-status branch (loading/completed/status/error all still carry a real DocumentEntity).
  const [searchIndexed, setSearchIndexed] = useState<boolean | null | undefined>(undefined);
  const [searchIndexName, setSearchIndexName] = useState<string | null | undefined>(undefined);

  // Shows one successful read of the document and returns the status it carried. Shared by the first read and the
  // follow-up polling below, so both map a status the same way (task 021's mapping — no second status table).
  const applyRead = useCallback(
    (data: BffDocumentProfileEnvelope['data']): DocumentSummaryStatusName | 'Unrecognized' => {
      const statusName = summaryStatusFromCode(data?.summaryStatus);

      // task 033: set on EVERY successful read, ahead of the summary-status branches below, so a
      // still-pending/failed/opted-out profile does not leave Find's index state stuck undefined.
      setSearchIndexed(data?.searchIndexed ?? null);
      setSearchIndexName(data?.searchIndexName ?? null);

      if (statusName === 'Unrecognized') {
        // Escalation trigger (task 021 POML): an option value outside the seven enumerated
        // states means the choice column changed underneath this closed acceptance set. Fail
        // honestly rather than guessing which bucket it belongs in.
        setOutcome({
          kind: 'error',
          message: 'This document has an unrecognized profile status. Contact support.',
        });
        return statusName;
      }

      if (statusName === 'Completed') {
        setOutcome({
          kind: 'completed',
          summary: data?.summary ?? '',
          tldr: data?.tldr ?? '',
          keywords: data?.keywords ?? '',
          documentType: data?.documentType ?? '',
        });
        return statusName;
      }

      setOutcome({ kind: 'status', status: statusName });
      return statusName;
    },
    []
  );

  useEffect(() => {
    if (!documentId) {
      setOutcome({ kind: 'no-identity' });
      setSearchIndexed(undefined);
      setSearchIndexName(undefined);
      return;
    }

    let cancelled = false;
    setOutcome({ kind: 'loading' });

    (async () => {
      try {
        const id = cleanGuid(documentId);
        const response = await apiClient.get<BffDocumentProfileEnvelope>(`/api/v1/documents/${id}`);
        if (cancelled) return;

        applyRead(response.data);
      } catch (err) {
        if (cancelled) return;
        const message =
          err instanceof ApiClientError
            ? err.error.detail || err.error.title
            : err instanceof Error
              ? err.message
              : 'Failed to load the document profile.';
        setOutcome({ kind: 'error', message });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [documentId, refreshToken, applyRead]);

  // A different document ends the wait for the previous one for good (coming back to it does not resume it).
  useEffect(() => {
    shownDocumentId.current = documentId;
    setFollowed(undefined);
  }, [documentId]);

  // Follows a profile job to its end (task 137, #1090). Runs only after a 202 for the SAME document, re-reads every
  // PROFILE_POLL_INTERVAL_MS WITHOUT the "loading" flash the first read has, and stops when the status leaves Pending,
  // at PROFILE_POLL_CAP_MS, on unmount, or when the document changes (the cleanup below; `followed` no longer matches).
  // The server writes Pending before it answers 202, so a read here never returns the status from before the click.
  useEffect(() => {
    if (!documentId || !followed || followed.documentId !== documentId) {
      return;
    }

    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const startedAt = Date.now();
    const id = cleanGuid(documentId);

    const poll = async (): Promise<void> => {
      try {
        const response = await apiClient.get<BffDocumentProfileEnvelope>(`/api/v1/documents/${id}`);
        if (cancelled) return;
        if (applyRead(response.data) !== 'Pending') {
          return; // the job's own result (or a state that needs no waiting): shown, done
        }
      } catch {
        // A failed re-read keeps the last status on screen (Pending) and tries again until the cap. It must not turn
        // a running job into an error, and it must never show Completed.
        if (cancelled) return;
      }

      if (Date.now() - startedAt >= PROFILE_POLL_CAP_MS) {
        return; // the cap: the last status stays, which is Pending, never a made-up Completed
      }
      timer = setTimeout(poll, PROFILE_POLL_INTERVAL_MS);
    };

    timer = setTimeout(poll, PROFILE_POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      if (timer !== undefined) {
        clearTimeout(timer);
      }
    };
  }, [documentId, followed, applyRead]);

  const generateProfile = useCallback(async (): Promise<boolean> => {
    if (!documentId) {
      // Defense-in-depth — the caller disables the control in this state so this path should be
      // unreachable in practice, but generateProfile() must never issue a network call without an id.
      return false;
    }

    setIsGenerating(true);
    setGenerateError(null);

    try {
      const id = cleanGuid(documentId);
      await apiClient.post<GenerateProfileResponse>(`/api/office/documents/${id}/generate-profile`);

      // Task 021 semantics: no second status table. Move the DISPLAYED state to Pending immediately
      // (the 202 confirms dispatch, not completion), then follow the job: the effect above re-reads until the
      // status leaves Pending, so the pane shows what the background profile actually lands on (#1090). Only for the
      // document the pane still shows: the user may have moved to another item while the request was in flight, and
      // that item's own read is what its screen must keep.
      if (shownDocumentId.current === documentId) {
        setOutcome({ kind: 'status', status: 'Pending' });
        setFollowed(previous => ({ documentId, run: (previous?.run ?? 0) + 1 }));
      }
      return true;
    } catch (err) {
      const message =
        err instanceof ApiClientError
          ? err.error.detail || err.error.title
          : err instanceof Error
            ? err.message
            : 'Failed to start profiling.';
      setGenerateError(message);
      return false;
    } finally {
      setIsGenerating(false);
    }
  }, [documentId]);

  // task 033: same mechanism generateProfile() already uses internally (bump refreshToken to re-run
  // the effect above) — exposed publicly so FindView can re-poll sprk_searchindexed after Run Index
  // without documentId having changed. A no-op when there is no identity to re-read.
  const refetch = useCallback(() => {
    if (!documentId) {
      return;
    }
    setRefreshToken(token => token + 1);
  }, [documentId]);

  return { outcome, generateProfile, isGenerating, generateError, searchIndexed, searchIndexName, refetch };
}
