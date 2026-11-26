/**
 * TodoDataverseService — the Smart To Do list's reads and writes, one copy for every host (#2972).
 *
 * Before #2972 the LegalWorkspace and SmartTodo code pages each carried a whole-file copy of `DataverseService`
 * with these methods, and the copies had drifted. LegalWorkspace's `DataverseService` now extends this class and
 * keeps only its own methods (matters, projects, invoices, documents, events); the SmartTodo code page uses this
 * class directly.
 *
 * Host-specific pieces are injected, not imported (ADR-012 — no `src/solutions/...` reach-in):
 *   - the Xrm.WebApi-shaped client (`ITodoWebApi`);
 *   - `createTodoRecord`: how a to-do is created. Both hosts create through the BFF (G5,
 *     `POST /api/v1/child-records/sprk_todo`, UAC-r2 task 147 r1) so the server decides the owner; they differ only in
 *     how they reach the BFF (LegalWorkspace initialises auth at start-up, the code page lazily on first create).
 *
 * Methods never throw; they return `ITodoServiceResult<T>`.
 */

import type { ITodo, IUserPreferenceRecord } from '../types/entities';
import type { IKanbanDataverseService } from '../types/kanban';
import { buildActiveTodosQuery, type TodoListScope } from './todoQueries';
import { todoFail, todoOk, todoTryCatch, type ITodoServiceResult } from './todoServiceResult';

/** The Xrm.WebApi members this service uses. `Xrm.WebApi` satisfies it. */
export interface ITodoWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ): Promise<{ entities: Array<Record<string, unknown>>; nextLink?: string }>;
  createRecord(entityLogicalName: string, data: Record<string, unknown>): Promise<{ id: string }>;
  updateRecord(entityLogicalName: string, id: string, data: Record<string, unknown>): Promise<unknown>;
}

/** Creates a `sprk_todo` from `payload` and resolves with its id; rejects with the server's message. */
export type CreateTodoRecord = (payload: Record<string, unknown>) => Promise<string>;

/** The to-do lifecycle states a list can set. "Restored" is not a status — restore sets Open. */
export type TodoStatus = 'Open' | 'Completed' | 'Dismissed';

/** sprk_todo.statuscode for each status (task 009): Open 1 / Completed 2 / Dismissed 659490002. */
const TODO_STATUSCODE: Record<TodoStatus, number> = {
  Open: 1,
  Completed: 2,
  Dismissed: 659490002,
};
const STATECODE_ACTIVE = 0;
const STATECODE_INACTIVE = 1;

const FV = '@OData.Community.Display.V1.FormattedValue';

/** Copies the formatted-value annotations the UI reads onto plain properties. */
function mapTodoFormattedValues(entities: Array<Record<string, unknown>>): ITodo[] {
  return entities.map(e => ({
    ...e,
    assignedToName: (e[`_sprk_assignedto_value${FV}`] as string) ?? '',
    statuscodeName: (e[`statuscode${FV}`] as string) ?? '',
    // Locale short date, for the search box's date match (UAT 2026-08-17).
    dueDateFormatted: (e[`sprk_duedate${FV}`] as string) ?? '',
  })) as unknown as ITodo[];
}

export class TodoDataverseService implements IKanbanDataverseService {
  constructor(
    private readonly todoWebApi: ITodoWebApi,
    private readonly createTodoRecord: CreateTodoRecord
  ) {}

  // ---------------------------------------------------------------------------
  // To-dos
  // ---------------------------------------------------------------------------

  /** Active (Open / In Progress) to-dos in `scope`, sorted by priority score desc, due date asc. */
  async getActiveTodos(scope: TodoListScope): Promise<ITodoServiceResult<ITodo[]>> {
    const query = buildActiveTodosQuery(scope);
    return todoTryCatch(async () => {
      const result = await this.todoWebApi.retrieveMultipleRecords('sprk_todo', query);
      return mapTodoFormattedValues(result.entities);
    }, 'TODOS_FETCH_ERROR');
  }

  /**
   * Creates an Open to-do titled `title` and resolves with its id. Sets no owner: the BFF decides it (UAC-r2 task 147
   * r1 — the client never sets the owner).
   */
  async createTodo(title: string): Promise<ITodoServiceResult<string>> {
    if (!title || title.trim().length === 0) {
      return todoFail('VALIDATION_ERROR', 'To-do title cannot be empty.');
    }
    const record = {
      sprk_name: title.trim(),
      statecode: STATECODE_ACTIVE,
      statuscode: TODO_STATUSCODE.Open,
    };
    return todoTryCatch(() => this.createTodoRecord(record), 'TODO_CREATE_ERROR');
  }

