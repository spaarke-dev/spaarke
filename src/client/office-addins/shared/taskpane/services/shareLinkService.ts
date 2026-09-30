import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '../utils/cleanGuid';

/**
 * shareLinkService.ts — spaarkeai-word-add-in-r1 task 037 (FR-17): the `shareDocument` Word ribbon
 * command's link-minting call.
 *
 * **Component justification (root CLAUDE.md §11).** `sendEmailService.ts` (task 036, FR-15) already
 * has a private `mintDocumentShareLink` that calls the SAME route this file calls. This file does NOT
 * import or extend it, and that is a deliberate, narrow exception, not an oversight:
 *
 * 1. **Existing** — `sendEmailService.mintDocumentShareLink` does exactly what this file needs.
 * 2. **Extension** — task 037's own POML places it in wave `P3-c` alongside sibling task 036 and
 *    binds: *"Do not modify … the share-link/send-email path — those belong to siblings in this
 *    wave."* `sendEmailService.ts` IS the send-email path (its own header names task 036). Exporting
 *    the existing private function would still be an edit to that file, so extension is unavailable
 *    under this task's own constraint — not a technical limitation.
 * 3. **Cost of doing nothing** — without a mint call, `shareDocument` cannot produce a share link at
 *    all, failing FR-17's Share acceptance criterion outright.
 *
 * A small, independent duplicate is therefore the correct call here, not scope creep — see
 * `projects/spaarkeai-word-add-in-r1/notes/037-manifest-change.md` for the full note. If the wave
 * boundary is later lifted, consolidating both call sites onto one shared function is a legitimate,
 * low-risk follow-up (same request/response shape, same route, same auth path).
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
