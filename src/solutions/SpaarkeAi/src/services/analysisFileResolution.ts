/**
 * analysisFileResolution.ts — Analysis file open/preview via the
 * `sprk_documentid` → `sprk_document` SPE hop (ADR-007).
 *
 * Task 013 (`ai-advanced-capabilities-analysis-hub-r1`, Phase 1 Data spine).
 *
 * Per ADR-007 + project CLAUDE.md ("Files via `sprk_documentid` →
 * `sprk_document`; NEVER duplicate SPE pointers on `sprk_analysis`"), a
 * `sprk_analysis` record never carries `speDriveItemId` / `GraphDriveId` /
 * `GraphItemId`. The ONLY thing `sprk_analysis` stores is the
 * `_sprk_documentid_value` lookup (the SPE *subject-pointer* — see
 * `types/sprkAnalysis.ts`). File open/preview reaches SPE by treating that
 * lookup value as a `sprk_document` id and handing it to the EXISTING
 * document-preview BFF surface, which resolves the SPE pointer server-side
 * through the `SpeFileStore` facade (ADR-007) — the client never sees, reads,
 * or stores a raw SPE pointer.
 *
 * This is the SAME pattern already shipped in this solution's
 * `ConversationPane.tsx` (FIX #7a — `fetchSavedPreviewUrl`): a `sprk_document`
 * id is fed to `RichFilePreviewDialog` via `documentId` +
 * `GET {bffBaseUrl}/api/documents/{id}/preview-url` as the `fetchPreviewUrl`
 * closure. Reused verbatim here (§11 reuse mandate) — no new BFF endpoint, no
 * new SPE-addressing service (ADR-013 / project constraint).
 *
 * Consumers (hub grid / reopen — later phases, tasks 030/031) call
 * `resolveAnalysisFilePreview(analysis, { bffBaseUrl, authenticatedFetch })`
 * to get either:
 *   - `{ status: 'resolved', documentId, documentName, fetchPreviewUrl }` —
 *     spread the last three fields directly into `<RichFilePreviewDialog>` /
 *     `<RichFilePreview>` props.
 *   - `{ status: 'no-document' }` — the Analysis has no linked document.
 *     Consumers MUST surface a clear "no file" state (per acceptance
 *     criteria) rather than opening a dialog with a fabricated pointer.
 *
 * @see ADR-007 — SpeFileStore facade / document-hop SPE addressing
 * @see ADR-013 — CRUD→AI via PublicContracts (N/A here — no AI-internal call)
 * @see src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts — `_sprk_documentid_value`
 * @see src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx
 *      (FIX #7a `fetchSavedPreviewUrl`) — the reused BFF call shape
 * @see src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx
 */

import type { ISprkAnalysisRecord } from '../types/sprkAnalysis';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Minimal fetch signature this module depends on — matches
 * `authenticatedFetch` from `../services/authInit.ts` / `@spaarke/auth`.
 * Modeled structurally (not imported) so this module stays a plain function
 * consumers can unit-test with any fetch stand-in.
 */
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

export interface AnalysisFilePreviewDeps {
  /** BFF base URL. When falsy, resolution short-circuits to `no-document`
   * (there is nowhere to fetch a preview URL from). */
  bffBaseUrl: string | null | undefined;
  /** Authenticated fetch — reused from the caller (no token snapshot, per ADR-028). */
  authenticatedFetch: AuthenticatedFetchFn;
}

/**
 * The Analysis has no linked `sprk_document` (`_sprk_documentid_value` is
 * absent). Consumers MUST render a clear "no file" state — this is the
 * escalation-avoidance path from the task's `<escalation>` trigger: rather
 * than fabricating an SPE pointer, resolution reports "no document" and lets
 * the caller decide the UX (e.g. disable the "Open file" action).
 */
export interface AnalysisFilePreviewNoDocument {
  status: 'no-document';
}

/**
 * The Analysis's `sprk_documentid` hop resolved to a `sprk_document` id.
 * `documentId` is that `sprk_document` id — NOT an SPE pointer; the SPE
 * pointer resolution happens server-side (inside the BFF's existing
 * `SpeFileStore`-backed preview-url endpoint) when `fetchPreviewUrl` is
 * invoked. Spread these three fields directly into `RichFilePreviewDialog` /
 * `RichFilePreview` props (`documentId`, `documentName`, `fetchPreviewUrl`).
 */
export interface AnalysisFilePreviewResolved {
  status: 'resolved';
  /** The linked `sprk_document` id (`_sprk_documentid_value`). */
  documentId: string;
  /** Best-effort display name — the OData formatted-value annotation when
   * present, else falls back to the Analysis's own `sprk_name`. */
  documentName: string;
  /** `RichFilePreview`-compatible closure. Resolves to `null` (never throws)
   * on any fetch failure — the renderer's existing "preview not available"
   * fallback handles that case. */
  fetchPreviewUrl: () => Promise<string | null>;
}

export type AnalysisFilePreviewResolution = AnalysisFilePreviewNoDocument | AnalysisFilePreviewResolved;

// ---------------------------------------------------------------------------
// Resolution
// ---------------------------------------------------------------------------

/**
 * Read the `sprk_document` id off an Analysis's `sprk_documentid` lookup.
 * Exported standalone so callers that only need the id (e.g. to gate an
 * "Open file" button's `disabled` state) don't need the full BFF-wired
 * resolution.
 *
 * This is the ENTIRE "document hop" read — one field access, no SPE pointer
 * field is read from `sprk_analysis` (per ADR-007 / acceptance criteria).
 */
export function resolveAnalysisDocumentId(analysis: Pick<ISprkAnalysisRecord, '_sprk_documentid_value'>): string | null {
  const id = analysis._sprk_documentid_value;
  return typeof id === 'string' && id.length > 0 ? id : null;
}

/**
 * Resolve an Analysis's file-preview inputs via the `sprk_documentid` →
 * `sprk_document` hop (ADR-007). Reuses the existing BFF
 * `GET /api/documents/{id}/preview-url` surface (same call shape as
 * `ConversationPane.tsx`'s `fetchSavedPreviewUrl`) — the BFF resolves the
 * `sprk_document`'s SPE pointer through `SpeFileStore` internally; this
 * module never sees a raw SPE pointer.
 *
 * Returns `{ status: 'no-document' }` when the Analysis has no
 * `sprk_documentid` — callers MUST surface a clear no-document state (per
 * acceptance criteria) rather than treating this as an error or fabricating
 * a pointer.
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
      const response = await authenticatedFetch(`${bffBaseUrl}/api/documents/${encodeURIComponent(documentId)}/preview-url`);
      if (!response.ok) return null;
      const data = (await response.json()) as { previewUrl?: string | null };
      return data.previewUrl ?? null;
    } catch {
      // Non-fatal — the renderer shows its own "preview not available"
      // fallback (matches the ConversationPane FIX #7a posture).
      return null;
    }
  };

  return { status: 'resolved', documentId, documentName, fetchPreviewUrl };
}
