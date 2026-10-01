import { useCallback, useEffect, useState } from 'react';
import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '../utils/cleanGuid';
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
 * Task 022 adds `generateProfile()`: POSTs the new `/api/office/documents/{id}/generate-profile`
 * trigger (fire-and-forget, 202 Accepted, mirrors Compose's shipped `refresh-profile` semantics —
 * unconditional overwrite, no confirmation). On a 202 the displayed status moves to Pending
 * immediately and the hook re-reads the record, per task 021's SAME status mapping — no second
 * status table.
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
   */
  generateProfile: () => Promise<void>;
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
  const [isGenerating, setIsGenerating] = useState(false);
  const [generateError, setGenerateError] = useState<string | null>(null);
  // task 033: undefined until the FIRST successful read returns (or documentId is absent) — kept
  // separate from `outcome` because searchIndexed is meaningful regardless of the profile's own
  // summary-status branch (loading/completed/status/error all still carry a real DocumentEntity).
  const [searchIndexed, setSearchIndexed] = useState<boolean | null | undefined>(undefined);
  const [searchIndexName, setSearchIndexName] = useState<string | null | undefined>(undefined);

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

        const data = response.data;
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
          return;
        }

        if (statusName === 'Completed') {
          setOutcome({
            kind: 'completed',
            summary: data?.summary ?? '',
            tldr: data?.tldr ?? '',
            keywords: data?.keywords ?? '',
            documentType: data?.documentType ?? '',
          });
          return;
        }

        setOutcome({ kind: 'status', status: statusName });
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
  }, [documentId, refreshToken]);

  const generateProfile = useCallback(async (): Promise<void> => {
    if (!documentId) {
      // Defense-in-depth — the caller disables the control in this state so this path should be
      // unreachable in practice, but generateProfile() must never issue a network call without an id.
      return;
    }

    setIsGenerating(true);
    setGenerateError(null);

    try {
      const id = cleanGuid(documentId);
      await apiClient.post<GenerateProfileResponse>(`/api/office/documents/${id}/generate-profile`);

      // Task 021 semantics: no second status table. Move the DISPLAYED state to Pending immediately
      // (the 202 confirms dispatch, not completion), then re-read so the pane eventually reflects
      // whatever status the background profile actually lands on.
      setOutcome({ kind: 'status', status: 'Pending' });
      setRefreshToken(token => token + 1);
    } catch (err) {
      const message =
        err instanceof ApiClientError
          ? err.error.detail || err.error.title
          : err instanceof Error
            ? err.message
            : 'Failed to start profiling.';
      setGenerateError(message);
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
