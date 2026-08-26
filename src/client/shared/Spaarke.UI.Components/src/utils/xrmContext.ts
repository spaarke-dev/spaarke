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
 * Minimal structural typing for the subset of a bound `Xrm.Page` attribute
 * that shared-lib consumers need at runtime — staging a form-buffer edit
 * (`setValue`) and, where the caller needs it, reading the current value or
 * dirty state. `getValue`/`getIsDirty` are optional because not every
 * consumer of {@link getXrmPage} needs them (declared narrower where they
 * do — see `FieldMappingHandler.ts`'s local `IXrmPageAttributeLike`).
 */
export interface XrmPageAttributeLike {
  setValue(value: unknown): void;
  getValue?(): unknown;
  getIsDirty?(): boolean;
}

/**
 * Minimal structural typing for the subset of `Xrm.Page` (the deprecated
 * but still-functional form-buffer API) that shared-lib consumers need.
 * Declared inline so this module does not take a dependency on `@types/xrm`.
 */
export interface XrmPageLike {
  getAttribute(name: string): XrmPageAttributeLike | null | undefined;
}

/**
 * Minimal XrmContext interface for type safety.
 * Subset of Xrm SDK types needed by shared components.
 */
export interface XrmContext {
  WebApi: XrmWebApi;
  Navigation?: XrmNavigation;
  Utility?: XrmUtility;
  App?: XrmApp;
  Page?: XrmPageLike;
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

  retrieveRecord(entityLogicalName: string, id: string, options?: string): Promise<Record<string, any>>;

  createRecord(entityLogicalName: string, data: Record<string, any>): Promise<EntityReference>;

  updateRecord(entityLogicalName: string, id: string, data: Record<string, any>): Promise<EntityReference>;

  deleteRecord(entityLogicalName: string, id: string): Promise<EntityReference>;
}

/**
 * Result from retrieveMultipleRecords
 */
export interface RetrieveMultipleResult {
  entities: Record<string, any>[];
  '@odata.nextLink'?: string;
  '@Microsoft.Dynamics.CRM.totalrecordcount'?: number;
  '@Microsoft.Dynamics.CRM.totalrecordcountlimitexceeded'?: boolean;
  '@Microsoft.Dynamics.CRM.fetchxmlpagingcookie'?: string;
  '@Microsoft.Dynamics.CRM.morerecords'?: boolean;
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
  pageType: 'entityrecord' | 'entitylist' | 'webresource' | 'custom';
  entityName?: string;
  entityId?: string;
  /**
   * Web resource logical name for `pageType: 'webresource'` pane navigation.
   * Matches the real `Xrm.App.sidePanes` pane.navigate() contract (widened
   * 2026-08-13, task 010, per the `pageType:'webresource'` usage already in
   * DataGridSidePaneOrchestrator / CalendarSidePane).
   */
  webresourceName?: string;
  /**
   * Saved view id for `pageType: 'entitylist'` navigation — matches the real
   * `Xrm.Navigation.navigateTo` `PageInputEntityList.viewId` contract
   * (widened 2026-08-13, task 060, spaarke-side-pane-navigation-history-r1,
   * for the Navigator Views tab's click-to-open-with-view-selected flow).
   */
  viewId?: string;
  /**
   * Saved view TYPE for `pageType: 'entitylist'` navigation — matches the
   * real `Xrm.Navigation.navigateTo` `PageInputEntityList.viewType` contract
   * (widened 2026-08-14, spaarke-side-pane-navigation-history-r1 UAT bug
   * fix). Without this, `navigateTo` falls back to the entity's DEFAULT view
   * even when `viewId` correctly identifies a personal (`userquery`) view —
   * Dataverse needs `viewType` to disambiguate a `userquery` id (`'4230'`)
   * from a system `savedquery` id (`'1039'`) sharing the same GUID space.
   * The Navigator Views tab (`ViewsTab.tsx`) always passes `'4230'` since it
   * only ever lists `userquery` views (see `ViewService.getAllUserQueries()`).
   */
  viewType?: string;
  data?: Record<string, any> | string;
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
  /**
   * Look up a previously-created pane by id. Returns `undefined` when no pane
   * with that id exists yet — used as the idempotency check before calling
   * `createPane` again (see DataGridSidePaneOrchestrator.registerPane).
   */
  getPane(paneId: string): SidePane | undefined;
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
  /** Select (focus/expand) this pane in the pane launcher. */
  select(): void;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Get the Xrm object from the appropriate context.
 *
 * - PCF controls have Xrm on window.Xrm or via context.webAPI
 * - Custom Pages run in a single iframe, so Xrm is on window.parent.Xrm
 * - Side-pane hosts (task 010, spaarke-side-pane-navigation-history-r1) run
 *   the code page nested one level deeper inside UCI's pane iframe, so Xrm
 *   may only be reachable on window.top.Xrm
 * - Returns undefined if Xrm is not available on any of the three frames
 *   (graceful degradation) — this function NEVER throws.
 *
 * Cheap and safe to call on every poll tick: it does no caching itself, so
 * callers that need fresh Xrm (e.g. a capture poller) should call getXrm()
 * again each time rather than holding a reference — per the task 001 spike
 * lesson, a cached Xrm reference can go stale across MDA navigations.
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
  // SDK boundary: Xrm is injected at runtime by the host (PCF / Custom Page).
  // Walk window -> parent -> top and return the first frame with a usable Xrm.
  // Try window.Xrm first (PCF controls or direct script access)
  try {
    const windowXrm = (window as unknown as { Xrm?: XrmContext }).Xrm;
    if (windowXrm?.WebApi) {
      return windowXrm;
    }
  } catch {
    // window.Xrm not available
  }

  // Try parent.Xrm for Custom Pages running in a single iframe
  try {
    if (typeof window !== 'undefined' && window.parent && window.parent !== window) {
      const parentXrm = (window.parent as unknown as { Xrm?: XrmContext }).Xrm;
      if (parentXrm?.WebApi) {
        return parentXrm;
      }
    }
  } catch {
    // Cross-origin access denied - expected in some environments
  }

  // Try top.Xrm for hosts nested deeper than one iframe (e.g. side panes)
  try {
    if (typeof window !== 'undefined' && window.top && window.top !== window) {
      const topXrm = (window.top as unknown as { Xrm?: XrmContext }).Xrm;
      if (topXrm?.WebApi) {
        return topXrm;
      }
    }
  } catch {
    // Cross-origin access denied - expected in some environments
  }

  return undefined;
}

/**
 * Get `Xrm.Page` — the deprecated-but-functional form-buffer API used to
 * stage field edits (`getAttribute(name).setValue(value)`) without an
 * immediate `Xrm.WebApi.updateRecord` round trip, avoiding a PCF re-render
 * flash (see `.claude/patterns/pcf/pcf-build-scaffold.md` gotcha #10).
 *
 * THE single shared accessor (FR-20) — consolidates the two near-identical
 * private `getXrmPage()` duplicates that previously lived in
 * `FieldMappingHandler.ts` and `MatterHeaderView.tsx`. Deliberately walks
 * only window -> parent (NOT the third `top` frame {@link getXrm} also
 * checks) — this mirrors exactly what both former duplicates did, per the
 * "no behavior change at either call site beyond swapping the accessor"
 * constraint (FR-20 task notes). Widen to a 3-frame walk in a follow-up if a
 * side-pane host ever needs `Xrm.Page` from `window.top`.
 *
 * NEVER throws — returns `null` when `Xrm.Page` is not reachable on either
 * frame (Xrm not yet injected, or a cross-origin SecurityError accessing
 * `window.parent`).
 *
 * @returns `Xrm.Page` (structurally typed as {@link XrmPageLike}), or `null`
 *
 * @example
 * ```typescript
 * const attr = getXrmPage()?.getAttribute('sprk_mattername');
 * attr?.setValue('New Name'); // stages in the form buffer
 * ```
 */
export function getXrmPage(): XrmPageLike | null {
  // Try window.Xrm.Page first (PCF controls or direct script access)
  try {
    const windowXrm = (window as unknown as { Xrm?: XrmContext }).Xrm;
    if (windowXrm?.Page) {
      return windowXrm.Page;
    }
  } catch {
    // window.Xrm not available
  }

  // Try parent.Xrm.Page for Custom Pages running in a single iframe
  try {
    if (typeof window !== 'undefined' && window.parent && window.parent !== window) {
      const parentXrm = (window.parent as unknown as { Xrm?: XrmContext }).Xrm;
      if (parentXrm?.Page) {
        return parentXrm.Page;
      }
    }
  } catch {
    // Cross-origin access denied - expected in some environments
  }

  return null;
}

/**
 * Check if we're running in a Custom Page (iframe) context
 *
 * @returns true if in Custom Page iframe
 */
export function isCustomPageContext(): boolean {
  try {
    return typeof window !== 'undefined' && window.parent !== undefined && window.parent !== window;
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
    // SDK boundary: Xrm runtime
    const xrm = (window as unknown as { Xrm?: { WebApi?: unknown } }).Xrm;
    return typeof xrm !== 'undefined' && xrm?.WebApi !== undefined;
  } catch {
    return false;
  }
}

/**
 * Detect the current theme from the host environment.
 * Uses Xrm.Utility.getGlobalContext().userSettings when available.
 *
 * OS `prefers-color-scheme` is intentionally NOT consulted — ADR-021 requires
 * the Spaarke theme system (not the OS) to control all UI surfaces.
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
  source: 'xrm' | 'default';
} {
  // Try Xrm global context first
  try {
    const xrm = getXrm();
    if (xrm?.Utility) {
      const globalContext = xrm.Utility.getGlobalContext();
      if (globalContext?.userSettings?.isDarkTheme !== undefined) {
        return {
          isDarkTheme: globalContext.userSettings.isDarkTheme,
          source: 'xrm',
        };
      }
    }
  } catch {
    // Xrm context not available or error accessing
  }

  // Default: light theme (OS prefers-color-scheme is intentionally NOT consulted)
  return {
    isDarkTheme: false,
    source: 'default',
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

/**
 * Get the current user's display name from Xrm context.
 *
 * Companion to {@link getCurrentUserId} — together they are THE current-user
 * identity mechanism shared client-wide (spaarkeai-assistant-enhancements-r1
 * task 014 / FR-A4). Consumers needing to resolve the current user onto a
 * different entity's assignee field (e.g. a `contact`-targeted lookup) build
 * on top of this identity, rather than introducing a second mechanism.
 *
 * @returns User display name (`userSettings.userName`) or undefined
 */
export function getCurrentUserName(): string | undefined {
  try {
    const xrm = getXrm();
    if (xrm?.Utility) {
      return xrm.Utility.getGlobalContext().userSettings.userName;
    }
  } catch {
    // Unable to get user name
  }
  return undefined;
}
