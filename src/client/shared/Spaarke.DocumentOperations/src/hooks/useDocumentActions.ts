/**
 * useDocumentActions — Document-specific action handlers for command bar
 *
 * Actions:
 *   1. Open in Web: GET /api/documents/{id}/open-links → open webUrl
 *   2. Open in Desktop: GET /api/documents/{id}/open-links → open desktopUrl
 *   3. Download: GET /api/documents/{id}/download → browser download
 *   4. Delete: confirm → DELETE /api/documents/{id}
 *   5. Email a Link: GET open-links → compose mailto:
 *   6. Send to Index: POST /api/documents/{id}/analyze
 *
 * Originally lived in `src/client/code-pages/SemanticSearch/src/hooks/useDocumentActions.ts`.
 * Moved verbatim into @spaarke/document-operations by task 031 (spaarkeai-compose-r1)
 * so SemanticSearch (task 032) and Compose (task 033) share a single
 * source of truth for document operations.
 *
 * Smallest-possible-change pivot: the hook now accepts a `bffBaseUrl`
 * parameter rather than importing SemanticSearch's local
 * `services/apiBase.getBffBaseUrl`. This decouples the hook from
 * SemanticSearch internals without altering any of the six actions'
 * fetch shapes, payloads, error semantics, or return contract.
 *
 * @see ADR-013: All document operations go through BFF API
 * @see FileAccessEndpoints.cs — open-links endpoint
 * @see DocumentOperationsEndpoints.cs — analyze, delete endpoints
 */

import { useState, useCallback } from 'react';
import { authenticatedFetch, isApiError, isAuthFailure, problemOf } from '@spaarke/auth';

// =============================================
// Types
// =============================================

interface OpenLinksResponse {
  webUrl: string;
  desktopUrl?: string;
  mimeType: string;
  fileName: string;
}

export interface UseDocumentActionsOptions {
  /**
   * Resolved BFF base URL (host only, e.g. `https://host.azurewebsites.net`).
   * Callers supply this from their own runtime-config plumbing (e.g.
   * `getBffBaseUrl()` cached after `resolveRuntimeConfig()`).
   */
  bffBaseUrl: string;
}

export interface UseDocumentActionsResult {
  openInWeb: (documentId: string) => Promise<void>;
  openInDesktop: (documentId: string) => Promise<void>;
  download: (documentId: string) => Promise<void>;
  deleteDocuments: (documentIds: string[], onSuccess: () => void) => Promise<void>;
  emailLink: (documentId: string) => Promise<void>;
  sendToIndex: (documentIds: string[]) => Promise<void>;
  isActing: boolean;
  /**
   * The last failed action as a sentence a user can read — "Couldn't delete the document: <reason>" — or
   * null. Consumers show it in their own notification surface; it clears when the next action starts.
   */
  actionError: string | null;
}

// =============================================
// Failure text
// =============================================

/** The sentence for an HTTP failure the server did not explain (no ProblemDetails `detail`). */
function statusSentence(status: number): string | null {
  if (status === 401) return 'Your sign-in has expired. Refresh the page to sign in again.';
  if (status === 403) return 'You do not have permission to do this with this document.';
  if (status === 404) return 'The document was not found.';
  if (status === 409) return 'The document was changed at the same time by someone else. Refresh and try again.';
  if (status === 429) return 'Too many requests. Wait a moment and try again.';
  if (status >= 500) return 'The document service is temporarily unavailable. Try again in a few minutes.';
  return null;
}

/**
 * Why an action failed, for a user. `authenticatedFetch` never returns a non-2xx Response: it throws
 * `ApiError` (status + ProblemDetails) or, once its 401 retries are spent, `AuthError`. So the reason is
 * the server's `detail`, else a sentence for the status, else its `title` — never the bare ApiError
 * message ("HTTP 500") or the browser's "Failed to fetch".
 */
function failureReason(err: unknown): string {
  if (isApiError(err)) {
    const problem = problemOf(err);
    const detail = typeof problem?.detail === 'string' ? problem.detail.trim() : '';
    const title = typeof problem?.title === 'string' ? problem.title.trim() : '';
    return detail || statusSentence(err.status) || title || `The request failed (HTTP ${err.status}).`;
  }
  if (isAuthFailure(err)) return 'Your sign-in has expired. Refresh the page to sign in again.';
  if (err instanceof TypeError) return 'The Spaarke server could not be reached. Check your connection.';
  return 'Something went wrong. Try again.';
}

/** "<frame>: <reason>" — e.g. "Couldn't delete the document: The document was not found." */
function failureMessage(frame: string, err: unknown): string {
  return `${frame}: ${failureReason(err)}`;
}

/** "the document" / "2 documents" for the action frames. */
function documentsPhrase(count: number): string {
  return count === 1 ? 'the document' : `${count} documents`;
}

