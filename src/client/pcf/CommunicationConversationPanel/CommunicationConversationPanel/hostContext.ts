/**
 * Pure derivation of the `(entityType, id)` regarding context this control
 * should render, given the host form's entity logical name + record id.
 * Extracted so the placement logic is testable without Xrm/auth/webAPI —
 * mirrors the sibling `CommunicationTimelineRegarding/hostContext.ts` (task 021
 * / FR-04). Surface 1 (task 030 / FR-13) uses the SAME regarding mechanism —
 * NO second regarding path (NFR-06 / ADR-024): the host record itself IS the
 * regarding target, so the panel reads the entity name + record id straight
 * from context and passes them, unchanged, to the shared read API + the
 * record-filtered modal.
 */

/**
 * The 11 ADR-024 regarding-family entities, mirroring `RegardingFieldMap.All`
 * (BFF, single source of truth). Kept here as a placement guard: a control
 * accidentally placed on an unsupported entity renders an "unavailable" notice
 * instead of guessing at a regarding target the by-regarding endpoint does not
 * support.
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
