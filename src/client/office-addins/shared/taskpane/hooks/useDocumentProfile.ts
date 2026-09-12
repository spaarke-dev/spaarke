import { useEffect, useState } from 'react';
import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '../utils/cleanGuid';
import { summaryStatusFromCode, type DocumentSummaryStatusName } from '../services/documentProfileChoices';

/**
 * useDocumentProfile.ts — spaarkeai-word-add-in-r1 task 021 (FR-07).
 *
 * Reads the four AI profile fields (`sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`,
 * `sprk_documenttype`) plus `sprk_filesummarystatus` for the `sprk_document` resolved by task 013,
 * via the EXISTING `GET /api/v1/documents/{id}` read (extended by this task to select + map those
 * five columns — no new BFF route; see task notes for the §11 grep evidence and the placement
 * decision).
 *
 * Deliberately does NOT call the BFF at all when `documentId` is absent (task 013 resolved no
 * identity, or hasn't resolved yet) — acceptance criterion "makes no profile read call" for the
 * no-identity case.
 */

/** @internal Response shape for `GET /api/v1/documents/{id}` (only the fields this hook reads). */
interface BffDocumentProfileEnvelope {
  data?: {
    summary?: string | null;
    tldr?: string | null;
    keywords?: string | null;
    documentType?: string | null;
    summaryStatus?: number | null;
  } | null;
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

/**
 * Reads the AI profile for the given resolved document id. Re-fetches whenever `documentId`
 * changes (e.g. task 022's Generate Profile flow re-triggers profiling and the caller re-mounts
 * or otherwise asks this hook to re-read — out of this task's scope, but the effect dependency
 * already supports it).
 */
export function useDocumentProfile(documentId: string | undefined): DocumentProfileOutcome {
  const [outcome, setOutcome] = useState<DocumentProfileOutcome>(
    documentId ? { kind: 'loading' } : { kind: 'no-identity' }
  );

  useEffect(() => {
    if (!documentId) {
      setOutcome({ kind: 'no-identity' });
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
  }, [documentId]);

  return outcome;
}
