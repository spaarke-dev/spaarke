/**
 * EntityFormLaunch.ts — Entity form command bar button handler for SpaarkeAi.
 *
 * This file is a Dataverse ribbon script invocation entry point ONLY.
 * It extracts entity context from the Xrm form context and delegates to
 * launch-resolver.ts. It contains no URL construction, no business logic,
 * and no direct Xrm.Navigation calls.
 *
 * Ribbon XML registration:
 *   Library:      $webresource:sprk_spaarkeai_entityformlaunch
 *   FunctionName: Sprk.SpaarkeAi.EntityFormLaunch.openFromEntityForm
 *   CrmParameters:
 *     <CrmParameter Value="PrimaryControl" />
 *
 * Behaviour: Extracts the current record's entityLogicalName and entityId
 * from PrimaryControl (the Xrm FormContext), then opens sprk_spaarkeai as
 * a modal dialog (target: 2) pre-seeded with the record's context. The AI
 * assistant loads playbooks and tools scoped to the entity type and record.
 *
 * Supported entities:
 *   sprk_matter     — Legal matter record
 *   contact         — Contact record
 *   sprk_document   — Document record (opens with document context)
 *   (any entity)    — Generic entity context (entity type + ID only)
 *
 * @see ADR-006 — Ribbon scripts must be invocation-only (no business logic)
 * @see launch-resolver.ts — All launch logic and URL assembly
 * @see docs/guides/spaarkeai-launch-points.md — Full launch point documentation
 */

/// <reference path="./xrm-globals.d.ts" />

import { openSpaarkeAi } from "../utils/launch-resolver";

/**
 * Opens the SpaarkeAi Code Page as a modal dialog from an entity form command
 * bar button. Extracts the current record's logical name and ID from the
 * Xrm form context (PrimaryControl) and passes them as entity context.
 *
 * Called by the ribbon command definition:
 *   FunctionName: Sprk.SpaarkeAi.EntityFormLaunch.openFromEntityForm
 *   CrmParameters: <CrmParameter Value="PrimaryControl" />
 *
 * @param primaryControl - The Xrm FormContext passed by the ribbon framework
 *   via the PrimaryControl CrmParameter. Provides access to the entity's
 *   logical name and record ID.
 */
export function openFromEntityForm(primaryControl: Xrm.FormContext): void {
  const entityLogicalName = primaryControl.data.entity.getEntityName();
  const entityId = primaryControl.data.entity.getId();

  openSpaarkeAi({ entityLogicalName, entityId }, 2);
}
