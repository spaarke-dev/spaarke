/**
 * Pure derivation of the `(entityType, id)` regarding context this control
 * should render, given the host form's entity logical name + record id.
 * Extracted so the placement logic is testable without Xrm/auth/webAPI —
 * mirrors the sibling `CommunicationTimeline/hostContext.ts`'s
 * `resolveThreadId` extraction rationale (task 021 / FR-04).
 *
 * Unlike the R1 thread-id control (host IS `sprk_communication` /
 * `sprk_communicationthread`, resolved via a lookup indirection), this
 * regarding-mode variant is placed directly on one of the 11 ADR-024
 * regarding-family host entities (`RegardingFieldMap.All` —
 * `Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs`). The
 * host record itself IS the regarding target — no lookup indirection
 * needed, just the entity name + record id straight from context.
 */

/**
 * The 11 ADR-024 regarding-family entities, mirroring `RegardingFieldMap.All`
 * (BFF, single source of truth). Kept here as a placement guard: a control
 * accidentally placed on an unsupported entity renders an "unavailable"
 * notice instead of guessing at a regarding target the by-regarding endpoint
 * does not support.
 */
export const REGARDING_FAMILY_ENTITIES: readonly string[] = [
  'sprk_matter',
  'sprk_project',
  'sprk_invoice',
  'sprk_servicerequest',
  'sprk_workassignment',
  'sprk_event',
  'sprk_budget',
  'sprk_analysis',
  'sprk_organization',
  'account',
  'contact',
];

export interface RegardingContext {
  entityType: string;
  id: string;
}

export function resolveRegardingContext(
  hostEntityName: string | undefined,
  hostRecordId: string | undefined
): RegardingContext | undefined {
  if (!hostEntityName || !hostRecordId) return undefined;
  if (!REGARDING_FAMILY_ENTITIES.includes(hostEntityName)) return undefined;
  return { entityType: hostEntityName, id: hostRecordId };
}
