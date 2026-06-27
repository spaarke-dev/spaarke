/**
 * TodoDetail types — Shared type definitions for the Todo Detail component.
 *
 * Single-entity model: all data comes from `sprk_todo` (per smart-todo-decoupling-r3 FR-09).
 * The legacy two-entity (`sprk_event` + `sprk_eventtodo`) shape was removed in R3 Phase 2
 * (per OS-1: no compat shims; pre-release tech-debt removal).
 *
 * Context-agnostic — no Xrm, no PCF APIs (ADR-012).
 */

// ---------------------------------------------------------------------------
// sprk_todo record shape (single-entity load result)
// ---------------------------------------------------------------------------

/**
 * A `sprk_todo` record as returned by Web API `retrieveRecord`.
 *
 * Fields mirror `src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md`.
 * Includes the 11 `sprk_regarding*` lookups + 4 resolver fields (ADR-024),
 * 5 Graph sync state fields, and the standard system fields.
 */
export interface ITodoRecord {
  // Primary identity
  sprk_todoid: string;
  sprk_name: string;

  // Core detail
  sprk_description?: string;
  /** Rich-text notes (was `sprk_eventtodo.sprk_todonotes` in R1/R2). */
  sprk_notes?: string;
  sprk_duedate?: string | null;
  sprk_completedon?: string | null;

  // Scoring (native — NOT mirrored from sprk_event)
  sprk_priorityscore?: number;
  sprk_effortscore?: number;

  // Kanban behavior (native — NOT mirrored from sprk_event)
  /** Choice: 100000000=Today, 100000001=Tomorrow, 100000002=Future. */
  sprk_todocolumn?: number;
  /** Locks column assignment against auto-reassign. */
  sprk_todopinned?: boolean;

  // State (per task 009: 1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed)
  /** 0=Active, 1=Inactive. */
  statecode?: number;
  /** 1=Open, 659490001=In Progress, 2=Completed, 659490002=Dismissed. */
  statuscode?: number;

  // Assignment
  _sprk_assignedto_value?: string | null;
  '_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue'?: string;

  // ---- Regarding (11 specific lookups, ADR-024) -----------------------------
  _sprk_regardingmatter_value?: string | null;
  _sprk_regardingproject_value?: string | null;
  _sprk_regardingevent_value?: string | null;
  _sprk_regardingcommunication_value?: string | null;
  _sprk_regardingworkassignment_value?: string | null;
  _sprk_regardinginvoice_value?: string | null;
  _sprk_regardingbudget_value?: string | null;
  _sprk_regardinganalysis_value?: string | null;
  _sprk_regardingorganization_value?: string | null;
  _sprk_regardingcontact_value?: string | null;
  _sprk_regardingdocument_value?: string | null;

  // ---- Resolver fields (populated by PolymorphicResolverService, ADR-024) ---
  _sprk_regardingrecordtype_value?: string | null;
  '_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue'?: string;
  sprk_regardingrecordid?: string | null;
  sprk_regardingrecordname?: string | null;
  sprk_regardingrecordurl?: string | null;

  // ---- Graph sync state -----------------------------------------------------
  sprk_graphtodolistid?: string | null;
  sprk_graphtodotaskid?: string | null;
  sprk_lastsyncedutc?: string | null;
  sprk_synchash?: string | null;
  sprk_syncerror?: string | null;
}

/**
 * OData `$select` fields for a `sprk_todo` retrieve.
 *
 * Use:
 *   await webApi.retrieveRecord("sprk_todo", id, `?$select=${TODO_DETAIL_SELECT}`)
 */
export const TODO_DETAIL_SELECT = [
  // Identity & primary name
  'sprk_todoid',
  'sprk_name',
  // Detail
  'sprk_description',
  'sprk_notes',
  'sprk_duedate',
  'sprk_completedon',
  // Scoring + Kanban behavior (native on sprk_todo)
  'sprk_priorityscore',
  'sprk_effortscore',
  'sprk_todocolumn',
  'sprk_todopinned',
  // State
  'statecode',
  'statuscode',
  // Assignment
  '_sprk_assignedto_value',
  // Regarding — 11 specific lookups
  '_sprk_regardingmatter_value',
  '_sprk_regardingproject_value',
  '_sprk_regardingevent_value',
  '_sprk_regardingcommunication_value',
  '_sprk_regardingworkassignment_value',
  '_sprk_regardinginvoice_value',
  '_sprk_regardingbudget_value',
  '_sprk_regardinganalysis_value',
  '_sprk_regardingorganization_value',
  '_sprk_regardingcontact_value',
  '_sprk_regardingdocument_value',
  // Resolver fields
  '_sprk_regardingrecordtype_value',
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  'sprk_regardingrecordurl',
  // Graph sync state
  'sprk_graphtodolistid',
  'sprk_graphtodotaskid',
  'sprk_lastsyncedutc',
  'sprk_synchash',
  'sprk_syncerror',
].join(',');

// ---------------------------------------------------------------------------
// Field-update types (write path)
// ---------------------------------------------------------------------------

/**
 * Updatable fields for `sprk_todo` via Web API `updateRecord`.
 *
 * Note on resolver fields: callers MUST use `PolymorphicResolverService.applyResolverFields`
 * (typically via `buildTodoRegardingUpdate` in
 * `@spaarke/ui-components/services/TodoRegardingUpdateBuilder`) when changing any
 * `sprk_regarding*` specific lookup — never set the four resolver fields directly
 * (ADR-024).
 *
 * The index signature accepts the dynamic `@odata.bind` keys produced by
 * `buildTodoRegardingUpdate` (e.g., `sprk_RegardingMatter@odata.bind`,
 * `sprk_RegardingRecordType@odata.bind`) along with the three plain resolver
 * text/URL fields (id, name, url) which the builder also populates per ADR-024.
 *
 * Concrete shape (typed for editor IntelliSense on the well-known fields):
 */
export interface ITodoFieldUpdates {
  sprk_name?: string;
  sprk_description?: string;
  sprk_notes?: string;
  sprk_duedate?: string | null;
  sprk_completedon?: string | null;
  sprk_priorityscore?: number;
  sprk_effortscore?: number;
  sprk_todocolumn?: number;
  sprk_todopinned?: boolean;
  statecode?: number;
  statuscode?: number;
  /** OData bind for the Assigned To lookup (systemuser table). */
  'sprk_AssignedTo@odata.bind'?: string | null;

  // ---- Resolver fields (FR-13) ---------------------------------------------
  // These three text/URL fields are populated by buildTodoRegardingUpdate and
  // MUST NOT be set directly. They are listed here so the type accepts payloads
  // produced by the helper. Always use the helper (ADR-024).
  sprk_regardingrecordid?: string | null;
  sprk_regardingrecordname?: string | null;
  sprk_regardingrecordurl?: string | null;

  // ---- Dynamic @odata.bind escape hatch ------------------------------------
  // Accepts the entity-specific lookup binds + sprk_regardingrecordtype bind
  // produced by buildTodoRegardingUpdate. Keys end in '@odata.bind'; values
  // are `string` (e.g. "/sprk_matters(guid)") or `null` to clear the binding.
  [odataBindKey: `${string}@odata.bind`]: string | null | undefined;
}

// ---------------------------------------------------------------------------
// Picker option types
// ---------------------------------------------------------------------------

export interface IContactOption {
  id: string;
  name: string;
}
