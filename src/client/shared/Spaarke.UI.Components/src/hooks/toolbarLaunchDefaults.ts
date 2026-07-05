/**
 * Modal size + entity mapping constants for record-header toolbar actions.
 *
 * Consumed by useRecordHeaderToolbarActions (task 012) and Notepad/SmartTodo
 * launch call sites. Establishes a single source of truth for Xrm.Navigation
 * modal sizing across all record-header consumers.
 *
 * Values verified via task 001 (Dataverse MCP schema verification 2026-07-02).
 * See projects/record-header-and-notepad-r1/notes/design-alignment-corrections.md.
 *
 * @see docs/standards/MODAL-DECISION-CRITERIA.md — Two-Layout Standard
 * @see .claude/patterns/ui/record-modal-selection.md
 */

/**
 * Layout 1 modal (R2 canonical standard) — 85% x 85%.
 *
 * Used for OOB Dataverse form dialogs and full-page Code Page launches
 * (e.g., SmartTodo code page). Every entity record row-click across
 * Spaarke workspaces uses this shape.
 *
 * Shape matches Xrm.Navigation.navigateTo `navigationOptions`:
 *   target=2 (modal), position=1 (center), width/height as percentages.
 */
export const LAYOUT_1_MODAL = {
  target: 2 as const,
  position: 1 as const,
  width: { value: 85, unit: '%' as const },
  height: { value: 85, unit: '%' as const },
};

/**
 * Notepad specialized-editor modal — 25% x 35% (v1.0.7).
 *
 * Original R1 spec was 70% x 80% (per FR-10). Live QA on v1.0.6 confirmed the
 * modal was oversized for a memo-editing task — Notepad is a tight scratchpad,
 * not a full document editor. Compact proportional sizing keeps the memo pane
 * close to the launcher and matches the "quick note" mental model.
 */
export const NOTEPAD_MODAL = {
  target: 2 as const,
  position: 1 as const,
  width: { value: 25, unit: '%' as const },
  height: { value: 35, unit: '%' as const },
};

/**
 * Webresource / Code Page name for the Notepad Vite SPA.
 *
 * Verified via Dataverse MCP query 2026-07-03 (v1.0.5 fix — after v1.0.2..v1.0.4
 * silently failed to open the modal). The deployed webresource in the dev
 * environment is `sprk_notepad` (webresourceid 7523b1db-e576-f111-ab0e-000d3a13a445,
 * displayname "Notepad HTML", type Webpage/HTML). The R1-spec-assumed name
 * `sprk_notepad_page` does NOT exist — sending it to `Xrm.Navigation.navigateTo`
 * produces a silent no-op (no console error, no modal). Matches the same
 * discovery pattern that established `sprk_smarttodo` (not `sprk_smarttodo_page`).
 */
export const NOTEPAD_WEBRESOURCE_NAME = 'sprk_notepad';

/**
 * Webresource / Code Page name for the SmartTodo Code Page.
 *
 * Verified via Dataverse MCP query in task 020 (2026-07-02): actual deployed
 * webresource is `sprk_smarttodo` (webresourceid f85a1884-962b-f111-88b5-7ced8d1dc988,
 * displayname "Smart To Do", type Webpage/HTML). The R4 spec-assumed name
 * `sprk_smarttodo_page` does NOT exist in the dev environment. Deployment
 * source: `scripts/Deploy-SmartTodo.ps1` + `src/solutions/SmartTodo/README.md`.
 *
 * See projects/record-header-and-notepad-r1/notes/smarttodo-webresource.md.
 */
export const SMARTTODO_WEBRESOURCE_NAME = 'sprk_smarttodo';

/**
 * `sprk_recordsummary` is a MULTILINE TEXT **field** on parent entities
 * (Matter today; more entities in future). It is NOT a separate Dataverse entity.
 *
 * Consumer pattern: read `record.sprk_recordsummary` from useRecordFieldValues
 * results — no separate Xrm.WebApi call needed.
 *
 * (Original design assumed an entity named `sprk_recordsummary`. Corrected by
 * task 001 schema verification. See notes/design-alignment-corrections.md §1.)
 */
export const RECORDSUMMARY_FIELD = 'sprk_recordsummary';

