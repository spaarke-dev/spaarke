/**
 * DataverseService — typed wrapper around ComponentFramework.WebApi (Xrm.WebApi).
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
import { IMatter, IEvent, IProject, IDocument } from '../types/entities';
import { TodoStatus, EventFilterCategory } from '../types/enums';
import {
  buildMattersQuery,
  buildEventsFeedQuery,
  buildProjectsQuery,
  buildDocumentsByMatterQuery,
  buildDocumentsByUserQuery,
  buildTodoItemsQuery,
  buildDismissedTodoQuery,
} from './queryHelpers';

// ---------------------------------------------------------------------------
// Option set integer constants (Dataverse choice field values)
// These match the Dataverse option set values for sprk_todostatus.
// ---------------------------------------------------------------------------

const TODO_STATUS_VALUES: Record<TodoStatus, number> = {
  Open: 0,
  Completed: 1,
  Dismissed: 2,
};

// ---------------------------------------------------------------------------
// Xrm.WebApi result shape helpers
// ---------------------------------------------------------------------------

/**
 * The Xrm.WebApi retrieveMultipleRecords result is typed as
 * ComponentFramework.WebApi.RetrieveMultipleResponse which has an `entities`
 * property containing the raw record objects.
 */
type WebApiRecord = ComponentFramework.WebApi.Entity;

/** Cast raw WebApi entities to a typed array. Uses unknown for safe coercion. */
function toTypedArray<T>(entities: WebApiRecord[]): T[] {
  return entities as unknown as T[];
}

// ---------------------------------------------------------------------------
// DataverseService class
// ---------------------------------------------------------------------------

