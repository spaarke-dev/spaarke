/**
 * registerComposeWidget.ts — Compose as a first-class DIRECT workspace widget
 * (spaarkeai-compose-r2 Wave 5, plan §"Wave 5 — Compose first-class").
 *
 * ## What this adds (STRICTLY ADDITIVE — dual-use, ZERO regression)
 *
 * Compose is already Pattern D: the widget lives in the shared lib
 * `@spaarke/compose-components` (`ComposeWorkspace`) with a thin LegalWorkspace
 * section shim (`composeEditor.registration.ts`). Today it mounts ONLY via the
 * `'workspace'` LAYOUT path (the "Compose" `sprk_workspacelayout` row →
 * `WorkspaceLayoutWidget` → `LegalWorkspaceApp(embedded)` → the compose
 * section). This module registers Compose ALSO as a Direct `WorkspaceWidget`
 * (`widgetType: 'compose'`) so it becomes a first-class registry citizen with
 * its own agent-visibility contract — WITHOUT removing or migrating the layout
 * path. Compose still works via the LegalWorkspace "Compose" layout row +
 * workspace dropdown entry and the standalone LegalWorkspace mount.
 *
 * UPDATE (spaarkeai-compose-r2 UNIFY — the Direct `'compose'` widget is now the
 * LIVE mount door): the earlier "nothing dispatches `widget_load{ widgetType:
 * 'compose' }`" note is NO LONGER TRUE. Several open-paths now dispatch a
 * `widget_load` for this Direct widget, and `WorkspacePane`'s `'compose'` branch
 * mounts it (single-tab reuse, DEF-08):
 *   - `WorkspacePane` compose-launch auto-install (ribbon `composeMode=editor`),
 *   - `ConversationPane.mountActiveSourceDocInCompose` (Open in Compose / revise),
 *   - `ConversationPane.handleDocAction` (the revise/draft doc-action chips),
 *   - the compose layout-reroute in `WorkspacePane`.
 * This registration is what makes those dispatches resolve a real component.
 * See the "Why here, not in @spaarke/ai-widgets" and "Picker" notes below.
 *
 * ## Why here (SpaarkeAi), not in `@spaarke/ai-widgets/register-workspace-widgets.ts`
 *
 * The Direct factory must resolve `ComposeWorkspace` from
 * `@spaarke/compose-components`. That package STATICALLY imports `PaneChannel`
 * from `@spaarke/ai-widgets`, so `@spaarke/ai-widgets` MUST NOT depend on
 * `@spaarke/compose-components` (documented cycle guard in
 * `WorkspaceLayoutWidget.tsx`). `SpaarkeAi` depends on BOTH packages, so it is
 * the cycle-safe home — mirroring the established `setDefaultWorkspaceRenderer`
 * bridge in `main.tsx` (which also injects a compose-components value into an
 * ai-widgets slot FROM the SpaarkeAi side for the same reason). The mount
 * adapter is dynamic-imported by the factory so this module stays cheap at
 * bootstrap and does not pull the TipTap/mammoth chain until (if ever) a
 * `'compose'` tab is resolved.
 *
 * ## Picker mechanism (verified 2026-07-13 — corrects drifted doc §3.1)
 *
 * The workspace dropdown ("Select Workspace" in `WorkspacePaneMenu`) lists BFF
 * LAYOUT ROWS from `useWorkspaceLayouts()` (`GET /api/workspace/layouts` — the
 * `sprk_workspacelayout` query), NOT the `WorkspaceWidgetRegistry`. Daily
 * Briefing / Calendar appear in the picker because they are LAYOUT ROWS, and so
 * is Compose (the system "Compose" row from W1a-010). Registry registration is
 * ORTHOGONAL to picker presence — therefore registering Compose as Direct does
 * NOT lose its dropdown entry. (The earlier "loses dropdown" concern was based
 * on the drifted `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §3.1; corrected in
 * that doc.)
 *
 * ## getVisibleState (the sanctioned agent-visibility contract, Pillar 9)
 *
 * `composeWidgetVisibility` exposes WHICH document the Compose tab holds to the
 * Assistant per-turn prompt. The registry `getVisibleState` slot returns the
 * CLOSED `SerializedWidgetState` union (Summary | DocumentViewer | Dashboard |
 * Table) — a compile-time-locked contract aligned 1:1 with
 * `WorkspaceTabWidgetType` (`_DiscriminatorAlignment` guard). A Compose tab
 * HOLDS A DOCUMENT, so we project the active-document identity onto the
 * existing `DocumentViewer` variant (`filename` = the resolved file name). This
 * is fully additive — it needs ZERO change to the union / `WorkspaceTab` / the
 * Pillar 9 prompt builder (no regression). See the LIMITATION note on
 * `composeWidgetVisibility`.
 *
 * @see ./ComposeDirectWidget.tsx — the WorkspaceWidgetProps → ComposeWorkspace adapter
 * @see ./composeWidgetData.ts — the `widgetData.compose` seed shape
 * @see src/solutions/SpaarkeAi/src/main.tsx — bootstrap side-effect caller
 * @see ADR-030 (typed pane events) · ADR-013/039 (no AI-internal types, no new
 *      dispatch endpoint) · ADR-015 (data minimization) · CLAUDE.md §11 (reuse)
 */

