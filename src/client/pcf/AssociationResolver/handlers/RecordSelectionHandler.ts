/**
 * RecordSelectionHandler - Manages regarding field population
 *
 * When a user selects a related record in AssociationResolver:
 * 1. Sets the entity-specific lookup field (e.g., sprk_regardingmatter for Matter)
 * 2. Sets 5 denormalized resolver fields (name, id, url, type, number)
 * 3. Clears the other N-1 entity-specific lookup fields
 *
 * SRFR-051 refactor: this handler is now a THIN ADAPTER over the shared
 * `@spaarke/ui-components` primitives. It no longer duplicates the
 * denormalized-write logic that lives in `PolymorphicResolverService`:
 *
 *   - Record Type lookup     → `resolveRecordType`         (was local `getRecordTypeByEntityLogicalName`)
 *   - Record URL construction → `buildRecordUrl`            (was local `buildRecordUrl` + `getAppIdFromUrl`)
 *   - Record Number source-field → `resolveRecordNumberFieldName` (new — 5th field per FR-B5-01)
 *
 * The handler retains a form-mode write path because AssociationResolver
 * writes to a MDA form's attributes via `Xrm.Page.getAttribute().setValue()`
 * — a fundamentally different data pathway from the payload-mutation
 * semantics of `applyResolverFields()` (which produces `@odata.bind`
 * payloads for `webApi.createRecord()` / `webApi.updateRecord()`). The
 * form-write orchestration lives here per ADR-012 (context-agnostic
 * shared lib) while the field-name decisions and metadata lookups are
 * fully delegated to the shared service.
 *
 * ADR Compliance:
 * - ADR-006: Logic in PCF control code, not Business Rules
 * - ADR-012: Shared services consumed via `@spaarke/ui-components` public exports only
 * - ADR-021: No UI changes here (Fluent UI v9 in components)
 * - ADR-024: Dual-field invariants preserved (5th field per FR-A4-01 project amendment / Path B)
 * - ADR-038: No `Mock<HttpMessageHandler>` in tests — inject `webApi` shim
 *
 * Entity configuration is loaded dynamically from `sprk_recordtype_ref`
 * (see `loadEntityConfigs()` below). The former hardcoded
 * `ENTITY_LOOKUP_CONFIGS` const was retired in SRFR-050 per FR-B4-01.
 *
 * @version 1.1.0 → 1.2.0 (SRFR-051 refactor; version bumped in SRFR-053)
 */

import { createLogger, buildRecordUrl, resolveRecordType, resolveRecordNumberFieldName } from '@spaarke/ui-components';

const logger = createLogger('AssociationResolver');

/**
 * Selection data passed when user picks a record
 */
export interface IRecordSelection {
  entityType: string; // Logical name (e.g., "sprk_matter")
  recordId: string; // GUID of selected record
  recordName: string; // Display name of selected record
}

/**
 * Entity configuration mapping
 */
export interface EntityLookupConfig {
  logicalName: string;
  displayName: string;
  regardingField: string;
  /**
   * Source-field logical name on the target entity, from
   * `sprk_recordtype_ref.sprk_regardingrecordnumberfield`. When set, the resolver
   * reads this field on the target record (e.g., `sprk_matternumber` on Matter) and
   * writes its value to the host's `sprk_regardingrecordnumber` column. Optional
   * because (a) some target entities have no canonical record-number field (OOB
   * account/contact per Owner Clarification Q-06 / NFR-06 graceful-blank behavior),
   * and (b) task SRFR-050 populates this dynamically via the config loader —
   * older callers that pre-date FR-B4-01 continue to compile unchanged.
   *
   * Consumers: task SRFR-020 (`PolymorphicResolverService.applyResolverFields` 5-field write)
   * and task SRFR-050 (AssociationResolver dynamic config loader).
   */
  regardingRecordNumberField?: string;
}

/**
 * Denormalized resolver field names.
 *
 * SRFR-051: `recordNumber` (5th field) added to reflect FR-B5-01. The
 * corresponding write is delegated to the shared service's data-driven
 * `resolveRecordNumberFieldName` helper — no field-list duplication here.
 */
const DENORMALIZED_FIELDS = {
  recordName: 'sprk_regardingrecordname',
  recordId: 'sprk_regardingrecordid',
  recordType: 'sprk_regardingrecordtype',
  recordUrl: 'sprk_regardingrecordurl',
  recordNumber: 'sprk_regardingrecordnumber',
} as const;

