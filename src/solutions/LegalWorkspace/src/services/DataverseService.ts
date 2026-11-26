/**
 * DataverseService — typed wrapper around Xrm.WebApi.
 *
 * This is the single data access layer for all client-side Dataverse operations
 * in the Legal Operations Workspace. All methods return IResult<T> — never throw.
 *
 * Usage (via useDataverseService hook):
 *   const service = useDataverseService(context);
 *   const result = await service.getMattersByUser(userId);
 *   if (result.success) { ... result.data ... } else { ... result.error ... }
 *
 * Constraints (per spec + ADR-006):
 *   - Simple entity queries use Xrm.WebApi (this service), NOT BFF endpoints
 *   - Complex aggregations (portfolio health, quick summary) go to BFF
 *   - AI features go to BFF (never called directly from client)
 */

import { createChildRecordViaBff } from '@spaarke/ui-components';
import { authenticatedFetch } from './authInit';
import { getBffBaseUrl } from '../config/runtimeConfig';
import { TodoDataverseService } from '@spaarke/smart-todo-components';
import { IResult, tryCatch } from '../types/result';
import { IMatter, IEvent, IProject, IDocument, IInvoice } from '../types/entities';
import { EventFilterCategory } from '../types/enums';
import type { IWebApi, WebApiEntity } from '../types/xrm';
import {
  buildMattersQuery,
  buildMattersTabQuery,
  buildEventsFeedQuery,
  buildProjectsQuery,
  buildProjectsTabQuery,
  buildInvoicesQuery,
  buildDocumentsByMatterQuery,
  buildDocumentsByUserQuery,
  buildDocumentsTabQuery,
  buildOwnerFilter,
} from './queryHelpers';

// ---------------------------------------------------------------------------
// Xrm.WebApi result shape helpers
// ---------------------------------------------------------------------------

/**
 * Alias for the entity record type from Xrm.WebApi queries.
 */
type WebApiRecord = WebApiEntity;

/** Cast raw WebApi entities to a typed array. Uses unknown for safe coercion. */
function toTypedArray<T>(entities: WebApiRecord[]): T[] {
  return entities as unknown as T[];
}

// ---------------------------------------------------------------------------
// Formatted value mapping helpers
//
// Xrm.WebApi returns lookup and choice formatted values as annotation
// properties (e.g. "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue").
// These mappers extract the display name and assign it to entity-specific
// type name properties (eventTypeName, matterTypeName, etc.) so UI
// components can read a typed display property without collision risk.
// ---------------------------------------------------------------------------

const FV = '@OData.Community.Display.V1.FormattedValue';

function mapEventFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    eventTypeName: (e[`_sprk_eventtype_ref_value${FV}`] as string) ?? '',
    regardingRecordTypeName: (e[`_sprk_regardingrecordtype_value${FV}`] as string) ?? '',
    assignedToName: (e[`_sprk_assignedto_value${FV}`] as string) ?? '',
  }));
}

function mapMatterFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    matterTypeName: (e[`_sprk_mattertype_value${FV}`] as string) ?? '',
    practiceAreaName: (e[`_sprk_practicearea_value${FV}`] as string) ?? '',
  }));
}

function mapProjectFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    projectTypeName: (e[`_sprk_projecttype_ref_value${FV}`] as string) ?? '',
    practiceAreaName: (e[`_sprk_practicearea_value${FV}`] as string) ?? '',
  }));
}

function mapDocumentFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    documentTypeName: (e[`sprk_documenttype${FV}`] as string) ?? '',
  }));
}

function mapMatterTabFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    matterTypeName: (e[`_sprk_mattertype_value${FV}`] as string) ?? '',
    practiceAreaName: (e[`_sprk_practicearea_value${FV}`] as string) ?? '',
    statuscodeName: (e[`statuscode${FV}`] as string) ?? '',
  }));
}

function mapProjectTabFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    projectTypeName: (e[`_sprk_projecttype_ref_value${FV}`] as string) ?? '',
    statuscodeName: (e[`statuscode${FV}`] as string) ?? '',
  }));
}

function mapInvoiceFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    statuscodeName: (e[`statuscode${FV}`] as string) ?? '',
  }));
}

function mapDocumentTabFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    documentTypeName: (e[`sprk_documenttype${FV}`] as string) ?? '',
    statuscodeName: (e[`statuscode${FV}`] as string) ?? '',
    checkedOutByName: (e[`_sprk_checkedoutby_value${FV}`] as string) ?? '',
  }));
}

// ---------------------------------------------------------------------------
// DataverseService class
// ---------------------------------------------------------------------------