  /** Sets a to-do's status (statecode follows: Open → Active, otherwise Inactive). */
  async updateTodoStatus(todoId: string, status: TodoStatus): Promise<ITodoServiceResult<void>> {
    const record = {
      statecode: status === 'Open' ? STATECODE_ACTIVE : STATECODE_INACTIVE,
      statuscode: TODO_STATUSCODE[status],
    };
    return todoTryCatch(async () => {
      await this.todoWebApi.updateRecord('sprk_todo', todoId, record);
    }, 'TODO_STATUS_UPDATE_ERROR');
  }

  /** Dismisses a to-do; `updateTodoStatus(id, 'Open')` restores it. */
  async dismissTodo(todoId: string): Promise<ITodoServiceResult<void>> {
    return this.updateTodoStatus(todoId, 'Dismissed');
  }

  // ---------------------------------------------------------------------------
  // Kanban persistence (IKanbanDataverseService). The `Event` names are kept for the
  // `useKanbanColumns` contract; the ids are sprk_todoid values.
  // ---------------------------------------------------------------------------

  /** Sets a to-do's Kanban column (sprk_todocolumn: 100000000 Today / 100000001 Tomorrow / 100000002 Future). */
  async updateEventColumn(todoId: string, column: number): Promise<ITodoServiceResult<void>> {
    return todoTryCatch(async () => {
      await this.todoWebApi.updateRecord('sprk_todo', todoId, { sprk_todocolumn: column });
    }, 'TODO_COLUMN_UPDATE_ERROR');
  }

  /** Pins (locks in its column) or unpins a to-do. */
  async updateEventPinned(todoId: string, pinned: boolean): Promise<ITodoServiceResult<void>> {
    return todoTryCatch(async () => {
      await this.todoWebApi.updateRecord('sprk_todo', todoId, { sprk_todopinned: pinned });
    }, 'TODO_PIN_UPDATE_ERROR');
  }

  /** Sets many to-dos' columns one by one (Xrm.WebApi has no $batch) and counts successes and failures. */
  async batchUpdateEventColumns(
    updates: Array<{ eventId: string; column: number }>
  ): Promise<ITodoServiceResult<{ succeeded: number; failed: number }>> {
    let succeeded = 0;
    let failed = 0;
    for (const update of updates) {
      try {
        await this.todoWebApi.updateRecord('sprk_todo', update.eventId, { sprk_todocolumn: update.column });
        succeeded++;
      } catch {
        failed++;
      }
    }
    return todoOk({ succeeded, failed });
  }

  // ---------------------------------------------------------------------------
  // User preferences (sprk_userpreference — one record per user and preference type)
  // ---------------------------------------------------------------------------

  /** The user's preference records of `preferenceType` (at most one). */
  async getUserPreferences(
    userId: string,
    preferenceType: number
  ): Promise<ITodoServiceResult<IUserPreferenceRecord[]>> {
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
    return todoTryCatch(async () => {
      const result = await this.todoWebApi.retrieveMultipleRecords('sprk_userpreference', query, 1);
      return result.entities as unknown as IUserPreferenceRecord[];
    }, 'USER_PREFERENCES_FETCH_ERROR');
  }

  /**
   * Writes `value` to the user's preference record of `preferenceType`, creating it when none exists, and resolves
   * with the record id. Pass `existingId` when known to skip the lookup.
   */
  async saveUserPreferences(
    userId: string,
    preferenceType: number,
    value: string,
    existingId?: string
  ): Promise<ITodoServiceResult<string>> {
    let id = existingId;
    if (!id) {
      const existing = await this.getUserPreferences(userId, preferenceType);
      if (existing.success && existing.data.length > 0) {
        id = existing.data[0].sprk_userpreferenceid;
      }
    }
    if (id) {
      const recordId = id;
      return todoTryCatch(async () => {
        await this.todoWebApi.updateRecord('sprk_userpreference', recordId, { sprk_preferencevalue: value });
        return recordId;
      }, 'USER_PREFERENCES_SAVE_ERROR');
    }
    return todoTryCatch(async () => {
      const result = await this.todoWebApi.createRecord('sprk_userpreference', {
        sprk_preferencetype: preferenceType,
        sprk_preferencevalue: value,
        'sprk_User@odata.bind': `/systemusers(${userId})`,
      });
      return result.id;
    }, 'USER_PREFERENCES_CREATE_ERROR');
  }
}
