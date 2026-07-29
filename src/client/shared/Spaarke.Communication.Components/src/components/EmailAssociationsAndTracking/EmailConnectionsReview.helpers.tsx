/**
 * Constants + pure helpers for the ASSOCIATIONS review view (task 035). Split
 * out of `EmailConnectionsReview.tsx` at code-review time (Step 9.5) purely
 * to keep the main component file under this repo's review-metric line
 * thresholds — no behavior change.
 */
import * as React from 'react';
import {
  Briefcase20Regular,
  Building20Regular,
  Person20Regular,
  Receipt20Regular,
  CalendarLtr20Regular,
  DocumentText20Regular,
} from '@fluentui/react-icons';
import { TODO_REGARDING_CATALOG, type RecordTypeCatalogEntry } from '@spaarke/ui-components';
import { entityLabel, type ProvenanceDoc } from '../../logic/connections';

// `sprk_associationstatus` = Resolved (task 002 verified integer; mirrors the
// same constant in `ConnectionsWriteHandler.ts` — kept local here, not
// imported, since it is a plain Dataverse OptionSet value, not write logic).
export const ASSOCIATION_STATUS_RESOLVED = 100000000;

// Synthesized when there is no engine provenance at all (every association
// was filed manually) so `deriveConnections` still has a doc to walk — the
// candidates array is empty, so it contributes nothing beyond letting
// `mergeFiledConnections` render the actually-filed rows.
export const EMPTY_PROVENANCE: ProvenanceDoc = {
  version: 1,
  direction: '',
  decision: {
    status: '',
    autoFiled: false,
    killSwitchEnabled: false,
    autoFileThreshold: 0.85,
    topDeterministicConfidence: 0,
    topConfidence: 0,
    aiInvolved: false,
    reason: '',
  },
  rungsFired: [],
  candidates: [],
  signals: [],
};

const ENTITY_ICON: Record<string, React.ReactElement> = {
  sprk_matter: <Briefcase20Regular />,
  sprk_organization: <Building20Regular />,
  account: <Building20Regular />,
  contact: <Person20Regular />,
  sprk_invoice: <Receipt20Regular />,
  sprk_event: <CalendarLtr20Regular />,
};

export function entityIcon(entity: string): React.ReactElement {
  return ENTITY_ICON[entity] ?? <DocumentText20Regular />;
}

/** Default "Link another record" catalog, derived from the canonical `TODO_REGARDING_CATALOG` (no live `sprk_recordtype_ref` query needed — `PolymorphicPicker` only uses `logicalName` functionally; `recordTypeRefId`/`regardingField` are display/key metadata). */
export const DEFAULT_LINK_CATALOG: RecordTypeCatalogEntry[] = TODO_REGARDING_CATALOG.map(c => ({
  recordTypeRefId: c.entityType,
  displayName: entityLabel(c.entityType),
  logicalName: c.entityType,
  regardingField: c.lookupAttribute,
}));

export function fieldFor(entityType: string): string {
  return TODO_REGARDING_CATALOG.find(c => c.entityType === entityType)?.lookupAttribute ?? `sprk_regarding_${entityType}`;
}
