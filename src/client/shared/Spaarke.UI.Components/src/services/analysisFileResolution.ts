/**
 * analysisFileResolution.ts — Analysis file open/preview via the
 * `sprk_documentid` → `sprk_document` SPE hop (ADR-007).
 *
 * HOISTED to `@spaarke/ui-components` (2026-07-29 duplicate-component audit)
 * from the SpaarkeAi solution so the shared-lib `AnalysisHubWidget` can CONSUME
 * it directly instead of restating the same document-hop + preview-url closure
 * locally (ADR-012 dependency direction previously forced that restatement).
 *
 * Per ADR-007 + the Analysis platform constraint ("Files via `sprk_documentid`
 * → `sprk_document`; NEVER duplicate SPE pointers on `sprk_analysis`"), a
 * `sprk_analysis` record never carries `speDriveItemId` / `GraphDriveId` /
 * `GraphItemId`. The ONLY thing it stores is the `_sprk_documentid_value`
 * lookup (the SPE *subject-pointer*). File open/preview reaches SPE by treating
 * that lookup value as a `sprk_document` id and handing it to the EXISTING
 * document-preview BFF surface (`GET /api/documents/{id}/preview-url`), which
 * resolves the SPE pointer server-side through `SpeFileStore` — the client
 * never sees, reads, or stores a raw SPE pointer. Same call shape as
 * `ConversationPane`'s `fetchSavedPreviewUrl` (§11 reuse — no new BFF surface).
 *
 * @see ADR-007 — SpeFileStore facade / document-hop SPE addressing
 * @see ./sprkAnalysis (../types/sprkAnalysis) — `_sprk_documentid_value`
 */

import type { ISprkAnalysisRecord } from '../types/sprkAnalysis';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Minimal fetch signature this module depends on — matches `authenticatedFetch`
 * from `@spaarke/auth`. Modeled structurally (not imported) so this module
 * stays a plain function consumers can unit-test with any fetch stand-in.
 */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

export interface AnalysisFilePreviewDeps {
  /** BFF base URL. When falsy, resolution short-circuits `fetchPreviewUrl` to `null`. */
  bffBaseUrl: string | null | undefined;
  /** Authenticated fetch — reused from the caller (no token snapshot, per ADR-028). */
  authenticatedFetch: AuthenticatedFetchFn;
}

/** The Analysis has no linked `sprk_document` (`_sprk_documentid_value` is absent). */
export interface AnalysisFilePreviewNoDocument {
  status: 'no-document';
}

/**
 * The Analysis's `sprk_documentid` hop resolved to a `sprk_document` id.
 * `documentId` is that `sprk_document` id — NOT an SPE pointer; SPE resolution
 * happens server-side (inside the BFF preview-url endpoint) when
 * `fetchPreviewUrl` is invoked.
 */
export interface AnalysisFilePreviewResolved {
  status: 'resolved';
  /** The linked `sprk_document` id (`_sprk_documentid_value`). */
  documentId: string;
  /** Best-effort display name — the OData formatted-value annotation when present,
   * else falls back to the Analysis's own `sprk_name`. */
  documentName: string;
  /** `RichFilePreview`-compatible closure. Resolves to `null` (never throws) on any
   * fetch failure — the renderer's existing "preview not available" fallback handles it. */
  fetchPreviewUrl: () => Promise<string | null>;
}

export type AnalysisFilePreviewResolution = AnalysisFilePreviewNoDocument | AnalysisFilePreviewResolved;

// ---------------------------------------------------------------------------
// Resolution
// ---------------------------------------------------------------------------

/**
 * Read the `sprk_document` id off an Analysis's `sprk_documentid` lookup.
 * The ENTIRE "document hop" read — one field access, no SPE pointer field is
 * read from `sprk_analysis` (ADR-007).
 */
export function resolveAnalysisDocumentId(
  analysis: Pick<ISprkAnalysisRecord, '_sprk_documentid_value'>
): string | null {
  const id = analysis._sprk_documentid_value;
  return typeof id === 'string' && id.length > 0 ? id : null;
}

/**
 * Resolve an Analysis's file-preview inputs via the `sprk_documentid` →
 * `sprk_document` hop (ADR-007). Reuses the existing BFF
 * `GET /api/documents/{id}/preview-url` surface — the BFF resolves the
 * `sprk_document`'s SPE pointer through `SpeFileStore` internally.
 *
 * Returns `{ status: 'no-document' }` when the Analysis has no `sprk_documentid`
 * — callers MUST surface a clear no-document state rather than fabricating a pointer.
 */
export function resolveAnalysisFilePreview(
  analysis: Pick<
    ISprkAnalysisRecord,
    '_sprk_documentid_value' | '_sprk_documentid_value@OData.Community.Display.V1.FormattedValue' | 'sprk_name'
  >,
  deps: AnalysisFilePreviewDeps
): AnalysisFilePreviewResolution {
  const documentId = resolveAnalysisDocumentId(analysis);
  if (!documentId) {
    return { status: 'no-document' };
  }

  const documentName =
    analysis['_sprk_documentid_value@OData.Community.Display.V1.FormattedValue'] ?? analysis.sprk_name ?? 'Document';

  const { bffBaseUrl, authenticatedFetch } = deps;

  const fetchPreviewUrl = async (): Promise<string | null> => {
    if (!bffBaseUrl) return null;
    try {
      const response = await authenticatedFetch(
        `${bffBaseUrl}/api/documents/${encodeURIComponent(documentId)}/preview-url`
      );
      if (!response.ok) return null;
      const data = (await response.json()) as { previewUrl?: string | null };
      return data.previewUrl ?? null;
    } catch {
      return null;
    }
  };

  return { status: 'resolved', documentId, documentName, fetchPreviewUrl };
}
