/**
 * AttachmentApiService
 *
 * Thin BFF client for the attachment preview surface. Mirrors the two
 * document-open calls `SemanticSearchControl/services/SemanticSearchApiService.ts`
 * makes — no new BFF endpoint is authored (root §11):
 *
 *   - GET /api/documents/{id}/preview-url  → ephemeral Graph embed URL (~10 min)
 *   - GET /api/documents/{id}/open-links   → desktop + web open URLs
 *
 * Auth is handled by `authenticatedFetch` from `@spaarke/auth` (ADR-028) —
 * token acquisition, caching, and 401 retry are the shared library's job. This
 * service NEVER touches MSAL directly (INV-7).
 */

import { authenticatedFetch } from '@spaarke/auth';

/** Response from GET /api/documents/{id}/preview-url. */
export interface PreviewUrlResponse {
  previewUrl: string;
  documentInfo: {
    name: string;
    mimeType: string;
  } | null;
  checkoutStatus: unknown;
  correlationId: string;
}

/** Response from GET /api/documents/{id}/open-links. */
export interface OpenLinksResponse {
  desktopUrl: string | null;
  webUrl: string;
  mimeType: string;
  fileName: string;
}

export class AttachmentApiService {
  private readonly apiBaseUrl: string;

  /**
   * @param apiBaseUrl BFF API base URL (host only; trailing slash tolerated).
   */
  constructor(apiBaseUrl: string) {
    this.apiBaseUrl = apiBaseUrl.replace(/\/+$/, '');
  }

  /**
   * Resolve a read-only preview embed URL for a document. Returns null when the
   * BFF cannot produce one (best-effort — the caller degrades to an error
   * message inside the modal).
   */
  async getPreviewUrl(documentId: string): Promise<string | null> {
    const endpoint = `${this.apiBaseUrl}/api/documents/${encodeURIComponent(documentId)}/preview-url`;
    try {
      const response = await authenticatedFetch(endpoint, { method: 'GET' });
      if (!response.ok) {
        console.warn('[CommunicationAttachments] getPreviewUrl failed:', response.status);
        return null;
      }
      const data = (await response.json()) as PreviewUrlResponse;
      return data.previewUrl ?? null;
    } catch (error) {
      console.error('[CommunicationAttachments] getPreviewUrl error:', error);
      return null;
    }
  }

  /**
   * Resolve desktop + web open URLs for a document. Used for the modal's
   * onOpenFile callback AND for the `.eml` download/open route (which bypasses
   * Graph inline preview entirely).
   */
  async getOpenLinks(documentId: string): Promise<OpenLinksResponse | null> {
    const endpoint = `${this.apiBaseUrl}/api/documents/${encodeURIComponent(documentId)}/open-links`;
    try {
      const response = await authenticatedFetch(endpoint, { method: 'GET' });
      if (!response.ok) {
        console.warn('[CommunicationAttachments] getOpenLinks failed:', response.status);
        return null;
      }
      return (await response.json()) as OpenLinksResponse;
    } catch (error) {
      console.error('[CommunicationAttachments] getOpenLinks error:', error);
      return null;
    }
  }
}

export default AttachmentApiService;
