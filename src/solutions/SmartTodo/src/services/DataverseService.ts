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

import { IResult, ok, fail, tryCatch } from '../types/result';
import { IMatter, IEvent, ITodo, IProject, IDocument, IInvoice, IUserPreference } from '../types/entities';
import { TodoStatus, EventFilterCategory } from '../types/enums';
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
  buildTodoItemsQuery,
  buildDismissedTodoQuery,
} from './queryHelpers';

// ---------------------------------------------------------------------------
// statuscode + statecode constants for sprk_todo (per task 009 customization)
// ---------------------------------------------------------------------------

/**
 * sprk_todo.statuscode values:
 *   1          = Open       (statecode 0 / Active)
 *   659490001  = In Progress(statecode 0 / Active)
 *   2          = Completed  (statecode 1 / Inactive)
 *   659490002  = Dismissed  (statecode 1 / Inactive)
 *
 * Maps the legacy TodoStatus string discriminator onto sprk_todo lifecycle.
 * "Restored" is not a distinct status — restore = set statuscode back to Open.
 */
const TODO_STATUSCODE_VALUES: Record<TodoStatus, number> = {
  Open: 1,
  Completed: 2,
  Dismissed: 659490002,
};

/** Active statecode for sprk_todo (0 = Active). */
const STATECODE_ACTIVE = 0;
/** Inactive statecode for sprk_todo (1 = Inactive). */
const STATECODE_INACTIVE = 1;

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

/**
 * Map formatted-value annotations on `sprk_todo` records into typed display
 * properties (assignedToName, statuscodeName) so UI components can read a
 * single property without collision risk.
 */
