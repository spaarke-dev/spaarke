/**
 * Xrm Context Utility
 *
 * Provides unified access to Xrm object from PCF controls and Custom Pages.
 * PCF controls have Xrm on window, Custom Pages (iframe) need parent.Xrm.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-022 PCF Platform Libraries
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Minimal XrmContext interface for type safety.
 * Subset of Xrm SDK types needed by shared components.
 */
export interface XrmContext {
  WebApi: XrmWebApi;
  Navigation?: XrmNavigation;
  Utility?: XrmUtility;
  App?: XrmApp;
}

/**
 * WebApi interface for data operations
 */
export interface XrmWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ): Promise<RetrieveMultipleResult>;

  retrieveRecord(
    entityLogicalName: string,
    id: string,
    options?: string
  ): Promise<Record<string, any>>;

  createRecord(
    entityLogicalName: string,
    data: Record<string, any>
  ): Promise<EntityReference>;

  updateRecord(
    entityLogicalName: string,
    id: string,
    data: Record<string, any>
  ): Promise<EntityReference>;

  deleteRecord(entityLogicalName: string, id: string): Promise<EntityReference>;
}

/**
 * Result from retrieveMultipleRecords
 */
export interface RetrieveMultipleResult {
  entities: Record<string, any>[];
  "@odata.nextLink"?: string;
  "@Microsoft.Dynamics.CRM.totalrecordcount"?: number;
  "@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded"?: boolean;
  "@Microsoft.Dynamics.CRM.fetchxmlpagingcookie"?: string;
  "@Microsoft.Dynamics.CRM.morerecords"?: boolean;
}

/**
 * Entity reference returned from create/update/delete
 */
export interface EntityReference {
  id: string;
  entityType: string;
}

/**
 * Navigation interface for opening forms, dialogs, etc.
 */
export interface XrmNavigation {
  openForm(options: OpenFormOptions): Promise<OpenFormResult>;
  openUrl(url: string, options?: WindowOptions): void;
  navigateTo(pageInput: PageInput): Promise<void>;
}

export interface OpenFormOptions {
  entityName: string;
  entityId?: string;
  formId?: string;
  openInNewWindow?: boolean;
  windowPosition?: number;
  relationship?: FormRelationship;
}

export interface FormRelationship {
  name: string;
  attributeName: string;
  relationshipType: number;
}

export interface OpenFormResult {
  savedEntityReference?: EntityReference[];
}

export interface WindowOptions {
  height?: number;
  width?: number;
}

export interface PageInput {
  pageType: "entityrecord" | "entitylist" | "webresource" | "custom";
  entityName?: string;
  entityId?: string;
  data?: Record<string, any>;
  name?: string;
}

/**
 * Utility interface for global context and user settings
 */
export interface XrmUtility {
  getGlobalContext(): GlobalContext;
  showProgressIndicator?(message: string): void;
  closeProgressIndicator?(): void;
}

export interface GlobalContext {
  userSettings: UserSettings;
  organizationSettings?: OrganizationSettings;
  getClientUrl(): string;
  getCurrentAppUrl(): string;
  getVersion(): string;
}

export interface UserSettings {
  userId: string;
  userName: string;
  languageId: number;
  dateFormattingInfo?: DateFormattingInfo;
  isDarkTheme?: boolean; // Only available in some contexts
}

export interface OrganizationSettings {
  uniqueName: string;
  baseCurrencyId: string;
  languageId: number;
}

export interface DateFormattingInfo {
  datePattern: string;
  timePattern: string;
  dateSeparator: string;
  timeSeparator: string;
}

/**
 * App interface for side panes
 */
export interface XrmApp {
  sidePanes: SidePanesApi;
}

export interface SidePanesApi {
  createPane(options: CreatePaneOptions): Promise<SidePane>;
  getSelectedPane(): SidePane | undefined;
  getAllPanes(): SidePane[];
}

export interface CreatePaneOptions {
  paneId: string;
  title?: string;
  canClose?: boolean;
  imageSrc?: string;
  hideHeader?: boolean;
  isSelected?: boolean;
  width?: number;
  alwaysRender?: boolean;
  keepBadgeOnSelect?: boolean;
}

