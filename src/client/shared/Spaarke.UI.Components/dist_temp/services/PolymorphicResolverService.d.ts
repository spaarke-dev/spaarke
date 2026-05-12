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
/** Minimal WebApi interface matching both Xrm.WebApi and our IWebApi type. */
export interface IPolymorphicWebApi {
    retrieveMultipleRecords(entityLogicalName: string, query: string, maxPageSize?: number): Promise<{
        entities: Record<string, unknown>[];
    }>;
}
/** Result of querying sprk_recordtype_ref for an entity's record type. */
export interface IRecordTypeRef {
    id: string;
    name: string;
}
/** Nav-prop entry from ManyToOneRelationships metadata discovery. */
export interface INavPropEntry {
    columnName: string;
    navPropName: string;
    referencedEntity: string;
}
/** All the fields needed to populate the polymorphic resolver on a record. */
export interface IResolverFieldValues {
    /** Entity-specific lookup @odata.bind value (e.g., `/sprk_matters(guid)`). */
    entitySpecificBind?: {
        navProp: string;
        value: string;
    };
    /** sprk_regardingrecordtype @odata.bind value. */
    recordTypeBind?: {
        navProp: string;
        value: string;
    };
    /** sprk_regardingrecordid — parent GUID as text. */
    recordId: string;
    /** sprk_regardingrecordname — parent display name. */
    recordName: string;
    /** sprk_regardingrecordurl — clickable URL to parent record. */
    recordUrl: string;
}
/**
 * Query sprk_recordtype_ref to get the record-type GUID for an entity logical name.
 * Results are cached for the lifetime of the page.
 */
export declare function resolveRecordType(webApi: IPolymorphicWebApi, entityLogicalName: string): Promise<IRecordTypeRef | null>;
/**
 * Build a Dataverse record URL for the sprk_regardingrecordurl field.
 * Tries to resolve clientUrl and appId from the Xrm context; falls back
 * to a relative URL.
 */
export declare function buildRecordUrl(entityLogicalName: string, recordId: string): string;
/**
 * Find a navigation property by referenced entity and optional column hint.
 */
export declare function findNavProp(entries: INavPropEntry[], referencedEntity: string, columnHint?: string): string | undefined;
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
export declare function applyResolverFields(webApi: IPolymorphicWebApi, entity: Record<string, unknown>, navProps: INavPropEntry[], parentEntityLogicalName: string, parentEntitySet: string, parentRecordId: string, parentRecordName: string, entityLookupHint?: string): Promise<void>;
//# sourceMappingURL=PolymorphicResolverService.d.ts.map