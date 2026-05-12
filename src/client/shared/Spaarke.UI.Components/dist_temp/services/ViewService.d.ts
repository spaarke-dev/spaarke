/**
 * ViewService
 *
 * Fetches saved views from Dataverse savedquery entity and merges with
 * custom configurations from sprk_gridconfiguration table.
 * Framework-agnostic: receives XrmContext as constructor argument.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-012
 */
import type { XrmContext } from '../utils/xrmContext';
import type { IViewDefinition, ViewType } from '../types/FetchXmlTypes';
/**
 * Options for getViews method
 */
export interface IGetViewsOptions {
    /** Include custom views from sprk_gridconfiguration (default: false) */
    includeCustom?: boolean;
    /** Include personal views from userquery (default: false) */
    includePersonal?: boolean;
    /** Filter by view type */
    viewTypes?: ViewType[];
}
/**
 * Service for fetching saved views and custom grid configurations.
 * Provides a unified interface for view management across PCF and Custom Pages.
 */
export declare class ViewService {
    private xrm;
    private fetchXmlService;
    private viewCache;
    private cacheTTL;
    /**
     * Create a new ViewService instance
     * @param xrm - XrmContext providing WebApi access
     */
    constructor(xrm: XrmContext);
    /**
     * Get all views for an entity
     * @param entityLogicalName - Entity to get views for
     * @param options - View retrieval options
     * @returns Promise resolving to sorted array of view definitions
     */
    getViews(entityLogicalName: string, options?: IGetViewsOptions): Promise<IViewDefinition[]>;
    /**
     * Get the default view for an entity
     * @param entityLogicalName - Entity to get default view for
     * @param options - View retrieval options
     * @returns Promise resolving to default view or undefined
     */
    getDefaultView(entityLogicalName: string, options?: IGetViewsOptions): Promise<IViewDefinition | undefined>;
    /**
     * Get a specific view by ID
     * @param viewId - View ID (savedqueryid or sprk_gridconfigurationid)
     * @param entityLogicalName - Entity the view belongs to
     * @returns Promise resolving to view definition or undefined
     */
    getViewById(viewId: string, entityLogicalName: string): Promise<IViewDefinition | undefined>;
    /**
     * Clear the view cache
     * @param entityLogicalName - Optional entity to clear cache for (clears all if not specified)
     */
    clearCache(entityLogicalName?: string): void;
    /**
     * Fetch saved queries (system views) for an entity
     */
    private fetchSavedQueries;
    /**
     * Fetch user queries (personal views) for an entity
     */
    private fetchUserQueries;
    /**
     * Fetch custom configurations from sprk_gridconfiguration
     */
    private fetchCustomConfigurations;
    /**
     * Map savedquery record to IViewDefinition
     */
    private mapSavedQueryToViewDefinition;
    /**
     * Map sprk_gridconfiguration record to IViewDefinition
     */
    private mapConfigurationToViewDefinition;
}
export type { IViewDefinition, ViewType } from '../types/FetchXmlTypes';
//# sourceMappingURL=ViewService.d.ts.map