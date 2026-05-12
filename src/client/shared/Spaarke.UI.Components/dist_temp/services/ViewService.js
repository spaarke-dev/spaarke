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
import { FetchXmlService } from './FetchXmlService';
/**
 * Service for fetching saved views and custom grid configurations.
 * Provides a unified interface for view management across PCF and Custom Pages.
 */
export class ViewService {
    /**
     * Create a new ViewService instance
     * @param xrm - XrmContext providing WebApi access
     */
    constructor(xrm) {
        this.viewCache = new Map();
        this.cacheTTL = 5 * 60 * 1000; // 5 minutes
        this.xrm = xrm;
        this.fetchXmlService = new FetchXmlService(xrm);
    }
    /**
     * Get all views for an entity
     * @param entityLogicalName - Entity to get views for
     * @param options - View retrieval options
     * @returns Promise resolving to sorted array of view definitions
     */
    async getViews(entityLogicalName, options = {}) {
        const cacheKey = `${entityLogicalName}_${options.includeCustom}_${options.includePersonal}`;
        // Check cache
        const cached = this.viewCache.get(cacheKey);
        if (cached && Date.now() - cached.timestamp < this.cacheTTL) {
            return cached.views;
        }
        const views = [];
        // Fetch saved queries (system views)
        const savedQueries = await this.fetchSavedQueries(entityLogicalName);
        views.push(...savedQueries);
        // Fetch personal views if requested
        if (options.includePersonal) {
            const personalViews = await this.fetchUserQueries(entityLogicalName);
            views.push(...personalViews);
        }
        // Fetch custom configurations if requested
        if (options.includeCustom) {
            const customViews = await this.fetchCustomConfigurations(entityLogicalName);
            views.push(...customViews);
        }
        // Filter by view type if specified
        let filteredViews = views;
        if (options.viewTypes && options.viewTypes.length > 0) {
            filteredViews = views.filter(v => options.viewTypes.includes(v.viewType));
        }
        // Sort by sortOrder, then by name
        filteredViews.sort((a, b) => {
            const orderDiff = (a.sortOrder ?? 100) - (b.sortOrder ?? 100);
            if (orderDiff !== 0)
                return orderDiff;
            return a.name.localeCompare(b.name);
        });
        // Parse columns for each view
        for (const view of filteredViews) {
            if (view.layoutXml && !view.columns) {
                view.columns = this.fetchXmlService.parseLayoutXml(view.layoutXml);
            }
        }
        // Cache results
        this.viewCache.set(cacheKey, {
            views: filteredViews,
            timestamp: Date.now(),
        });
        return filteredViews;
    }
    /**
     * Get the default view for an entity
     * @param entityLogicalName - Entity to get default view for
     * @param options - View retrieval options
     * @returns Promise resolving to default view or undefined
     */
    async getDefaultView(entityLogicalName, options = {}) {
        const views = await this.getViews(entityLogicalName, options);
        // Find view marked as default
        const defaultView = views.find(v => v.isDefault);
        if (defaultView) {
            return defaultView;
        }
        // Return first view if no default
        return views[0];
    }
    /**
     * Get a specific view by ID
     * @param viewId - View ID (savedqueryid or sprk_gridconfigurationid)
     * @param entityLogicalName - Entity the view belongs to
     * @returns Promise resolving to view definition or undefined
     */
    async getViewById(viewId, entityLogicalName) {
        // Try to find in cache first
        const views = await this.getViews(entityLogicalName, {
            includeCustom: true,
        });
        const cached = views.find(v => v.id === viewId);
        if (cached) {
            return cached;
        }
        // Fetch directly from savedquery
        try {
            const record = await this.xrm.WebApi.retrieveRecord('savedquery', viewId, '?$select=savedqueryid,name,returnedtypecode,fetchxml,layoutxml,isdefault,querytype');
            return this.mapSavedQueryToViewDefinition(record);
        }
        catch {
            // View not found in savedquery, might be custom
            return undefined;
        }
    }
    /**
     * Clear the view cache
     * @param entityLogicalName - Optional entity to clear cache for (clears all if not specified)
     */
    clearCache(entityLogicalName) {
        if (entityLogicalName) {
            // Clear cache entries for specific entity
            for (const key of this.viewCache.keys()) {
                if (key.startsWith(entityLogicalName)) {
                    this.viewCache.delete(key);
                }
            }
        }
        else {
            this.viewCache.clear();
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // Private methods
    // ─────────────────────────────────────────────────────────────────────────────
    /**
     * Fetch saved queries (system views) for an entity
     */
    async fetchSavedQueries(entityLogicalName) {
        try {
            const filter = [
                `returnedtypecode eq '${entityLogicalName}'`,
                'statecode eq 0', // Active only
                'querytype eq 0', // Public views only (not quick find, etc.)
                'isquickfindquery eq false',
            ].join(' and ');
            const result = await this.xrm.WebApi.retrieveMultipleRecords('savedquery', `?$select=savedqueryid,name,returnedtypecode,fetchxml,layoutxml,isdefault,querytype&$filter=${filter}&$orderby=name`);
            return result.entities.map(record => this.mapSavedQueryToViewDefinition(record));
        }
        catch (error) {
            console.error('[ViewService] Failed to fetch saved queries:', error);
            return [];
        }
    }
    /**
     * Fetch user queries (personal views) for an entity
     */
    async fetchUserQueries(entityLogicalName) {
        try {
            const filter = [
                `returnedtypecode eq '${entityLogicalName}'`,
                'statecode eq 0', // Active only
                'querytype eq 0', // Main query type
            ].join(' and ');
            const result = await this.xrm.WebApi.retrieveMultipleRecords('userquery', `?$select=userqueryid,name,returnedtypecode,fetchxml,layoutxml&$filter=${filter}&$orderby=name`);
            return result.entities.map(record => ({
                id: record.userqueryid,
                name: record.name,
                entityLogicalName: record.returnedtypecode,
                fetchXml: record.fetchxml,
                layoutXml: record.layoutxml,
                isDefault: false,
                viewType: 'userquery',
                sortOrder: 200, // Personal views after system views
            }));
        }
        catch (error) {
            console.error('[ViewService] Failed to fetch user queries:', error);
            return [];
        }
    }
    /**
     * Fetch custom configurations from sprk_gridconfiguration
     */
    async fetchCustomConfigurations(entityLogicalName) {
        try {
            const filter = [
                `sprk_entitylogicalname eq '${entityLogicalName}'`,
                'statecode eq 0', // Active only
            ].join(' and ');
            const result = await this.xrm.WebApi.retrieveMultipleRecords('sprk_gridconfiguration', `?$select=sprk_gridconfigurationid,sprk_name,sprk_entitylogicalname,sprk_viewtype,sprk_savedviewid,sprk_fetchxml,sprk_layoutxml,sprk_configjson,sprk_isdefault,sprk_sortorder,sprk_iconname&$filter=${filter}&$orderby=sprk_sortorder`);
            return result.entities.map(record => this.mapConfigurationToViewDefinition(record));
        }
        catch (error) {
            // Entity might not exist - this is expected in some environments
            console.debug('[ViewService] sprk_gridconfiguration not available:', error);
            return [];
        }
    }
    /**
     * Map savedquery record to IViewDefinition
     */
    mapSavedQueryToViewDefinition(record) {
        return {
            id: record.savedqueryid,
            name: record.name,
            entityLogicalName: record.returnedtypecode,
            fetchXml: record.fetchxml,
            layoutXml: record.layoutxml,
            isDefault: record.isdefault,
            viewType: 'savedquery',
            sortOrder: record.isdefault ? 0 : 100,
        };
    }
    /**
     * Map sprk_gridconfiguration record to IViewDefinition
     */
    mapConfigurationToViewDefinition(record) {
        const viewType = record.sprk_viewtype;
        return {
            id: record.sprk_gridconfigurationid,
            name: record.sprk_name,
            entityLogicalName: record.sprk_entitylogicalname,
            fetchXml: record.sprk_fetchxml || '',
            layoutXml: record.sprk_layoutxml || '',
            isDefault: record.sprk_isdefault,
            viewType: 'custom',
            sortOrder: record.sprk_sortorder ?? 50,
            iconName: record.sprk_iconname,
            // Store reference to savedquery if this is a SavedView type
            ...(viewType === 1 && { savedViewId: record.sprk_savedviewid }),
        };
    }
}
//# sourceMappingURL=ViewService.js.map