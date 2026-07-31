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
import { EntityCreationService, type AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { IUploadedFile, UploadedFileType } from '../FileUpload/fileUploadTypes';
import type {
  IRecordLookupTarget,
  IPickedRecord,
  IRecipient,
  IEmailTemplateSummary,
  IEmailTemplateRenderResult,
  IEmailAiDraftRequest,
  IEmailAiDraftResult,
} from './EmailComposer.types';

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
  /**
   * Upload a locally-picked file to SPE + create a governed `sprk_document`, so it
   * flows into the send payload (owner UAT 2026-07-30, item 9b). Only present when
   * `authenticatedFetch` + `bffBaseUrl` are supplied — omitted otherwise (local
   * picks stay display-only). Reuses the proven wizard upload path (single SPE
   * container per deployment, resolved from the current user's owning BU).
   */
  onUploadLocalAttachment?: (file: File) => Promise<{ documentId: string; driveItemId?: string; linkUrl?: string }>;
  /**
   * List OOB Dataverse `template` records for the compose template picker (Wave E). Xrm-only —
   * always present. The composer's template button also needs {@link onRenderEmailTemplate}
   * (auth + BFF), so the button stays hidden when render is unavailable (e.g. the harness).
   */
  onListEmailTemplates: () => Promise<IEmailTemplateSummary[]>;
  /**
   * Render a chosen template via the BFF (`POST /api/communications/template/render`), merging
   * `{!entity.field}` codes from the primary regarding. Only present when `authenticatedFetch` +
   * `bffBaseUrl` are supplied — omitted otherwise (composer hides the template button).
   */
  onRenderEmailTemplate?: (args: {
    templateId: string;
    regardingEntityType?: string;
    regardingRecordId?: string;
  }) => Promise<IEmailTemplateRenderResult>;
  /**
   * Generate/refine the message body via AI (Wave E). Calls the BFF
   * `POST /api/communications/draft`. Only present when `authenticatedFetch` + `bffBaseUrl` are
   * supplied — omitted otherwise (composer hides the sparkle button).
   */
  onDraftWithAi?: (req: IEmailAiDraftRequest) => Promise<IEmailAiDraftResult>;
  /**
   * Resolve a recipient-openable SPE sharing link for a `sprk_document` (R2 item 12) via the BFF
   * (`POST /api/documents/{id}/share-link`). Only present when `authenticatedFetch` + `bffBaseUrl`
   * are supplied — omitted otherwise (links keep their original internal URL).
   */
  onResolveShareLink?: (documentId: string) => Promise<string | null>;
}

/** Best-effort SPE upload never reads this — map for display parity only, default 'pdf'. */
function deriveUploadedFileType(mimeType: string): UploadedFileType {
  if (mimeType.includes('spreadsheet') || mimeType.includes('excel')) return 'xlsx';
  if (mimeType.includes('word') || mimeType.includes('document')) return 'docx';
  return 'pdf';
}

/**
 * Build the Xrm-backed compose lookup handlers. `clientUrl` is resolved from
 * `Xrm.Utility.getGlobalContext().getClientUrl()` when not supplied — it is only
 * used to build record deep-link URLs for the picked records (best-effort;
 * absent → a relative link is omitted).
 */
/**
 * Resolve the signed-in user's mailbox address (item 3) for the compose "From:" row.
 * Walks `Xrm.Utility.getGlobalContext().userSettings.userId` → `systemuser.internalemailaddress`.
 * Returns `undefined` outside an MDA host or if the email is unset — callers then fall back
 * to a generic label. Best-effort; never throws.
 */
export async function resolveCurrentUserEmail(): Promise<string | undefined> {
  try {
    const xrm = getXrm();
    const userId: string | undefined = xrm?.Utility?.getGlobalContext?.()?.userSettings?.userId;
    if (!xrm?.WebApi || !userId) return undefined;
    const clean = String(userId).replace(/[{}]/g, '');
    const rec = await xrm.WebApi.retrieveRecord('systemuser', clean, '?$select=internalemailaddress');
    const email = rec?.internalemailaddress;
    return typeof email === 'string' && email.includes('@') ? email : undefined;
  } catch {
    return undefined;
  }
}

