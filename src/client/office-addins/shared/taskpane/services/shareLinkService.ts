import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '../utils/cleanGuid';

/**
 * shareLinkService.ts — the add-in's ONE share-link minter, called by Send Email (`sendEmailService.ts`, task 036,
 * FR-15). The Word ribbon's Share command (task 037, FR-17) was its other caller until task 089 (UAT-10) replaced
 * that button with "Open Spaarke"; this service stays because Send Email needs it.
 *
 * Task 037 wrote it as a duplicate of a private copy in `sendEmailService.ts`, because its wave forbade
 * touching that file; task 075 removed the copy and pointed Send Email here.
 *
 * The route itself (`POST /api/documents/{documentId}/share-link`, `FileAccessEndpoints.cs`) is
 * EXISTING, shipped server infrastructure — calling it is ordinary client reuse, not new BFF surface,
 * so root CLAUDE.md §10 (BFF hygiene) placement questions do not apply here.
 */

/** @internal Response shape from `POST /api/documents/{documentId}/share-link`. */
interface ShareLinkResponseBody {
  url: string;
  expiresAt: string;
  scope: string;
}

export type MintShareLinkResult = { ok: true; url: string } | { ok: false; message: string };

function shareLinkEndpoint(documentId: string): string {
  return `/api/documents/${documentId}/share-link`;
}

/**
 * Mint a document's recipient-openable share link via the EXISTING share-link route, at its existing
 * expiry policy. No request body is sent — omitting `allowExternalRecipients` keeps the route's
 * default (organization-scoped) behavior. `documentId` is canonicalized via `cleanGuid` (ADR-044)
 * before it reaches the URL path segment.
 */
export async function mintDocumentShareLink(documentId: string): Promise<MintShareLinkResult> {
  const id = cleanGuid(documentId);
  if (!id) {
    return { ok: false, message: 'No document id was available to share.' };
  }

  try {
    const response = await apiClient.post<ShareLinkResponseBody>(shareLinkEndpoint(id));
    return { ok: true, url: response.url };
  } catch (err) {
    if (err instanceof ApiClientError) {
      const status = err.error.status;
      if (status === 401 || status === 403) {
        return { ok: false, message: "You don't have permission to share this document." };
      }
      return { ok: false, message: err.error.detail || err.message || 'Could not create the share link.' };
    }
    return { ok: false, message: err instanceof Error ? err.message : 'Could not create the share link.' };
  }
}
