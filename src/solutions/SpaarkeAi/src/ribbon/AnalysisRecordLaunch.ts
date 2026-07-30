/**
 * AnalysisRecordLaunch.ts — `sprk_matter`/`sprk_project` form "Analysis" command-bar handlers.
 *
 * Project:  ai-advanced-capabilities-analysis-hub-r1
 * Task:     052 — Extend openSpaarkeAi (analysisId/worktype/regarding) + ribbon launcher
 * Phase:    Phase 5 — Entry model + record integration
 *
 * Behaviour:
 *   Two ribbon-invocation entry points, both delegating to `openSpaarkeAi` (never
 *   `surfaceLaunchRegistry` — ADR-039 / spec §13.3, record-driven opens are the
 *   deterministic `openSpaarkeAi` deep-link, not the reactive in-chat registry):
 *
 *     1. `openNewAnalysisFromRecord` — "New analysis" command on the `sprk_matter` /
 *        `sprk_project` form command bar (entry case 2b). Reads the open record's
 *        id + logical name from the Xrm form context, then opens the SpaarkeAi modal
 *        with `worktype` (supplied by the ribbon's CrmParameter — this file has no
 *        opinion on which work type a given button represents) and `regarding`
 *        set to the parent record. No `analysisId` is passed, so the three-pane
 *        shell shows the "Create new analysis" hub cards.
 *
 *     2. `openExistingAnalysis` — invoked when an existing analysis is opened from
 *        the record's Analyses subgrid (task 051's form subgrid; entry case 2d).
 *        Takes the target `sprk_analysis` record id directly (resolved by the
 *        ribbon/grid command wiring — e.g. the selected row's id) and opens the
 *        SpaarkeAi modal with `analysisId` set, so the three-pane shell resolves
 *        the existing analysis and its bound session with NO hub cards.
 *
 * Architecture (ADR-006 — ribbon scripts are invocation-only):
 *   This file contains ZERO URL construction or navigation logic. All URL assembly,
 *   parameter-encoding, and `Xrm.Navigation.navigateTo` plumbing lives in
 *   `utils/launch-resolver.ts` (`openSpaarkeAi`). No exceptions propagate out of
 *   either exported function — ribbon callbacks must never throw or the entire
 *   command bar errors out (mirrors `DocumentComposeLaunch.openInCompose`).
 *
 * Ribbon XML registration (deployed by task 071 — this task authors the JS only):
 *   Library:      $webresource:sprk_spaarkeai_analysisrecordlaunch
 *   New:          FunctionName: Sprk.SpaarkeAi.AnalysisRecordLaunch.openNewAnalysisFromRecord
 *                 CrmParameters: <CrmParameter Value="PrimaryControl" />
 *                                <CrmParameter Value="StaticValue" StaticValue="<worktype>" />
 *   Existing:     FunctionName: Sprk.SpaarkeAi.AnalysisRecordLaunch.openExistingAnalysis
 *                 CrmParameters: <CrmParameter Value="SelectedControlSelectedItemIds" /> (or
 *                                equivalent grid-selection parameter resolved by task 071)
 *
 * @see ADR-006 — Ribbon scripts must be invocation-only (no business logic)
 * @see ADR-028 — Spaarke Auth v2 (no manual token handling at the launch boundary)
 * @see ADR-039 — Grounded Execution & Closed Catalogs (record-driven opens via
 *      `openSpaarkeAi`, never `surfaceLaunchRegistry`)
 * @see launch-resolver.ts — `openSpaarkeAi` does the actual modal open
 * @see ribbon/DocumentComposeLaunch.ts — the mirrored ribbon-script precedent
 * @see projects/ai-advanced-capabilities-analysis-hub-r1/tasks/052-extend-openspaarkeai-ribbon-launcher.poml
 * @see projects/ai-advanced-capabilities-analysis-hub-r1/tasks/051-record-analysis-subgrid-forms.poml
 */

/// <reference path="./xrm-globals.d.ts" />

import { openSpaarkeAi } from "../utils/launch-resolver";

