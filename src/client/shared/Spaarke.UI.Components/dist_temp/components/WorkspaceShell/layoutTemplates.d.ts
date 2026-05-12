/**
 * Layout template definitions for workspace personalization.
 *
 * Each template describes a CSS grid arrangement that the Layout Wizard (Step 1)
 * presents as a visual thumbnail. Template IDs are stable string literals stored
 * in the Dataverse user-configuration JSON — do NOT rename them.
 *
 * Standards: ADR-012 (shared component library)
 */
/** All valid layout template identifiers (stable — stored in Dataverse). */
export type LayoutTemplateId = "2-col-equal" | "3-row-mixed" | "sidebar-main" | "single-column" | "3-col-equal" | "hero-grid" | "single-column-5" | "3-col-3-row" | "hero-2x2";
/** A single row within a layout template. */
export interface LayoutTemplateRow {
    /** Stable row identifier (e.g., "row-1"). */
    id: string;
    /** CSS grid-template-columns value for this row (desktop). */
    gridTemplateColumns: string;
    /** Responsive override at max-width 767px. Defaults to "1fr" if omitted. */
    gridTemplateColumnsSmall?: string;
    /** Number of column slots in this row. */
    slotCount: number;
}
/** A layout template describing a grid arrangement for workspace sections. */
export interface LayoutTemplate {
    /** Stable identifier stored in Dataverse user configuration. */
    id: LayoutTemplateId;
    /** Human-readable name shown in the Layout Wizard. */
    name: string;
    /** Short description shown below the template thumbnail. */
    description: string;
    /** Ordered row definitions that compose the grid. */
    rows: readonly LayoutTemplateRow[];
    /** Total number of section slots across all rows. */
    slotCount: number;
    /** Optional path to a preview thumbnail image. */
    thumbnail?: string;
}
/** All available layout templates. Order matches the Layout Wizard display. */
export declare const LAYOUT_TEMPLATES: readonly LayoutTemplate[];
/**
 * Look up a layout template by its stable ID.
 * Returns `undefined` if the ID does not match any known template.
 */
export declare function getLayoutTemplate(id: LayoutTemplateId): LayoutTemplate | undefined;
//# sourceMappingURL=layoutTemplates.d.ts.map