/**
 * launch-resolver.ts — URL parameter assembly and navigation for the SpaarkeAi Code Page.
 *
 * All launch points that open `sprk_spaarkeai` route through this module.
 * Ribbon scripts (WorkspaceLaunch, EntityFormLaunch, DocumentComposeLaunch) call
 * the exported functions below — they contain ZERO URL construction or business
 * logic of their own.
 *
 * Supported launch contexts:
 *   1. Workspace command bar — no entity context; opens global AI assistant
 *   2. Entity form command bar — entityLogicalName + entityId from the open record
 *   3. Deep-link URL — external systems pass parameters as query string
 *   4. M365 Copilot handoff — matterId parameter from Declarative Agent action
 *   5. Document → Compose modal (spaarkeai-compose-r1 task 046) — opens the
 *      SpaarkeAi modal directly into the Compose editor surface; pre-seeded with
 *      `sprkDocumentId` + `speDriveItemId` so `ComposeEditor` can load the DOCX
 *      on mount. The "modal with full-screen toggle" UX (locked decision in
 *      design.md §14 row 3) is the Xrm dialog chrome itself opened at target=2,
 *      90%×90% — the platform provides the expand-to-full-screen button.
 *
 * URL parameter contract (all optional):
 *   entityLogicalName  — Dataverse logical name of the record (e.g. "sprk_matter")
 *   entityId           — GUID of the record (braces stripped)
 *   matterId           — Alias for a matter record GUID (M365 Copilot handoff shorthand)
 *   composeMode        — "editor" when the modal should boot directly into Compose
 *                        (bypasses the three-pane shell for a focused Compose UX
 *                        per Path A — design.md §14 row 3)
 *   sprkDocumentId     — GUID of the `sprk_document` record (Compose only)
 *   speDriveItemId     — SPE drive-item id (Compose only)
 *   speDriveId         — SPE container drive id (Compose only — optional; can be
 *                        resolved from runtime config when omitted)
 *   speFileName        — Display name of the document (Compose only — optional)
 *
 * @see ADR-006 — Ribbon scripts are invocation-only; business logic lives here
 * @see docs/guides/spaarkeai-launch-points.md — Full URL format documentation
 * @see projects/spaarkeai-compose-r1/design.md §14 row 3 — Path A entry UX (locked)
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Parameters passed to every SpaarkeAi launch point. All fields are optional. */
export interface SpaarkeAiLaunchParams {
  /** Dataverse logical name of the entity record to scope the AI session to. */
  entityLogicalName?: string;
  /** GUID of the entity record (with or without braces — braces are stripped). */
  entityId?: string;
  /**
   * Matter GUID shorthand used by the M365 Copilot Declarative Agent handoff action.
   * When present, StandaloneAiProvider resolves entity context using this ID as the
   * matter record GUID without requiring an Xrm form context.
   */
  matterId?: string;
}

/**
 * Compose-specific launch parameters (spaarkeai-compose-r1 task 046).
 *
 * Used by the Document → Compose modal entry path (Path A per design.md §14 row 3).
 * The Code Page reads these URL params in `main.tsx` and, when `composeMode === 'editor'`,
 * mounts `ComposeWorkspace` directly with the document pointer pre-seeded instead of
 * rendering the standard three-pane shell.
 *
 * All Compose-specific fields are OPTIONAL at the type level so callers can
 * progressively enhance (e.g. a future "Open new Compose" workspace-bar launcher
 * could pass only `composeMode='editor'` for an empty-state open). At runtime,
 * `composeMode='editor'` without `speDriveItemId` renders the Compose empty state
 * (Browse / Search affordances) per FR-19 + design.md §14 row 5.
 */
export interface SpaarkeAiComposeLaunchParams extends SpaarkeAiLaunchParams {
  /**
   * Routes the modal directly into the Compose editor surface.
   * Currently only the value `"editor"` is defined; reserved values may include
   * future Compose layouts (e.g. `"review"`).
   */
  composeMode?: "editor";

