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
 * Notepad specialized-editor modal — 70% x 80%.
 *
 * Notepad is a lightweight memo editor; a smaller modal keeps the surface
 * proportional to a text-editing task. Distinct from Layout 1 per FR-10.
 */
export const NOTEPAD_MODAL = {
  target: 2 as const,
  position: 1 as const,
  width: { value: 70, unit: '%' as const },
  height: { value: 80, unit: '%' as const },
};

/**
 * Webresource / Code Page name for the Notepad Vite SPA.
 * Deployed as a full-page Code Page in Dataverse per task 039.
 */
export const NOTEPAD_WEBRESOURCE_NAME = 'sprk_notepad_page';

/**
 * Webresource / Code Page name for the SmartTodo Code Page.
 * Verify against target environment during task 020 — may need adjustment.
 */
export const SMARTTODO_WEBRESOURCE_NAME = 'sprk_smarttodo_page';

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
