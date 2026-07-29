/**
 * createXrmEmailComposeHandlers.ts
 *
 * Xrm-backed factory that builds the composer's advanced-lookup callbacks
 * (`onLookupRecipients` / `onLookupRecord` / `onAddRelationship`) + the
 * record-lookup catalog for a Dataverse-hosted mount (MDA code page or
 * workspace widget). It mirrors the PROVEN handlers the CommunicationActions
 * PCF (`CommunicationActionsApp.tsx`) builds from `Xrm.Utility.lookupObjects`,
 * lifted into ONE shared factory so the two `EmailWorkspace` mounts (the Email
 * code page + the SpaarkeAi `email` widget) never hand-roll — and never drift —
 * their own copies (NFR-06 dual-mount parity). The PCF keeps its own inline
 * copy (React-16 boundary, out of this factory's scope).
 *
 * Xrm-coupled by design (like `createXrmDataService` / `createXrmNavigationService`
 * in this same lib): it resolves `window.Xrm` via the shared cross-frame
 * `getXrm()` walker. The `EmailComposer` engine itself stays context-agnostic
 * (ADR-012) — these callbacks are injected by the host mount, never imported by
 * the engine.
 */
import { getXrm } from '../../services/xrmGlobal';
import type { IRecordLookupTarget, IPickedRecord, IRecipient } from './EmailComposer.types';

/**
 * Record-lookup targets offered by the composer's "insert a link to a record" /
 * "link a document" tool (owner UAT round 5, RegardingResolver set). Document
 * first (the primary attach case); the rest are linked. Mirrors the PCF's
 * `RECORD_LOOKUP_CATALOG`.
 */
export const EMAIL_RECORD_LOOKUP_CATALOG: IRecordLookupTarget[] = [
  { logicalName: 'sprk_document', displayName: 'Document' },
  { logicalName: 'sprk_matter', displayName: 'Matter' },
  { logicalName: 'sprk_project', displayName: 'Project' },
  { logicalName: 'sprk_event', displayName: 'Event' },
  { logicalName: 'sprk_communication', displayName: 'Communication' },
  { logicalName: 'sprk_workassignment', displayName: 'Work Assignment' },
  { logicalName: 'sprk_invoice', displayName: 'Invoice' },
  { logicalName: 'sprk_budget', displayName: 'Budget' },
  { logicalName: 'sprk_analysis', displayName: 'Analysis' },
  { logicalName: 'sprk_organization', displayName: 'Organization' },
  { logicalName: 'contact', displayName: 'Contact' },
];

// Entity types the connector's "add a relationship" picker offers — the
// regarding-able records (a Document is an attachment, not a regarding
// relationship, so it's excluded). Mirrors the PCF's `REGARDING_ENTITY_TYPES`.
const REGARDING_ENTITY_TYPES = EMAIL_RECORD_LOOKUP_CATALOG.filter(c => c.logicalName !== 'sprk_document').map(
  c => c.logicalName
);

// Primary-email field per recipient entity type (contact → emailaddress1,
// systemuser → internalemailaddress). Mirrors the PCF's `EMAIL_FIELD`.
const RECIPIENT_EMAIL_FIELD: Record<string, string> = {
  contact: 'emailaddress1',
  systemuser: 'internalemailaddress',
};

export interface XrmEmailComposeHandlers {
  recordLookupCatalog: IRecordLookupTarget[];
  onLookupRecipients: (field: 'to' | 'cc' | 'bcc') => Promise<IRecipient[] | null>;
  onLookupRecord: (entityType: string) => Promise<IPickedRecord | null>;
  onAddRelationship: () => Promise<IPickedRecord | null>;
}

/**
 * Build the Xrm-backed compose lookup handlers. `clientUrl` is resolved from
 * `Xrm.Utility.getGlobalContext().getClientUrl()` when not supplied — it is only
 * used to build record deep-link URLs for the picked records (best-effort;
 * absent → a relative link is omitted).
 */
export function createXrmEmailComposeHandlers(options?: { clientUrl?: string }): XrmEmailComposeHandlers {
  const resolveClientUrl = (): string => {
    if (options?.clientUrl) return options.clientUrl;
    try {
      return getXrm()?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';
    } catch {
      return '';
    }
  };

  const buildRecordUrl = (entityType: string, id: string): string | undefined => {
    const base = resolveClientUrl().replace(/\/+$/, '');
    return base ? `${base}/main.aspx?pagetype=entityrecord&etn=${entityType}&id=${id}` : undefined;
  };

  // Attachments "look up a record": a document pick attaches; any other record
  // type is linked in the body. Runs the OOB picker for the chosen type.
  const onLookupRecord = async (entityType: string): Promise<IPickedRecord | null> => {
    const xrm = getXrm();
    if (!xrm?.Utility?.lookupObjects) return null;
    const results = await xrm.Utility.lookupObjects({
      entityTypes: [entityType],
      defaultEntityType: entityType,
      allowMultiSelect: false,
    });
    const picked = results?.[0];
    if (!picked) return null;
    const id = String(picked.id).replace(/[{}]/g, '').toLowerCase();
    return { entityType, id, name: picked.name, url: buildRecordUrl(entityType, id) };
  };

  // Advanced recipient lookup: the OOB people picker over contact + systemuser;
  // the picked record's primary email is resolved into a chip. Multi-select.
  const onLookupRecipients = async (_field: 'to' | 'cc' | 'bcc'): Promise<IRecipient[] | null> => {
    const xrm = getXrm();
    if (!xrm?.Utility?.lookupObjects) return null;
    const results = await xrm.Utility.lookupObjects({
      entityTypes: ['contact', 'systemuser'],
      allowMultiSelect: true,
    });
    if (!results || results.length === 0) return null;
    const recipients: IRecipient[] = [];
    for (const p of results) {
      const field = RECIPIENT_EMAIL_FIELD[p.entityType as string];
      if (!field) continue;
      const id = String(p.id).replace(/[{}]/g, '');
      try {
        const rec = await xrm.WebApi.retrieveRecord(p.entityType, id, `?$select=${field}`);
        const email = rec?.[field];
        if (typeof email === 'string' && email.includes('@')) {
          recipients.push({
            email,
            displayName: p.name,
            resolved: true,
            sourceId: id,
            entityType: p.entityType as 'contact' | 'systemuser',
          });
        }
      } catch (err) {
        console.warn('[EmailCompose] recipient email resolve failed:', err);
      }
    }
    return recipients.length > 0 ? recipients : null;
  };

  // Connector toolbar icon → add a relationship. Runs the OOB lookup across the
  // regarding-able entity types and returns the picked record; the composer
  // shows it in "Related to" and it is written when the email is SENT.
  const onAddRelationship = async (): Promise<IPickedRecord | null> => {
    const xrm = getXrm();
    if (!xrm?.Utility?.lookupObjects) return null;
    const results = await xrm.Utility.lookupObjects({ entityTypes: REGARDING_ENTITY_TYPES, allowMultiSelect: false });
    const picked = results?.[0];
    if (!picked?.id || !picked?.entityType) return null;
    const id = String(picked.id).replace(/[{}]/g, '').toLowerCase();
    return { entityType: picked.entityType, id, name: picked.name, url: buildRecordUrl(picked.entityType, id) };
  };

  return { recordLookupCatalog: EMAIL_RECORD_LOOKUP_CATALOG, onLookupRecipients, onLookupRecord, onAddRelationship };
}
