/**
 * matterService.ts
 * Matter creation flow orchestrator for the Create New Matter wizard.
 *
 * Responsibilities:
 *   1. Create sprk_matter Dataverse record via IDataService
 *   2. Upload files to SPE via BFF SpeFileStore endpoint
 *   3. Execute selected follow-on actions (assign counsel, draft summary, send email)
 *
 * Partial failure handling:
 *   - Matter record creation failure -> hard error (abort, return error result)
 *   - File upload failure -> soft warning (matter created, return warning result)
 *   - Follow-on action failure -> soft warning (matter created, log per-action error)
 *
 * All methods return ICreateMatterResult -- never throw.
 *
 * Dependencies are injected via constructor -- no solution-specific imports.
 * authenticatedFetch and bffBaseUrl are passed at construction time.
 */
import { EntityCreationService } from '../../services/EntityCreationService';
// ---------------------------------------------------------------------------
// Metadata discovery -- find correct OData navigation property names
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
 * @returns Map: { columnLogicalName -> navigationPropertyName }
 */
const _navPropCache = {};
async function _discoverNavProps(entityLogicalName) {
    if (_navPropCache[entityLogicalName]) {
        return _navPropCache[entityLogicalName];
    }
    try {
        const url = `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
            `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName`;
        const resp = await fetch(url, { credentials: 'include' });
        if (!resp.ok) {
            console.warn(`[MatterService] Nav-prop discovery failed for ${entityLogicalName}:`, resp.status);
            return {};
        }
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const json = await resp.json();
        const rels = 
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        json.value ?? [];
        const map = {};
        for (const r of rels) {
            map[r.ReferencingAttribute] = r.ReferencingEntityNavigationPropertyName;
        }
        console.info(`[MatterService] Nav-props for ${entityLogicalName}:`, map);
        _navPropCache[entityLogicalName] = map;
        return map;
    }
    catch (err) {
        console.warn(`[MatterService] Nav-prop discovery error for ${entityLogicalName}:`, err);
        return {};
    }
}
/**
 * Resolve a navigation property name for a lookup column.
 * Uses discovered metadata if available, falls back to column logical name.
 */
function _resolveNavProp(navPropMap, columnLogical) {
    return navPropMap[columnLogical] ?? columnLogical;
}
// ---------------------------------------------------------------------------
// MatterService class
// ---------------------------------------------------------------------------
export class MatterService {
    constructor(dataService, authenticatedFetch, bffBaseUrl, _containerId) {
        this._containerId = _containerId;
        this._dataService = dataService;
        // EntityCreationService expects IWebApiWithCreate which has createRecord returning { id: string }.
        // Wrap IDataService to adapt createRecord return type.
        const webApiAdapter = {
            createRecord: async (entityName, data) => {
                const id = await dataService.createRecord(entityName, data);
                return { id };
            },
            retrieveRecord: (entityName, id, options) => dataService.retrieveRecord(entityName, id, options),
            retrieveMultipleRecords: (entityName, options) => dataService.retrieveMultipleRecords(entityName, options),
            updateRecord: async (entityName, id, data) => {
                await dataService.updateRecord(entityName, id, data);
                return { id };
            },
            deleteRecord: async (entityName, id) => {
                await dataService.deleteRecord(entityName, id);
                return { id };
            },
        };
        this._entityService = new EntityCreationService(webApiAdapter, authenticatedFetch, bffBaseUrl);
    }
    /**
     * Full matter creation flow:
     *   1. Create sprk_matter record
     *   2. Upload files to SPE via BFF (using EntityCreationService)
     *   3. Create sprk_document records linking files to the matter
     *   4. Execute selected follow-on actions
     *
     * Returns ICreateMatterResult -- never throws.
     */
    async createMatter(form, uploadedFiles, followOnActions, onUploadProgress) {
        const warnings = [];
        // -- Step 1: Create Dataverse record --
        let matterId;
        // Discover correct OData navigation property names from entity metadata
        const navPropMap = await _discoverNavProps('sprk_matter');
        // Build full entity payload with scalar fields + lookup bindings
        const entity = {
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
                const matterTypeRecord = await this._dataService.retrieveRecord('sprk_mattertype_ref', form.matterTypeId, '?$select=sprk_mattertypecode');
                const typeCode = matterTypeRecord?.sprk_mattertypecode ?? '';
                if (typeCode) {
                    const random6 = String(Math.floor(100000 + Math.random() * 900000));
                    entity['sprk_matternumber'] = `${typeCode}-${random6}`;
                    console.info('[MatterService] Generated matter number:', entity['sprk_matternumber']);
                }
            }
            catch (err) {
                console.warn('[MatterService] Could not look up matter type code for numbering:', err);
                // Non-fatal -- continue without matter number
            }
        }
        // Add lookup bindings using discovered nav-prop names
        const lookups = [];
        if (form.matterTypeId)
            lookups.push({ col: 'sprk_mattertype', entitySet: 'sprk_mattertype_refs', guid: form.matterTypeId });
        if (form.practiceAreaId)
            lookups.push({ col: 'sprk_practicearea', entitySet: 'sprk_practicearea_refs', guid: form.practiceAreaId });
        if (form.assignedAttorneyId)
            lookups.push({ col: 'sprk_assignedattorney1', entitySet: 'contacts', guid: form.assignedAttorneyId });
        if (form.assignedParalegalId)
            lookups.push({ col: 'sprk_assignedparalegal1', entitySet: 'contacts', guid: form.assignedParalegalId });
        if (form.assignedOutsideCounselId)
            lookups.push({ col: 'sprk_assignedlawfirm1', entitySet: 'sprk_organizations', guid: form.assignedOutsideCounselId });
        for (const lk of lookups) {
            const navProp = navPropMap[lk.col] ?? lk.col;
            entity[`${navProp}@odata.bind`] = `/${lk.entitySet}(${lk.guid})`;
        }
        try {
            console.info('[MatterService] createRecord payload:', JSON.stringify(entity, null, 2));
            // IDataService.createRecord returns Promise<string> (just the id)
            matterId = await this._dataService.createRecord('sprk_matter', entity);
            console.info('[MatterService] createRecord success, matterId:', matterId);
        }
        catch (err) {
            console.error('[MatterService] createRecord error:', err);
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const errObj = err;
            const message = errObj?.message || (err instanceof Error ? err.message : 'Unknown error');
            return {
                status: 'error',
                errorMessage: `Failed to create matter record: ${message}`,
                warnings: [],
            };
        }
        // -- Step 2: Upload files to SPE via BFF + create document records --
        if (uploadedFiles.length > 0 && this._containerId) {
            const uploadResult = await this._entityService.uploadFilesToSpe(this._containerId, uploadedFiles, onUploadProgress);
            if (!uploadResult.success) {
                warnings.push(`File upload failed (${uploadResult.failureCount} of ${uploadedFiles.length}). ` +
                    'Files can be added from the matter record.');
            }
            else if (uploadResult.uploadedFiles.length > 0) {
                // Discover nav-prop for sprk_document -> sprk_matter lookup
                const docNavProps = await _discoverNavProps('sprk_document');
                const docMatterNavProp = _resolveNavProp(docNavProps, 'sprk_matter');
                // Create sprk_document records linking uploaded files to the matter
                const linkResult = await this._entityService.createDocumentRecords('sprk_matters', matterId, docMatterNavProp, uploadResult.uploadedFiles, {
                    containerId: this._containerId,
                    parentRecordName: form.matterName.trim(),
                });
                if (linkResult.warnings.length > 0) {
                    warnings.push(...linkResult.warnings);
                }
            }
            if (uploadResult.failureCount > 0 && uploadResult.successCount > 0) {
                warnings.push(`${uploadResult.failureCount} file(s) failed to upload: ` +
                    uploadResult.errors.map((e) => e.fileName).join(', '));
            }
        }
        else if (uploadedFiles.length > 0 && !this._containerId) {
            warnings.push('File upload skipped -- no SPE container configured. Files can be added later.');
        }
        // -- Step 3: Follow-on actions --
        if (followOnActions.assignCounsel) {
            const counselResult = await this._assignCounsel(matterId, followOnActions.assignCounsel, navPropMap);
            if (!counselResult.success) {
                warnings.push(counselResult.warning ?? 'Failed to assign counsel. Please assign manually.');
            }
        }
        if (followOnActions.draftSummary) {
            const summaryResult = await this._distributeSummary(matterId, form.matterName, followOnActions.draftSummary);
            if (!summaryResult.success) {
                warnings.push(summaryResult.warning ?? 'Failed to distribute summary email. Please send manually.');
            }
        }
        if (followOnActions.sendEmail) {
            const emailResult = await this._entityService.sendEmail({
                to: followOnActions.sendEmail.to,
                subject: followOnActions.sendEmail.subject,
                body: followOnActions.sendEmail.body,
                associations: [{ entityType: 'sprk_matter', entityId: matterId, entityName: form.matterName.trim() }],
            });
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
    // -- Private helpers --
    async _assignCounsel(matterId, input, navPropMap) {
        try {
            const navProp = _resolveNavProp(navPropMap, 'sprk_assignedlawfirm1');
            const updatePayload = {
                [`${navProp}@odata.bind`]: `/contacts(${input.contactId})`,
            };
            await this._dataService.updateRecord('sprk_matter', matterId, updatePayload);
            return { success: true };
        }
        catch (err) {
            const message = err instanceof Error ? err.message : 'Unknown error';
            return {
                success: false,
                warning: `Could not assign counsel (${message}). Please assign from the matter record.`,
            };
        }
    }
    async _distributeSummary(matterId, matterName, input) {
        if (input.recipientEmails.length === 0) {
            return { success: true };
        }
        return this._entityService.sendEmail({
            to: input.recipientEmails,
            subject: `Matter Summary: ${matterName}`,
            body: `Summary distribution for matter "${matterName}" to: ${input.recipientEmails.join('; ')}`,
            associations: [{ entityType: 'sprk_matter', entityId: matterId, entityName: matterName }],
        });
    }
}
// ---------------------------------------------------------------------------
// Contact search helper (for AssignCounselStep and lookup fields)
// ---------------------------------------------------------------------------
/**
 * Search contact records by name fragment.
 * Uses standard Dataverse contact entity (fullname, emailaddress1).
 * Returns up to 10 matching contacts.
 * Throws on error -- callers should handle gracefully.
 */
export async function searchContacts(dataService, nameFilter) {
    if (!nameFilter || nameFilter.trim().length < 2) {
        return [];
    }
    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query = `?$select=contactid,fullname,emailaddress1` +
        `&$filter=contains(fullname,'${safeFilter}')` +
        `&$orderby=fullname asc` +
        `&$top=10`;
    console.info('[MatterService] searchContacts query:', 'contact', query);
    try {
        const result = await dataService.retrieveMultipleRecords('contact', query);
        console.info('[MatterService] searchContacts results:', result.entities.length);
        // Map to IContact shape for backward compatibility
        return result.entities.map((e) => ({
            sprk_contactid: e['contactid'],
            sprk_name: e['fullname'],
            sprk_email: e['emailaddress1'] || '',
        }));
    }
    catch (err) {
        console.error('[MatterService] searchContacts error:', err);
        throw err;
    }
}
/**
 * Search contacts and return as ILookupItem[] (for LookupField compatibility).
 */
export async function searchContactsAsLookup(dataService, nameFilter) {
    const contacts = await searchContacts(dataService, nameFilter);
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
export async function searchMatterTypes(dataService, nameFilter) {
    if (!nameFilter || nameFilter.trim().length < 1) {
        return [];
    }
    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query = `?$select=sprk_mattertype_refid,sprk_mattertypename` +
        `&$filter=contains(sprk_mattertypename,'${safeFilter}')` +
        `&$orderby=sprk_mattertypename asc` +
        `&$top=10`;
    console.info('[MatterService] searchMatterTypes query:', 'sprk_mattertype_ref', query);
    try {
        const result = await dataService.retrieveMultipleRecords('sprk_mattertype_ref', query);
        console.info('[MatterService] searchMatterTypes results:', result.entities.length, result.entities);
        return result.entities.map((e) => ({
            id: e['sprk_mattertype_refid'],
            name: e['sprk_mattertypename'],
        }));
    }
    catch (err) {
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
export async function searchPracticeAreas(dataService, nameFilter) {
    if (!nameFilter || nameFilter.trim().length < 1) {
        return [];
    }
    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query = `?$select=sprk_practicearea_refid,sprk_practiceareaname` +
        `&$filter=contains(sprk_practiceareaname,'${safeFilter}')` +
        `&$orderby=sprk_practiceareaname asc` +
        `&$top=10`;
    console.info('[MatterService] searchPracticeAreas query:', 'sprk_practicearea_ref', query);
    try {
        const result = await dataService.retrieveMultipleRecords('sprk_practicearea_ref', query);
        console.info('[MatterService] searchPracticeAreas results:', result.entities.length, result.entities);
        return result.entities.map((e) => ({
            id: e['sprk_practicearea_refid'],
            name: e['sprk_practiceareaname'],
        }));
    }
    catch (err) {
        console.error('[MatterService] searchPracticeAreas error:', err);
        throw err;
    }
}
/**
 * Streams the BFF AI endpoint to generate a draft matter summary (SSE).
 * Fires onProgress callbacks as the backend emits progress events.
 * Returns a fallback response if the endpoint is unavailable (graceful degradation).
 *
 * @param matterName - Name of the matter
 * @param matterType - Type of the matter
 * @param practiceArea - Practice area
 * @param callbacks - SSE progress callbacks
 * @param signal - Optional abort signal
 * @param authenticatedFetch - Authenticated fetch function for BFF API calls
 * @param bffBaseUrl - BFF API base URL
 */
export async function streamAiDraftSummary(matterName, matterType, practiceArea, callbacks = {}, signal, authenticatedFetch, bffBaseUrl) {
    const { onProgress } = callbacks;
    // If no authenticatedFetch is provided, return fallback
    if (!authenticatedFetch || !bffBaseUrl) {
        return buildFallbackSummary(matterName, matterType, practiceArea);
    }
    try {
        const response = await authenticatedFetch(`${bffBaseUrl}/api/workspace/matters/ai-summary`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ matterName, matterType, practiceArea }),
            signal,
        });
        if (!response.ok || !response.body) {
            return buildFallbackSummary(matterName, matterType, practiceArea);
        }
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let resultSummary = null;
        let streamDone = false;
        while (!streamDone) {
            const { done, value } = await reader.read();
            if (done)
                break;
            buffer += decoder.decode(value, { stream: true });
            const parts = buffer.split('\n\n');
            buffer = parts.pop() ?? '';
            for (const part of parts) {
                const line = part.trim();
                if (!line.startsWith('data:'))
                    continue;
                const raw = line.slice(5).trim();
                if (raw === '[DONE]') {
                    streamDone = true;
                    break;
                }
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                let chunk;
                try {
                    chunk = JSON.parse(raw);
                }
                catch {
                    continue;
                }
                if (chunk.type === 'progress' && chunk.step && onProgress) {
                    onProgress(chunk.step);
                }
                else if (chunk.type === 'result' && chunk.content) {
                    try {
                        // eslint-disable-next-line @typescript-eslint/no-explicit-any
                        const parsed = JSON.parse(chunk.content);
                        if (parsed?.summary)
                            resultSummary = String(parsed.summary);
                    }
                    catch { /* skip malformed result */ }
                }
                else if (chunk.type === 'error') {
                    throw new Error(chunk.content ?? 'Summary generation failed');
                }
            }
        }
        return resultSummary ? { summary: resultSummary } : buildFallbackSummary(matterName, matterType, practiceArea);
    }
    catch {
        return buildFallbackSummary(matterName, matterType, practiceArea);
    }
}
/**
 * @deprecated Use streamAiDraftSummary for SSE-based progress feedback.
 * Calls the BFF AI endpoint to generate a draft matter summary (REST, no progress).
 *
 * @param matterName - Name of the matter
 * @param matterType - Type of the matter
 * @param practiceArea - Practice area
 * @param authenticatedFetch - Authenticated fetch function for BFF API calls
 * @param bffBaseUrl - BFF API base URL
 */
export async function fetchAiDraftSummary(matterName, matterType, practiceArea, authenticatedFetch, bffBaseUrl) {
    return streamAiDraftSummary(matterName, matterType, practiceArea, {}, undefined, authenticatedFetch, bffBaseUrl);
}
// ---------------------------------------------------------------------------
// Organization search helper (for Outside Counsel lookup)
// ---------------------------------------------------------------------------
/**
 * Search sprk_organization records by name fragment.
 * Returns up to 10 matching organizations as ILookupItem.
 */
export async function searchOrganizationsAsLookup(dataService, nameFilter) {
    if (!nameFilter || nameFilter.trim().length < 2) {
        return [];
    }
    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query = `?$select=sprk_organizationid,sprk_organizationname` +
        `&$filter=contains(sprk_organizationname,'${safeFilter}')` +
        `&$orderby=sprk_organizationname asc` +
        `&$top=10`;
    console.info('[MatterService] searchOrganizations query:', 'sprk_organization', query);
    try {
        const result = await dataService.retrieveMultipleRecords('sprk_organization', query);
        console.info('[MatterService] searchOrganizations results:', result.entities.length);
        return result.entities.map((e) => ({
            id: e['sprk_organizationid'],
            name: e['sprk_organizationname'],
        }));
    }
    catch (err) {
        console.error('[MatterService] searchOrganizations error:', err);
        throw err;
    }
}
// ---------------------------------------------------------------------------
// User search helper (for SendEmailStep -- lookup systemuser table)
// ---------------------------------------------------------------------------
/**
 * Search systemuser records by name fragment.
 * Returns up to 10 active users as ILookupItem.
 * Name format: "Full Name (email)" for disambiguation.
 */
export async function searchUsersAsLookup(dataService, nameFilter) {
    if (!nameFilter || nameFilter.trim().length < 2) {
        return [];
    }
    const safeFilter = nameFilter.trim().replace(/'/g, "''");
    const query = `?$select=systemuserid,fullname,internalemailaddress` +
        `&$filter=contains(fullname,'${safeFilter}') and isdisabled eq false` +
        `&$orderby=fullname asc` +
        `&$top=10`;
    console.info('[MatterService] searchUsers query:', 'systemuser', query);
    try {
        const result = await dataService.retrieveMultipleRecords('systemuser', query);
        console.info('[MatterService] searchUsers results:', result.entities.length);
        return result.entities.map((e) => ({
            id: e['systemuserid'],
            name: e['fullname'] + (e['internalemailaddress'] ? ` (${e['internalemailaddress']})` : ''),
        }));
    }
    catch (err) {
        console.error('[MatterService] searchUsers error:', err);
        throw err;
    }
}
function buildFallbackSummary(matterName, matterType, practiceArea) {
    const type = matterType || 'general';
    const area = practiceArea || 'legal services';
    return {
        summary: `This ${type.toLowerCase()} matter, "${matterName}", has been created in the ${area} practice area. ` +
            `Key stakeholders have been identified and initial documentation has been uploaded for review. ` +
            `Next steps include counsel assignment and matter planning. ` +
            `Please review and update this summary with specific matter objectives and timeline.`,
    };
}
//# sourceMappingURL=matterService.js.map