  /**
   * Dataverse `sprk_document` record GUID (braces stripped). May be undefined
   * for ephemeral documents not yet promoted (Path B). When present, the Compose
   * surface uses it for ChatSession binding + post-Save idempotency.
   */
  sprkDocumentId?: string;

  /**
   * SPE drive-item id of the DOCX to load. Required for `ComposeEditor` to fetch
   * the file via `GET /api/compose/documents/{speDriveItemId}`. If omitted with
   * `composeMode='editor'`, the workspace renders its empty-state picker.
   */
  speDriveItemId?: string;

  /**
   * SPE container drive id. Required by the BFF Load endpoint as a query
   * parameter. Code Pages may also resolve this from runtime config (the
   * container catalog), so it's optional at the launch boundary.
   */
  speDriveId?: string;

  /**
   * Display name of the document (e.g. "Acme MSA Draft 3.docx"). Used by the
   * Compose surface for the workspace title + open-in-Word handoff. Optional
   * because the BFF Load response also returns it.
   */
  speFileName?: string;
}

/** Dialog opening mode — matches Xrm.Navigation.NavigationOptions.target values. */
export type LaunchTarget =
  /** Open as a full-page navigate (replaces current page). */
  | 1
  /** Open as a modal dialog (overlays current page). */
  | 2;

// ---------------------------------------------------------------------------
// buildLaunchUrl
// ---------------------------------------------------------------------------

/**
 * Assembles the `data` query string that Xrm.Navigation.navigateTo passes to
 * the web resource as its URL parameters.
 *
 * Omits any keys with undefined or empty string values to keep URLs clean.
 *
 * @param params - Launch parameters (all optional).
 * @returns A URL-encoded query string without a leading `?`.
 *
 * @example
 *   buildLaunchUrl({ entityLogicalName: "sprk_matter", entityId: "abc-123" })
 *   // => "entityLogicalName=sprk_matter&entityId=abc-123"
 */
export function buildLaunchUrl(
  params: SpaarkeAiLaunchParams | SpaarkeAiComposeLaunchParams,
): string {
  const record: Record<string, string> = {};

  if (params.entityLogicalName) {
    record["entityLogicalName"] = params.entityLogicalName;
  }

  if (params.entityId) {
    // Strip braces that Dataverse adds to GUIDs: {abc-123} → abc-123
    record["entityId"] = params.entityId.replace(/^\{|\}$/g, "");
  }

  if (params.matterId) {
    record["matterId"] = params.matterId.replace(/^\{|\}$/g, "");
  }

  // Compose-specific params (spaarkeai-compose-r1 task 046). When `composeMode`
  // is absent, none of these fire, so non-Compose launches keep their existing
  // wire format byte-for-byte.
  const composeParams = params as SpaarkeAiComposeLaunchParams;
  if (composeParams.composeMode) {
    record["composeMode"] = composeParams.composeMode;
  }
  if (composeParams.sprkDocumentId) {
    record["sprkDocumentId"] = composeParams.sprkDocumentId.replace(/^\{|\}$/g, "");
  }
  if (composeParams.speDriveItemId) {
    record["speDriveItemId"] = composeParams.speDriveItemId;
  }
  if (composeParams.speDriveId) {
    record["speDriveId"] = composeParams.speDriveId;
  }
  if (composeParams.speFileName) {
    record["speFileName"] = composeParams.speFileName;
  }

  return new URLSearchParams(record).toString();
}

// ---------------------------------------------------------------------------
// openSpaarkeAi
// ---------------------------------------------------------------------------

/**
 * Opens the SpaarkeAi Code Page (`sprk_spaarkeai`) via Xrm.Navigation.navigateTo.
 *
 * Falls back gracefully when `Xrm` is not available — this can happen when the
 * function is called from a deep-link or M365 context where Xrm is not injected.
 * In that case, the function logs a warning and does nothing; the caller (e.g.
 * the M365 Copilot agent) constructs the full URL directly without calling this.
 *
 * @param params  - Entity context and optional matterId to pass to the Code Page.
 * @param target  - 1 = full page, 2 = modal dialog (default: 2).
 *
 * @example
 *   // Workspace button — no entity context
 *   openSpaarkeAi({});
 *
 *   // Entity form button — pass matter context
 *   openSpaarkeAi({ entityLogicalName: "sprk_matter", entityId: "abc-123" });
 *
 *   // Modal dialog (default)
 *   openSpaarkeAi({ matterId: "abc-123" }, 2);
 */
