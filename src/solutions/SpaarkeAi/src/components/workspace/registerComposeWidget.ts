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
 * ## Agent visibility (Pillar 9)
 *
 * This registration carries NO client-side agent-visibility derivation. The
 * former `composeWidgetVisibility` / registry `getVisibleState` slot was
 * deleted 2026-10-03 (C-21, #1112): the client derivation was never called in
 * production — the BFF derives each tab's agent-visible state from
 * `widgetData` itself (`SprkChatAgentFactory.TryDeriveVisibleState`). The
 * active-document ROUTING identity remains owned by the `composeActionBridge`
 * conduit (`registerActiveDocument` → `POST /api/compose/active-document`).
 *
 * @see ./ComposeDirectWidget.tsx — the WorkspaceWidgetProps → ComposeWorkspace adapter
 * @see ./composeWidgetData.ts — the `widgetData.compose` seed shape
 * @see src/solutions/SpaarkeAi/src/main.tsx — bootstrap side-effect caller
 * @see ADR-030 (typed pane events) · ADR-013/039 (no AI-internal types, no new
 *      dispatch endpoint) · ADR-015 (data minimization) · CLAUDE.md §11 (reuse)
 */

import {
  registerWorkspaceWidget,
  // FR-15 (task 050): assistantContract is a REQUIRED registration member —
  // Compose declares an EXPLICIT opt-out (its read/write fidelity is governed
  // separately by ADR-049; outside R3 scope). See COMPOSE_WIDGET_METADATA.
  assistantContractOptOut,
  type WidgetMetadata,
  type WorkspaceWidgetComponent,
} from "@spaarke/ai-widgets";

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
  // FR-B1/FR-C3 (task 020): Compose is the drafting/editing surface — the
  // dedicated 'compose-doc' bucket (distinct from the read-only 'document'
  // viewer bucket used by DocumentViewerWidget/redline-viewer/ContractComparison).
  contextType: "compose-doc",
  // FR-08 enumeration (task 022) → FR-15 ENFORCEMENT (task 050): Compose is
  // enumerated in the widget-type ↔ context-type map above
  // (contextType: 'compose-doc') but is outside R3's overview (FR-06/07 — "all
  // grids + Briefing + Calendar") and per-item (FR-09/11 — Email + Documents
  // only) scope; Compose write/read fidelity is governed separately by ADR-049
  // and untouched by this project (spec Out-of-Scope). Task 022's "deliberately
  // OMITTED" intent is now expressed EXPLICITLY as a required, documented
  // opt-out marker (task 050) instead of silent absence.
  assistantContract: assistantContractOptOut(
    "Compose drafting/editing surface — read/write fidelity governed separately by ADR-049; " +
      "outside R3 overview (FR-06/07) + per-item (FR-09/11) scope (spec Out-of-Scope)."
  ),
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
      }))
  );
}

// Register at module-eval time (top-level side effect) — matches the
// register-workspace-widgets.ts convention.
registerComposeWidget();