export function createXrmEmailComposeHandlers(options?: {
  clientUrl?: string;
  /** Auth-aware fetch (ADR-028) — required to enable new-file upload (item 9b). */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** BFF base URL (no `/api` suffix) — required to enable new-file upload (item 9b). */
  bffBaseUrl?: string;
}): XrmEmailComposeHandlers {
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

  // New-file upload (item 9b): a locally-picked file → SPE bytes → governed
  // `sprk_document`, so it becomes send-eligible (the send path is documentId-only,
  // ADR-045 — there is no raw-bytes attach). Reuses the wizard's proven services.
  // Only wired when the host supplies auth + BFF URL (else local picks stay display-only).
  const { authenticatedFetch, bffBaseUrl } = options ?? {};
  const onUploadLocalAttachment =
    authenticatedFetch && bffBaseUrl
      ? async (file: File): Promise<{ documentId: string; driveItemId?: string; linkUrl?: string }> => {
          const xrm = getXrm();
          if (!xrm?.WebApi) throw new Error('Dataverse is unavailable — cannot upload the attachment.');

          // Single SPE container per deployment, resolved from the current user's owning
          // Business Unit (`businessunit.sprk_containerid`). No per-record container.
          const userId: string | undefined = xrm.Utility?.getGlobalContext?.()?.userSettings?.userId;
          if (!userId) throw new Error('Could not resolve the current user for upload.');
          const bu = await EntityCreationService.resolveUserBuDefaults(xrm.WebApi, userId);
          if (!bu.containerId) {
            throw new Error('No document storage container is configured for your business unit.');
          }

          const svc = new EntityCreationService(xrm.WebApi, authenticatedFetch, bffBaseUrl);
          const uploaded: IUploadedFile = {
            id: `email-attach:${file.name}:${file.size}`,
            name: file.name,
            sizeBytes: file.size,
            fileType: deriveUploadedFileType(file.type),
            file,
          };
          const uploadResult = await svc.uploadFilesToSpe(bu.containerId, [uploaded]);
          const meta = uploadResult.uploadedFiles[0];
          if (!meta) throw new Error(uploadResult.errors[0]?.error ?? 'File upload failed.');

          // Create the governed `sprk_document` UNASSOCIATED (the email may have no persisted
          // regarding yet). Canonical container field is `sprk_graphdriveid`; `sprk_containerid`
          // stays NULL on the document (design INV). Mirrors DocumentRecordService's unassociated payload.
          const payload: Record<string, unknown> = {
            sprk_documentname: meta.name,
            sprk_filename: meta.name,
            sprk_filesize: meta.size,
            sprk_graphitemid: meta.id,
            sprk_graphdriveid: bu.containerId,
            sprk_filepath: meta.webUrl ?? null,
            sprk_hasfile: true,
          };
          if (bu.searchIndexName) payload.sprk_searchindexname = bu.searchIndexName;
          if (bu.searchIndexId) {
            payload['sprk_AI_Search_Index@odata.bind'] = `/sprk_aisearchindexes(${bu.searchIndexId})`;
          }
          const documentId = await svc.createEntityRecord('sprk_document', payload);

          return { documentId, driveItemId: meta.id, linkUrl: meta.webUrl };
        }
      : undefined;

  // Template picker (Wave E): LIST the OOB `template` records via Xrm.WebApi (host-only, no auth),
  // and RENDER a chosen one via the BFF so `{!entity.field}` codes merge from the primary regarding.
  const onListEmailTemplates = async (): Promise<IEmailTemplateSummary[]> => {
    const xrm = getXrm();
    if (!xrm?.WebApi) return [];
    try {
      const res = await xrm.WebApi.retrieveMultipleRecords('template', '?$select=templateid,title&$orderby=title asc');
      return (res?.entities ?? [])
        .map((e: Record<string, unknown>) => ({
          id: String(e.templateid ?? ''),
          name: (e.title as string) || '(untitled)',
        }))
        .filter((t: IEmailTemplateSummary) => t.id.length > 0);
    } catch (err) {
      console.warn('[EmailCompose] template list failed:', err);
      return [];
    }
  };

  const onRenderEmailTemplate =
    authenticatedFetch && bffBaseUrl
      ? async (args: {
          templateId: string;
          regardingEntityType?: string;
          regardingRecordId?: string;
        }): Promise<IEmailTemplateRenderResult> => {
          const base = bffBaseUrl.replace(/\/+$/, '');
          const resp = await authenticatedFetch(`${base}/api/communications/template/render`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              templateId: args.templateId,
              regardingEntityType: args.regardingEntityType,
              regardingRecordId: args.regardingRecordId,
            }),
          });
          if (!resp.ok) {
            throw new Error(`Template render failed (${resp.status})`);
          }
          const data = (await resp.json()) as Partial<IEmailTemplateRenderResult>;
          return { subject: data.subject ?? '', body: data.body ?? '', isHtml: !!data.isHtml };
        }
      : undefined;

  // AI "sparkle" draft (Wave E): POST the intent + current body/subject to the BFF, which owns the
  // prompt text (admin-editable growth path) and calls Azure OpenAI. Only wired with auth + BFF URL.
  const onDraftWithAi =
    authenticatedFetch && bffBaseUrl
      ? async (req: IEmailAiDraftRequest): Promise<IEmailAiDraftResult> => {
          const base = bffBaseUrl.replace(/\/+$/, '');
          const resp = await authenticatedFetch(`${base}/api/communications/draft`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              intent: req.intent,
              userInstruction: req.userInstruction,
              currentBody: req.currentBody,
              isHtml: req.isHtml,
              subject: req.subject,
            }),
          });
          if (!resp.ok) {
            throw new Error(`AI draft failed (${resp.status})`);
          }
          const data = (await resp.json()) as Partial<IEmailAiDraftResult>;
          return { text: data.text ?? '', isHtml: data.isHtml };
        }
      : undefined;

  // Recipient-openable SPE sharing link for a linked document (R2 item 12): POST the documentId to
  // the BFF, which resolves the doc's drive/item + creates an anonymous view link. Best-effort —
  // returns null on any non-2xx so the send keeps the prior (internal) URL. Only wired with auth + BFF.
  const onResolveShareLink =
    authenticatedFetch && bffBaseUrl
      ? async (documentId: string): Promise<string | null> => {
          try {
            const base = bffBaseUrl.replace(/\/+$/, '');
            const resp = await authenticatedFetch(
              `${base}/api/documents/${encodeURIComponent(documentId)}/share-link`,
              { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' }
            );
            if (!resp.ok) return null;
            const data = (await resp.json()) as { url?: string };
            return data.url ?? null;
          } catch {
            return null;
          }
        }
      : undefined;

  return {
    recordLookupCatalog: EMAIL_RECORD_LOOKUP_CATALOG,
    onLookupRecipients,
    onLookupRecord,
    onAddRelationship,
    onUploadLocalAttachment,
    onListEmailTemplates,
    onRenderEmailTemplate,
    onDraftWithAi,
    onResolveShareLink,
  };
}
