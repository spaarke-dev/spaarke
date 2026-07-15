/**
 * View Data Service — WebAPI executors for view-driven data fetching.
 *
 * Retrieves saved-view FetchXML from Dataverse, resolves a query by priority,
 * and executes FetchXML via `webApi`. The PURE FetchXML-string helpers
 * (injectContextFilter, injectRequiredAttributes, applyMaxItems,
 * substituteParameters) were extracted to ./fetchXmlBuilders in VHVU-050 so the
 * "pure vs executor" boundary is explicit. They are re-exported below so
 * existing importers keep their `from './ViewDataService'` paths.
 *
 * Tasks 040-042 - Visualization Module R2 · split VHVU-050
 */

import type { IChartDefinition } from '../types';
import type { IConfigWebApi } from './ConfigurationLoader';
import { logger } from '../utils/logger';
import { injectContextFilter, applyMaxItems, substituteParameters, type ISubstitutionParams } from './fetchXmlBuilders';

// VHVU-050 — pure FetchXML-string helpers now live in ./fetchXmlBuilders.
// Re-export them here so existing importers (DataAggregationService, the PCF
// visual containers) keep working without touching their import paths.
export { injectContextFilter, injectRequiredAttributes, applyMaxItems, substituteParameters } from './fetchXmlBuilders';
export type { ISubstitutionParams } from './fetchXmlBuilders';

/**
 * Context for view-driven data fetching
 */
export interface IViewDataContext {
  /** Saved view ID (savedquery GUID) */
  viewId: string;
  /** Optional context filter for related record filtering */
  contextFilter?: { fieldName: string; recordId: string };
  /** Maximum items to return */
  maxItems?: number;
  /** Runtime parameters for FetchXML substitution */
  substitutionParams?: ISubstitutionParams;
  /** Additional parameter mappings JSON */
  paramMappings?: string;
}

/**
 * View data service error
 */
export class ViewDataError extends Error {
  constructor(
    message: string,
    public readonly cause?: unknown
  ) {
    super(message);
    this.name = 'ViewDataError';
  }
}

/**
 * Event record mapped from Dataverse
 */
export interface IEventRecord {
  eventId: string;
  eventName: string;
  eventTypeName: string;
  dueDate: Date;
  daysUntilDue: number;
  isOverdue: boolean;
  eventTypeColor?: string;
  description?: string;
  assignedTo?: string;
}

/**
 * Cache for view FetchXML definitions
 */
const viewCache = new Map<string, { fetchXml: string; entityName: string; timestamp: number }>();
const VIEW_CACHE_TTL_MS = 10 * 60 * 1000; // 10 minutes (views change infrequently)

/**
 * Clear view cache
 */
export function clearViewCache(viewId?: string): void {
  if (viewId) {
    viewCache.delete(viewId);
  } else {
    viewCache.clear();
  }
}

/**
 * Retrieve FetchXML from a Dataverse saved view (savedquery entity)
 *
 * @param webApi - WebAPI interface
 * @param viewId - The savedquery GUID
 * @returns Object containing the fetchxml string and entity logical name
 */
export async function getViewFetchXml(
  webApi: IConfigWebApi,
  viewId: string
): Promise<{ fetchXml: string; entityName: string }> {
  const normalizedId = viewId.replace(/[{}]/g, '').toLowerCase();

  // Check cache
  const cached = viewCache.get(normalizedId);
  if (cached && Date.now() - cached.timestamp < VIEW_CACHE_TTL_MS) {
    logger.debug('ViewDataService', `View cache hit for ${normalizedId}`);
    return { fetchXml: cached.fetchXml, entityName: cached.entityName };
  }

  logger.info('ViewDataService', `Retrieving view definition: ${normalizedId}`);

  // Try savedquery (system views) first, then userquery (personal views)
  const viewEntities = ['savedquery', 'userquery'];

  for (const entityType of viewEntities) {
    try {
      logger.debug('ViewDataService', `Trying ${entityType} for ${normalizedId}`);
      const record = await webApi.retrieveRecord(entityType, normalizedId, '?$select=fetchxml,returnedtypecode,name');

      const fetchXml = record.fetchxml as string;
      const entityName = record.returnedtypecode as string;

      if (!fetchXml) {
        throw new ViewDataError(`View ${normalizedId} has no FetchXML defined`);
      }

      // Cache the result
      viewCache.set(normalizedId, {
        fetchXml,
        entityName: entityName || '',
        timestamp: Date.now(),
      });

      logger.info('ViewDataService', `Retrieved view from ${entityType}: ${record.name}`, {
        entityName,
        fetchXmlLength: fetchXml.length,
      });

      return { fetchXml, entityName };
    } catch (error) {
      if (error instanceof ViewDataError) throw error;

      const msg = extractViewErrorMessage(error);
      logger.debug('ViewDataService', `${entityType} lookup failed: ${msg}`);

      // If this is the last entity type to try, throw the error
      if (entityType === viewEntities[viewEntities.length - 1]) {
        if (msg.includes('does not exist') || msg.includes('not found') || msg.includes('0x80040217')) {
          throw new ViewDataError(`View not found in savedquery or userquery: ${normalizedId}`);
        }
        throw new ViewDataError(`Failed to retrieve view: ${msg}`, error);
      }
      // Otherwise, continue to the next entity type
    }
  }

  // Should not reach here, but just in case
  throw new ViewDataError(`View not found: ${normalizedId}`);
}

