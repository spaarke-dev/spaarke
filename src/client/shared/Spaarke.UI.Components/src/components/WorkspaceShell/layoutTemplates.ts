/**
 * Layout template definitions for workspace personalization.
 *
 * Each template describes a CSS grid arrangement that the Layout Wizard (Step 1)
 * presents as a visual thumbnail. Template IDs are stable string literals stored
 * in the Dataverse user-configuration JSON — do NOT rename them.
 *
 * Standards: ADR-012 (shared component library)
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** All valid layout template identifiers (stable — stored in Dataverse). */
export type LayoutTemplateId =
  | "2-col-equal"
  | "3-row-mixed"
  | "sidebar-main"
  | "single-column"
  | "3-col-equal"
  | "hero-grid"
  | "single-column-5"
  | "3-col-3-row"
  | "hero-2x2";

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

// ---------------------------------------------------------------------------
// Template definitions
// ---------------------------------------------------------------------------

const TWO_COL_EQUAL: LayoutTemplate = {
  id: "2-col-equal",
  name: "Two Column",
  description: "Two rows of two equal-width columns.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr 1fr", slotCount: 2 },
    { id: "row-2", gridTemplateColumns: "1fr 1fr", slotCount: 2 },
  ],
  slotCount: 4,
};

const THREE_ROW_MIXED: LayoutTemplate = {
  id: "3-row-mixed",
  name: "Three Row",
  description:
    "Two columns top and bottom with a full-width middle row. Default layout.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr 1fr", slotCount: 2 },
    { id: "row-2", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-3", gridTemplateColumns: "1fr 1fr", slotCount: 2 },
  ],
  slotCount: 5,
};

const SIDEBAR_MAIN: LayoutTemplate = {
  id: "sidebar-main",
  name: "Sidebar + Main",
  description: "Narrow sidebar beside a wide main area, three rows each.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr 2fr", slotCount: 2 },
    { id: "row-2", gridTemplateColumns: "1fr 2fr", slotCount: 2 },
    { id: "row-3", gridTemplateColumns: "1fr 2fr", slotCount: 2 },
  ],
  slotCount: 6,
};

const SINGLE_COLUMN: LayoutTemplate = {
  id: "single-column",
  name: "Single Column",
  description: "Four stacked full-width rows.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-2", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-3", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-4", gridTemplateColumns: "1fr", slotCount: 1 },
  ],
  slotCount: 4,
};

const THREE_COL_EQUAL: LayoutTemplate = {
  id: "3-col-equal",
  name: "Three Column",
  description: "Two rows of three equal-width columns.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr 1fr 1fr", slotCount: 3 },
    { id: "row-2", gridTemplateColumns: "1fr 1fr 1fr", slotCount: 3 },
  ],
  slotCount: 6,
};

const HERO_GRID: LayoutTemplate = {
  id: "hero-grid",
  name: "Hero + Grid",
  description: "Full-width hero row on top with a three-column row below.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-2", gridTemplateColumns: "1fr 1fr 1fr", slotCount: 3 },
  ],
  slotCount: 4,
};

const SINGLE_COLUMN_5: LayoutTemplate = {
  id: "single-column-5",
  name: "Single Column (5)",
  description: "Five stacked full-width rows.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-2", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-3", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-4", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-5", gridTemplateColumns: "1fr", slotCount: 1 },
  ],
  slotCount: 5,
};

const THREE_COL_THREE_ROW: LayoutTemplate = {
  id: "3-col-3-row",
  name: "Three Column, Three Row",
  description: "Three rows of three equal-width columns (9 slots).",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr 1fr 1fr", slotCount: 3 },
    { id: "row-2", gridTemplateColumns: "1fr 1fr 1fr", slotCount: 3 },
    { id: "row-3", gridTemplateColumns: "1fr 1fr 1fr", slotCount: 3 },
  ],
  slotCount: 9,
};

const HERO_2X2: LayoutTemplate = {
  id: "hero-2x2",
  name: "Hero + 2×2 Grid",
  description: "Full-width hero row on top with two rows of two columns below.",
  rows: [
    { id: "row-1", gridTemplateColumns: "1fr", slotCount: 1 },
    { id: "row-2", gridTemplateColumns: "1fr 1fr", slotCount: 2 },
    { id: "row-3", gridTemplateColumns: "1fr 1fr", slotCount: 2 },
  ],
  slotCount: 5,
};

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

/** All available layout templates. Order matches the Layout Wizard display. */
export const LAYOUT_TEMPLATES: readonly LayoutTemplate[] = [
  TWO_COL_EQUAL,
  THREE_ROW_MIXED,
  SIDEBAR_MAIN,
  SINGLE_COLUMN,
  SINGLE_COLUMN_5,
  THREE_COL_EQUAL,
  THREE_COL_THREE_ROW,
  HERO_GRID,
  HERO_2X2,
] as const;

/**
 * Look up a layout template by its stable ID.
 * Returns `undefined` if the ID does not match any known template.
 */
export function getLayoutTemplate(
  id: LayoutTemplateId,
): LayoutTemplate | undefined {
  return LAYOUT_TEMPLATES.find((t) => t.id === id);
}
