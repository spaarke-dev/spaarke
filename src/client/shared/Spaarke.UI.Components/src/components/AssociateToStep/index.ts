/**
 * AssociateToStep — barrel export
 *
 * Reusable wizard step for optionally associating a newly created record
 * with an existing Dataverse parent record via the lookup side pane.
 *
 * @see AssociateToStep.tsx  — Component implementation
 * @see types.ts             — AssociateToStepProps, AssociationResult, EntityTypeOption
 * @see ADR-012              — Shared Component Library
 */

export { AssociateToStep } from "./AssociateToStep";
export type {
    AssociateToStepProps,
    AssociationResult,
    EntityTypeOption,
} from "./types";