export interface SidePane {
  paneId: string;
  title?: string;
  navigate(pageInput: PageInput): Promise<void>;
  close(): void;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Get the Xrm object from the appropriate context.
 *
 * - PCF controls have Xrm on window.Xrm or via context.webAPI
 * - Custom Pages run in an iframe, so Xrm is on window.parent.Xrm
 * - Returns undefined if Xrm is not available (graceful degradation)
 *
 * @returns XrmContext or undefined if not available
 *
 * @example
 * ```typescript
 * const xrm = getXrm();
 * if (xrm) {
 *   const result = await xrm.WebApi.retrieveMultipleRecords("account", "?$top=10");
 * }
 * ```
 */
export function getXrm(): XrmContext | undefined {
  // Try window.Xrm first (PCF controls or direct script access)
  try {
    const windowXrm = (window as any).Xrm;
    if (windowXrm?.WebApi) {
      return windowXrm as XrmContext;
    }
  } catch {
    // window.Xrm not available
  }

  // Try parent.Xrm for Custom Pages running in iframe
  try {
    if (typeof window !== "undefined" && window.parent && window.parent !== window) {
      const parentXrm = (window.parent as any).Xrm;
      if (parentXrm?.WebApi) {
        return parentXrm as XrmContext;
      }
    }
  } catch {
    // Cross-origin access denied - expected in some environments
  }

  return undefined;
}

/**
 * Check if we're running in a Custom Page (iframe) context
 *
 * @returns true if in Custom Page iframe
 */
export function isCustomPageContext(): boolean {
  try {
    return typeof window !== "undefined" &&
           window.parent !== undefined &&
           window.parent !== window;
  } catch {
    return false;
  }
}

/**
 * Check if we're running in a PCF control context
 *
 * @returns true if in PCF context (has window.Xrm directly)
 */
export function isPcfContext(): boolean {
  try {
    return typeof (window as any).Xrm !== "undefined" &&
           (window as any).Xrm?.WebApi !== undefined;
  } catch {
    return false;
  }
}

/**
 * Detect the current theme from the host environment.
 * Uses Xrm.Utility.getGlobalContext().userSettings when available.
 *
 * @returns Object with isDarkTheme boolean and source of detection
 *
 * @example
 * ```typescript
 * const theme = detectThemeFromHost();
 * if (theme.isDarkTheme) {
 *   // Apply dark theme styles
 * }
 * ```
 */
export function detectThemeFromHost(): {
  isDarkTheme: boolean;
  source: "xrm" | "media-query" | "default";
} {
  // Try Xrm global context first
  try {
    const xrm = getXrm();
    if (xrm?.Utility) {
      const globalContext = xrm.Utility.getGlobalContext();
      if (globalContext?.userSettings?.isDarkTheme !== undefined) {
        return {
          isDarkTheme: globalContext.userSettings.isDarkTheme,
          source: "xrm",
        };
      }
    }
  } catch {
    // Xrm context not available or error accessing
  }

  // Fall back to prefers-color-scheme media query
  try {
    if (typeof window !== "undefined" && window.matchMedia) {
      const darkModeQuery = window.matchMedia("(prefers-color-scheme: dark)");
      return {
        isDarkTheme: darkModeQuery.matches,
        source: "media-query",
      };
    }
  } catch {
    // matchMedia not available
  }

  // Default to light theme
  return {
    isDarkTheme: false,
    source: "default",
  };
}

/**
 * Get the organization's base URL from Xrm context
 *
 * @returns Base URL string or undefined
 */
export function getClientUrl(): string | undefined {
  try {
    const xrm = getXrm();
    if (xrm?.Utility) {
      return xrm.Utility.getGlobalContext().getClientUrl();
    }
  } catch {
    // Unable to get client URL
  }
  return undefined;
}

/**
 * Get the current user's ID from Xrm context
 *
 * @returns User ID string (GUID without braces) or undefined
 */
export function getCurrentUserId(): string | undefined {
  try {
    const xrm = getXrm();
    if (xrm?.Utility) {
      return xrm.Utility.getGlobalContext().userSettings.userId;
    }
  } catch {
    // Unable to get user ID
  }
  return undefined;
}
