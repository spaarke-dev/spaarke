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

import type { XrmContext } from "../utils/xrmContext";
import type { IViewDefinition, ViewType } from "../types/FetchXmlTypes";
import { FetchXmlService } from "./FetchXmlService";

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
 * Savedquery record from Dataverse
 */
interface ISavedQueryRecord {
  savedqueryid: string;
  name: string;
  returnedtypecode: string;
  fetchxml: string;
  layoutxml: string;
  isdefault: boolean;
  querytype: number;
  isquickfindquery: boolean;
  statecode: number;
}

/**
 * Service for fetching saved views and custom grid configurations.
 * Provides a unified interface for view management across PCF and Custom Pages.
 */
export class ViewService {
  private xrm: XrmContext;
  private fetchXmlService: FetchXmlService;
  private viewCache: Map<string, { views: IViewDefinition[]; timestamp: number }> = new Map();
  private cacheTTL: number = 5 * 60 * 1000; // 5 minutes

  /**
   * Create a new ViewService instance
   * @param xrm - XrmContext providing WebApi access
   */
  constructor(xrm: XrmContext) {
    this.xrm = xrm;
    this.fetchXmlService = new FetchXmlService(xrm);
  }

  /**
   * Get all views for an entity
   * @param entityLogicalName - Entity to get views for
   * @param options - View retrieval options
   * @returns Promise resolving to sorted array of view definitions
   */
  async getViews(
    entityLogicalName: string,
    options: IGetViewsOptions = {}
  ): Promise<IViewDefinition[]> {
    const cacheKey = `${entityLogicalName}_${options.includeCustom}_${options.includePersonal}`;

    // Check cache
    const cached = this.viewCache.get(cacheKey);
    if (cached && Date.now() - cached.timestamp < this.cacheTTL) {
      return cached.views;
    }

    const views: IViewDefinition[] = [];

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
      filteredViews = views.filter((v) => options.viewTypes!.includes(v.viewType));
    }

    // Sort by sortOrder, then by name
    filteredViews.sort((a, b) => {
      const orderDiff = (a.sortOrder ?? 100) - (b.sortOrder ?? 100);
      if (orderDiff !== 0) return orderDiff;
      return a.name.localeCompare(b.name);
    });

    // Parse columns for each view
    for (const view of filteredViews) {
      if (view.layoutXml && !view.columns) {
        view.columns = this.fetchXmlService.parseLayoutXml(view.layoutXml);
      }
    }

    // Cache results
    this.viewCache.set(cacheKey, { views: filteredViews, timestamp: Date.now() });