/**
 * The to-do and user-preference methods (getActiveTodos, createTodo, updateTodoStatus, dismissTodo,
 * getUserPreferences, saveUserPreferences and the Kanban column/pin writes) come from the shared
 * `TodoDataverseService` (#2972) — the SmartTodo code page uses the same class. This class adds the
 * workspace's own queries (matters, projects, invoices, documents, events, notifications).
 */
export class DataverseService extends TodoDataverseService {
  /**
   * Construct with the Xrm.WebApi interface (or compatible).
   * Obtain via: getWebApi() from xrmProvider.ts
   */
  constructor(
    private readonly _webApi: IWebApi,
    /**
     * UAC-r2 task 147 r1 (owner round 28 item 1): how a to-do is CREATED — through the BFF (G5), which decides its owner.
     * Injectable for tests.
     */
    createTodoRecord: (payload: WebApiEntity) => Promise<string> = payload =>
      createChildRecordViaBff(authenticatedFetch, getBffBaseUrl(), 'sprk_todo', payload)
  ) {
    super(_webApi, createTodoRecord);
  }

  // -------------------------------------------------------------------------
  // Matter queries
  // -------------------------------------------------------------------------

  /**
   * Retrieve active matters owned by a user for the My Portfolio widget.
   *
   * OData: GET sprk_matters?$select=...&$filter=_ownerid_value eq {userId}&$orderby=sprk_name asc&$top={top}
   *
   * @param userId  - The GUID of the current user (context.userSettings.userId)
   * @param options - Optional overrides (top: default 5)
   * @returns       IResult<IMatter[]>
   */
  async getMattersByUser(
    userId: string,
    options: { top?: number } = {}
  ): Promise<IResult<IMatter[]>> {
    const top = options.top ?? 5;
    const query = buildMattersQuery(userId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_matter', query, top);
      return toTypedArray<IMatter>(mapMatterFormattedValues(result.entities));
    }, 'MATTERS_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // Event feed queries
  // -------------------------------------------------------------------------

  /**
   * Retrieve the updates feed (sprk_event records) for a user, with optional
   * category filtering.
   *
   * Sort: priorityscore desc, modifiedon desc (critical priority items first).
   *
   * Supported categories:
   *   All, HighPriority, Overdue, Alerts, Emails, Documents, Invoices, Tasks
   *
   * @param userId   - The GUID of the current user
   * @param filter   - Filter category (default: All)
   * @param options  - Optional overrides (top: default 50)
   * @returns        IResult<IEvent[]>
   */
  async getEventsFeed(
    userId: string,
    filter: EventFilterCategory = EventFilterCategory.All,
    options: { top?: number; scope?: 'my' | 'all'; businessUnitId?: string } = {}
  ): Promise<IResult<IEvent[]>> {
    const top = options.top ?? 50;
    const ctx = options.scope ? { userId, scope: options.scope, businessUnitId: options.businessUnitId } : undefined;
    const query = buildEventsFeedQuery(userId, filter, top, ctx);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_event', query, top);
      return toTypedArray<IEvent>(mapEventFormattedValues(result.entities));
    }, 'EVENTS_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // Project queries
  // -------------------------------------------------------------------------

  /**
   * Retrieve projects owned by a user for the My Portfolio widget (Projects tab).
   *
   * OData: GET sprk_projects?$select=...&$filter=_ownerid_value eq {userId}&$orderby=modifiedon desc&$top={top}
   *
   * @param userId   - The GUID of the current user
   * @param options  - Optional overrides (top: default 5)
   * @returns        IResult<IProject[]>
   */
  async getProjectsByUser(
    userId: string,
    options: { top?: number } = {}
  ): Promise<IResult<IProject[]>> {
    const top = options.top ?? 5;
    const query = buildProjectsQuery(userId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_project', query, top);
      return toTypedArray<IProject>(mapProjectFormattedValues(result.entities));
    }, 'PROJECTS_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // User contact resolution
  // -------------------------------------------------------------------------

  /**
   * Resolve the linked contact ID for a systemuser.
   *
   * Many lookup fields (sprk_assignedattorney1, sprk_assignedparalegal1) point
   * to contacts rather than systemusers. This method retrieves the
   * systemuser._contactid_value lookup to enable broad owner filtering.
   *
   * @param userId - The systemuser GUID
   * @returns IResult<string | null> — the contact GUID or null if none linked
   */
  async getUserContactId(userId: string): Promise<IResult<string | null>> {
    return tryCatch(async () => {
      const result = await this._webApi.retrieveRecord(
        'systemuser',
        userId,
        '?$select=_contactid_value'
      );
      return (result._contactid_value as string) ?? null;
    }, 'USER_CONTACT_RESOLVE_ERROR');
  }

  // -------------------------------------------------------------------------
  // Broad filter queries (Matters/Projects/Invoices tabs)
  // -------------------------------------------------------------------------

  /**
   * Retrieve matters matching the broad owner filter for the Matters tab.
   * Includes records where the user is owner, modifier, assigned attorney, or assigned paralegal.
   */
  async getMattersByBroadFilter(
    userId: string,
    contactId: string | null,
    options: { top?: number } = {}
  ): Promise<IResult<IMatter[]>> {
    const top = options.top ?? 50;
    const query = buildMattersTabQuery(userId, contactId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_matter', query, top);
      return toTypedArray<IMatter>(mapMatterTabFormattedValues(result.entities));
    }, 'MATTERS_TAB_FETCH_ERROR');
  }

  /**
   * Retrieve projects matching the broad owner filter for the Projects tab.
   * Includes records where the user is owner, modifier, assigned attorney, or assigned paralegal.
   */
  async getProjectsByBroadFilter(
    userId: string,
    contactId: string | null,
    options: { top?: number } = {}
  ): Promise<IResult<IProject[]>> {
    const top = options.top ?? 50;
    const query = buildProjectsTabQuery(userId, contactId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_project', query, top);
      return toTypedArray<IProject>(mapProjectTabFormattedValues(result.entities));
    }, 'PROJECTS_TAB_FETCH_ERROR');
  }

