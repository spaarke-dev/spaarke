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
  /**
   * ai-advanced-capabilities-analysis-hub-r1 task 041 (FR-13): the ACTIVE work type the launch
   * carries (e.g. `'agreement-analysis'`). Cross-cutting — applies regardless of which of the
   * three mutually-exclusive door shapes above is present. Threaded to
   * `ComposeLaunchContextValue.activeWorkType` by `buildLaunchFromSeed`, then to
   * `<ComposeWorkspace activeWorkType>` so the AI toolbar scopes via `getToolsForSurface`.
   * Absent for every pre-existing seed — no regression (ComposeEditor defaults to `'*'`).
   */
  activeWorkType?: string;
  /**
   * agreements-r1 A3 (`bd64a69d4`): the picked agreement sub-domain (`sprk_agreementtype.sprk_key`,
   * e.g. `'nda'` / `'employment'`) the wizard-finish seed has carried since Phase 1 — declared here
   * (task 033) so the ConversationPane wizard hand-off listener reads it TYPED instead of untyped.
   */
  subDomain?: string;
  /**
   * agreements-r1 task 033 (FR-17 wizard→review auto-run bridge) — the three hand-off fields the
   * Create-Analysis wizard adds on finish for an agreement analysis:
   *
   * `composeSessionId`: the wizard-minted ANALYSIS-OWNED chat session (HostContext sentinel →
   *   create-time durable `sprk_analysis` FK). Dual-consumed: (1) `ComposeDirectWidget` threads it
   *   as `<ComposeWorkspace initialSessionId>` so the BFF Load RESUMES it as the document session
   *   (chat ≡ document session — the upload-mount door's coincidence, now for the stored door);
   *   (2) ConversationPane's workspace listener ADOPTS it as the pane's active chat session.
   * `analysisId`: the `sprk_analysis` GUID (bare lowercase, ADR-044) the session is durably bound
   *   to — the listener's once-per-Analysis dedupe key. NEVER an `sprk_analysisoutput` id.
   * `autoRunReview`: arms the explicit review door (`useAgreementReviewGate.runExplicit` on
   *   `subDomain`'s pack) once the DEF-10 register lands the file. Only ever set alongside
   *   `composeSessionId` + `subDomain`. Absent for every non-wizard seed — no behavior change.
   */
  composeSessionId?: string;
  analysisId?: string;
  autoRunReview?: boolean;
}

/** Tab `widgetData` shape for the `'compose'` Direct widget. */
export interface ComposeWidgetData {
  /** Active-document identity seed; absent on a fresh/empty Compose tab. */
  compose?: ComposeWidgetSeed;
}