    return filteredViews;
  }

  /**
   * Get the default view for an entity
   * @param entityLogicalName - Entity to get default view for
   * @param options - View retrieval options
   * @returns Promise resolving to default view or undefined
   */
  async getDefaultView(
    entityLogicalName: string,
    options: IGetViewsOptions = {}
  ): Promise<IViewDefinition | undefined> {
    const views = await this.getViews(entityLogicalName, options);

    // Find view marked as default
    const defaultView = views.find((v) => v.isDefault);
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
  async getViewById(
    viewId: string,
    entityLogicalName: string
  ): Promise<IViewDefinition | undefined> {
    // Try to find in cache first
    const views = await this.getViews(entityLogicalName, { includeCustom: true });
    const cached = views.find((v) => v.id === viewId);
    if (cached) {
      return cached;
    }

    // Fetch directly from savedquery
    try {
      const record = await this.xrm.WebApi.retrieveRecord(
        "savedquery",
        viewId,
        "?$select=savedqueryid,name,returnedtypecode,fetchxml,layoutxml,isdefault,querytype"
      );

      return this.mapSavedQueryToViewDefinition(record as ISavedQueryRecord);
    } catch {
      // View not found in savedquery, might be custom
      return undefined;
    }
  }

  /**
   * Clear the view cache
   * @param entityLogicalName - Optional entity to clear cache for (clears all if not specified)
   */
  clearCache(entityLogicalName?: string): void {
    if (entityLogicalName) {
      // Clear cache entries for specific entity
      for (const key of this.viewCache.keys()) {
        if (key.startsWith(entityLogicalName)) {
          this.viewCache.delete(key);
        }
      }
    } else {
      this.viewCache.clear();
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Private methods
  // ─────────────────────────────────────────────────────────────────────────────

  /**
   * Fetch saved queries (system views) for an entity
   */
  private async fetchSavedQueries(entityLogicalName: string): Promise<IViewDefinition[]> {
    try {
      const filter = [
        `returnedtypecode eq '${entityLogicalName}'`,
        "statecode eq 0", // Active only
        "querytype eq 0", // Public views only (not quick find, etc.)
        "isquickfindquery eq false",
      ].join(" and ");

      const result = await this.xrm.WebApi.retrieveMultipleRecords(
        "savedquery",
        `?$select=savedqueryid,name,returnedtypecode,fetchxml,layoutxml,isdefault,querytype&$filter=${filter}&$orderby=name`
      );

      return (result.entities as ISavedQueryRecord[]).map((record) =>
        this.mapSavedQueryToViewDefinition(record)
      );
    } catch (error) {
      console.error("[ViewService] Failed to fetch saved queries:", error);
      return [];
    }
  }

  /**
   * Fetch user queries (personal views) for an entity
   */
  private async fetchUserQueries(entityLogicalName: string): Promise<IViewDefinition[]> {
    try {
      const filter = [
        `returnedtypecode eq '${entityLogicalName}'`,
        "statecode eq 0", // Active only
        "querytype eq 0", // Main query type
      ].join(" and ");

      const result = await this.xrm.WebApi.retrieveMultipleRecords(
        "userquery",
        `?$select=userqueryid,name,returnedtypecode,fetchxml,layoutxml&$filter=${filter}&$orderby=name`
      );

      return result.entities.map((record) => ({
        id: record.userqueryid as string,
        name: record.name as string,
        entityLogicalName: record.returnedtypecode as string,
        fetchXml: record.fetchxml as string,
        layoutXml: record.layoutxml as string,
        isDefault: false,
        viewType: "userquery" as ViewType,
        sortOrder: 200, // Personal views after system views
      }));
    } catch (error) {
      console.error("[ViewService] Failed to fetch user queries:", error);
      return [];
    }
  }

  /**
   * Fetch custom configurations from sprk_gridconfiguration
   */
  private async fetchCustomConfigurations(entityLogicalName: string): Promise<IViewDefinition[]> {
    try {
      const filter = [
        `sprk_entitylogicalname eq '${entityLogicalName}'`,
        "statecode eq 0", // Active only
      ].join(" and ");

      const result = await this.xrm.WebApi.retrieveMultipleRecords(
        "sprk_gridconfiguration",
        `?$select=sprk_gridconfigurationid,sprk_name,sprk_entitylogicalname,sprk_viewtype,sprk_savedviewid,sprk_fetchxml,sprk_layoutxml,sprk_configjson,sprk_isdefault,sprk_sortorder,sprk_iconname&$filter=${filter}&$orderby=sprk_sortorder`
      );

      return result.entities.map((record) => this.mapConfigurationToViewDefinition(record));
    } catch (error) {
      // Entity might not exist - this is expected in some environments
      console.debug("[ViewService] sprk_gridconfiguration not available:", error);
      return [];
    }
  }

  /**
   * Map savedquery record to IViewDefinition
   */
  private mapSavedQueryToViewDefinition(record: ISavedQueryRecord): IViewDefinition {
    return {
      id: record.savedqueryid,
      name: record.name,
      entityLogicalName: record.returnedtypecode,
      fetchXml: record.fetchxml,
      layoutXml: record.layoutxml,
      isDefault: record.isdefault,
      viewType: "savedquery",
      sortOrder: record.isdefault ? 0 : 100,
    };
  }

  /**
   * Map sprk_gridconfiguration record to IViewDefinition
   */
  private mapConfigurationToViewDefinition(record: Record<string, unknown>): IViewDefinition {
    const viewType = record.sprk_viewtype as number;

    return {
      id: record.sprk_gridconfigurationid as string,
      name: record.sprk_name as string,
      entityLogicalName: record.sprk_entitylogicalname as string,
      fetchXml: (record.sprk_fetchxml as string) || "",
      layoutXml: (record.sprk_layoutxml as string) || "",
      isDefault: record.sprk_isdefault as boolean,
      viewType: "custom",
      sortOrder: (record.sprk_sortorder as number) ?? 50,
      iconName: record.sprk_iconname as string | undefined,
      // Store reference to savedquery if this is a SavedView type
      ...(viewType === 1 && { savedViewId: record.sprk_savedviewid as string }),
    };
  }
}

// Re-export types for convenience
export type { IViewDefinition, ViewType } from "../types/FetchXmlTypes";