/**
 * Opens the SpaarkeAi modal directly into the "Create new analysis" hub, pre-seeded
 * with the open `sprk_matter`/`sprk_project` record as the regarding context.
 *
 * Called by the ribbon command definition:
 *   FunctionName: Sprk.SpaarkeAi.AnalysisRecordLaunch.openNewAnalysisFromRecord
 *   CrmParameters: <CrmParameter Value="PrimaryControl" />
 *                  <CrmParameter Value="StaticValue" StaticValue="<worktype>" />
 *
 * Failure modes:
 *   - Form is in "new" state (no GUID yet): logs a warn and exits without opening
 *     anything. The ribbon enable rule should prevent this; the runtime guard is
 *     defensive (mirrors `DocumentComposeLaunch.openInCompose`).
 *
 * No exceptions propagate out — ribbon callbacks must never throw or the entire
 * command bar errors out.
 *
 * @param primaryControl - The Xrm FormContext passed by the ribbon framework via
 *   the PrimaryControl CrmParameter. Supplies the open record's id + logical name.
 * @param worktype - Analysis work type (Choice value, e.g. `SprkAnalysisWorkType`)
 *   supplied by the ribbon command's StaticValue CrmParameter.
 *
 *   ⚠️ SEAM NOTE (2026-07-29 audit): the specific VALUE is not yet load-bearing.
 *   `openSpaarkeAi` / `main.tsx` treat any NON-EMPTY `worktype` as "open the hub in
 *   new-analysis mode", and `AnalysisHubWidget` always renders all three cards — it
 *   does NOT pre-select a card from this value. So today this parameter functions as
 *   a boolean "new mode" flag; a Matter/Project ribbon button's StaticValue only needs
 *   to be non-empty. Making a button open a SPECIFIC work-type card (owner design
 *   intent — the "Create Analysis" card-selector) is a future change that would consume
 *   this value in the hub's card-render path. This function does not choose or default
 *   the value — that decision lives in the ribbon/catalog data, never in this script.
 */
export function openNewAnalysisFromRecord(
  primaryControl: Xrm.FormContext,
  worktype: string,
): void {
  let entityId: string;
  try {
    entityId = primaryControl.data.entity.getId();
  } catch (err) {
    console.warn(
      "[AnalysisRecordLaunch] Could not read record id from primaryControl:",
      err,
    );
    return;
  }

  // Strip braces (matches existing entityId handling in launch-resolver).
  const normalizedEntityId = entityId.replace(/^\{|\}$/g, "");
  if (!normalizedEntityId) {
    console.warn(
      "[AnalysisRecordLaunch] Record id is empty (likely an unsaved form). Ignoring New Analysis command.",
    );
    return;
  }

  if (!worktype) {
    // A missing/empty worktype would silently degrade to the default cold-load
    // instead of opening the "Create new analysis" hub (main.tsx's analysisMode
    // derivation requires a non-empty worktype). Fail loudly rather than opening
    // a modal that doesn't do what the button promised.
    console.warn(
      "[AnalysisRecordLaunch] No worktype supplied to the New Analysis command. Ignoring — check the ribbon's StaticValue CrmParameter.",
    );
    return;
  }

  const entityLogicalName = primaryControl.data.entity.getEntityName();

  openSpaarkeAi(
    {
      entityLogicalName,
      entityId: normalizedEntityId,
      worktype,
      // `regarding` carried for symmetry with the entity-context channel above
      // (which is authoritative — see launch-resolver.ts's URL parameter contract).
      regarding: normalizedEntityId,
    },
    2,
  );
}

/**
 * Opens the SpaarkeAi modal directly into an EXISTING `sprk_analysis` record,
 * resolved from a record's Analyses subgrid (task 051 — entry case 2d).
 *
 * Called by the ribbon/grid command definition wiring resolved in task 071
 * (e.g. `SelectedControlSelectedItemIds` from the subgrid selection).
 *
 * Failure modes:
 *   - No analysis id resolved (empty selection): logs a warn and exits without
 *     opening anything.
 *
 * No exceptions propagate out — ribbon callbacks must never throw or the entire
 * command bar errors out.
 *
 * @param analysisId - GUID of the `sprk_analysis` record to open (braces stripped
 *   the same way `entityId` is elsewhere in this module).
 */
export function openExistingAnalysis(analysisId: string): void {
  const normalizedAnalysisId = (analysisId ?? "").replace(/^\{|\}$/g, "");
  if (!normalizedAnalysisId) {
    console.warn(
      "[AnalysisRecordLaunch] No analysis id resolved from the subgrid selection. Ignoring Open Analysis command.",
    );
    return;
  }

  openSpaarkeAi({ analysisId: normalizedAnalysisId }, 2);
}
