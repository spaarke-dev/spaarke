/**
 * Local types for the CommunicationConnections PCF.
 *
 * A trimmed copy of the shape the review surface needs — mirrors
 * `code-pages/CommunicationPage/src/types/communication.ts` (the prototype
 * design source), narrowed to what the PCF reads from the control context.
 */

/**
 * `sprk_associationstatus` option-set VALUES (task 002, verified via Dataverse MCP).
 * Records in a REVIEW status flow through the association review surface (FR-17);
 * `Resolved` is already filed (auto-filed or confirmed).
 */
export enum AssociationStatus {
  Resolved = 100000000,
  PendingReview = 100000001,
  Unresolved = 100000002, // legacy → treated as Pending Review
  Suggested = 100000003,
  Ambiguous = 100000004,
}

/** Statuses that surface the review affordances (need human confirm/correct). */
export const REVIEW_STATUSES: readonly number[] = [
  AssociationStatus.Suggested,
  AssociationStatus.PendingReview,
  AssociationStatus.Unresolved,
  AssociationStatus.Ambiguous,
] as const;

export function isReviewStatus(status: number | null | undefined): boolean {
  return status != null && REVIEW_STATUSES.includes(status);
}

/**
 * The subset of the host `sprk_communication` record the review surface renders.
 * Assembled by the App from the PCF control context (bound params + Xrm.Page id),
 * NOT retrieved as a whole row.
 */
export interface ICommunicationRecord {
  sprk_communicationid: string;
  sprk_associationstatus?: number | null;
  /** Association Engine decision trail (task 015 JSON) — read by the review surface (FR-17). */
  sprk_associationprovenance?: string | null;
  /** Denormalized regarding (ADR-024) — present when Resolved. */
  sprk_regardingrecordname?: string | null;
}
