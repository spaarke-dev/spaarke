/**
 * DocumentApiService — BFF API calls for document preview and open links.
 *
 * Uses the existing authenticatedFetch() from bffAuthProvider.ts for Bearer
 * token authentication and getBffBaseUrl() from config/bffConfig.ts for
 * API base URL resolution.
 *
 * API endpoints (same as SemanticSearch PCF):
 *   GET /api/documents/{id}/preview-url  → ephemeral iframe preview URL
 *   GET /api/documents/{id}/open-links   → web URL + desktop protocol URL
 */

import { authenticatedFetch } from './bffAuthProvider';
import { getBffBaseUrl } from '../config/bffConfig';

// ---------------------------------------------------------------------------
// Response types
// ---------------------------------------------------------------------------

export interface IOpenLinksResponse {
  webUrl: string;
  desktopUrl: string | null;
  mimeType: string;
  fileName: string;
}

export interface IPreviewUrlResponse {
  previewUrl: string;
  documentInfo: {
    name: string;
    mimeType: string;
  } | null;
  checkoutStatus: unknown;
  correlationId: string;
}

// ---------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------

/**
 * Get a read-only preview URL for a document.
 * Returns an ephemeral embed URL (~10 min expiry) or null on failure.
 */
export async function getDocumentPreviewUrl(documentId: string): Promise<string | null> {
  const base = getBffBaseUrl();
  const url = `${base}/documents/${encodeURIComponent(documentId)}/preview-url`;

  try {
    const response = await authenticatedFetch(url);
    if (!response.ok) {
      console.warn('[DocumentApiService] getPreviewUrl failed:', response.status);
      return null;
    }
    const data = await response.json() as IPreviewUrlResponse;
    return data.previewUrl ?? null;
  } catch (err) {
    console.error('[DocumentApiService] getPreviewUrl error:', err);
    return null;
  }
}

/**
 * Get open links (web URL and desktop protocol URL) for a document.
 * Returns the open links or null on failure.
 */
export async function getDocumentOpenLinks(documentId: string): Promise<IOpenLinksResponse | null> {
  const base = getBffBaseUrl();
  const url = `${base}/documents/${encodeURIComponent(documentId)}/open-links`;

  try {
    const response = await authenticatedFetch(url);
    if (!response.ok) {
      console.warn('[DocumentApiService] getOpenLinks failed:', response.status);
      return null;
    }
    return await response.json() as IOpenLinksResponse;
  } catch (err) {
    console.error('[DocumentApiService] getOpenLinks error:', err);
    return null;
  }
}
