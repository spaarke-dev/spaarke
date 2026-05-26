/**
 * launch-resolver.ts — URL parameter assembly and navigation for the SpaarkeAi Code Page.
 *
 * All launch points that open `sprk_spaarkeai` route through this module.
 * Ribbon scripts (WorkspaceLaunch, EntityFormLaunch) call the exported functions
 * below — they contain ZERO URL construction or business logic of their own.
 *
 * Supported launch contexts:
 *   1. Workspace command bar — no entity context; opens global AI assistant
 *   2. Entity form command bar — entityLogicalName + entityId from the open record
 *   3. Deep-link URL — external systems pass parameters as query string
 *   4. M365 Copilot handoff — matterId parameter from Declarative Agent action
 *
 * URL parameter contract (all optional):
 *   entityLogicalName  — Dataverse logical name of the record (e.g. "sprk_matter")
 *   entityId           — GUID of the record (braces stripped)
 *   matterId           — Alias for a matter record GUID (M365 Copilot handoff shorthand)
 *
 * @see ADR-006 — Ribbon scripts are invocation-only; business logic lives here
 * @see docs/guides/spaarkeai-launch-points.md — Full URL format documentation
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
export function buildLaunchUrl(params: SpaarkeAiLaunchParams): string {
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