// Dynamic entity config cache - loaded from sprk_recordtype_ref
let dynamicEntityConfigs: EntityLookupConfig[] | null = null;
let entityConfigsLoading = false;
const entityConfigsLoadPromise: {
  promise: Promise<EntityLookupConfig[]> | null;
} = { promise: null };

/**
 * Load entity configurations dynamically from sprk_recordtype_ref.
 *
 * The former hardcoded ENTITY_LOOKUP_CONFIGS const was retired in SRFR-050
 * (FR-B4-01). The dynamic catalog is now the sole source of entity configs.
 * If the catalog query fails or returns zero rows, we resolve to an empty
 * array so consumers can gracefully degrade (dropdown empty, no fields
 * cleared/set) rather than silently binding to a stale hardcoded list.
 */
export async function loadEntityConfigs(webApi: ComponentFramework.WebApi): Promise<EntityLookupConfig[]> {
  // Return cached if available
  if (dynamicEntityConfigs) {
    return dynamicEntityConfigs;
  }

  // If already loading, wait for that promise
  if (entityConfigsLoading && entityConfigsLoadPromise.promise) {
    return entityConfigsLoadPromise.promise;
  }

  entityConfigsLoading = true;
  entityConfigsLoadPromise.promise = (async () => {
    try {
      logger.logInfo('RecordSelection', 'Loading entity configs from sprk_recordtype_ref...');
      // SRFR-023 (FR-B4-01): include sprk_regardingrecordnumberfield so downstream
      // resolvers (task SRFR-020) can write the 5th field, sprk_regardingrecordnumber.
      const query = `?$filter=statecode eq 0&$select=sprk_recordtype_refid,sprk_recordlogicalname,sprk_recorddisplayname,sprk_regardingfield,sprk_regardingrecordnumberfield&$orderby=sprk_recorddisplayname`;
      const result = await webApi.retrieveMultipleRecords('sprk_recordtype_ref', query);

      if (result.entities && result.entities.length > 0) {
        dynamicEntityConfigs = result.entities
          .filter((e: Record<string, unknown>) => e.sprk_recordlogicalname && e.sprk_regardingfield)
          .map((e: Record<string, unknown>) => ({
            logicalName: e.sprk_recordlogicalname as string,
            displayName: (e.sprk_recorddisplayname || e.sprk_recordlogicalname) as string,
            regardingField: e.sprk_regardingfield as string,
            // Optional — undefined when the catalog row leaves it blank
            // (e.g., OOB account/contact per Owner Clarification Q-06).
            regardingRecordNumberField: (e.sprk_regardingrecordnumberfield as string | undefined) || undefined,
          }));

        logger.logInfo(
          'RecordSelection',
          `Loaded ${dynamicEntityConfigs.length} entity configs`,
          dynamicEntityConfigs.map(c => c.logicalName)
        );
        return dynamicEntityConfigs;
      } else {
        logger.logWarn(
          'RecordSelection',
          'No Record Types returned from sprk_recordtype_ref — resolving to empty config list'
        );
        dynamicEntityConfigs = [];
        return dynamicEntityConfigs;
      }
    } catch (error) {
      logger.logError(
        'RecordSelection',
        'Error loading entity configs from sprk_recordtype_ref — resolving to empty config list',
        error
      );
      dynamicEntityConfigs = [];
      return dynamicEntityConfigs;
    } finally {
      entityConfigsLoading = false;
    }
  })();

  return entityConfigsLoadPromise.promise;
}

/**
 * Get entity configs (sync accessor over the dynamic catalog cache).
 *
 * Returns the cached `dynamicEntityConfigs` from the last successful
 * `loadEntityConfigs()` call. Returns an empty array during the initial
 * pre-load window (before `loadEntityConfigs()` has resolved). Callers
 * that require populated configs must `await loadEntityConfigs()` first.
 */
export function getEntityConfigs(): EntityLookupConfig[] {
  return dynamicEntityConfigs ?? [];
}

/**
 * Get Xrm.Page from parent window (PCF runs in iframe)
 */
function getXrmPage(): Xrm.Page | null {
  try {
    const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
    return xrm?.Page || null;
  } catch (error) {
    logger.logWarn('RecordSelection', 'Unable to access Xrm.Page', error);
    return null;
  }
}

/**
 * Set a lookup field value
 * Handles gracefully if field doesn't exist on form
 */
