/**
 * gateAssociationContract.ts
 *
 * FR-05 — the consumer-side ADAPTER for the core's published `GateDecisionV2`
 * association affordance (`Services/Ai/PublicContracts/GateDecisionV2.cs` —
 * `GateAssociationAffordance` / `GateAssociationTargetType`).
 *
 * WHY THIS FILE EXISTS (the "guard" the task POML asks for):
 * FR-05's design intent is that the optional parent-association prompt lives
 * INSIDE the one Tier-2c gate dialog (no bespoke confirmation banner
 * anywhere). That dialog surface is `ActionConfirmationDialog`
 * (`@spaarke/ui-components`'s `SprkChat` family) — OWNED BY A DIFFERENT
 * SURFACE than this task's write boundary (`src/solutions/SpaarkeAi/src/
 * components/**` only; this task does not touch
 * `Spaarke.UI.Components`). The gate-dialog-HOSTING integration (mounting
 * `CreateOnSaveAssociationPrompt` inside that dialog, reading a live
 * `GateDecisionV2` payload from the BFF) is therefore explicitly deferred to
 * whichever task next owns that dialog's wiring — this file is what that
 * task consumes, so it does not need to reinvent the mapping or fork a new
 * banner to bridge the two shapes.
 *
 * This module is INERT until a host imports it — it renders nothing on its
 * own and is not called anywhere yet in this task. It exists so the
 * consumer-selection half (built in this task) and the gate-dialog-hosting
 * half (future task, core-A0-gated) share one mapping, per CLAUDE.md §11
 * (extend, don't fork).
 *
 * @see src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/GateDecisionV2.cs
 *      — the authoritative C# shape this file mirrors (tolerant-reader,
 *      additive-only versioning per that file's header).
 * @see CreateOnSaveAssociationPrompt.tsx — the component this adapter feeds.
 */

import type { AssociationResult } from '@spaarke/ui-components';
import type { ICreateOnSaveAssociationPromptProps } from './CreateOnSaveAssociationPrompt';
import { DOCUMENT_ASSOCIATION_TARGETS, type DocumentAssociationEntityType } from './documentAssociationWrite';

// ---------------------------------------------------------------------------
// Mirrored shapes (camelCase JSON — matches System.Text.Json default
// serialization of the C# records/enums in GateDecisionV2.cs)
// ---------------------------------------------------------------------------

/** Mirrors `GateAssociationTargetType` (C#). */
export type GateAssociationTargetType = 'None' | 'Matter' | 'Project' | 'Invoice' | 'WorkAssignment';

/** Mirrors `GateAssociationAffordance` (C#) — tolerant reader: unknown extra JSON fields are ignored by callers. */
export interface IGateAssociationAffordance {
  allowedTargets: GateAssociationTargetType[];
  selected: GateAssociationTargetType;
  selectedRecordId?: string | null;
  required?: boolean;
}

const _TARGET_TYPE_TO_ENTITY_TYPE: Record<Exclude<GateAssociationTargetType, 'None'>, DocumentAssociationEntityType> =
  {
    Matter: 'sprk_matter',
    Project: 'sprk_project',
    Invoice: 'sprk_invoice',
    WorkAssignment: 'sprk_workassignment',
  };

const _ENTITY_TYPE_TO_TARGET_TYPE: Record<DocumentAssociationEntityType, Exclude<GateAssociationTargetType, 'None'>> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_invoice: 'Invoice',
  sprk_workassignment: 'WorkAssignment',
};

// ---------------------------------------------------------------------------
// Adapter: GateAssociationAffordance -> CreateOnSaveAssociationPrompt props
// ---------------------------------------------------------------------------

/**
 * Maps a `GateAssociationAffordance` (as carried on `GateDecisionV2.association`)
 * onto the props `CreateOnSaveAssociationPrompt` needs. A future host (the
 * task that wires the live Tier-2c gate dialog) calls this ONCE per render
 * instead of hand-rolling the mapping — keeping the affordance's
 * `allowedTargets` restriction and any pre-selected value honored exactly as
 * the core engine declared them, with no local reinterpretation.
 *
 * Never throws: an affordance with an unrecognized/empty `allowedTargets`
 * degrades to the full four-target default (fail-open on the UI restriction
 * only — the write path in `documentAssociationWrite.ts` still validates the
 * final selection).
 */
export function mapGateAssociationAffordanceToPromptDefaults(
  affordance: IGateAssociationAffordance | null | undefined
): Pick<ICreateOnSaveAssociationPromptProps, 'allowedTargets' | 'initialValue'> {
  if (!affordance) {
    return { allowedTargets: DOCUMENT_ASSOCIATION_TARGETS, initialValue: null };
  }

  const allowed = affordance.allowedTargets
    .filter((t): t is Exclude<GateAssociationTargetType, 'None'> => t !== 'None')
    .map(t => _TARGET_TYPE_TO_ENTITY_TYPE[t])
    .filter((entityType): entityType is DocumentAssociationEntityType => Boolean(entityType));

  const allowedTargets =
    allowed.length > 0
      ? DOCUMENT_ASSOCIATION_TARGETS.filter(t => allowed.includes(t.entityType as DocumentAssociationEntityType))
      : DOCUMENT_ASSOCIATION_TARGETS;

  const initialValue: AssociationResult | null =
    affordance.selected !== 'None' && affordance.selectedRecordId
      ? {
          entityType: _TARGET_TYPE_TO_ENTITY_TYPE[affordance.selected as Exclude<GateAssociationTargetType, 'None'>],
          recordId: affordance.selectedRecordId,
          recordName: '',
        }
      : null;

  return { allowedTargets, initialValue };
}

/**
 * Inverse mapping — the user's `AssociationResult` selection back onto a
 * `GateAssociationAffordance`-shaped payload, for a future host that needs to
 * report the resolved selection back through the gate contract (e.g. to
 * confirm the gate with the chosen association attached).
 */
export function mapAssociationResultToGateAffordance(
  association: AssociationResult | null | undefined,
  allowedTargets: readonly { entityType: string }[] = DOCUMENT_ASSOCIATION_TARGETS
): IGateAssociationAffordance {
  const allowed: GateAssociationTargetType[] = [
    'None',
    ...allowedTargets.map(
      t => _ENTITY_TYPE_TO_TARGET_TYPE[t.entityType as DocumentAssociationEntityType]
    ).filter((t): t is Exclude<GateAssociationTargetType, 'None'> => Boolean(t)),
  ];

  if (!association) {
    return { allowedTargets: allowed, selected: 'None', selectedRecordId: null, required: false };
  }

  const selected = _ENTITY_TYPE_TO_TARGET_TYPE[association.entityType as DocumentAssociationEntityType] ?? 'None';

  return {
    allowedTargets: allowed,
    selected,
    selectedRecordId: association.recordId,
    required: false,
  };
}
