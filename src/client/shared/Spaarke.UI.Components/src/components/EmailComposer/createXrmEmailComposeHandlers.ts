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
import { cleanGuid } from '../../utils/guid';
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
   * Resolve the link a linked `sprk_document` carries in the message body: the Spaarke RECORD link
   * (task 098). Always present (offline — needs only the host's org URL); resolves `null` when the org URL
   * is unavailable. Never a file sharing link, and never calls the BFF share-link route.
   */
  onResolveShareLink: (documentId: string) => Promise<string | null>;
}

/** The Spaarke model-driven app's unique name — the app a record link must open in (matches the add-in's `SPAARKE_APP_NAME` default). */
export const SPAARKE_APP_UNIQUE_NAME = 'sprk_MatterManagement';

/**
 * Build the Spaarke record link `{orgUrl}/main.aspx?appname={app}&pagetype=entityrecord&etn={entity}&id={id}`.
 * Returns `null` — never a relative, http or guessed URL — when `orgUrl` is not an absolute https URL or the
 * id is empty, so a caller omits the link rather than emit a dead one in a mail client. Same shape the Word /
 * Outlook add-ins use for "open record" (`openRecordLauncher.buildOpenRecordUrl`), minus `navbar=off`: a
 * recipient gets the full app.
 */
export function buildSpaarkeRecordLink(
  orgUrl: string | undefined,
  entityType: string,
  recordId: string,
  appName: string = SPAARKE_APP_UNIQUE_NAME
): string | null {
  const base = (orgUrl ?? '').trim().replace(/\/+$/, '');
  const id = (recordId ?? '').trim();
  if (!base || !id || !entityType) return null;
  try {
    if (new URL(base).protocol !== 'https:') return null;
  } catch {
    return null;
  }
  const app = appName.trim();
  const appParam = app ? `appname=${encodeURIComponent(app)}&` : '';
  return `${base}/main.aspx?${appParam}pagetype=entityrecord&etn=${encodeURIComponent(entityType)}&id=${encodeURIComponent(id)}`;
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
    const clean = cleanGuid(userId);
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

  const buildRecordUrl = (entityType: string, id: string): string | undefined =>
    buildSpaarkeRecordLink(resolveClientUrl(), entityType, id) ?? undefined;

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
    const id = cleanGuid(picked.id);
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
      const id = cleanGuid(p.id);
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
    const id = cleanGuid(picked.id);
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

          // 🔴 PARENTLESS UPLOAD. Task 076: this flow genuinely has no owning record when the bytes
          // move — the `sprk_document` is created AFTER the upload and deliberately unassociated,
          // because the email may have no persisted regarding yet. So it uses the record-LESS route
          // (`PUT /api/obo/me/files/{path}`), where the SERVER derives the container from the acting
          // user's business unit. Task 076's classification (project notes
          // `task-076-client-cutover-and-supplier-classification.md:54-55`) reads this as legitimately
          // parentless, alongside the Analysis wizard's standalone-document flow — the only other
          // caller of `uploadFilesWithoutRecord` at HEAD.
          //
          // The client no longer resolves or names that container. The value is the same one it used
          // to compute here; the difference is that the client is no longer the authority for it.
          const userId: string | undefined = xrm.Utility?.getGlobalContext?.()?.userSettings?.userId;
          if (!userId) throw new Error('Could not resolve the current user for upload.');
          // Still needed for the SEARCH-INDEX routing fields below; the container half of this
          // result is no longer read by anything on this path.
          const bu = await EntityCreationService.resolveUserBuDefaults(xrm.WebApi, userId);

          const svc = new EntityCreationService(xrm.WebApi, authenticatedFetch, bffBaseUrl);
          const uploaded: IUploadedFile = {
            id: `email-attach:${file.name}:${file.size}`,
            name: file.name,
            sizeBytes: file.size,
            fileType: deriveUploadedFileType(file.type),
            file,
          };
          const uploadResult = await svc.uploadFilesWithoutRecord([uploaded]);
          const meta = uploadResult.uploadedFiles[0];
          if (!meta) throw new Error(uploadResult.errors[0]?.error ?? 'File upload failed.');

          // Create the governed `sprk_document` UNASSOCIATED (the email may have no persisted
          // regarding yet). Mirrors DocumentRecordService's unassociated payload.
          //
          // 🔴 NO SPE POINTER in the create payload (unified-access-control-r2 task 166 f1; owner round 21
          // item 1 (i)): `sprk_graphdriveid` / `sprk_graphitemid` are field-secured, writable by the BFF only.
          // The BFF attaches the uploaded file below — after checking that this caller created the row and
          // uploaded the file, and that the file is in the row's derived container — and stamps the pointer,
          // `sprk_hasfile` and `sprk_filepath` itself.
          const payload: Record<string, unknown> = {
            sprk_documentname: meta.name,
            sprk_filename: meta.name,
            sprk_filesize: meta.size,
          };
          if (bu.searchIndexName) payload.sprk_searchindexname = bu.searchIndexName;
          if (bu.searchIndexId) {
            payload['sprk_AI_Search_Index@odata.bind'] = `/sprk_aisearchindexes(${bu.searchIndexId})`;
          }
          const documentId = await svc.createEntityRecord('sprk_document', payload);
          try {
            await svc.attachUploadedFile(documentId, meta);
          } catch (attachErr) {
            // Never leave a "document" with no file behind; the attach failure is what the user sees.
            try {
              await xrm.WebApi.deleteRecord('sprk_document', documentId);
            } catch (deleteErr) {
              console.warn('[EmailCompose] could not remove the document whose file was not attached:', deleteErr);
            }
            throw attachErr;
          }

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

  // The link a linked document carries in the message body: the SPAARKE RECORD link — never a file
  // sharing link (spaarkeai-word-add-in-r1 task 098, owner decision 2026-10-05). Every Spaarke document
  // lives in a SharePoint Embedded container, where Graph refuses sharing links, so this handler no longer
  // calls `POST /api/documents/{id}/share-link` (its retirement is coordinated with unified-access-control-r2,
  // which owns its authorization and tests — task 098 note). External recipients open the
  // file through the external access platform, the same way an internal user opens it from Spaarke.
  //
  // Pure + offline (no auth, no BFF): the org URL comes from the host. No org URL → `null`, which the
  // engine turns into an OMITTED link plus a message to the author — never a broken or internal SPE URL.
  // The seam keeps its historical name so every consumer (ADR-045: one engine) is covered by this one change.
  const onResolveShareLink = async (documentId: string): Promise<string | null> =>
    buildSpaarkeRecordLink(resolveClientUrl(), 'sprk_document', cleanGuid(documentId));

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