/**
 * Extract a readable error message from Dataverse WebAPI error objects.
 * PCF WebAPI errors are plain objects with { errorCode, message }, not Error instances.
 */
function extractViewErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === 'object') {
    const obj = error as Record<string, unknown>;
    if (typeof obj.message === 'string') return obj.message;
    try {
      return JSON.stringify(error);
    } catch {
      /* ignore */
    }
  }
  return String(error);
}

// ──────────────────────────────────────────────────
// Query Priority Resolution (Task 042)
// ──────────────────────────────────────────────────

/**
 * Query source resolved by priority.
 * Priority order: pcfOverride → customFetchXml → view → directEntity
 */
export type QuerySource = 'pcfOverride' | 'customFetchXml' | 'view' | 'directEntity';

/**
 * Resolved query result from priority resolution
 */
export interface IResolvedQuery {
  /** Which source provided the query */
  source: QuerySource;
  /** The FetchXML to execute (if applicable) */
  fetchXml?: string;
  /** Entity logical name for the query */
  entityName: string;
}

/**
 * Inputs for query priority resolution
 */
export interface IQueryResolutionInputs {
  /** Chart definition with query configuration */
  chartDefinition: IChartDefinition;
  /** Optional PCF-level FetchXML override (from fetchXmlOverride property) */
  fetchXmlOverride?: string;
  /** Runtime substitution parameters */
  substitutionParams?: ISubstitutionParams;
  /** WebAPI for view retrieval */
  webApi: IConfigWebApi;
}

/**
 * Resolve which query source to use based on priority.
 *
 * Priority order (highest to lowest):
 * 1. PCF fetchXmlOverride property → per-deployment override
 * 2. Chart Definition sprk_fetchxmlquery → custom FetchXML on the record
 * 3. Chart Definition sprk_baseviewid → saved view reference
 * 4. Direct entity query → fallback using sprk_entitylogicalname
 *
 * @param inputs - All available query sources and parameters
 * @returns Resolved query with source, fetchXml, and entityName
 */
export async function resolveQuery(inputs: IQueryResolutionInputs): Promise<IResolvedQuery> {
  const { chartDefinition, fetchXmlOverride, substitutionParams, webApi } = inputs;
  const entityName = chartDefinition.sprk_entitylogicalname || 'sprk_event';

  // Priority 1: PCF override
  if (fetchXmlOverride?.trim()) {
    logger.info('ViewDataService', 'Using PCF fetchXmlOverride', {
      entityName,
    });
    let fetchXml = fetchXmlOverride;

    if (substitutionParams) {
      fetchXml = substituteParameters(fetchXml, substitutionParams, chartDefinition.sprk_fetchxmlparams);
    }

    return { source: 'pcfOverride', fetchXml, entityName };
  }

  // Priority 2: Custom FetchXML on chart definition
  if (chartDefinition.sprk_fetchxmlquery?.trim()) {
    logger.info('ViewDataService', 'Using custom FetchXML from chart definition', { entityName });
    let fetchXml = chartDefinition.sprk_fetchxmlquery;

    if (substitutionParams) {
      fetchXml = substituteParameters(fetchXml, substitutionParams, chartDefinition.sprk_fetchxmlparams);
    }

    return { source: 'customFetchXml', fetchXml, entityName };
  }

  // Priority 3: Saved view
  if (chartDefinition.sprk_baseviewid?.trim()) {
    logger.info('ViewDataService', 'Using saved view', {
      viewId: chartDefinition.sprk_baseviewid,
      entityName,
    });

    const { fetchXml: viewFetchXml, entityName: viewEntity } = await getViewFetchXml(
      webApi,
      chartDefinition.sprk_baseviewid
    );

    let fetchXml = viewFetchXml;
    if (substitutionParams) {
      fetchXml = substituteParameters(fetchXml, substitutionParams, chartDefinition.sprk_fetchxmlparams);
    }

    return {
      source: 'view',
      fetchXml,
      entityName: viewEntity || entityName,
    };
  }

  // Priority 4: Direct entity query (caller builds FetchXML from entity name)
  logger.info('ViewDataService', 'Using direct entity query (no FetchXML source)', { entityName });
  return { source: 'directEntity', entityName };
}

/**
 * Calculate days until a due date from today
 */
function calculateDaysUntilDue(dueDate: Date): {
  daysUntilDue: number;
  isOverdue: boolean;
} {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(dueDate);
  due.setHours(0, 0, 0, 0);
  const diffMs = due.getTime() - today.getTime();
  const daysUntilDue = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  return { daysUntilDue, isOverdue: daysUntilDue < 0 };
}

/**
 * Map a Dataverse entity record to IEventRecord
 */
