/**
 * matterService.ts
 * Matter creation flow orchestrator for the Create New Matter wizard.
 *
 * Responsibilities:
 *   1. Create sprk_matter Dataverse record via Xrm.WebApi
 *   2. Upload files to SPE via BFF SpeFileStore endpoint
 *   3. Execute selected follow-on actions (assign counsel, draft summary, send email)
 *
 * Partial failure handling:
 *   - Matter record creation failure → hard error (abort, return error result)
 *   - File upload failure → soft warning (matter created, return warning result)
 *   - Follow-on action failure → soft warning (matter created, log per-action error)
 *
 * All methods return ICreateMatterResult — never throw.
 */

import type { ICreateMatterFormState } from './formTypes';
import type { IUploadedFile } from './wizardTypes';
import type { IContact, ILookupItem } from '../../types/entities';
import type { IWebApi, WebApiEntity } from '../../types/xrm';
import { EntityCreationService } from '../../services/EntityCreationService';
import type { IUploadProgress } from '../../services/EntityCreationService';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/bffAuthProvider';
import { getXrm, getUserId } from '../../services/xrmProvider';

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

export type CreateMatterResultStatus = 'success' | 'partial' | 'error';

export interface ICreateMatterResult {
  /** Overall status. */
  status: CreateMatterResultStatus;
  /** The GUID of the created sprk_matter record (present on success and partial). */
  matterId?: string;
  /** Display name of the created matter. */
  matterName?: string;
  /** Human-readable error message (set on error status). */
  errorMessage?: string;
  /** Non-fatal warnings (e.g. file upload failed after record was created). */
  warnings: string[];
}

// ---------------------------------------------------------------------------
// Follow-on action inputs
// ---------------------------------------------------------------------------

export interface IAssignCounselInput {
  /** Contact GUID to assign as lead counsel. */
  contactId: string;
  /** Display name (used for optimistic UI, not sent to server). */
  contactName: string;
}

export interface IDraftSummaryInput {
  /** Recipient email addresses for distribution. */
  recipientEmails: string[];
}

export interface ISendEmailInput {
  /** Email recipient address(es). */
  to: string;
  /** Email subject line. */
  subject: string;
  /** Email body. */
  body: string;
}

export interface IFollowOnActions {
  assignCounsel?: IAssignCounselInput;
  draftSummary?: IDraftSummaryInput;
  sendEmail?: ISendEmailInput;
}

// ---------------------------------------------------------------------------
// BFF endpoints — file upload uses existing SPE endpoint via SdapApiClient (Task 6),
// draft summary handled by front-end Xrm.WebApi email activity (no BFF needed).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Dataverse entity helpers
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Metadata discovery — find correct OData navigation property names
// ---------------------------------------------------------------------------

/**
 * Query the Dataverse entity metadata API to discover the actual
 * single-valued navigation property names for lookup columns on an entity.
 *
 * Dataverse uses PascalCase navigation property names (e.g. "sprk_MatterType")
 * which differ from the lowercase column logical names ("sprk_mattertype").
 * The @odata.bind syntax requires the nav-prop name, not the column name.
 *
 * Results are cached per entity to avoid repeated metadata calls.
 *
 * @param entityLogicalName - e.g. 'sprk_matter', 'sprk_document'
 * @returns Map: { columnLogicalName → navigationPropertyName }
 */
