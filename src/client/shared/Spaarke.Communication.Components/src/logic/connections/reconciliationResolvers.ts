/**
 * reconciliationResolvers.ts — the shared `resolveReview` / `resolveRegarding`
 * wiring for every ReconciliationWorkspace mount.
 *
 * Extracted (UAT Fix #6) from `CommunicationReconciliation/src/main.tsx` so the
 * THREE mounts of `ReconciliationWorkspace` — the standalone code page, the
 * SpaarkeAi direct widget, and the LegalWorkspace `reconciliation` section shim
 * (`reconciliation.registration.ts`) — share ONE copy of this logic and cannot
 * drift (§11). Host-specific adapter construction (the Xrm `webApi` bridge)
 * stays in each host; only these host-agnostic mappers are shared.
 *
 * - `buildResolveReview` maps a grid row → `EmailConnectionsReview` props via the
 *   reused ADR-024 single regarding-write path (`writeContext.hostRecordId` ===
 *   `communicationId`).
 * - `resolveRegarding` maps a grid row → its CONFIRMED `ReconcileRegarding | null`
 *   (NFR-10 gate) via the shipped pure `derivePrimaryReview` reducer.
 */
import { ASSOCIATION_STATUS_RESOLVED_VALUE, COMMUNICATION_REGARDING_FIELDS } from './provenance';
import type { EmailWorkspaceWebApi } from '../../components/EmailWorkspace/EmailWorkspace.types';
import type { EmailConnectionsReviewProps } from '../../components/EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';
import type { ReconcileRegarding } from '../../components/ReconcileTabs/FieldUpdateReconcileTab';

const COMMUNICATION_ENTITY = 'sprk_communication';
const PRIMARY_ID_FIELD = 'sprk_communicationid';

/** Read a string field off an untyped grid row. */
function str(record: Record<string, unknown>, field: string): string | null {
  const v = record[field];
  return typeof v === 'string' ? v : v == null ? null : String(v);
}

/** Read a numeric field off an untyped grid row (option-set value). */
function num(record: Record<string, unknown>, field: string): number | null {
  const v = record[field];
  if (typeof v === 'number') return v;
  if (v == null || v === '') return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
}

/**
 * Row → `EmailConnectionsReview` props (052 Related-to picker). `webApi` is the
 * host's Xrm.WebApi-backed bridge; both the additive `writeContext` and the
 * `pickerWebApi` point at it. `onChanged` fires after a confirm.
 */
export function buildResolveReview(
  webApi: EmailWorkspaceWebApi,
  onChanged: () => void
): (record: Record<string, unknown>) => EmailConnectionsReviewProps {
  return record => {
    const id = str(record, PRIMARY_ID_FIELD) ?? '';
    return {
      communicationId: id,
      associationStatus: num(record, 'sprk_associationstatus'),
      associationProvenanceJson: str(record, 'sprk_associationprovenance'),
      regardingRecordName: str(record, 'sprk_regardingrecordname'),
      regardingRecordNumber: str(record, 'sprk_regardingrecordnumber'),
      regardingRecordType: str(record, 'sprk_regardingrecordtypename'),
      writeContext: {
        webApi,
        hostEntity: COMMUNICATION_ENTITY,
        hostRecordId: id,
      },
      pickerWebApi: webApi,
      onAssociationsChanged: onChanged,
    };
  };
}

/** Bare, lowercased GUID (braces stripped) or null. */
function normGuid(value: string | null): string | null {
  if (!value) return null;
  const bare = value.replace(/[{}]/g, '').trim().toLowerCase();
  return bare.length === 36 ? bare : null;
}

/**
 * Read a typed regarding lookup's target GUID off a full `retrieveRecord` row.
 * Xrm.WebApi (and the BFF client) surface single-valued lookups as the OData
 * annotation `_<field>_value` (e.g. `_sprk_regardingmatter_value`). The grid
 * query does NOT select these — the workspace's on-open enrichment fetch merges
 * the full record so they are present by the time the gate is evaluated.
 */
function regardingLookupValue(record: Record<string, unknown>, field: string): string | null {
  return normGuid(str(record, `_${field}_value`));
}

/**
 * Row → CONFIRMED `ReconcileRegarding | null` (NFR-10 gate). The tabs un-gate
 * only when the record is Resolved AND a typed regarding lookup with a real
 * entity + GUID can be reconstructed from the row.
 *
 * Why not `derivePrimaryReview` here: a manually-confirmed primary is carried in
 * the DENORM fields (`sprk_regardingrecordname/number/typename`), which have NO
 * entity logical name or target GUID — so the reducer's confirmed primary comes
 * back with `entity: ''`, `targetId: ''` and the gate could never open (the
 * bug behind Fields/Tasks staying disabled after a confirm). Instead we read the
 * actual typed regarding lookups the engine/UI wrote (`sprk_regardingmatter`,
 * `sprk_regardingperson`, …) — the field name yields the entity deterministically
 * (`COMMUNICATION_REGARDING_FIELDS`) and the `_value` annotation yields the GUID.
 * The lookup matching the denorm primary id is preferred; otherwise the first
 * populated regarding lookup scopes the tabs.
 */
export function resolveRegarding(record: Record<string, unknown>): ReconcileRegarding | null {
  if (num(record, 'sprk_associationstatus') !== ASSOCIATION_STATUS_RESOLVED_VALUE) return null;

  const primaryId = normGuid(str(record, 'sprk_regardingrecordid'));
  let firstFiled: ReconcileRegarding | null = null;
  for (const { field, entityType } of COMMUNICATION_REGARDING_FIELDS) {
    const recordId = regardingLookupValue(record, field);
    if (!recordId) continue;
    if (!firstFiled) firstFiled = { entityType, recordId };
    // Prefer the typed lookup that IS the denorm primary (owns the display fields).
    if (primaryId && recordId === primaryId) return { entityType, recordId };
  }
  return firstFiled;
}
