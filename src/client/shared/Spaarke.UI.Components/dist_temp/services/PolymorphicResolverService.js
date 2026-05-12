/**
 * PolymorphicResolverService.ts
 *
 * Shared service for the Polymorphic Resolver pattern (ADR-024).
 * Provides helpers to populate both entity-specific lookups and
 * denormalized resolver fields when programmatically creating or
 * updating Dataverse records.
 *
 * Used by: WorkAssignmentService, EventService, CommunicationService,
 *          EntityCreationService, and any future wizard/service that
 *          creates child records with regarding associations.
 *
 * The pattern uses two field groups on the child entity:
 *   1. Entity-specific lookup: sprk_regarding{entity} (one per parent type)
 *   2. Resolver fields (denormalized for cross-entity views):
 *      - sprk_regardingrecordtype  (Lookup → sprk_recordtype_ref)
 *      - sprk_regardingrecordid    (Text — parent GUID)
 *      - sprk_regardingrecordname  (Text — parent display name)
 *      - sprk_regardingrecordurl   (URL — clickable link to parent)
 */
// ---------------------------------------------------------------------------
// Record Type Ref cache
// ---------------------------------------------------------------------------
const _recordTypeCache = new Map();
/**
 * Query sprk_recordtype_ref to get the record-type GUID for an entity logical name.
 * Results are cached for the lifetime of the page.
 */
export async function resolveRecordType(webApi, entityLogicalName) {
    const cached = _recordTypeCache.get(entityLogicalName);
    if (cached)
        return cached;
    try {
        const query = `?$filter=sprk_recordlogicalname eq '${entityLogicalName}' and statecode eq 0` +
            `&$select=sprk_recordtype_refid,sprk_recorddisplayname`;
        const result = await webApi.retrieveMultipleRecords('sprk_recordtype_ref', query);
        if (result.entities?.length > 0) {
            const rec = result.entities[0];
            const entry = {
                id: rec['sprk_recordtype_refid'],
                name: rec['sprk_recorddisplayname'],
            };
            _recordTypeCache.set(entityLogicalName, entry);
            return entry;
        }
    }
    catch (err) {
        console.warn(`[PolymorphicResolver] resolveRecordType(${entityLogicalName}) error:`, err);
    }
    return null;
}
// ---------------------------------------------------------------------------
// Record URL builder
// ---------------------------------------------------------------------------
/**
 * Build a Dataverse record URL for the sprk_regardingrecordurl field.
 * Tries to resolve clientUrl and appId from the Xrm context; falls back
 * to a relative URL.
 */
export function buildRecordUrl(entityLogicalName, recordId) {
    const cleanId = recordId.replace(/[{}]/g, '').toLowerCase();
    try {
        // Walk frames to find Xrm
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = window.Xrm ?? window.parent?.Xrm ?? window.top?.Xrm;
        const globalCtx = xrm?.Utility?.getGlobalContext?.();
        const clientUrl = globalCtx?.getClientUrl?.() ?? '';
        if (clientUrl) {
            const url = new URL('/main.aspx', clientUrl);
            // Try to get app ID from URL params
            const appId = new URLSearchParams(window.location.search).get('appid') ??
                new URLSearchParams(window.parent?.location?.search ?? '').get('appid') ??
                '';
            if (appId)
                url.searchParams.set('appid', appId.replace(/[{}]/g, '').toLowerCase());
            url.searchParams.set('pagetype', 'entityrecord');
            url.searchParams.set('etn', entityLogicalName);
            url.searchParams.set('id', cleanId);
            return url.toString();
        }
    }
    catch {
        // Cross-origin or missing Xrm — fall back
    }
    // Fallback: relative URL
    return `/main.aspx?pagetype=entityrecord&etn=${entityLogicalName}&id=${cleanId}`;
}
// ---------------------------------------------------------------------------
// Nav-prop helpers
// ---------------------------------------------------------------------------
/**
 * Find a navigation property by referenced entity and optional column hint.
 */
export function findNavProp(entries, referencedEntity, columnHint) {
    const matches = entries.filter(e => e.referencedEntity === referencedEntity);
    if (matches.length === 0)
        return undefined;
    if (matches.length === 1)
        return matches[0].navPropName;
    if (columnHint) {
        const hinted = matches.find(e => e.columnName.includes(columnHint));
        if (hinted)
            return hinted.navPropName;
    }
    return matches[0].navPropName;
}
// ---------------------------------------------------------------------------
// High-level: apply resolver fields to an entity payload
// ---------------------------------------------------------------------------
/**
 * Populate all polymorphic resolver fields on an entity payload object.
 *
 * Sets:
 *   - Entity-specific lookup via @odata.bind (if navProps provided)
 *   - sprk_regardingrecordid (text)
 *   - sprk_regardingrecordname (text)
 *   - sprk_regardingrecordurl (URL)
 *   - sprk_regardingrecordtype via @odata.bind to sprk_recordtype_ref
 *
 * @param webApi - WebApi for querying sprk_recordtype_ref
 * @param entity - The entity payload to populate (mutated in place)
 * @param navProps - Nav-props for the child entity (from discoverNavProps)
 * @param parentEntityLogicalName - e.g. "sprk_matter"
 * @param parentEntitySet - e.g. "sprk_matters"
 * @param parentRecordId - GUID of the parent record
 * @param parentRecordName - Display name of the parent record
 * @param entityLookupHint - Hint for finding the entity-specific nav-prop (e.g. "matter")
 */
export async function applyResolverFields(webApi, entity, navProps, parentEntityLogicalName, parentEntitySet, parentRecordId, parentRecordName, entityLookupHint) {
    // 1. Bind entity-specific regarding lookup
    const entityNavProp = findNavProp(navProps, parentEntityLogicalName, entityLookupHint);
    if (entityNavProp) {
        entity[`${entityNavProp}@odata.bind`] = `/${parentEntitySet}(${parentRecordId})`;
    }
    else {
        console.warn(`[PolymorphicResolver] No nav-prop for ${parentEntityLogicalName} (hint: ${entityLookupHint}), skipping entity-specific lookup`);
    }
    // 2. Populate denormalized text/URL fields
    entity['sprk_regardingrecordid'] = parentRecordId.replace(/[{}]/g, '').toLowerCase();
    entity['sprk_regardingrecordname'] = parentRecordName;
    entity['sprk_regardingrecordurl'] = buildRecordUrl(parentEntityLogicalName, parentRecordId);
    // 3. Bind sprk_regardingrecordtype lookup to sprk_recordtype_ref
    const recordType = await resolveRecordType(webApi, parentEntityLogicalName);
    if (recordType) {
        const rtNavProp = findNavProp(navProps, 'sprk_recordtype_ref', 'regardingrecordtype');
        if (rtNavProp) {
            entity[`${rtNavProp}@odata.bind`] = `/sprk_recordtype_refs(${recordType.id})`;
        }
        else {
            console.warn('[PolymorphicResolver] No nav-prop for sprk_recordtype_ref (regardingrecordtype)');
        }
    }
    else {
        console.warn(`[PolymorphicResolver] Record type ref not found for ${parentEntityLogicalName}`);
    }
}
//# sourceMappingURL=PolymorphicResolverService.js.map