export function openSpaarkeAi(
  params: SpaarkeAiLaunchParams,
  target: LaunchTarget = 2
): void {
  if (typeof Xrm === "undefined") {
    // Deep-link / non-Xrm context — the page is opened directly via URL, not
    // through Xrm.Navigation. No action needed here.
    console.warn(
      "[launch-resolver] Xrm global not available. SpaarkeAi must be opened via direct URL."
    );
    return;
  }

  const data = buildLaunchUrl(params);

  void Xrm.Navigation.navigateTo(
    {
      pageType: "webresource",
      webresourceName: "sprk_spaarkeai",
      data,
    },
    {
      target,
      width: { value: 90, unit: "%" },
      height: { value: 90, unit: "%" },
    }
  );
}

// ---------------------------------------------------------------------------
// openSpaarkeAiCompose (spaarkeai-compose-r1 task 046)
// ---------------------------------------------------------------------------

/**
 * Opens the SpaarkeAi Code Page directly into the Compose editor surface
 * (Path A entry per design.md §14 row 3 — "Modal with full-screen toggle").
 *
 * Behaviour vs. `openSpaarkeAi`:
 *   - Always opens as a modal (target=2) — Compose is a focused-work surface and
 *     full-page navigation would lose the user's record context. The platform-
 *     provided "Expand" button on the Xrm dialog header is the full-screen toggle
 *     (no custom modal abstraction needed per POML constraint).
 *   - Forces `composeMode='editor'` so `main.tsx` routes to the Compose surface
 *     instead of the standard three-pane shell.
 *   - Threads `sprkDocumentId` + `speDriveItemId` (and optionally `speDriveId` +
 *     `speFileName`) through the URL so `ComposeWorkspace` can mount with the
 *     document pre-loaded.
 *   - Reuses the same 90%×90% modal sizing as `openSpaarkeAi` so the modal chrome
 *     looks identical to other SpaarkeAi launches (FR-19).
 *
 * Falls back gracefully when `Xrm` is not available — logs a warning and does
 * nothing; the caller (deep-link or test) constructs the URL directly via
 * {@link buildLaunchUrl}.
 *
 * @param params - Compose launch parameters (document pointer + optional entity
 *                 context if the caller wants to thread it through).
 *
 * @example
 *   // Ribbon button on sprk_document form: open the current record in Compose.
 *   openSpaarkeAiCompose({
 *     entityLogicalName: "sprk_document",
 *     entityId: "f1a2b3c4-...",
 *     sprkDocumentId: "f1a2b3c4-...",
 *     speDriveItemId: "01ABCDEF...",
 *     speFileName: "Acme MSA Draft.docx",
 *   });
 *
 *   // Empty-state Compose open (no document yet):
 *   openSpaarkeAiCompose({});
 */
export function openSpaarkeAiCompose(
  params: SpaarkeAiComposeLaunchParams,
): void {
  if (typeof Xrm === "undefined") {
    console.warn(
      "[launch-resolver] Xrm global not available. SpaarkeAi Compose must be opened via direct URL.",
    );
    return;
  }

  // Force composeMode='editor' so main.tsx routes to the Compose surface.
  // Callers may still override by passing composeMode explicitly, but the
  // default for this entry point is the editor surface.
  const data = buildLaunchUrl({
    ...params,
    composeMode: params.composeMode ?? "editor",
  });

  void Xrm.Navigation.navigateTo(
    {
      pageType: "webresource",
      webresourceName: "sprk_spaarkeai",
      data,
    },
    {
      // Always modal (target=2) — see header comment for rationale.
      target: 2,
      width: { value: 90, unit: "%" },
      height: { value: 90, unit: "%" },
    },
  );
}
