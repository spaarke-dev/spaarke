/**
 * Xrm Context Utility
 *
 * Provides unified access to the host's Xrm object from PCF controls, Custom
 * Pages, code pages and embedded frames. See {@link getXrm} for the one frame
 * walk and its window-first order rule.
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
  /**
   * `navigationOptions` widened by task 081 (C-8) when `openEmailCompose.ts`
   * / `openEmailRecord.ts` converged onto this interface from the untyped
   * `services/xrmGlobal.ts` walker — both already called the real
   * `Xrm.Navigation.navigateTo(pageInput, navigationOptions)` two-argument
   * form (`target`/`position`/`width`/`height` to open as a centered modal
   * dialog), which this interface hadn't declared because nothing typed had
   * exercised it yet.
   */
  navigateTo(pageInput: PageInput, navigationOptions?: NavigateToOptions): Promise<void>;
}

/**
 * The real `Xrm.Navigation.navigateTo` second-argument shape (target window /
 * dialog sizing). See {@link XrmNavigation.navigateTo}.
 */
export interface NavigateToOptions {
  /** `1` = inline, `2` = modal dialog. */
  target?: number;
  /** `1` = center (only meaningful with `target: 2`). */
  position?: number;
  /** Pixels, or a percentage-of-viewport object (structurally matches `OobSizeDimension`). */
  width?: number | { value: number; unit: '%' };
  height?: number | { value: number; unit: '%' };
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
 * Options accepted by {@link XrmUtility.lookupObjects} — the OOB Dataverse
 * lookup picker dialog (`Xrm.Utility.lookupObjects`). Structurally typed to
 * the subset shared-lib consumers need; declared inline so this module does
 * not take a dependency on `@types/xrm` (mirrors {@link XrmPageLike} above).
 *
 * @see record-header-and-notepad-r2 FR-15 / FR-15a
 * @see CommunicationActionsApp.tsx:405-424 — the canonical call-shape precedent
 */
export interface LookupObjectsOptions {
  /** Target table logical name(s) the picker searches / allows selecting from. */
  entityTypes: string[];
  /** Which entity type's view is shown first when `entityTypes` has more than one. */
  defaultEntityType?: string;
  /** `false` restricts the picker to a single selection. */
  allowMultiSelect?: boolean;
}

/**
 * A single record selected via {@link XrmUtility.lookupObjects}. This shape
 * IS the Xrm lookup value the form buffer expects — `[{ id, name, entityType }]`
 * — so no translation layer sits between the picker result and
 * `Xrm.Page.getAttribute(n).setValue([...])` (see `useRecordHeaderFields.saveLookup`).
 */
export interface LookupObjectsResultItem {
  /** GUID of the selected record. May arrive brace-wrapped — callers normalize. */
  id: string;
  /** Display name (primary attribute) of the selected record. */
  name: string;
  /** Logical name of the entity the selected record belongs to. */
  entityType: string;
}

/**
 * Utility interface for global context and user settings
 */
export interface XrmUtility {
  getGlobalContext(): GlobalContext;
  showProgressIndicator?(message: string): void;
  closeProgressIndicator?(): void;
  /**
   * Opens the native Dataverse lookup picker dialog (Records / Recent /
   * Advanced / "+ New" per the target table's own Dataverse configuration).
   * Resolves with the selected record(s), or an empty array when the user
   * cancels — it never rejects on cancel. Optional because callers must
   * feature-detect (`typeof xrm.Utility?.lookupObjects === 'function'`)
   * before invoking, the same optionality precedent as
   * `showProgressIndicator` above.
   *
   * @see record-header-and-notepad-r2 FR-15 / FR-15a
   * @see CommunicationActionsApp.tsx:405-424 — the canonical call-shape precedent
   */
  lookupObjects?(options: LookupObjectsOptions): Promise<LookupObjectsResultItem[]>;
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
 * A capability the resolved Xrm must have, checked PER FRAME during the walk
 * (a frame whose Xrm lacks it is skipped, and the walk continues outward).
 *
 * - `'webApi'` (the default): `Xrm.WebApi` is present
 * - `'navigation'`: `Xrm.Navigation.navigateTo` is a function
 * - `'openForm'`: `Xrm.Navigation.openForm` is a function
 * - `'openUrl'`: `Xrm.Navigation.openUrl` is a function
 * - `'utility'`: `Xrm.Utility.getGlobalContext` is a function
 * - `'clientUrl'`: `Xrm.Utility.getGlobalContext().getClientUrl()` returns a non-empty string
 * - `'lookupObjects'`: `Xrm.Utility.lookupObjects` is a function
 * - `'metadata'`: `Xrm.Utility.getEntityMetadata` is a function
 * - `'pageContext'`: `Xrm.Utility.getPageContext` is a function
 * - `'sidePanes'`: `Xrm.App.sidePanes` is present
 * - `'page'`: `Xrm.Page` is present
 * - a predicate, for anything else
 *
 * Why per frame: a child frame can carry a PARTIAL Xrm (e.g. a web resource
 * that loaded `ClientGlobalContext.js.aspx`, or an embedding host's shim with
 * only `WebApi`), so "first frame with any Xrm" can return an object that
 * lacks what the caller needs while an outer frame has it.
 */
export type XrmCapability =
  | 'webApi'
  | 'navigation'
  | 'openForm'
  | 'openUrl'
  | 'utility'
  | 'clientUrl'
  | 'lookupObjects'
  | 'metadata'
  | 'pageContext'
  | 'sidePanes'
  | 'page'
  | ((xrm: any) => boolean); // eslint-disable-line @typescript-eslint/no-explicit-any

/**
 * What {@link getXrm} returns for a capability OTHER than the default
 * `'webApi'`: the frame's Xrm is only guaranteed to have what was asked for,
 * so `WebApi` is optional here (task 081 round 4, review F10). The default
 * form, `getXrm()` / `getXrm('webApi')`, returns {@link XrmContext} with a
 * non-optional `WebApi`.
 */
export type XrmPartialContext = Omit<XrmContext, 'WebApi'> & { WebApi?: XrmWebApi };

/** How many ancestors {@link getXrm} visits above `window` before trying `window.top`. */
export const XRM_MAX_FRAME_DEPTH = 10;

/* eslint-disable @typescript-eslint/no-explicit-any */
function hasCapability(xrm: any, capability: XrmCapability): boolean {
  if (typeof capability === 'function') return capability(xrm) === true;
  switch (capability) {
    case 'webApi':
      return !!xrm.WebApi;
    case 'navigation':
      return typeof xrm.Navigation?.navigateTo === 'function';
    case 'openForm':
      return typeof xrm.Navigation?.openForm === 'function';
    case 'openUrl':
      return typeof xrm.Navigation?.openUrl === 'function';
    case 'utility':
      return typeof xrm.Utility?.getGlobalContext === 'function';
    case 'clientUrl': {
      const url = xrm.Utility?.getGlobalContext?.()?.getClientUrl?.();
      return typeof url === 'string' && url.length > 0;
    }
    case 'lookupObjects':
      return typeof xrm.Utility?.lookupObjects === 'function';
    case 'metadata':
      return typeof xrm.Utility?.getEntityMetadata === 'function';
    case 'pageContext':
      return typeof xrm.Utility?.getPageContext === 'function';
    case 'sidePanes':
      return !!xrm.App?.sidePanes;
    case 'page':
      return !!xrm.Page;
    default:
      return false;
  }
}

/** The frame's Xrm if it has every required capability; undefined otherwise. Never throws. */
function usableXrm(frame: Window, required: readonly XrmCapability[]): XrmContext | undefined {
  try {
    const xrm = (frame as any).Xrm;
    if (xrm && required.every(c => hasCapability(xrm, c))) return xrm as XrmContext;
  } catch {
    // Cross-origin frame (SecurityError reading .Xrm) or a throwing capability probe.
  }
  return undefined;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * THE Xrm lookup (spaarke-ontology-platform-r1 task 081 / C-8). Every client
 * surface resolves the host's Xrm through this function; there is no second
 * frame walk.
 *
 * ## Order (the rule)
 * **window first**, then each ancestor in turn (`window.parent`,
 * `window.parent.parent`, ... at most {@link XRM_MAX_FRAME_DEPTH} levels), then
 * `window.top`. The NEAREST frame whose Xrm has the required capability wins.
 *   - A PCF control on a form runs in the main UCI document: `window.Xrm`.
 *   - A Custom Page / code page in a dialog iframe: `window.parent.Xrm`.
 *   - Side panes and Teams-style hosts nest deeper; every intermediate frame
 *     is tried (the former window -> parent -> top walk skipped them).
 * Window-first is correct because the nearest Xrm belongs to the context the
 * code runs in. The historical reason some sites read `parent` first ("the
 * child frame's Xrm may lack `Navigation.navigateTo`", VisualHost) is handled
 * by the capability check, not by frame order: pass `'navigation'` and a child
 * frame whose Xrm lacks it is skipped.
 *
 * Each frame is read in its own try/catch, so a cross-origin frame anywhere in
 * the chain is skipped instead of aborting the walk. NEVER throws.
 *
 * Child frames (embedded code pages, iframes) must call this rather than read
 * `parent.Xrm` / `window.Xrm` directly: no host writes a global `window.Xrm`
 * shim any more.
 *
 * Cheap and safe to call on every poll tick: it does no caching itself, so
 * callers that need fresh Xrm (e.g. a capture poller) should call getXrm()
 * again each time rather than holding a reference — per the task 001 spike
 * lesson, a cached Xrm reference can go stale across MDA navigations.
 *
 * @param required capability (or capabilities, all required) the Xrm must
 *   have; default `'webApi'`.
 * @returns the nearest Xrm with the capability, or undefined.
 *
 * @example
 * ```typescript
 * const xrm = getXrm();
 * if (xrm) {
 *   const result = await xrm.WebApi.retrieveMultipleRecords("account", "?$top=10");
 * }
 * const nav = getXrm('navigation')?.Navigation; // nearest frame that can navigate
 * ```
 */
export function getXrm(required?: 'webApi'): XrmContext | undefined;
/** A capability list that starts with `'webApi'` also guarantees `WebApi`. */
export function getXrm(required: readonly ['webApi', ...XrmCapability[]]): XrmContext | undefined;
export function getXrm(required: XrmCapability | readonly XrmCapability[]): XrmPartialContext | undefined;
export function getXrm(
  required: XrmCapability | readonly XrmCapability[] = 'webApi'
): XrmContext | XrmPartialContext | undefined {
  if (typeof window === 'undefined') return undefined;
  const capabilities: readonly XrmCapability[] = Array.isArray(required)
    ? (required as readonly XrmCapability[])
    : [required as XrmCapability];

  const visited: Window[] = [];
  let frame: Window = window;
  for (let depth = 0; depth <= XRM_MAX_FRAME_DEPTH; depth++) {
    visited.push(frame);
    const xrm = usableXrm(frame, capabilities);
    if (xrm) return xrm;

    let next: Window | null = null;
    try {
      next = frame.parent;
    } catch {
      next = null;
    }
    if (!next || next === frame) break;
    frame = next;
  }

  try {
    const top = window.top;
    if (top && !visited.includes(top)) {
      const xrm = usableXrm(top, capabilities);
      if (xrm) return xrm;
    }
  } catch {
    // window.top unavailable
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
 * `FieldMappingHandler.ts` and `MatterHeaderView.tsx`. Resolves through
 * {@link getXrm} with the `'page'` capability (task 081 / C-8), so it uses the
 * one frame walk: the nearest frame whose Xrm has `Page`. Before task 081 it
 * had its own window -> parent walk; the result is unchanged wherever that
 * walk found `Xrm.Page`, and it now also finds it on deeper ancestors / top.
 *
 * NEVER throws — returns `null` when `Xrm.Page` is not reachable.
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
  return getXrm('page')?.Page ?? null;
}

/**
 * The id of the record whose FORM hosts this code page / web resource, or
 * `undefined` when no frame exposes one.
 *
 * Read from the nearest frame (the shared {@link getXrm} walk) whose Xrm yields
 * an id: the legacy form buffer `Xrm.Page.data.entity.getId()` first, then
 * `Xrm.Utility.getPageContext().input.entityId`. Returned as the platform
 * gives it (may be brace-wrapped) — callers normalise with `cleanGuid`.
 *
 * One copy for the dataset code pages that are launched from a parent form
 * (`sprk_kpiassessmentspage`, `sprk_invoicespage`), which each hand-rolled the
 * same `parent -> top` walk (task 081 round 4, review F3). NEVER throws.
 *
 * Frame set: the shared walk — window FIRST, then every ancestor (up to
 * {@link XRM_MAX_FRAME_DEPTH}), then `top`. The replaced loops tried only
 * `[parent, top]`: they never read the page's own window and skipped
 * intermediate ancestors. In the dataset pages' normal host (a dialog iframe
 * whose own window has no Xrm) the result is the same.
 */
export function getHostFormRecordId(): string | undefined {
  /* eslint-disable @typescript-eslint/no-explicit-any */
  const read = (x: any): string | undefined => {
    try {
      const legacyId = x?.Page?.data?.entity?.getId?.();
      if (legacyId) return legacyId as string;
      const entityId = x?.Utility?.getPageContext?.()?.input?.entityId;
      return entityId ? (entityId as string) : undefined;
    } catch {
      return undefined;
    }
  };
  return read(getXrm((x: any) => !!read(x)));
  /* eslint-enable @typescript-eslint/no-explicit-any */
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
    const xrm = getXrm('utility');
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
    const xrm = getXrm('clientUrl');
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
    const xrm = getXrm('utility');
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
    const xrm = getXrm('utility');
    if (xrm?.Utility) {
      return xrm.Utility.getGlobalContext().userSettings.userName;
    }
  } catch {
    // Unable to get user name
  }
  return undefined;
}