import {
  registerWorkspaceWidget,
  type RegistryGetAgentVisibleState,
  type SerializedDocumentViewerState,
  type WidgetMetadata,
  type WorkspaceWidgetComponent,
} from "@spaarke/ai-widgets";
import type { ComposeWidgetSeed } from "./composeWidgetData";

// ---------------------------------------------------------------------------
// getVisibleState — Pillar 9 agent-visibility derivation for a Compose tab
// ---------------------------------------------------------------------------

/**
 * Derive the agent-visible state for a Compose tab from `widgetData.compose`.
 *
 * Returns a `SerializedDocumentViewerState` — the Compose tab holds a DOCUMENT,
 * so the existing `DocumentViewer` variant is the natural, additive projection.
 * The `filename` field carries the active-document identity (resolved across the
 * stored / upload / draft door shapes) so the Assistant knows which document the
 * Compose tab is editing. Returns `null` when there is no `compose` payload OR
 * no identifiable document (privacy default per ADR-015 + the registry
 * opt-out contract).
 *
 * **LIMITATION (honest, per plan §Wave 5 acceptance + item 3):** the ROUTING
 * identity fields the Wave-2/3 seed also carries — `source`, `sessionFileId`,
 * `documentSessionId`, `sprkDocumentId` — are NOT surfaced in the serialized
 * PROMPT state. Two reasons:
 *   1. The registry `getVisibleState` slot is locked to the closed
 *      `SerializedWidgetState` union; the `DocumentViewer` variant has no field
 *      for those routing ids, and widening the union would touch
 *      `WorkspaceTab` + the Pillar 9 prompt builder (a cross-cutting change,
 *      NOT zero-regression).
 *   2. Per ADR-015 (data minimization) those internal routing ids should be
 *      WITHHELD from the LLM prompt anyway — the human-facing `filename` is the
 *      correct agent-visible identity.
 * The routing identity remains owned by the `composeActionBridge`
 * active-document conduit (`registerActiveDocument` → `POST /api/compose/active-document`),
 * which is DELIBERATELY NOT retired — `getVisibleState` (a read-only prompt
 * projection) does not and cannot cover it (a client→server routing write for
 * DEF-11/DEF-12 edit routing). Retiring it would regress edit routing.
 */
export const composeWidgetVisibility: RegistryGetAgentVisibleState = (
  widgetData: unknown
): SerializedDocumentViewerState | null => {
  if (widgetData === null || typeof widgetData !== "object") return null;
  const compose = (widgetData as { compose?: unknown }).compose;
  if (compose === null || typeof compose !== "object") return null;
  const seed = compose as ComposeWidgetSeed;

  // Resolve the human-facing file identity across the three door shapes.
  const fileName =
    (typeof seed.fileName === "string" && seed.fileName) ||
    (typeof seed.upload?.fileName === "string" && seed.upload.fileName) ||
    (typeof seed.draft?.fileName === "string" && seed.draft.fileName) ||
    "";

  // Any active-document identity at all (stored / upload / draft)?
  const hasIdentity =
    fileName.length > 0 ||
    (typeof seed.speDriveItemId === "string" && seed.speDriveItemId.length > 0) ||
    (typeof seed.sprkDocumentId === "string" && seed.sprkDocumentId.length > 0) ||
    (typeof seed.upload?.sessionFileId === "string" && seed.upload.sessionFileId.length > 0) ||
    (typeof seed.draft?.ledgerRef === "string" && seed.draft.ledgerRef.length > 0) ||
    (typeof seed.draft?.html === "string" && seed.draft.html.length > 0);
  if (!hasIdentity) return null;

  return {
    widgetType: "DocumentViewer",
    // The active-document identity the Assistant needs: which file is open.
    filename: fileName.length > 0 ? fileName : "Compose document",
    // The Compose seed carries no MIME / size; leave them at their neutral
    // "unknown" sentinels rather than asserting a type. hasSelection is false —
    // editor selection is not surfaced through widgetData.
    mimeType: "",
    sizeBytes: 0,
    hasSelection: false,
  };
};

// ---------------------------------------------------------------------------
// Metadata + registration
// ---------------------------------------------------------------------------

const COMPOSE_WIDGET_METADATA: WidgetMetadata = {
  displayName: "Compose",
  category: "document",
  icon: "DocumentEditRegular",
  // Single Compose surface per session — mirrors DEF-08 single-tab-reuse intent.
  allowMultiple: false,
  // After the document-category output widgets (ContractComparison 40) and
  // before StatusSummary (50); keeps Compose in the document cluster.
  defaultOrder: 45,
};

let _registered = false;

/**
 * Register the `'compose'` Direct widget (idempotent). Runs as a top-level
 * side-effect on module import AND is exposed as an explicit sentinel so the
 * bootstrap call site in `main.tsx` reads intentionally and is not tree-shaken.
 * `registerWorkspaceWidget` itself ignores duplicate keys (first wins), so a
 * double invocation is harmless.
 */
export function registerComposeWidget(): void {
  if (_registered) return;
  _registered = true;
  registerWorkspaceWidget(
    "compose",
    COMPOSE_WIDGET_METADATA,
    () =>
      import("./ComposeDirectWidget").then((m) => ({
        default: m.ComposeDirectWidget as WorkspaceWidgetComponent,
      })),
    composeWidgetVisibility
  );
}

// Register at module-eval time (top-level side effect) — matches the
// register-workspace-widgets.ts convention.
registerComposeWidget();
