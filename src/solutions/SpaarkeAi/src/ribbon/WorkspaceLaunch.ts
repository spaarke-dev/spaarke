/**
 * WorkspaceLaunch.ts — Workspace command bar button handler for SpaarkeAi.
 *
 * This file is a Dataverse ribbon script invocation entry point ONLY.
 * It contains no URL construction, no business logic, and no direct
 * Xrm.Navigation calls — all of that lives in launch-resolver.ts.
 *
 * Ribbon XML registration:
 *   Library:      $webresource:sprk_spaarkeai_workspacelaunch
 *   FunctionName: Sprk.SpaarkeAi.WorkspaceLaunch.openFromWorkspace
 *   CrmParameters: (none — workspace button has no entity context)
 *
 * Behaviour: Opens sprk_spaarkeai as a full-page (target: 1) workspace
 * without any entity context. The AI assistant opens in general mode,
 * ready for a new conversation without a pre-seeded matter or document.
 *
 * @see ADR-006 — Ribbon scripts must be invocation-only (no business logic)
 * @see launch-resolver.ts — All launch logic and URL assembly
 * @see docs/guides/spaarkeai-launch-points.md — Full launch point documentation
 */

/// <reference path="./xrm-globals.d.ts" />

import { openSpaarkeAi } from "../utils/launch-resolver";

/**
 * Opens the SpaarkeAi Code Page from the Spaarke workspace global navigation
 * command bar. No entity context is passed — the AI assistant opens in
 * general (unscoped) mode as a full-page navigation.
 *
 * Called by the ribbon command definition:
 *   FunctionName: Sprk.SpaarkeAi.WorkspaceLaunch.openFromWorkspace
 */
export function openFromWorkspace(): void {
  openSpaarkeAi({}, 1);
}
