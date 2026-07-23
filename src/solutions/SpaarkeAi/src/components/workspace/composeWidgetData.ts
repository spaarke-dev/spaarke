/**
 * composeWidgetData.ts — shared shape for the Compose Direct-widget (Wave 5).
 *
 * The `'compose'` Direct registration (see `registerComposeWidget.ts`) and its
 * mount adapter (`ComposeDirectWidget.tsx`) both narrow the tab's `widgetData`
 * to this shape. It is the SAME structural `compose` seed the layout path
 * already carries on a `workspace` tab (`widgetData.compose` — resolved by the
 * server or the "Open in Compose" affordance and translated to a
 * `ComposeLaunchContext` by `main.tsx`'s `SpaarkeAiWorkspaceRenderer`). We
 * re-declare it here (structural mirror, not an import) so the Direct widget
 * stays additive and does not couple to the layout renderer.
 *
 * Three mutually-exclusive door shapes (mirrors `main.tsx` seed translation):
 *   - stored document  → `speDriveItemId` (+ `sprkDocumentId`, `speDriveId`)
 *   - transient upload  → `upload.{ sessionId, sessionFileId }`
 *   - AI-drafted seed   → `draft.{ ledgerRef, sessionId }` (Part A) or `draft.html` (Part B)
 */

/** The active-document identity seed carried on a Compose tab's `widgetData.compose`. */
export interface ComposeWidgetSeed {
  /** Dataverse `sprk_documentid` (present after first-Save promotion). */
  sprkDocumentId?: string;
  /** SPE drive-item id for a stored document. */
  speDriveItemId?: string;
  /** SPE drive id (multi-tenant scoping). */
  speDriveId?: string | null;
  /** Human-readable file name for UI labelling / agent visibility. */
  fileName?: string | null;
  /** Transient Assistant-upload pointer (no SPE item; create-on-save). */
  upload?: {
    sessionId?: string;
    sessionFileId?: string;
    fileName?: string | null;
  };
  /** DEF-08 AI-drafted full-document seed (Part A ledgerRef / Part B inline html). */
  draft?: {
    ledgerRef?: string;
    sessionId?: string;
    html?: string;
    fileName?: string | null;
  };
}

/** Tab `widgetData` shape for the `'compose'` Direct widget. */
export interface ComposeWidgetData {
  /** Active-document identity seed; absent on a fresh/empty Compose tab. */
  compose?: ComposeWidgetSeed;
}