export class DataverseService {
  /**
   * Construct with the PCF framework's WebApi interface.
   * Obtain via: context.webAPI (ComponentFramework.Context<IInputs>.webAPI)
   */
  constructor(private readonly _webApi: ComponentFramework.WebApi) {}

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
      return toTypedArray<IMatter>(result.entities);
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
      return toTypedArray<IEvent>(result.entities);
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
      return toTypedArray<IProject>(result.entities);
    }, 'PROJECTS_FETCH_ERROR');
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
      return toTypedArray<IDocument>(result.entities);
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
      return toTypedArray<IDocument>(result.entities);
    }, 'DOCUMENTS_FETCH_ERROR');
  }

  // -------------------------------------------------------------------------
  // To-Do CRUD operations
  // -------------------------------------------------------------------------

  /**
   * Retrieve active (non-dismissed) to-do items for the Smart To Do list.
   *
   * Returns sprk_event records where todoflag=true and todostatus != Dismissed.
   * Sort: priorityscore desc, duedate asc.
   *
   * @param userId - The GUID of the current user
   * @returns      IResult<IEvent[]>
   */
  async getActiveTodos(userId: string): Promise<IResult<IEvent[]>> {
    const query = buildTodoItemsQuery(userId);
    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_event', query);
      return toTypedArray<IEvent>(result.entities);
    }, 'TODOS_FETCH_ERROR');
  }

  /**
   * Retrieve dismissed to-do items (collapsible dismissed section).
   *
   * Returns sprk_event records where todoflag=true and todostatus=Dismissed.
   *
   * @param userId - The GUID of the current user
   * @returns      IResult<IEvent[]>
   */
  async getDismissedTodos(userId: string): Promise<IResult<IEvent[]>> {
    const query = buildDismissedTodoQuery(userId);
    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_event', query);
      return toTypedArray<IEvent>(result.entities);
    }, 'DISMISSED_TODOS_FETCH_ERROR');
  }

  /**
   * Create a new manual to-do item as a sprk_event record.
   *
   * Sets:
   *   sprk_todoflag    = true
   *   sprk_todosource  = 'User'   (manually created by the user)
   *   sprk_todostatus  = 0 (Open)
   *   _ownerid_value   = userId   (assign to the current user)
   *
   * @param title  - The subject / title for the to-do item
   * @param userId - The GUID of the current user (will be set as owner)
   * @returns      IResult<string> — the new sprk_eventid GUID on success
   */
  async createTodo(title: string, userId: string): Promise<IResult<string>> {
    if (!title || title.trim().length === 0) {
      return fail('VALIDATION_ERROR', 'To-do title cannot be empty.');
    }

    const record: ComponentFramework.WebApi.Entity = {
      sprk_subject: title.trim(),
      sprk_todoflag: true,
      sprk_todosource: 'User',
      sprk_todostatus: TODO_STATUS_VALUES.Open,
      // Assign owner — Xrm.WebApi uses a special bind syntax for lookups:
      // "systemuserid@odata.bind": "/systemusers({userId})"
      'ownerid@odata.bind': `/systemusers(${userId})`,
    };

    return tryCatch(async () => {
      const result = await this._webApi.createRecord('sprk_event', record);
      return result.id;
    }, 'TODO_CREATE_ERROR');
  }

  /**
   * Update the todostatus on an existing sprk_event to-do item.
   *
   * Used for:
   *   - Checkbox toggle: Open → Completed
   *   - Restore dismissed: Dismissed → Open
   *
   * @param eventId - The sprk_eventid GUID to update
   * @param status  - The new TodoStatus value ('Open', 'Completed', 'Dismissed')
   * @returns       IResult<void>
   */
  async updateTodoStatus(eventId: string, status: TodoStatus): Promise<IResult<void>> {
    const record: ComponentFramework.WebApi.Entity = {
      sprk_todostatus: TODO_STATUS_VALUES[status],
    };

    return tryCatch(async () => {
      await this._webApi.updateRecord('sprk_event', eventId, record);
    }, 'TODO_STATUS_UPDATE_ERROR');
  }

  /**
   * Toggle the todoflag on a sprk_event record.
   *
   * Used for:
   *   - Feed item flag toggle: adds or removes from To Do list
   *   - "Add to To Do" in AI Summary dialog footer
   *   - FeedTodoSyncContext.toggleFlag (cross-block state synchronisation)
   *
   * When flagging (flagged=true):
   *   - Sets sprk_todoflag = true
   *   - Sets sprk_todosource = 'User'  (user-initiated via flag button)
   *   - Leaves sprk_todostatus unchanged (Open is the default on new records)
   *
   * When unflagging (flagged=false):
   *   - Sets sprk_todoflag = false
   *   - Leaves todostatus as-is (does not auto-dismiss)
   *
   * @param eventId - The sprk_eventid GUID to update
   * @param flagged - true to flag (add to To Do), false to unflag
   * @returns       IResult<void>
   */
  async toggleTodoFlag(eventId: string, flagged: boolean): Promise<IResult<void>> {
    const record: ComponentFramework.WebApi.Entity = flagged
      ? {
          sprk_todoflag: true,
          sprk_todosource: 'User',
        }
      : {
          sprk_todoflag: false,
        };

    return tryCatch(async () => {
      await this._webApi.updateRecord('sprk_event', eventId, record);
    }, 'TODO_FLAG_TOGGLE_ERROR');
  }

  /**
   * Dismiss a to-do item by setting its status to 'Dismissed'.
   *
   * The item will move from the active to-do list to the collapsible
   * "dismissed" section. todoflag remains true so it can be recovered.
   *
   * @param eventId - The sprk_eventid GUID to dismiss
   * @returns       IResult<void>
   */
  async dismissTodo(eventId: string): Promise<IResult<void>> {
    return this.updateTodoStatus(eventId, 'Dismissed');
  }

  /**
   * Delete a to-do record entirely (hard delete).
   * Only used for user-created items where the user explicitly removes it.
   * System-generated and flagged items should use dismissTodo instead.
   *
   * @param eventId - The sprk_eventid GUID to delete
   * @returns       IResult<void>
   */
  async deleteTodo(eventId: string): Promise<IResult<void>> {
    return tryCatch(async () => {
      await this._webApi.deleteRecord('sprk_event', eventId);
    }, 'TODO_DELETE_ERROR');
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

    // Notification types: document events, invoice events, status-changes, AI analysis
    const typeFilter = [
      `sprk_type eq 'document'`,
      `sprk_type eq 'invoice'`,
      `sprk_type eq 'status-change'`,
      `sprk_type eq 'ai-analysis'`,
      `sprk_type eq 'financial-alert'`,
    ].join(' or ');

    const filter =
      `_ownerid_value eq ${userId}` +
      ` and modifiedon gt ${cutoffIso}` +
      ` and (${typeFilter})`;

    const query = `?$select=${[
      'sprk_eventid',
      'sprk_subject',
      'sprk_type',
      'sprk_todoflag',
      '_sprk_regarding_value',
      'modifiedon',
      'createdon',
    ].join(',')}&$filter=${filter}&$orderby=modifiedon desc&$top=${top}`;

    return tryCatch(async () => {
      const result = await this._webApi.retrieveMultipleRecords('sprk_event', query, top);
      return toTypedArray<IEvent>(result.entities);
    }, 'NOTIFICATIONS_FETCH_ERROR');
  }
}