  /**
   * Retrieve invoices matching the broad owner filter for the Invoices tab.
   * Includes records where the user is owner, modifier, assigned attorney, or assigned paralegal.
   */
  async getInvoicesByBroadFilter(
    userId: string,
    contactId: string | null,
    options: { top?: number } = {}
  ): Promise<IResult<IInvoice[]>> {
    const top = options.top ?? 50;
    const query = buildInvoicesQuery(userId, contactId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_invoice', query, top);
      return toTypedArray<IInvoice>(mapInvoiceFormattedValues(result.entities));
    }, 'INVOICES_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // Document queries
  // -------------------------------------------------------------------------

  /**
   * Retrieve documents associated with a specific matter (My Portfolio — Documents tab
   * when a matter context is available).
   *
   * OData: GET sprk_documents?$select=...&$filter=_sprk_matter_value eq {matterId}&$orderby=modifiedon desc&$top={top}
   *
   * @param matterId - The GUID of the matter to filter by
   * @param options  - Optional overrides (top: default 5)
   * @returns        IResult<IDocument[]>
   */
  async getDocumentsByMatter(
    matterId: string,
    options: { top?: number } = {}
  ): Promise<IResult<IDocument[]>> {
    const top = options.top ?? 5;
    const query = buildDocumentsByMatterQuery(matterId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_document', query, top);
      return toTypedArray<IDocument>(mapDocumentFormattedValues(result.entities));
    }, 'DOCUMENTS_BY_MATTER_FETCH_ERROR');
  }

