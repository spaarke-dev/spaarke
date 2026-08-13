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
import { derivePrimaryReview } from './provenance';
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

/**
 * Row → CONFIRMED `ReconcileRegarding | null` (NFR-10 gate). Only a green
 * (Resolved) primary with a resolvable typed entity + id enables the
 * Fields/Tasks tabs; anything else stays gated.
 */
export function resolveRegarding(record: Record<string, unknown>): ReconcileRegarding | null {
  const model = derivePrimaryReview(
    str(record, 'sprk_associationprovenance'),
    num(record, 'sprk_associationstatus'),
    undefined,
    {
      recordName: str(record, 'sprk_regardingrecordname'),
      recordNumber: str(record, 'sprk_regardingrecordnumber'),
      entityType: null,
      recordTypeLabel: str(record, 'sprk_regardingrecordtypename'),
    }
  );
  const primary = model.state === 'confirmed' ? model.primary : undefined;
  if (primary && primary.entity && primary.targetId) {
    return { entityType: primary.entity, recordId: primary.targetId };
  }
  return null;
}