function setLookupValue(fieldName: string, entityType: string, id: string, name: string): boolean {
  const xrmPage = getXrmPage();
  if (!xrmPage) {
    logger.logWarn('RecordSelection', 'Xrm.Page not available');
    return false;
  }

  try {
    const attr = xrmPage.getAttribute(fieldName);
    if (attr) {
      // Format GUID without braces for lookup value
      const formattedId = id.replace(/[{}]/g, '');
      attr.setValue([
        {
          id: formattedId,
          name: name,
          entityType: entityType,
        },
      ]);
      logger.logDebug('RecordSelection', `Set ${fieldName} to ${name} (${formattedId})`);
      return true;
    } else {
      logger.logWarn('RecordSelection', `Field ${fieldName} not found on form`);
      return false;
    }
  } catch (error) {
    logger.logError('RecordSelection', `Error setting ${fieldName}`, error);
    return false;
  }
}

/**
 * Clear a lookup field value
 * Handles gracefully if field doesn't exist on form
 */
function clearLookupValue(fieldName: string): boolean {
  const xrmPage = getXrmPage();
  if (!xrmPage) {
    logger.logWarn('RecordSelection', 'Xrm.Page not available');
    return false;
  }

  try {
    const attr = xrmPage.getAttribute(fieldName);
    if (attr) {
      attr.setValue(null);
      logger.logDebug('RecordSelection', `Cleared ${fieldName}`);
      return true;
    } else {
      // Field not on form - not an error, just skip
      return true;
    }
  } catch (error) {
    logger.logError('RecordSelection', `Error clearing ${fieldName}`, error);
    return false;
  }
}

/**
 * Set a text field value
 * Handles gracefully if field doesn't exist on form
 */
function setTextValue(fieldName: string, value: string | null): boolean {
  const xrmPage = getXrmPage();
  if (!xrmPage) {
    logger.logWarn('RecordSelection', 'Xrm.Page not available');
    return false;
  }

  try {
    const attr = xrmPage.getAttribute(fieldName);
    if (attr) {
      attr.setValue(value);
      logger.logDebug('RecordSelection', `Set ${fieldName} to "${value}"`);
      return true;
    } else {
      logger.logWarn('RecordSelection', `Field ${fieldName} not found on form`);
      return false;
    }
  } catch (error) {
    logger.logError('RecordSelection', `Error setting ${fieldName}`, error);
    return false;
  }
}

/**
 * Result of resolving the 5-field denormalized write via shared primitives.
 * Consumed by the form-write path (setValue) — not a create/update payload.
 */
interface IResolvedResolverFields {
  recordName: string;
  recordId: string;
  recordUrl: string;
  recordTypeLookup: { id: string; name: string } | null;
  recordNumber: string | null;
  recordNumberSourceField: string | null;
}

/**
 * Resolve the 5 denormalized resolver-field values using shared primitives.
 *
 * SRFR-051 core delegation: all metadata + URL + record-number logic is
 * sourced from `@spaarke/ui-components` — no local duplication remains.
 * Returned values are then written to the form via `applyResolvedFieldsToForm()`
 * (or omitted per NFR-06 graceful-blank when null).
 *
 * @param webApi                    WebApi shim
 * @param parentEntityLogicalName   Target entity (e.g., "sprk_matter")
 * @param parentRecordId            GUID of target record (with or without braces)
 * @param parentRecordName          Display name of target record
 * @param sourceRecordNumberField   Optional override for the record-number
 *                                  source-field name. When omitted, resolved
 *                                  via shared `resolveRecordNumberFieldName`.
 */
