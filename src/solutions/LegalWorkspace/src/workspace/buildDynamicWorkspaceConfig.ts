/**
 * buildDynamicWorkspaceConfig.ts — Merges stored layout JSON (from sprk_sectionsjson)
 * with the SECTION_REGISTRY to produce a WorkspaceConfig for WorkspaceShell.
 *
 * The layout JSON describes "which sections go where" (row structure + column widths),
 * while the registry describes "how to create each section" (factory functions).
 * This function reconciles the two and handles edge cases gracefully.
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9)
 */

import type {
  WorkspaceConfig,
  WorkspaceRowConfig,
  SectionConfig,
  SectionRegistration,
  SectionFactoryContext,
} from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Layout JSON shape (stored in sprk_sectionsjson Dataverse column)
// ---------------------------------------------------------------------------

/** A single row in the persisted layout JSON. */
export interface LayoutJsonRow {
  /** Stable row identifier (e.g., "row-1"). */
  id: string;
  /** CSS grid-template-columns value for desktop (e.g., "1fr 1fr"). */
  columns: string;
  /** Responsive override at max-width 767px. Defaults to "1fr" if omitted. */
  columnsSmall?: string;
  /** Ordered section IDs assigned to this row's slots. */
  sections: string[];
}

/** Record ownership scope for workspace queries. */
export type WorkspaceScope = "my" | "all";

/** Top-level layout JSON persisted in Dataverse. */
export interface LayoutJson {
  /** Schema version for forward compatibility. Currently only version 1 is supported. */
  schemaVersion: number;
  /** Ordered row definitions with section assignments. */
  rows: LayoutJsonRow[];
  /** Record scope: "my" = user-owned only, "all" = user + team/BU owned. Default: "my". */
  scope?: WorkspaceScope;
}

// ---------------------------------------------------------------------------
// Supported schema version
// ---------------------------------------------------------------------------

const SUPPORTED_SCHEMA_VERSION = 1;

// ---------------------------------------------------------------------------
// System default layout — matches the current hardcoded 5-section config
// (3-row-mixed template: 2 cols top, 1 col middle, 2 cols bottom)
// ---------------------------------------------------------------------------

/**
 * Default layout JSON matching the original hardcoded workspace configuration.
 * Used as fallback when no user configuration exists or when schemaVersion
 * is unsupported.
 *
 * Layout: 3-row-mixed
 *   Row 1: get-started | quick-summary   (1fr 1fr)
 *   Row 2: latest-updates                (1fr)
 *   Row 3: todo | documents              (1fr 1fr)
 */
export const SYSTEM_DEFAULT_LAYOUT_JSON: LayoutJson = {
  schemaVersion: 1,
  rows: [
    {
      id: "row-1",
      columns: "1fr 1fr",
      sections: ["get-started", "quick-summary"],
    },
    {
      id: "row-2",
      columns: "1fr",
      sections: ["latest-updates"],
    },
    {
      id: "row-3",
      columns: "1fr 1fr",
      sections: ["todo", "documents"],
    },
  ],
};

// ---------------------------------------------------------------------------
// Helper: look up a registration by ID
// ---------------------------------------------------------------------------

function findRegistration(
  id: string,
  registry: readonly SectionRegistration[],
): SectionRegistration | undefined {
  return registry.find((r) => r.id === id);
}

// ---------------------------------------------------------------------------
// Core builder
// ---------------------------------------------------------------------------

/**
 * Build a WorkspaceConfig by merging layout JSON with the section registry.
 *
 * @param layoutJson  - Persisted layout from sprk_sectionsjson (or SYSTEM_DEFAULT_LAYOUT_JSON)
 * @param registry    - All available section registrations (typically SECTION_REGISTRY)
 * @param context     - Standard SectionFactoryContext passed to each section factory
 * @returns A complete WorkspaceConfig ready for <WorkspaceShell config={...} />
 */