function mapRecordToEvent(record: Record<string, unknown>): IEventRecord {
  const dueDate = record.sprk_duedate ? new Date(record.sprk_duedate as string) : new Date();
  const { daysUntilDue, isOverdue } = calculateDaysUntilDue(dueDate);

  // Event type from formatted value annotation or FetchXML link-entity alias
  const eventTypeName =
    (record['_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue'] as string) ||
    (record['eventtype.sprk_name'] as string) ||
    'Event';

  // Event type color from FetchXML link-entity alias (if available)
  const eventTypeColor = (record['eventtype.sprk_eventtypecolor'] as string) || undefined;

  return {
    eventId: (record.sprk_eventid as string) || (record[`${getEntityPrimaryKey(record)}`] as string) || '',
    eventName: (record.sprk_eventname as string) || 'Untitled Event',
    eventTypeName,
    dueDate,
    daysUntilDue,
    isOverdue,
    eventTypeColor: eventTypeColor || undefined,
    description: record.sprk_description as string | undefined,
    assignedTo: (record['_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue'] as string) || undefined,
  };
}

/**
 * Attempt to find the primary key field from a record
 */
function getEntityPrimaryKey(record: Record<string, unknown>): string {
  // Look for common patterns: sprk_eventid, contactid, accountid, etc.
  for (const key of Object.keys(record)) {
    if (key.endsWith('id') && !key.startsWith('_') && !key.includes('@')) {
      return key;
    }
  }
  return 'id';
}

/**
 * Execute a view-based query and return mapped event records.
 *
 * Flow:
 * 1. Retrieve the saved view's FetchXML from Dataverse
 * 2. Inject context filter if provided
 * 3. Apply max items limit
 * 4. Execute the FetchXML query via WebAPI
 * 5. Map results to IEventRecord array
 *
 * @param webApi - WebAPI interface for Dataverse access
 * @param viewContext - View query parameters
 * @returns Array of mapped event records
 */
export async function fetchEventsFromView(
  webApi: IConfigWebApi,
  viewContext: IViewDataContext
): Promise<IEventRecord[]> {
  const { viewId, contextFilter, maxItems } = viewContext;

  logger.info('ViewDataService', 'Fetching events from view', {
    viewId,
    contextFilter,
    maxItems,
  });

  // Step 1: Get the view's FetchXML
  const { fetchXml: rawFetchXml, entityName } = await getViewFetchXml(webApi, viewId);

  // Step 1.5: Apply parameter substitution if params provided
  let fetchXml = viewContext.substitutionParams
    ? substituteParameters(rawFetchXml, viewContext.substitutionParams, viewContext.paramMappings)
    : rawFetchXml;

  // Step 2: Inject context filter if provided
  if (contextFilter?.fieldName && contextFilter?.recordId) {
    fetchXml = injectContextFilter(fetchXml, contextFilter.fieldName, contextFilter.recordId);
    logger.debug('ViewDataService', 'Injected context filter', {
      fieldName: contextFilter.fieldName,
    });
  }

  // Step 3: Apply max items limit
  if (maxItems && maxItems > 0) {
    fetchXml = applyMaxItems(fetchXml, maxItems);
  }

  // Step 4: Execute FetchXML query via WebAPI
  try {
    const encodedFetchXml = encodeURIComponent(fetchXml);
    const queryOptions = `?fetchXml=${encodedFetchXml}`;

    const result = await webApi.retrieveMultipleRecords(entityName, queryOptions);

    logger.info('ViewDataService', `View query returned ${result.entities.length} records`, {
      entityName,
      viewId,
    });

    // Step 5: Map to event records
    return result.entities.map(mapRecordToEvent);
  } catch (error) {
    const msg = error instanceof Error ? error.message : String(error);
    logger.error('ViewDataService', 'Failed to execute view query', error);
    throw new ViewDataError(`Failed to execute view query: ${msg}`, error);
  }
}

/**
 * Fetch events using a chart definition's view configuration.
 * Convenience wrapper that extracts view parameters from chart definition.
 *
 * @param webApi - WebAPI interface
 * @param chartDefinition - Chart definition containing view configuration
 * @param contextRecordId - Optional current record ID for context filtering
 * @returns Array of mapped event records
 */
export async function fetchEventsFromChartDefinition(
  webApi: IConfigWebApi,
  chartDefinition: IChartDefinition,
  contextRecordId?: string
): Promise<IEventRecord[]> {
  const viewId = chartDefinition.sprk_baseviewid;

  if (!viewId) {
    logger.warn('ViewDataService', 'No view ID configured, falling back to direct query');
    return [];
  }

  const viewContext: IViewDataContext = {
    viewId,
    maxItems: chartDefinition.sprk_maxdisplayitems || 10,
  };

  // Add context filter if configured
  if (chartDefinition.sprk_contextfieldname && contextRecordId) {
    viewContext.contextFilter = {
      fieldName: chartDefinition.sprk_contextfieldname,
      recordId: contextRecordId,
    };
  }

  return fetchEventsFromView(webApi, viewContext);
}