  /**
   * Retrieve recent documents owned by a user for the My Portfolio widget (Documents tab).
   *
   * OData: GET sprk_documents?$select=...&$filter=_ownerid_value eq {userId}&$orderby=modifiedon desc&$top={top}
   *
   * @param userId  - The GUID of the current user
   * @param options - Optional overrides (top: default 5)
   * @returns       IResult<IDocument[]>
   */
  async getDocumentsByUser(
    userId: string,
    options: { top?: number } = {}
  ): Promise<IResult<IDocument[]>> {
    const top = options.top ?? 5;
    const query = buildDocumentsByUserQuery(userId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_document', query, top);
      return toTypedArray<IDocument>(mapDocumentFormattedValues(result.entities));
    }, 'DOCUMENTS_FETCH_ERROR');
  }

  /**
   * Retrieve documents for the Documents tab using the broad filter.
   *
   * Returns documents where the user is owner, recent creator, recent modifier,
   * workspace-flagged, or checked out by the user.
   */
  async getDocumentsForTab(
    userId: string,
    options: { top?: number; scope?: 'my' | 'all'; businessUnitId?: string } = {}
  ): Promise<IResult<IDocument[]>> {
    const top = options.top ?? 50;
    const ctx = options.scope ? { userId, scope: options.scope, businessUnitId: options.businessUnitId } : undefined;
    const query = buildDocumentsTabQuery(userId, top, ctx);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_document', query, top);
      return toTypedArray<IDocument>(mapDocumentTabFormattedValues(result.entities));
    }, 'DOCUMENTS_TAB_FETCH_ERROR');
  }

  /**
   * Retrieve documents using a specific saved view's filter and order.
   *
   * Xrm.WebApi.retrieveMultipleRecords does NOT support the `savedQuery` OData
   * parameter. Instead we fetch the view's FetchXML and execute it directly.
   * Works for both system views (savedquery) and personal views (userquery).
   */
  async getDocumentsForView(
    viewId: string,
    options: { top?: number; viewType?: string } = {}
  ): Promise<IResult<IDocument[]>> {
    const top = options.top ?? 50;

    return tryCatch(async () => {
      // 1. Retrieve the view's FetchXML
      const entity = options.viewType === 'userquery' ? 'userquery' : 'savedquery';
      const viewRecord = await this._webApi.retrieveRecord(entity, viewId, '?$select=fetchxml');
      let fetchXml = viewRecord.fetchxml as string;
      if (!fetchXml) throw new Error(`View ${viewId} has no fetchxml`);

      // 2. Inject top limit into the fetchxml
      fetchXml = fetchXml.replace(/<fetch\b/, `<fetch top="${top}"`);

      // 3. Execute via FetchXML query (supported by Xrm.WebApi)
      const result = await this._webApi.retrieveMultipleRecords(
        'sprk_document',
        `?fetchXml=${encodeURIComponent(fetchXml)}`,
        top
      );
      return toTypedArray<IDocument>(mapDocumentTabFormattedValues(result.entities));
    }, 'DOCUMENTS_VIEW_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // Generic record count
  // -------------------------------------------------------------------------

  /**
   * Get the count of records matching a filter.
   * Uses a minimal $select + $filter query and counts result entities.
   *
   * @param entityName - The logical name of the entity (e.g. "sprk_matter")
   * @param primaryKey - The primary key field to select (e.g. "sprk_matterid")
   * @param filter     - OData $filter expression
   * @param maxCount   - Maximum records to retrieve for counting (default 500)
   * @returns          IResult<number> — the count of matching records
   */
  async getRecordCount(
    entityName: string,
    primaryKey: string,
    filter: string,
    maxCount: number = 500
  ): Promise<IResult<number>> {
    const query = `?$select=${primaryKey}&$filter=${filter}&$top=${maxCount}`;

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords(entityName, query, maxCount);
      return result.entities.length;
    }, `${entityName.toUpperCase()}_COUNT_ERROR`);
  }

  // -------------------------------------------------------------------------
  // Notification / unread event queries
  // -------------------------------------------------------------------------

  /**
   * Retrieve recent notification-worthy events for Block 7 (Notification Panel).
   *
   * Notifications derive from sprk_event records where the event represents
   * a document, invoice, status change, or AI analysis result.
   * Filtered to the last N days and ordered by modifiedon desc.
   *
   * @param userId     - The GUID of the current user
   * @param daysBack   - How many days back to look (default 7)
   * @param top        - Max records to return (default 20)
   * @returns          IResult<IEvent[]>
   */
  async getNotifications(
    userId: string,
    options: { daysBack?: number; top?: number; scope?: 'my' | 'all'; businessUnitId?: string } = {}
  ): Promise<IResult<IEvent[]>> {
    const daysBack = options.daysBack ?? 7;
    const top = options.top ?? 20;

    const cutoff = new Date();
    cutoff.setDate(cutoff.getDate() - daysBack);
    const cutoffIso = cutoff.toISOString();

    const ctx = options.scope ? { userId, scope: options.scope, businessUnitId: options.businessUnitId } : undefined;
    const ownerClause = ctx ? buildOwnerFilter(ctx) : `_ownerid_value eq ${userId}`;
    const filter =
      ownerClause +
      ` and modifiedon gt ${cutoffIso}`;

    const query = `?$select=${[
      'sprk_eventid',
      'sprk_eventname',
      '_sprk_eventtype_ref_value',
      'sprk_regardingrecordid',
      'sprk_regardingrecordname',
      'modifiedon',
      'createdon',
    ].join(',')}&$filter=${filter}&$orderby=modifiedon desc&$top=${top}`;

    // Notification-worthy event types (matched against lookup display name)
    const notificationTypes = new Set([
      'filing', 'status change', 'notification', 'approval',
    ]);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_event', query, top);
      const mapped = mapEventFormattedValues(result.entities);
      // Client-side filter by event type display name
      const filtered = mapped.filter(e => {
        const typeName = ((e as Record<string, unknown>).eventTypeName as string ?? '').toLowerCase();
        return notificationTypes.has(typeName);
      });
      return toTypedArray<IEvent>(filtered);
    }, 'NOTIFICATIONS_FETCH_ERROR');
  }
}