export function buildDynamicWorkspaceConfig(
  layoutJson: LayoutJson,
  registry: readonly SectionRegistration[],
  context: SectionFactoryContext,
): WorkspaceConfig {
  // -------------------------------------------------------------------------
  // Step 0: Inject scope from layout JSON into the factory context
  // -------------------------------------------------------------------------
  const effectiveContext: SectionFactoryContext = {
    ...context,
    scope: layoutJson.scope ?? "my",
  };

  // -------------------------------------------------------------------------
  // Step 1: Validate schema version
  // -------------------------------------------------------------------------
  if (layoutJson.schemaVersion !== SUPPORTED_SCHEMA_VERSION) {
    console.warn(
      `[buildDynamicWorkspaceConfig] Unsupported schemaVersion ${layoutJson.schemaVersion} ` +
        `(expected ${SUPPORTED_SCHEMA_VERSION}). Falling back to system default layout.`,
    );
    return buildDynamicWorkspaceConfig(
      SYSTEM_DEFAULT_LAYOUT_JSON,
      registry,
      context,
    );
  }

  // -------------------------------------------------------------------------
  // Step 2: Resolve sections and build rows
  // -------------------------------------------------------------------------
  const allSections: SectionConfig[] = [];
  const rows: WorkspaceRowConfig[] = [];

  for (const jsonRow of layoutJson.rows) {
    const resolvedSectionIds: string[] = [];

    for (const sectionId of jsonRow.sections) {
      const registration = findRegistration(sectionId, registry);

      if (!registration) {
        console.warn(
          `Unknown section ID: ${sectionId}, skipping`,
        );
        continue;
      }

      // Call the factory to produce the SectionConfig
      const sectionConfig = registration.factory(effectiveContext);

      // Apply defaultHeight from registration if factory didn't set minHeight
      if (registration.defaultHeight && !sectionConfig.style?.minHeight) {
        sectionConfig.style = {
          ...sectionConfig.style,
          minHeight: registration.defaultHeight,
        };
      }

      allSections.push(sectionConfig);
      resolvedSectionIds.push(sectionConfig.id);
    }

    // Skip empty rows (all sections were unknown/filtered out)
    if (resolvedSectionIds.length === 0) {
      continue;
    }

    // Determine grid columns: if more sections resolved than the template
    // columns can hold, we need to handle overflow
    const templateSlotCount = countSlots(jsonRow.columns);

    if (resolvedSectionIds.length <= templateSlotCount) {
      // Normal case: sections fit within the row's column template
      rows.push({
        id: jsonRow.id,
        sectionIds: resolvedSectionIds,
        gridTemplateColumns: jsonRow.columns,
        gridTemplateColumnsSmall: jsonRow.columnsSmall,
      });
    } else {
      // Overflow: more sections than template slots — split into the
      // original row (filled to capacity) plus auto-appended 1fr rows
      const firstBatch = resolvedSectionIds.slice(0, templateSlotCount);
      rows.push({
        id: jsonRow.id,
        sectionIds: firstBatch,
        gridTemplateColumns: jsonRow.columns,
        gridTemplateColumnsSmall: jsonRow.columnsSmall,
      });

      // Remaining sections go into auto-appended single-column rows
      const overflow = resolvedSectionIds.slice(templateSlotCount);
      for (let i = 0; i < overflow.length; i++) {
        rows.push({
          id: `${jsonRow.id}-overflow-${i + 1}`,
          sectionIds: [overflow[i]],
          gridTemplateColumns: "1fr",
        });
      }
    }
  }

  // -------------------------------------------------------------------------
  // Step 3: Return the complete WorkspaceConfig
  // -------------------------------------------------------------------------
  return {
    layout: "rows",
    rows,
    sections: allSections,
  };
}

// ---------------------------------------------------------------------------
// Utility: count column slots from a grid-template-columns string
// ---------------------------------------------------------------------------

/**
 * Estimates the number of column slots from a CSS grid-template-columns value.
 * Handles common patterns: "1fr 1fr" → 2, "1fr 2fr" → 2, "1fr" → 1,
 * "repeat(3, 1fr)" → 3.
 */
function countSlots(gridTemplateColumns: string | undefined): number {
  if (!gridTemplateColumns) return 1;
  const trimmed = gridTemplateColumns.trim();

  // Handle repeat() notation: "repeat(3, 1fr)" → 3
  const repeatMatch = trimmed.match(/^repeat\(\s*(\d+)\s*,/);
  if (repeatMatch) {
    return parseInt(repeatMatch[1], 10);
  }

  // Otherwise count space-separated tokens
  return trimmed.split(/\s+/).length;
}