function mapTodoFormattedValues(entities: WebApiRecord[]): WebApiRecord[] {
  return entities.map(e => ({
    ...e,
    assignedToName: (e[`_sprk_assignedto_value${FV}`] as string) ?? '',
    statuscodeName: (e[`statuscode${FV}`] as string) ?? '',
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

export class DataverseService {
  /**
   * Construct with the Xrm.WebApi interface (or compatible).
   * Obtain via: getWebApi() from xrmProvider.ts
   */
  constructor(private readonly _webApi: IWebApi) {}

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
    options: { top?: number } = {}
  ): Promise<IResult<IEvent[]>> {
    const top = options.top ?? 50;
    const query = buildEventsFeedQuery(userId, filter, top);

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
    options: { top?: number } = {}
  ): Promise<IResult<IDocument[]>> {
    const top = options.top ?? 50;
    const query = buildDocumentsTabQuery(userId, top);

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_document', query, top);
      return toTypedArray<IDocument>(mapDocumentTabFormattedValues(result.entities));
    }, 'DOCUMENTS_TAB_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // To-Do CRUD operations (sprk_todo — first-class entity per R3 FR-09 / FR-11)
  // -------------------------------------------------------------------------

  /**
   * Retrieve active (non-completed, non-dismissed) to-do items for the Smart
   * To Do list.
   *
   * Returns `sprk_todo` records where statecode = 0 (Active), statuscode in
   * (Open, In Progress), AND assignee = userId.
   *
   * R4 task 031 / FR-07 / OD-2 — "Assigned to Me" is the SOLE filter mode.
   * The legacy R3 `mode` parameter (My Tasks / AssignedToMe / All) is removed.
   *
   * Sort: priorityscore desc, duedate asc.
   *
   * @param userId - The GUID of the current user
   */
  async getActiveTodos(userId: string): Promise<IResult<ITodo[]>> {
    const query = buildTodoItemsQuery(userId);
    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_todo', query);
      return toTypedArray<ITodo>(mapTodoFormattedValues(result.entities));
    }, 'TODOS_FETCH_ERROR');
  }

  /**
   * Retrieve dismissed to-do items (collapsible dismissed section).
   *
   * Returns `sprk_todo` records where statuscode = 659490002 (Dismissed).
   *
   * @param userId - The GUID of the current user
   */
  async getDismissedTodos(userId: string): Promise<IResult<ITodo[]>> {
    const query = buildDismissedTodoQuery(userId);
    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_todo', query);
      return toTypedArray<ITodo>(mapTodoFormattedValues(result.entities));
    }, 'DISMISSED_TODOS_FETCH_ERROR');
  }

  /**
   * Create a new manual to-do as a `sprk_todo` record.
   *
   * Sets:
   *   sprk_name       = title
   *   statuscode      = 1 (Open)
   *   statecode       = 0 (Active)
   *   ownerid         = userId
   *
   * Per OS-1: no `sprk_todoflag` write. Per FR-09: the source field is no
   * longer modelled on the entity — provenance lives in audit logs instead.
   *
   * @param title  - The subject / title for the to-do item
   * @param userId - The GUID of the current user (will be set as owner)
   * @returns      IResult<string> — the new sprk_todoid GUID on success
   */
  async createTodo(title: string, userId: string): Promise<IResult<string>> {
    if (!title || title.trim().length === 0) {
      return fail('VALIDATION_ERROR', 'To-do title cannot be empty.');
    }

    const record: WebApiEntity = {
      sprk_name: title.trim(),
      statecode: STATECODE_ACTIVE,
      statuscode: TODO_STATUSCODE_VALUES.Open,
      // Assign owner via the standard OData bind for `ownerid`.
      'ownerid@odata.bind': `/systemusers(${userId})`,
    };

    return tryCatch(async () => {
      const result = await this._webApi.createRecord('sprk_todo', record);
      return result.id;
    }, 'TODO_CREATE_ERROR');
  }

  /**
   * Update the statuscode/statecode on an existing `sprk_todo` item.
   *
   * Status transitions (per task 009):
   *   - Open       → statecode 0 / statuscode 1
   *   - Completed  → statecode 1 / statuscode 2
   *   - Dismissed  → statecode 1 / statuscode 659490002
   *
   * @param todoId - The sprk_todoid GUID to update
   * @param status - The new TodoStatus value ('Open', 'Completed', 'Dismissed')
   */
  async updateTodoStatus(todoId: string, status: TodoStatus): Promise<IResult<void>> {
    const statuscode = TODO_STATUSCODE_VALUES[status];
    const statecode = status === 'Open' ? STATECODE_ACTIVE : STATECODE_INACTIVE;
    const record: WebApiEntity = { statecode, statuscode };

    return tryCatch(async () => {
      await this._webApi.updateRecord('sprk_todo', todoId, record);
    }, 'TODO_STATUS_UPDATE_ERROR');
  }

  /**
   * Dismiss a to-do item by setting its status to Dismissed.
   *
   * The item moves from the active list to the collapsible "dismissed" section
   * and can be restored later via `updateTodoStatus(id, 'Open')`.
   *
   * @param todoId - The sprk_todoid GUID to dismiss
   */
  async dismissTodo(todoId: string): Promise<IResult<void>> {
    return this.updateTodoStatus(todoId, 'Dismissed');
  }

  /**
   * Delete a to-do record entirely (hard delete).
   * Reserved for user-created items where the user explicitly removes it.
   * Prefer `dismissTodo` to preserve audit history.
   *
   * @param todoId - The sprk_todoid GUID to delete
   */
  async deleteTodo(todoId: string): Promise<IResult<void>> {
    return tryCatch(async () => {
      await this._webApi.deleteRecord('sprk_todo', todoId);
    }, 'TODO_DELETE_ERROR');
  }

  // -------------------------------------------------------------------------
  // Kanban & User Preference operations
  // -------------------------------------------------------------------------

  /**
   * Retrieve user preferences by type.
   * Queries sprk_userpreference filtered by user and preference type.
   *
   * @param userId - The GUID of the current user
   * @param preferenceType - The preference type choice value (e.g., 100000000 for TodoKanbanThresholds)
   * @returns IResult<IUserPreference[]>
   */
  async getUserPreferences(
    userId: string,
    preferenceType: number
  ): Promise<IResult<IUserPreference[]>> {
    const select = [
      'sprk_userpreferenceid',
      'sprk_preferencetype',
      'sprk_preferencevalue',
      '_sprk_user_value',
      'createdon',
      'modifiedon',
    ].join(',');

    const filter = `_sprk_user_value eq ${userId} and sprk_preferencetype eq ${preferenceType}`;
    const query = `?$select=${select}&$filter=${filter}&$top=1`;

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_userpreference', query, 1);
      return toTypedArray<IUserPreference>(result.entities);
    }, 'USER_PREFERENCES_FETCH_ERROR');
  }

  /**
   * Create or update a user preference record.
   * If a record already exists for this user + type, updates it; otherwise creates a new one.
   *
   * @param userId - The GUID of the current user
   * @param preferenceType - The preference type choice value
   * @param value - The JSON string to store in sprk_preferencevalue
   * @param existingId - Optional existing record ID (if known, avoids a query)
   * @returns IResult<string> — the preference record ID
   */
  async saveUserPreferences(
    userId: string,
    preferenceType: number,
    value: string,
    existingId?: string
  ): Promise<IResult<string>> {
    // If we already know the record ID, just update
    if (existingId) {
      return tryCatch(async () => {
        await this._webApi.updateRecord('sprk_userpreference', existingId, {
          sprk_preferencevalue: value,
        });
        return existingId;
      }, 'USER_PREFERENCES_SAVE_ERROR');
    }

    // Check if a record exists for this user + type
    const existing = await this.getUserPreferences(userId, preferenceType);
    if (existing.success && existing.data.length > 0) {
      const id = existing.data[0].sprk_userpreferenceid;
      return tryCatch(async () => {
        await this._webApi.updateRecord('sprk_userpreference', id, {
          sprk_preferencevalue: value,
        });
        return id;
      }, 'USER_PREFERENCES_SAVE_ERROR');
    }

    // Create new preference record
    return tryCatch(async () => {
      const record: WebApiEntity = {
        sprk_preferencetype: preferenceType,
        sprk_preferencevalue: value,
        'sprk_User@odata.bind': `/systemusers(${userId})`,
      };
      const result = await this._webApi.createRecord('sprk_userpreference', record);
      return result.id;
    }, 'USER_PREFERENCES_CREATE_ERROR');
  }

  /**
   * Update the Kanban column assignment for a to-do.
   *
   * @param todoId - The sprk_todoid GUID
   * @param column - sprk_todocolumn choice (100000000=Today, 100000001=Tomorrow, 100000002=Future)
   */
  async updateEventColumn(todoId: string, column: number): Promise<IResult<void>> {
    return tryCatch(async () => {
      await this._webApi.updateRecord('sprk_todo', todoId, {
        sprk_todocolumn: column,
      });
    }, 'TODO_COLUMN_UPDATE_ERROR');
  }

  /**
   * Update the pin/lock state for a to-do.
   *
   * @param todoId - The sprk_todoid GUID
   * @param pinned - true to pin (lock in current column), false to unpin
   */
  async updateEventPinned(todoId: string, pinned: boolean): Promise<IResult<void>> {
    return tryCatch(async () => {
      await this._webApi.updateRecord('sprk_todo', todoId, {
        sprk_todopinned: pinned,
      });
    }, 'TODO_PIN_UPDATE_ERROR');
  }

  /**
   * Batch update Kanban column assignments for multiple to-do items.
   * Used by the Recalculate action to reassign unpinned items.
   *
   * Executes sequential updateRecord calls (Xrm.WebApi has no $batch support).
   * Returns count of successes and failures.
   *
   * Param-name retained as `eventId` for backward compatibility with the hook
   * signature; the value is a `sprk_todoid`.
   */
  async batchUpdateEventColumns(
    updates: Array<{ eventId: string; column: number }>
  ): Promise<IResult<{ succeeded: number; failed: number }>> {
    let succeeded = 0;
    let failed = 0;

    for (const update of updates) {
      try {
        await this._webApi.updateRecord('sprk_todo', update.eventId, {
          sprk_todocolumn: update.column,
        });
        succeeded++;
      } catch {
        failed++;
      }
    }

    return ok({ succeeded, failed });
  }

  /**
   * Update the description of a to-do.
   *
   * @param todoId - The sprk_todoid GUID
   * @param description - The new description text
   */
  async updateEventDescription(todoId: string, description: string): Promise<IResult<void>> {
    return tryCatch(async () => {
      await this._webApi.updateRecord('sprk_todo', todoId, {
        sprk_description: description,
      });
    }, 'TODO_DESCRIPTION_UPDATE_ERROR');
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
    options: { daysBack?: number; top?: number } = {}
  ): Promise<IResult<IEvent[]>> {
    const daysBack = options.daysBack ?? 7;
    const top = options.top ?? 20;

    const cutoff = new Date();
    cutoff.setDate(cutoff.getDate() - daysBack);
    const cutoffIso = cutoff.toISOString();

    // sprk_eventtype is a lookup — cannot filter server-side by display name.
    // Fetch all recent events for the user and filter client-side by type.
    const filter =
      `_ownerid_value eq ${userId}` +
      ` and modifiedon gt ${cutoffIso}`;

    // Per R3 FR-29 / OS-1, `sprk_event.sprk_todoflag` is removed in Phase 1.
    // Notifications now project a calendar-only sprk_event shape.
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