const _navPropCache: Record<string, Record<string, string>> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<Record<string, string>> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[MatterService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
      return {};
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const json: any = await resp.json();
    const rels: Array<{ ReferencingAttribute: string; ReferencingEntityNavigationPropertyName: string }> =
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (json as any).value ?? [];

    const map: Record<string, string> = {};
    for (const r of rels) {
      map[r.ReferencingAttribute] = r.ReferencingEntityNavigationPropertyName;
    }

    console.info(`[MatterService] Nav-props for ${entityLogicalName}:`, map);
    _navPropCache[entityLogicalName] = map;
    return map;
  } catch (err) {
    console.warn(`[MatterService] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return {};
  }
}

/**
 * Resolve a navigation property name for a lookup column.
 * Uses discovered metadata if available, falls back to column logical name.
 */
function _resolveNavProp(navPropMap: Record<string, string>, columnLogical: string): string {
  return navPropMap[columnLogical] ?? columnLogical;
}

// ---------------------------------------------------------------------------
// Email activity helpers
// ---------------------------------------------------------------------------

/**
 * Build a Dataverse email entity payload with activity parties for From/To.
 *
 * Activity parties are required for the email router (server-side sync) to
 * process the email. The `email_activity_parties` collection is a deep-insert
 * on the email entity.
 *
 * Participation type masks:
 *   1 = Sender (From)
 *   2 = To Recipient
 *   3 = CC Recipient
 *   4 = BCC Recipient
 */
/**
 * Resolve an email address to a Dataverse contact record.
 * Returns the contact GUID if found, null otherwise.
 * Searches emailaddress1 (primary email) on the contact entity.
 */
async function _resolveEmailToContact(
  webApi: IWebApi,
  email: string
): Promise<string | null> {
  try {
    const safeEmail = email.trim().replace(/'/g, "''");
    const result = await webApi.retrieveMultipleRecords(
      'contact',
      `?$select=contactid&$filter=emailaddress1 eq '${safeEmail}'&$top=1`,
      1
    );
    if (result.entities.length > 0) {
      const contactId = result.entities[0]['contactid'] as string;
      console.info(`[MatterService] Resolved email "${email}" to contact: ${contactId}`);
      return contactId;
    }
    console.info(`[MatterService] No contact found for email "${email}"`);
    return null;
  } catch (err) {
    console.warn(`[MatterService] Contact lookup failed for "${email}":`, err);
    return null;
  }
}

/**
 * Build a Dataverse email entity payload with activity parties for From/To.
 *
 * Recipients MUST be resolved to Dataverse records (contact, systemuser, etc.)
 * because Dataverse blocks sending to unresolved email addresses by default.
 * The resolvedToParties parameter contains pre-resolved activity party objects.
 */
function _buildEmailEntity(params: {
  subject: string;
  body: string;
  matterId: string;
  matterName?: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  resolvedToParties: any[];
}): WebApiEntity {
  const { subject, body, matterId, matterName, resolvedToParties } = params;

  // Build a link to the matter record for the email recipient
  const xrm = getXrm();
  const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';
  const matterUrl = clientUrl
    ? `${clientUrl}/main.aspx?etn=sprk_matter&id=${matterId}&pagetype=entityrecord`
    : '';

  // Append matter link to the email body
  let fullBody = body;
  if (matterUrl) {
    const label = matterName ? `View Matter: ${matterName}` : 'View Matter';
    fullBody += `\n\n---\n${label}\n${matterUrl}`;
  }

  // Build activity parties: From (current user) + resolved To recipients
  const userId = getUserId();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const activityParties: any[] = [];

  if (userId) {
    activityParties.push({
      'partyid_systemuser@odata.bind': `/systemusers(${userId})`,
      participationtypemask: 1, // From (Sender)
    });
  }

  activityParties.push(...resolvedToParties);

  const emailEntity: WebApiEntity = {
    subject,
    description: fullBody,
    directioncode: true, // Outgoing
    email_activity_parties: activityParties,
  };

  return emailEntity;
}

/**
 * Resolve email addresses to Dataverse activity party objects.
 * For each address, tries to find a matching contact record.
 * - If found: uses partyid_contact@odata.bind (resolved recipient)
 * - If not found: uses addressused (unresolved — may be blocked by org settings)
 */
async function _resolveToParties(
  webApi: IWebApi,
  toAddress: string
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
): Promise<any[]> {
  const addresses = toAddress.split(/[;,]/).map((a) => a.trim()).filter(Boolean);
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const parties: any[] = [];

  for (const addr of addresses) {
    const contactId = await _resolveEmailToContact(webApi, addr);
    if (contactId) {
      // Resolved to a contact record — Dataverse will accept this
      parties.push({
        'partyid_contact@odata.bind': `/contacts(${contactId})`,
        participationtypemask: 2, // To
      });
    } else {
      // Unresolved — use addressused as fallback
      parties.push({
        addressused: addr,
        participationtypemask: 2, // To
      });
    }
  }

  return parties;
}

/**
 * Call the Dataverse SendEmail bound action to mark an email as "Pending Send".
 * Server-side sync will process it through the configured mailbox (Exchange/SMTP).
 *
 * SendEmail is a BOUND action on the email entity — the correct OData URL is:
 *   POST /api/data/v9.0/emails({id})/Microsoft.Dynamics.CRM.SendEmail
 *
 * The unbound format (POST /api/data/v9.0/SendEmail) returns 404.
 */
async function _sendEmail(emailActivityId: string): Promise<void> {
  const xrm = getXrm();
  const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';

  if (!clientUrl) {
    console.warn('[MatterService] Cannot determine Dataverse URL — cannot send email');
    throw new Error('Cannot determine Dataverse URL for SendEmail');
  }

  const url = `${clientUrl}/api/data/v9.0/emails(${emailActivityId})/Microsoft.Dynamics.CRM.SendEmail`;
  console.info('[MatterService] Calling SendEmail bound action:', url);

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'OData-Version': '4.0',
      },
      credentials: 'include',
      body: JSON.stringify({ IssueSend: true }),
    });

    if (response.ok || response.status === 204) {
      console.info('[MatterService] SendEmail action succeeded for:', emailActivityId);
    } else {
      const errorText = await response.text().catch(() => 'Unknown error');
      console.error('[MatterService] SendEmail failed:', response.status, errorText);
      throw new Error(`SendEmail failed: ${response.status} ${errorText}`);
    }
  } catch (err) {
    console.error('[MatterService] SendEmail error:', err);
    throw err;
  }
}

// ---------------------------------------------------------------------------
// MatterService class
// ---------------------------------------------------------------------------

export class MatterService {
  private readonly _entityService: EntityCreationService;

  constructor(
    private readonly _webApi: IWebApi,
    private readonly _containerId?: string
  ) {
    this._entityService = new EntityCreationService(_webApi);
  }

  /**
   * Full matter creation flow:
   *   1. Create sprk_matter record
   *   2. Upload files to SPE via BFF (using EntityCreationService)
   *   3. Create sprk_document records linking files to the matter
   *   4. Execute selected follow-on actions
   *
   * Returns ICreateMatterResult — never throws.
   */
  async createMatter(
    form: ICreateMatterFormState,
    uploadedFiles: IUploadedFile[],
    followOnActions: IFollowOnActions,
    onUploadProgress?: (progress: IUploadProgress) => void
  ): Promise<ICreateMatterResult> {
    const warnings: string[] = [];

    // ── Step 1: Create Dataverse record ─────────────────────────────────────
    let matterId: string;

    // Discover correct OData navigation property names from entity metadata
    const navPropMap = await _discoverNavProps('sprk_matter');

    // Build full entity payload with scalar fields + lookup bindings
    const entity: WebApiEntity = {
      sprk_mattername: form.matterName.trim(),
    };
    if (form.summary && form.summary.trim() !== '') {
      entity['sprk_matterdescription'] = form.summary.trim();
    }
    // Store the SPE container ID on the matter record
    if (this._containerId) {
      entity['sprk_containerid'] = this._containerId;
    }

    // Generate matter number: {matterTypeCode}-{random 6 digits}
    if (form.matterTypeId) {
      try {
        const matterTypeRecord = await this._webApi.retrieveRecord(
          'sprk_mattertype_ref',
          form.matterTypeId,
          '?$select=sprk_mattertypecode'
        );
        const typeCode = (matterTypeRecord?.sprk_mattertypecode as string) ?? '';
        if (typeCode) {
          const random6 = String(Math.floor(100000 + Math.random() * 900000));
          entity['sprk_matternumber'] = `${typeCode}-${random6}`;
          console.info('[MatterService] Generated matter number:', entity['sprk_matternumber']);
        }
      } catch (err) {
        console.warn('[MatterService] Could not look up matter type code for numbering:', err);
        // Non-fatal — continue without matter number
      }
    }

    // Add lookup bindings using discovered nav-prop names
    const lookups: Array<{ col: string; entitySet: string; guid: string }> = [];
    if (form.matterTypeId) lookups.push({ col: 'sprk_mattertype', entitySet: 'sprk_mattertype_refs', guid: form.matterTypeId });
    if (form.practiceAreaId) lookups.push({ col: 'sprk_practicearea', entitySet: 'sprk_practicearea_refs', guid: form.practiceAreaId });
    if (form.assignedAttorneyId) lookups.push({ col: 'sprk_assignedattorney', entitySet: 'contacts', guid: form.assignedAttorneyId });
    if (form.assignedParalegalId) lookups.push({ col: 'sprk_assignedparalegal', entitySet: 'contacts', guid: form.assignedParalegalId });

    for (const lk of lookups) {
      const navProp = navPropMap[lk.col] ?? lk.col;
      entity[`${navProp}@odata.bind`] = `/${lk.entitySet}(${lk.guid})`;
    }

    try {
      console.info('[MatterService] createRecord payload:', JSON.stringify(entity, null, 2));
      const result = await this._webApi.createRecord('sprk_matter', entity);
      matterId = result.id;
      console.info('[MatterService] createRecord success, matterId:', matterId);
    } catch (err) {
      console.error('[MatterService] createRecord error:', err);
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const errObj = err as any;
      const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
      return {
        status: 'error',
        errorMessage: `Failed to create matter record: ${message}`,
        warnings: [],
      };
    }

    // ── Step 2: Upload files to SPE via BFF + create document records ─────
    if (uploadedFiles.length > 0 && this._containerId) {
      const uploadResult = await this._entityService.uploadFilesToSpe(
        this._containerId,
        uploadedFiles,
        onUploadProgress
      );

      if (!uploadResult.success) {
        warnings.push(
          `File upload failed (${uploadResult.failureCount} of ${uploadedFiles.length}). ` +
          'Files can be added from the matter record.'
        );
      } else if (uploadResult.uploadedFiles.length > 0) {
        // Discover nav-prop for sprk_document → sprk_matter lookup
        const docNavProps = await _discoverNavProps('sprk_document');
        const docMatterNavProp = _resolveNavProp(docNavProps, 'sprk_matter');

        // Create sprk_document records linking uploaded files to the matter
        const linkResult = await this._entityService.createDocumentRecords(
          'sprk_matters',
          matterId,
          docMatterNavProp,
          uploadResult.uploadedFiles,
          {
            containerId: this._containerId,
            parentRecordName: form.matterName.trim(),
          }
        );
        if (linkResult.warnings.length > 0) {
          warnings.push(...linkResult.warnings);
        }
      }

      if (uploadResult.failureCount > 0 && uploadResult.successCount > 0) {
        warnings.push(
          `${uploadResult.failureCount} file(s) failed to upload: ` +
          uploadResult.errors.map((e) => e.fileName).join(', ')
        );
      }
    } else if (uploadedFiles.length > 0 && !this._containerId) {
      warnings.push('File upload skipped — no SPE container configured. Files can be added later.');
    }

    // ── Step 3: Follow-on actions ─────────────────────────────────────────
    if (followOnActions.assignCounsel) {
      const counselResult = await this._assignCounsel(
        matterId,
        followOnActions.assignCounsel,
        navPropMap
      );
      if (!counselResult.success) {
        warnings.push(counselResult.warning ?? 'Failed to assign counsel. Please assign manually.');
      }
    }

    if (followOnActions.draftSummary) {
      const summaryResult = await this._distributeSummary(
        matterId,
        form.matterName,
        followOnActions.draftSummary
      );
      if (!summaryResult.success) {
        warnings.push(summaryResult.warning ?? 'Failed to distribute summary email. Please send manually.');
      }
    }

    if (followOnActions.sendEmail) {
      const emailResult = await this._createEmailActivity(
        matterId,
        followOnActions.sendEmail
      );
      if (!emailResult.success) {
        warnings.push(emailResult.warning ?? 'Failed to create email activity. Please send manually.');
      }
    }

    return {
      status: warnings.length > 0 ? 'partial' : 'success',
      matterId,
      matterName: form.matterName.trim(),
      warnings,
    };
  }

  // ── Private helpers ────────────────────────────────────────────────────────

  private async _assignCounsel(
    matterId: string,
    input: IAssignCounselInput,
    navPropMap: Record<string, string>
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      const navProp = _resolveNavProp(navPropMap, 'sprk_assignedoutsidecounsel');
      const updatePayload: WebApiEntity = {
        [`${navProp}@odata.bind`]: `/contacts(${input.contactId})`,
      };
      await this._webApi.updateRecord('sprk_matter', matterId, updatePayload);
      return { success: true };
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not assign counsel (${message}). Please assign from the matter record.`,
      };
    }
  }

  private async _distributeSummary(
    matterId: string,
    matterName: string,
    input: IDraftSummaryInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      if (input.recipientEmails.length === 0) {
        return { success: true };
      }

      const recipients = input.recipientEmails.join('; ');

      // Resolve email addresses to Dataverse contact records.
      // Resolved recipients get proper activity history on their contact record.
      const resolvedToParties = await _resolveToParties(this._webApi, recipients);

      // Build email entity with activity parties (From/To) for server-side sync.
      const emailEntity = _buildEmailEntity({
        subject: `Matter Summary: ${matterName}`,
        body: `Summary distribution for matter "${matterName}" to: ${recipients}`,
        resolvedToParties,
        matterId,
        matterName,
      });

      const result = await this._createAndSendEmail(emailEntity, matterId);
      return result;
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not create summary email (${message}). Please distribute manually.`,
      };
    }
  }

  private async _createEmailActivity(
    matterId: string,
    input: ISendEmailInput
  ): Promise<{ success: boolean; warning?: string }> {
    try {
      // Resolve email addresses to Dataverse contact records.
      const resolvedToParties = await _resolveToParties(this._webApi, input.to);

      // Build email entity with activity parties (From/To) for server-side sync.
      const emailEntity = _buildEmailEntity({
        subject: input.subject,
        body: input.body,
        resolvedToParties,
        matterId,
      });

      const result = await this._createAndSendEmail(emailEntity, matterId);
      return result;
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      return {
        success: false,
        warning: `Could not create email activity (${message}). Please send manually.`,
      };
    }
  }

  /**
   * Create an email activity and trigger send via the Dataverse SendEmail action.
   * Server-side sync will process the email through the configured mailbox.
   *
   * Strategy:
   *   1. Discover regarding nav-prop via metadata (email → sprk_matter relationship)
   *   2. Create email with regarding binding if relationship exists
   *   3. If create-with-regarding fails, create without and patch regarding separately
   *   4. Call SendEmail bound action to mark "Pending Send"
   */
  private async _createAndSendEmail(
    emailEntity: WebApiEntity,
    matterId: string
  ): Promise<{ success: boolean; warning?: string }> {
    let emailId: string;
    let regardingSet = false;

    // Discover the correct regarding nav-prop name from email entity metadata.
    // The regarding relationship to sprk_matter requires activities to be enabled
    // on the sprk_matter entity. The nav-prop name may differ from the column name.
    const emailNavProps = await _discoverNavProps('email');
    const regardingNavProp = emailNavProps['regardingobjectid']
      ? 'regardingobjectid'
      : null;

    // Look for the specific sprk_matter regarding nav-prop.
    // It could be 'regardingobjectid_sprk_matter' or discovered from metadata.
    let regardingBindingKey = '';
    if (regardingNavProp) {
      // Standard pattern: regardingobjectid_ENTITYNAME@odata.bind
      regardingBindingKey = 'regardingobjectid_sprk_matter@odata.bind';
    }
    // Also check if there's a specific nav-prop discovered for sprk_matter regarding
    for (const [col, nav] of Object.entries(emailNavProps)) {
      if (col.startsWith('regardingobjectid') && nav.toLowerCase().includes('sprk_matter')) {
        regardingBindingKey = `${nav}@odata.bind`;
        console.info('[MatterService] Discovered regarding nav-prop for sprk_matter:', nav);
        break;
      }
    }

    // Attempt 1: create with regarding binding
    if (regardingBindingKey) {
      try {
        emailEntity[regardingBindingKey] = `/sprk_matters(${matterId})`;
        console.info('[MatterService] Creating email with regarding:', regardingBindingKey);
        const result = await this._webApi.createRecord('email', emailEntity);
        emailId = result.id;
        regardingSet = true;
      } catch (err) {
        console.warn('[MatterService] Create with regarding failed:', err);
        delete emailEntity[regardingBindingKey];
        // Fall through to attempt without regarding
      }
    }

    // Attempt 2: create without regarding
    if (!regardingSet) {
      try {
        console.info('[MatterService] Creating email without regarding');
        const result = await this._webApi.createRecord('email', emailEntity);
        emailId = result.id;
      } catch (secondErr) {
        // Attempt 3: minimal payload (no activity parties, no regarding)
        console.warn('[MatterService] Email create failed, trying minimal:', secondErr);
        const minimalEntity: WebApiEntity = {
          subject: emailEntity['subject'] as string,
          description: emailEntity['description'] as string,
          directioncode: true,
        };
        const result = await this._webApi.createRecord('email', minimalEntity);
        emailId = result.id;
      }
    }

    console.info('[MatterService] Email activity created:', emailId, 'regarding set:', regardingSet);

    // If regarding was not set during create, try patching it separately.
    // This handles cases where the deep-insert fails but a direct update works.
    if (!regardingSet && matterId) {
      try {
        const patchKey = regardingBindingKey || 'regardingobjectid_sprk_matter@odata.bind';
        await this._webApi.updateRecord('email', emailId, {
          [patchKey]: `/sprk_matters(${matterId})`,
        });
        console.info('[MatterService] Regarding set via separate update');
        regardingSet = true;
      } catch (patchErr) {
        console.warn('[MatterService] Could not set regarding (activities may not be enabled on sprk_matter):', patchErr);
      }
    }

    // Send the email via the Dataverse SendEmail bound action.
    // This marks the email as "Pending Send" for server-side sync to process.
    try {
      await _sendEmail(emailId);
      console.info('[MatterService] SendEmail action succeeded for:', emailId);
    } catch (sendErr) {
      console.warn('[MatterService] SendEmail action failed (email created as draft):', sendErr);
      return {
        success: true,
        warning: 'Email created as draft but could not be sent automatically. Please send from the email record.',
      };
    }

    const warnings: string[] = [];
    if (!regardingSet) {
      warnings.push('Email sent but not linked to matter (activities may not be enabled on sprk_matter entity).');
    }

    return {
      success: true,
      warning: warnings.length > 0 ? warnings.join(' ') : undefined,
    };
  }
}

// ---------------------------------------------------------------------------
// Contact search helper (for AssignCounselStep and lookup fields)
// ---------------------------------------------------------------------------

/**
 * Search contact records by name fragment.
 * Uses standard Dataverse contact entity (fullname, emailaddress1).
 * Returns up to 10 matching contacts.
 * Throws on error — callers should handle gracefully.
 */
export async function searchContacts(
  webApi: IWebApi,
  nameFilter: string
): Promise<IContact[]> {
  if (!nameFilter || nameFilter.trim().length < 2) {
    return [];
  }

  const safeFilter = nameFilter.trim().replace(/'/g, "''");
  const query =
    `?$select=contactid,fullname,emailaddress1` +
    `&$filter=contains(fullname,'${safeFilter}')` +
    `&$orderby=fullname asc` +
    `&$top=10`;

  console.info('[MatterService] searchContacts query:', 'contact', query);
  try {
    const result = await webApi.retrieveMultipleRecords('contact', query, 10);
    console.info('[MatterService] searchContacts results:', result.entities.length);
    // Map to IContact shape for backward compatibility
    return result.entities.map((e) => ({
      sprk_contactid: e['contactid'] as string,
      sprk_name: e['fullname'] as string,
      sprk_email: (e['emailaddress1'] as string) || '',
    })) as unknown as IContact[];
  } catch (err) {
    console.error('[MatterService] searchContacts error:', err);
    throw err;
  }
}

/**
 * Search contacts and return as ILookupItem[] (for LookupField compatibility).
 */
export async function searchContactsAsLookup(
  webApi: IWebApi,
  nameFilter: string
): Promise<ILookupItem[]> {
  const contacts = await searchContacts(webApi, nameFilter);
  return contacts.map((c) => ({
    id: c.sprk_contactid,
    name: c.sprk_name + (c.sprk_email ? ` (${c.sprk_email})` : ''),
  }));
}

// ---------------------------------------------------------------------------
// Matter Type search helper
// ---------------------------------------------------------------------------

/**
 * Search sprk_mattertype records by name fragment.
 * Returns up to 10 matching matter types as ILookupItem.
 */
export async function searchMatterTypes(
  webApi: IWebApi,
  nameFilter: string
): Promise<ILookupItem[]> {
  if (!nameFilter || nameFilter.trim().length < 1) {
    return [];
  }

  const safeFilter = nameFilter.trim().replace(/'/g, "''");
  const query =
    `?$select=sprk_mattertype_refid,sprk_mattertypename` +
    `&$filter=contains(sprk_mattertypename,'${safeFilter}')` +
    `&$orderby=sprk_mattertypename asc` +
    `&$top=10`;

  console.info('[MatterService] searchMatterTypes query:', 'sprk_mattertype_ref', query);
  try {
    const result = await webApi.retrieveMultipleRecords('sprk_mattertype_ref', query, 10);
    console.info('[MatterService] searchMatterTypes results:', result.entities.length, result.entities);
    return result.entities.map((e) => ({
      id: e['sprk_mattertype_refid'] as string,
      name: e['sprk_mattertypename'] as string,
    }));
  } catch (err) {
    console.error('[MatterService] searchMatterTypes error:', err);
    throw err;
  }
}

// ---------------------------------------------------------------------------
// Practice Area search helper
// ---------------------------------------------------------------------------

/**
 * Search sprk_practicearea records by name fragment.
 * Returns up to 10 matching practice areas as ILookupItem.
 */
export async function searchPracticeAreas(
  webApi: IWebApi,
  nameFilter: string
): Promise<ILookupItem[]> {
  if (!nameFilter || nameFilter.trim().length < 1) {
    return [];
  }

  const safeFilter = nameFilter.trim().replace(/'/g, "''");
  const query =
    `?$select=sprk_practicearea_refid,sprk_practiceareaname` +
    `&$filter=contains(sprk_practiceareaname,'${safeFilter}')` +
    `&$orderby=sprk_practiceareaname asc` +
    `&$top=10`;

  console.info('[MatterService] searchPracticeAreas query:', 'sprk_practicearea_ref', query);
  try {
    const result = await webApi.retrieveMultipleRecords('sprk_practicearea_ref', query, 10);
    console.info('[MatterService] searchPracticeAreas results:', result.entities.length, result.entities);
    return result.entities.map((e) => ({
      id: e['sprk_practicearea_refid'] as string,
      name: e['sprk_practiceareaname'] as string,
    }));
  } catch (err) {
    console.error('[MatterService] searchPracticeAreas error:', err);
    throw err;
  }
}

// ---------------------------------------------------------------------------
// AI draft summary helper (for DraftSummaryStep)
// ---------------------------------------------------------------------------

export interface IAiDraftSummaryResponse {
  /** The generated summary text. */
  summary: string;
}

/**
 * Calls the BFF AI endpoint to generate a draft matter summary.
 * Uses authenticated fetch with Bearer token for BFF API.
 * Returns a fallback response if the endpoint is unavailable (graceful degradation).
 */
export async function fetchAiDraftSummary(
  matterName: string,
  matterType: string,
  practiceArea: string
): Promise<IAiDraftSummaryResponse> {
  try {
    const bffBaseUrl = getBffBaseUrl();
    const response = await authenticatedFetch(
      `${bffBaseUrl}/workspace/matters/ai-summary`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ matterName, matterType, practiceArea }),
      }
    );

    if (!response.ok) {
      return buildFallbackSummary(matterName, matterType, practiceArea);
    }

    const data: IAiDraftSummaryResponse = await response.json();
    return data;
  } catch {
    return buildFallbackSummary(matterName, matterType, practiceArea);
  }
}

function buildFallbackSummary(
  matterName: string,
  matterType: string,
  practiceArea: string
): IAiDraftSummaryResponse {
  const type = matterType || 'general';
  const area = practiceArea || 'legal services';
  return {
    summary:
      `This ${type.toLowerCase()} matter, "${matterName}", has been created in the ${area} practice area. ` +
      `Key stakeholders have been identified and initial documentation has been uploaded for review. ` +
      `Next steps include counsel assignment and matter planning. ` +
      `Please review and update this summary with specific matter objectives and timeline.`,
  };
}