async function resolveResolverFields(
  webApi: ComponentFramework.WebApi,
  parentEntityLogicalName: string,
  parentRecordId: string,
  parentRecordName: string,
  sourceRecordNumberField?: string
): Promise<IResolvedResolverFields> {
  const cleanId = parentRecordId.replace(/[{}]/g, '').toLowerCase();

  // 1. Record URL — delegated to shared `buildRecordUrl`
  const recordUrl = buildRecordUrl(parentEntityLogicalName, cleanId);

  // 2. Record Type lookup — delegated to shared `resolveRecordType`
  const recordTypeLookup = await resolveRecordType(webApi, parentEntityLogicalName);
  if (!recordTypeLookup) {
    logger.logWarn('RecordSelection', `Record Type not found for entity: ${parentEntityLogicalName}`);
  }

  // 3. Record Number — delegated to shared `resolveRecordNumberFieldName`
  //    then query the target record for the source-field VALUE.
  //    NFR-06 graceful-blank: null-in → null-out; log warn; do not throw.
  const explicitOverride =
    typeof sourceRecordNumberField === 'string' && sourceRecordNumberField.trim().length > 0
      ? sourceRecordNumberField.trim()
      : null;

  const recordNumberSourceField =
    explicitOverride ?? (await resolveRecordNumberFieldName(webApi, parentEntityLogicalName));

  let recordNumber: string | null = null;
  if (recordNumberSourceField) {
    try {
      const primaryIdAttr = `${parentEntityLogicalName}id`;
      const query = `?$filter=${primaryIdAttr} eq ${cleanId}&$select=${recordNumberSourceField}&$top=1`;
      const result = await webApi.retrieveMultipleRecords(parentEntityLogicalName, query, 1);
      if (result.entities && result.entities.length > 0) {
        const raw = result.entities[0][recordNumberSourceField];
        if (typeof raw === 'string' && raw.trim().length > 0) {
          recordNumber = raw.trim();
        } else if (typeof raw === 'number') {
          recordNumber = String(raw);
        }
      }
      if (recordNumber === null) {
        logger.logWarn(
          'RecordSelection',
          `Target ${parentEntityLogicalName}(${cleanId}) has null/empty "${recordNumberSourceField}"; skipping sprk_regardingrecordnumber write (NFR-06).`
        );
      }
    } catch (err) {
      logger.logWarn(
        'RecordSelection',
        `Failed to read "${recordNumberSourceField}" from ${parentEntityLogicalName}(${cleanId}) — skipping sprk_regardingrecordnumber write (NFR-06).`,
        err
      );
    }
  } else {
    logger.logWarn(
      'RecordSelection',
      `No sprk_regardingrecordnumberfield mapping for "${parentEntityLogicalName}"; skipping sprk_regardingrecordnumber write (NFR-06).`
    );
  }

  return {
    recordName: parentRecordName,
    recordId: cleanId,
    recordUrl,
    recordTypeLookup,
    recordNumber,
    recordNumberSourceField,
  };
}

/**
 * Apply the resolved denormalized fields to the Xrm.Page form via
 * `setValue()` calls. This is the form-mode adapter half of the thin
 * adapter — the shared service provides the VALUES, this function
 * writes them to the form.
 *
 * @returns `true` if all writes that were attempted succeeded (skipped
 *          writes for graceful-blank are not considered failures).
 */
function applyResolvedFieldsToForm(resolved: IResolvedResolverFields): boolean {
  const errors: string[] = [];

  if (!setTextValue(DENORMALIZED_FIELDS.recordName, resolved.recordName)) {
    errors.push(DENORMALIZED_FIELDS.recordName);
  }

  if (!setTextValue(DENORMALIZED_FIELDS.recordId, resolved.recordId)) {
    errors.push(DENORMALIZED_FIELDS.recordId);
  }

  if (!setTextValue(DENORMALIZED_FIELDS.recordUrl, resolved.recordUrl)) {
    errors.push(DENORMALIZED_FIELDS.recordUrl);
  }

  if (resolved.recordTypeLookup) {
    if (
      !setLookupValue(
        DENORMALIZED_FIELDS.recordType,
        'sprk_recordtype_ref',
        resolved.recordTypeLookup.id,
        resolved.recordTypeLookup.name
      )
    ) {
      errors.push(DENORMALIZED_FIELDS.recordType);
    }
  } else {
    // Record Type unresolved — treat as a write failure so the caller
    // surfaces the correct error to the user.
    errors.push(DENORMALIZED_FIELDS.recordType);
  }

  // 5th field (SRFR-051 / FR-B5-01) — graceful-blank when null (NFR-06).
  if (resolved.recordNumber !== null) {
    if (!setTextValue(DENORMALIZED_FIELDS.recordNumber, resolved.recordNumber)) {
      errors.push(DENORMALIZED_FIELDS.recordNumber);
    }
  }

  return errors.length === 0;
}

/**
 * Result of handling a record selection
 */
export interface IRecordSelectionResult {
  success: boolean;
  lookupFieldSet: boolean;
  denormalizedFieldsSet: boolean;
  otherLookupsCleared: number;
  errors: string[];
}

/**
 * Handle record selection - main entry point (async)
 *
 * When a record is selected:
 * 1. Clear all N entity-specific lookup fields
 * 2. Set the selected entity's lookup field
 * 3. Resolve + write the 5 denormalized fields (via shared primitives)
 *
 * @param selection - The selected record details
 * @param webApi - WebAPI for Record Type + record-number queries
 */