// =============================================
// Helpers
// =============================================

async function getDocumentLinks(bffBaseUrl: string, documentId: string): Promise<OpenLinksResponse> {
  const response = await authenticatedFetch(`${bffBaseUrl}/api/documents/${documentId}/open-links`);
  return response.json() as Promise<OpenLinksResponse>;
}

async function deleteDocument(bffBaseUrl: string, documentId: string): Promise<void> {
  await authenticatedFetch(`${bffBaseUrl}/api/documents/${documentId}`, { method: 'DELETE' });
}

async function analyzeDocument(bffBaseUrl: string, documentId: string): Promise<void> {
  await authenticatedFetch(`${bffBaseUrl}/api/documents/${documentId}/analyze`, {
    method: 'POST',
  });
}

// =============================================
// Hook
// =============================================

export function useDocumentActions(options: UseDocumentActionsOptions): UseDocumentActionsResult {
  const { bffBaseUrl } = options;
  const [isActing, setIsActing] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const openInWeb = useCallback(
    async (documentId: string) => {
      setIsActing(true);
      setActionError(null);
      try {
        const links = await getDocumentLinks(bffBaseUrl, documentId);
        window.open(links.webUrl, '_blank');
      } catch (err) {
        setActionError(failureMessage("Couldn't open the document", err));
      } finally {
        setIsActing(false);
      }
    },
    [bffBaseUrl]
  );

  const openInDesktop = useCallback(
    async (documentId: string) => {
      setIsActing(true);
      setActionError(null);
      try {
        const links = await getDocumentLinks(bffBaseUrl, documentId);
        if (links.desktopUrl) {
          window.open(links.desktopUrl);
        } else {
          // Fallback to web URL if no desktop protocol URL available
          window.open(links.webUrl, '_blank');
        }
      } catch (err) {
        setActionError(failureMessage("Couldn't open the document in the desktop app", err));
      } finally {
        setIsActing(false);
      }
    },
    [bffBaseUrl]
  );

  const download = useCallback(
    async (documentId: string) => {
      setIsActing(true);
      setActionError(null);
      try {
        const url = `${bffBaseUrl}/api/documents/${documentId}/download`;
        // Use a hidden link to trigger browser download
        const response = await authenticatedFetch(url);
        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = blobUrl;
        // Extract filename from Content-Disposition or use fallback
        const disposition = response.headers.get('Content-Disposition');
        const match = disposition?.match(/filename\*?=(?:UTF-8'')?["']?([^"';\n]+)/i);
        a.download = match?.[1] ?? `document-${documentId}`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(blobUrl);
      } catch (err) {
        setActionError(failureMessage("Couldn't download the document", err));
      } finally {
        setIsActing(false);
      }
    },
    [bffBaseUrl]
  );

  const deleteDocuments = useCallback(
    async (documentIds: string[], onSuccess: () => void) => {
      const count = documentIds.length;
      const confirmed = window.confirm(
        `Are you sure you want to delete ${count} document${count !== 1 ? 's' : ''}? This action cannot be undone.`
      );
      if (!confirmed) return;

      setIsActing(true);
      setActionError(null);
      try {
        await Promise.all(documentIds.map(id => deleteDocument(bffBaseUrl, id)));
        onSuccess();
      } catch (err) {
        setActionError(failureMessage(`Couldn't delete ${documentsPhrase(count)}`, err));
      } finally {
        setIsActing(false);
      }
    },
    [bffBaseUrl]
  );

  const emailLink = useCallback(
    async (documentId: string) => {
      setIsActing(true);
      setActionError(null);
      try {
        const links = await getDocumentLinks(bffBaseUrl, documentId);
        const subject = encodeURIComponent(`Document: ${links.fileName}`);
        const body = encodeURIComponent(`View this document:\n${links.webUrl}`);
        window.location.href = `mailto:?subject=${subject}&body=${body}`;
      } catch (err) {
        setActionError(failureMessage("Couldn't create the email link", err));
      } finally {
        setIsActing(false);
      }
    },
    [bffBaseUrl]
  );

  const sendToIndex = useCallback(
    async (documentIds: string[]) => {
      setIsActing(true);
      setActionError(null);
      try {
        await Promise.all(documentIds.map(id => analyzeDocument(bffBaseUrl, id)));
      } catch (err) {
        setActionError(failureMessage(`Couldn't send ${documentsPhrase(documentIds.length)} to the index`, err));
      } finally {
        setIsActing(false);
      }
    },
    [bffBaseUrl]
  );

  return {
    openInWeb,
    openInDesktop,
    download,
    deleteDocuments,
    emailLink,
    sendToIndex,
    isActing,
    actionError,
  };
}