/**
 * ADR-024 dual-field pattern — `sprk_memo`'s entity-specific regarding lookups.
 *
 * Key   = parent entity logical name.
 * Value = lookup field name on `sprk_memo` that points at that parent.
 *
 * `sprk_memo` supports exactly six parent entities (schema-limited).
 * Any launch context whose `regardingEntity` is not in this map cannot
 * create memos and must render an error surface (FR-13 / FR-19).
 *
 * Verified via Dataverse MCP `describe('tables/sprk_memo')` in task 001.
 * See notes/sprk-memo-schema.md.
 */
export const SUPPORTED_MEMO_PARENTS: Record<string, string> = {
  sprk_matter: 'sprk_regardingmatter',
  sprk_project: 'sprk_regardingproject',
  sprk_event: 'sprk_regardingevent',
  sprk_invoice: 'sprk_regardinginvoice',
  sprk_budget: 'sprk_regardingbudget',
  sprk_workassignment: 'sprk_regardingworkassignment',
};

/**
 * Build the OData `$filter` clause for memo-count / memo-list queries per FR-06.
 *
 * Uses the entity-specific lookup field for the given parent entity. Returns
 * `null` when the parent entity is not supported by the `sprk_memo` schema —
 * callers should treat null as "memos not applicable for this entity" and
 * skip the count / list surface.
 *
 * @param regardingEntity - The parent entity logical name (e.g. "sprk_matter").
 * @param regardingId     - The parent record GUID (no braces).
 * @returns OData filter string, or `null` if the entity is not supported.
 *
 * @example
 * buildMemoFilterForParent("sprk_matter", "00000000-0000-0000-0000-000000000001")
 * // → "_sprk_regardingmatter_value eq 00000000-0000-0000-0000-000000000001"
 */
export function buildMemoFilterForParent(regardingEntity: string, regardingId: string): string | null {
  const lookupField = SUPPORTED_MEMO_PARENTS[regardingEntity];
  if (!lookupField) return null;
  return `_${lookupField}_value eq ${regardingId}`;
}

/**
 * ADR-024 dual-field pattern — `sprk_todo`'s entity-specific regarding lookups.
 *
 * `sprk_todo` does NOT have a polymorphic `regardingobjectid` lookup — verified via
 * Dataverse MCP `describe('tables/sprk_todo')` on 2026-07-03 after live QA surfaced a
 * "Could not find a property named '_regardingobjectid_value'" 400 error.
 *
 * `sprk_todo` supports **11 parent entity lookups** (a superset of `sprk_memo`'s six):
 * Matter, Project, Event, Invoice, Budget, WorkAssignment, Analysis, Communication,
 * Contact, Document, Organization.
 *
 * Key   = parent entity logical name.
 * Value = lookup field name on `sprk_todo` that points at that parent.
 */
export const SUPPORTED_TODO_PARENTS: Record<string, string> = {
  sprk_matter: 'sprk_regardingmatter',
  sprk_project: 'sprk_regardingproject',
  sprk_event: 'sprk_regardingevent',
  sprk_invoice: 'sprk_regardinginvoice',
  sprk_budget: 'sprk_regardingbudget',
  sprk_workassignment: 'sprk_regardingworkassignment',
  sprk_analysis: 'sprk_regardinganalysis',
  sprk_communication: 'sprk_regardingcommunication',
  contact: 'sprk_regardingcontact',
  sprk_document: 'sprk_regardingdocument',
  sprk_organization: 'sprk_regardingorganization',
};

/**
 * Build the OData `$filter` clause for todo-count queries per FR-09.
 *
 * Mirrors {@link buildMemoFilterForParent} but uses `SUPPORTED_TODO_PARENTS`.
 * Returns `null` when the parent entity is not supported by the `sprk_todo` schema.
 *
 * @param regardingEntity - Parent entity logical name (e.g. "sprk_matter").
 * @param regardingId     - Parent record GUID (no braces).
 * @returns OData filter string, or `null` if the entity is not supported.
 *
 * @example
 * buildTodoFilterForParent("sprk_matter", "00000000-0000-0000-0000-000000000001")
 * // → "_sprk_regardingmatter_value eq 00000000-0000-0000-0000-000000000001"
 */
export function buildTodoFilterForParent(regardingEntity: string, regardingId: string): string | null {
  const lookupField = SUPPORTED_TODO_PARENTS[regardingEntity];
  if (!lookupField) return null;
  return `_${lookupField}_value eq ${regardingId}`;
}