export async function handleRecordSelection(
  selection: IRecordSelection,
  webApi: ComponentFramework.WebApi
): Promise<IRecordSelectionResult> {
  const result: IRecordSelectionResult = {
    success: false,
    lookupFieldSet: false,
    denormalizedFieldsSet: false,
    otherLookupsCleared: 0,
    errors: [],
  };

  logger.logInfo('RecordSelection', `Processing selection: ${selection.entityType} - ${selection.recordName}`);

  // Find the config for the selected entity type
  const selectedConfig = getEntityConfigs().find(c => c.logicalName === selection.entityType);
  if (!selectedConfig) {
    const error = `Unknown entity type: ${selection.entityType}`;
    logger.logError('RecordSelection', error);
    result.errors.push(error);
    return result;
  }

  // Step 1: Clear ALL entity-specific lookup fields first (including the one we'll set)
  //         This is the AssociationResolver-specific behavior that wraps around the
  //         shared 5-field write — preserved verbatim by SRFR-051.
  for (const config of getEntityConfigs()) {
    if (clearLookupValue(config.regardingField)) {
      result.otherLookupsCleared++;
    }
  }
  // Adjust count - we'll set one back
  result.otherLookupsCleared--;

  // Step 2: Set the selected entity's lookup field
  result.lookupFieldSet = setLookupValue(
    selectedConfig.regardingField,
    selection.entityType,
    selection.recordId,
    selection.recordName
  );

  if (!result.lookupFieldSet) {
    result.errors.push(`Failed to set ${selectedConfig.regardingField}`);
  }

  // Step 3: Resolve + write the 5 denormalized fields via shared primitives
  //         (SRFR-051 delegation: all field-value construction, URL, record-type,
  //          and record-number logic now lives in @spaarke/ui-components.)
  const resolved = await resolveResolverFields(
    webApi,
    selection.entityType,
    selection.recordId,
    selection.recordName,
    selectedConfig.regardingRecordNumberField
  );

  const denormalizedSuccess = applyResolvedFieldsToForm(resolved);
  result.denormalizedFieldsSet = denormalizedSuccess;

  if (!denormalizedSuccess) {
    if (!resolved.recordTypeLookup) {
      result.errors.push(`Record Type not found for entity: ${selection.entityType}`);
    } else {
      result.errors.push('Failed to write one or more denormalized fields');
    }
  }

  // Overall success if lookup was set (denormalized fields are secondary)
  result.success = result.lookupFieldSet;

  logger.logInfo(
    'RecordSelection',
    `Result: success=${result.success}, lookupSet=${result.lookupFieldSet}, denormalized=${result.denormalizedFieldsSet}, cleared=${result.otherLookupsCleared}, recordNumber=${resolved.recordNumber ?? 'null'}`
  );

  return result;
}

/**
 * Clear all regarding fields (used when clearing selection)
 */
export function clearAllRegardingFields(): void {
  logger.logInfo('RecordSelection', 'Clearing all regarding fields');

  // Clear all entity-specific lookups
  for (const config of getEntityConfigs()) {
    clearLookupValue(config.regardingField);
  }

  // Clear denormalized fields (5 fields per SRFR-051)
  setTextValue(DENORMALIZED_FIELDS.recordName, null);
  setTextValue(DENORMALIZED_FIELDS.recordId, null);
  setTextValue(DENORMALIZED_FIELDS.recordUrl, null);
  setTextValue(DENORMALIZED_FIELDS.recordNumber, null);
  clearLookupValue(DENORMALIZED_FIELDS.recordType); // lookup — use clearLookupValue
}

/**
 * Get the entity config for a logical name.
 *
 * Reads from `dynamicEntityConfigs` (populated by `loadEntityConfigs()`).
 * Returns `undefined` if the catalog has not yet loaded or does not contain
 * the requested logical name — callers must handle the not-found case.
 */
export function getEntityConfig(logicalName: string): EntityLookupConfig | undefined {
  return (dynamicEntityConfigs ?? []).find(c => c.logicalName === logicalName);
}

/**
 * Get all entity configs (for dropdown population).
 *
 * Returns a shallow copy of the dynamic catalog. Returns `[]` during the
 * initial pre-load window — dropdowns should render "loading" state until
 * `loadEntityConfigs()` resolves.
 */
export function getAllEntityConfigs(): EntityLookupConfig[] {
  return [...(dynamicEntityConfigs ?? [])];
}

/**
 * Result of auto-detecting a pre-populated regarding field
 */
export interface IDetectedParentContext {
  entityType: string; // Logical name (e.g., "sprk_matter")
  entityDisplayName: string; // Display name (e.g., "Matter")
  recordId: string; // GUID of the parent record
  recordName: string; // Display name of the parent record
  regardingField: string; // The field that was populated (e.g., "sprk_regardingmatter")
}

/**
 * Detect if any regarding lookup field is pre-populated (from subgrid context)
 *
 * When creating an Event from a parent's subgrid, Dataverse can auto-populate
 * the lookup field via relationship mapping. This function checks all
 * entity-specific lookup fields to find which one (if any) is populated.
 *
 * @returns The detected parent context, or null if no parent is detected
 */
export function detectPrePopulatedParent(): IDetectedParentContext | null {
  const xrmPage = getXrmPage();
  if (!xrmPage) {
    logger.logDebug('RecordSelection', 'Xrm.Page not available for parent detection');
    return null;
  }

  logger.logInfo('RecordSelection', 'Checking for pre-populated regarding fields...');

  // Check each entity-specific lookup field
  for (const config of getEntityConfigs()) {
    try {
      const attr = xrmPage.getAttribute(config.regardingField);
      if (attr) {
        const value = attr.getValue() as Xrm.LookupValue[] | null;
        // Lookup values are arrays with entity references
        if (value && Array.isArray(value) && value.length > 0) {
          const lookupValue = value[0] as Xrm.LookupValue;
          if (lookupValue && lookupValue.id) {
            const detected: IDetectedParentContext = {
              entityType: config.logicalName,
              entityDisplayName: config.displayName,
              recordId: lookupValue.id.replace(/[{}]/g, ''),
              recordName: lookupValue.name || '',
              regardingField: config.regardingField,
            };
            logger.logInfo(
              'RecordSelection',
              `Detected pre-populated parent: ${config.displayName} - ${detected.recordName} (${detected.recordId})`
            );
            return detected;
          }
        }
      }
    } catch (error) {
      logger.logWarn('RecordSelection', `Error checking ${config.regardingField}`, error);
    }
  }

  logger.logInfo('RecordSelection', 'No pre-populated regarding field detected');
  return null;
}

/**
 * Complete the association for a detected parent context
 *
 * When a parent is auto-detected from a subgrid, this function:
 * 1. Resolves + writes the 5 denormalized fields via shared primitives
 * 2. Does NOT clear other lookups (only one should be populated from subgrid)
 *
 * @param detectedParent - The detected parent context from detectPrePopulatedParent()
 * @param webApi - WebAPI for Record Type + record-number queries
 * @returns Result of the operation
 */
export async function completeAutoDetectedAssociation(
  detectedParent: IDetectedParentContext,
  webApi: ComponentFramework.WebApi
): Promise<IRecordSelectionResult> {
  const result: IRecordSelectionResult = {
    success: false,
    lookupFieldSet: true, // Already set by Dataverse
    denormalizedFieldsSet: false,
    otherLookupsCleared: 0,
    errors: [],
  };

  logger.logInfo(
    'RecordSelection',
    `Completing auto-detected association: ${detectedParent.entityDisplayName} - ${detectedParent.recordName}`
  );

  // Look up the source-field override (if the loaded config has it).
  // Absent config → let the shared service resolve via metadata.
  const config = getEntityConfig(detectedParent.entityType);

  // Resolve + write the 5 denormalized fields via shared primitives
  const resolved = await resolveResolverFields(
    webApi,
    detectedParent.entityType,
    detectedParent.recordId,
    detectedParent.recordName,
    config?.regardingRecordNumberField
  );

  const denormalizedSuccess = applyResolvedFieldsToForm(resolved);
  result.denormalizedFieldsSet = denormalizedSuccess;

  if (!denormalizedSuccess) {
    if (!resolved.recordTypeLookup) {
      result.errors.push(`Record Type not found for entity: ${detectedParent.entityType}`);
    } else {
      result.errors.push('Failed to write one or more denormalized fields');
    }
  }

  result.success = denormalizedSuccess;

  logger.logInfo(
    'RecordSelection',
    `Auto-detection completion result: success=${result.success}, denormalized=${result.denormalizedFieldsSet}, recordNumber=${resolved.recordNumber ?? 'null'}`
  );

  return result;
}
