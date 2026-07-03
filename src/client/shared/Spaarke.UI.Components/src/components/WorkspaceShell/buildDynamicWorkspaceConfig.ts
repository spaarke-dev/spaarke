/**
 * buildDynamicWorkspaceConfig.ts — Merges stored layout JSON with a section registry to
 * produce a WorkspaceConfig for WorkspaceShell.
 *
 * The layout JSON describes "which sections go where" (row structure + column widths),
 * while the registry describes "how to create each section" (factory functions). This
 * function reconciles the two and handles edge cases gracefully.
 *
 * Hoisted from `src/solutions/LegalWorkspace/src/workspace/buildDynamicWorkspaceConfig.ts`
 * in task 067 — see notes/drafts/067-factory-inventory.md for the architectural decision.
 * The original LegalWorkspace file now re-exports from this canonical location.
 *
 * Standards: ADR-012 (shared component library — pure, type-only deps), ADR-021 (Fluent v9)
 */

import type {
  WorkspaceConfig,
  WorkspaceRowConfig,
  SectionConfig,
  SectionRegistration,
  SectionFactoryContext,
} from './types';

// ---------------------------------------------------------------------------
// Layout JSON shape (stored in sprk_sectionsjson Dataverse column)
// ---------------------------------------------------------------------------

/**
 * Per-instance override shape for a section placed in a layout row.
 *
 * Spec: FR-03 (spaarke-dataset-grid-framework-r2, task 012, 2026-07-02).
 *
 * Rationale: the same section registration (e.g., `communications`) can appear
 * in different dashboards with different labels ("Email" vs "Communications"),
 * different config records (`configIdOverride` for the alt grid), different
 * pageSizes, or restricted view pickers. Previously the only way to vary these
 * was to author a whole new registration + config record. `SectionInstance` lets
 * a single registration participate in multiple layouts with per-placement
 * customization.
 *
 * All fields except `id` are optional. When omitted, framework/registration/
 * config-record defaults apply — matching pre-FR-03 behavior.
 *
 * Wire-through:
 * - `configIdOverride` — surfaced to the section factory via
 *   `SectionFactoryContext.sectionInstance`. The factory reads it (if it embeds
 *   a `<DataGrid />`) to pick an alternate `configId`.
 * - `label` — overwrites `SectionConfig.title` post-factory. Underlying
 *   metadata (`SectionMetadata.label`) unchanged.
 * - `overrides.pageSize` — surfaced via `SectionFactoryContext.sectionInstance`.
 *   Factories forward it to the `<DataGrid />` grid `behavior.pageSize` fallback
 *   chain (see `configResolution.resolveEffectivePageSize`).
 * - `overrides.availableViews` — surfaced via
 *   `SectionFactoryContext.sectionInstance`. Factories forward it to the
 *   `<DataGrid />` availableViews chain (see
 *   `configResolution.resolveEffectiveAvailableViews`). REPLACES config-level
 *   `SourceSavedQuery.availableViews` (FR-05) when both are set.
 */
export interface SectionInstance {
  /** Registration ID (matches `SectionRegistration.id` — same as the bare-string form). */
  id: string;
  /**
   * When set, the section's `<DataGrid />` uses this `configId` in place of the
   * registration's baked-in default. Enables one registration to render different
   * `sprk_gridconfiguration` records per placement.
   */
  configIdOverride?: string;
  /**
   * When set, the section header title is rendered as this string. The underlying
   * `SectionMetadata.label` remains unchanged (wizard picker still shows the
   * registration label). Folds design Issue 6 (per-instance rename).
   */
  label?: string;
  /**
   * Per-instance DataGrid behavior overrides. Highest precedence; overrides the
   * config record's `behavior.pageSize` and `SourceSavedQuery.availableViews`.
   */
  overrides?: {
    /**
     * Effective `pageSize` for this section instance's grid. Highest precedence:
     * SectionInstance.overrides.pageSize > config record's `behavior.pageSize` >
     * framework default (25, per FR-07).
     */
    pageSize?: number;
    /**
     * Effective savedquery allowlist for this section instance's ViewSelector.
     * Highest precedence: SectionInstance.overrides.availableViews REPLACES
     * config-level `SourceSavedQuery.availableViews` (FR-05) when both are set.
     */
    availableViews?: string[];
  };
}

/**
 * Normalize a `LayoutJsonRow.sections` entry into a `SectionInstance`.
 *
 * Bare-string entries (the pre-FR-03 shape used by every published layout as of
 * 2026-07-02) are widened into `{ id: entry }`. Object entries are returned
 * verbatim. Enables `buildDynamicWorkspaceConfig` and consumers to treat both
 * shapes uniformly without repeated conditional logic.
 *
 * Spec: FR-03 (spaarke-dataset-grid-framework-r2, task 012).
 *
 * @param entry - A bare section-id string OR a `SectionInstance` object.
 * @returns Normalized `SectionInstance`. Never mutates the input.
 */
export function normalizeSection(entry: string | SectionInstance): SectionInstance {
  if (typeof entry === 'string') {
    return { id: entry };
  }
  return entry;
}

/** A single row in the persisted layout JSON. */
export interface LayoutJsonRow {
  /** Stable row identifier (e.g., "row-1"). */
  id: string;
  /** CSS grid-template-columns value for desktop (e.g., "1fr 1fr"). */
  columns: string;
  /** Responsive override at max-width 767px. Defaults to "1fr" if omitted. */
  columnsSmall?: string;
  /**
   * Ordered section entries assigned to this row's slots.
   *
   * Two shapes accepted (widened by FR-03, task 012):
   *   - Bare string `"communications"` — treated as `{ id: "communications" }`.
   *     Preserves back-compat with every published `sprk_workspacelayout`
   *     record predating FR-03.
   *   - `SectionInstance` object with per-placement overrides (label, configId,
   *     behavior). See `SectionInstance`.
   *
   * Normalization is handled by `normalizeSection` at the read boundary so that
   * downstream code always sees `SectionInstance`.
   */
  sections: Array<string | SectionInstance>;
  /**
   * Optional. When set, this row is clamped to the height (accepts any CSS length:
   * '480px', '80vh', '100vh'). Sections respect this ceiling regardless of their
   * `contentSizing`.
   *
   * Spec: FR-02 (spaarke-dataset-grid-framework-r2). Applied by the framework as
   * `maxHeight` + `overflow: hidden` on the row wrapper. When both `rowHeight`
   * (row-level) AND `contentSizing: 'clamped'` (section-level) are set, the row's
   * ceiling wins — the row wrapper's `maxHeight` provides the constraint and the
   * section's own `defaultHeight` still applies to its inner card, but the row's
   * `overflow: hidden` prevents any over-constraint on the outer wrapper.
   *
   * Omitted `rowHeight` preserves current behavior (row grows to fit sections;
   * sections apply their own `contentSizing` per FR-01).
   */
  readonly rowHeight?: string;
}

/** Record ownership scope for workspace queries. */
export type WorkspaceScope = 'my' | 'all';

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
      id: 'row-1',
      columns: '1fr 1fr',
      sections: ['get-started', 'quick-summary'],
    },
    {
      id: 'row-2',
      columns: '1fr',
      sections: ['latest-updates'],
    },
    {
      id: 'row-3',
      columns: '1fr 1fr',
      sections: ['todo', 'documents'],
    },
  ],
};

// ---------------------------------------------------------------------------
// Helper: look up a registration by ID
// ---------------------------------------------------------------------------

function findRegistration(id: string, registry: readonly SectionRegistration[]): SectionRegistration | undefined {
  return registry.find(r => r.id === id);
}

// ---------------------------------------------------------------------------
// Core builder
// ---------------------------------------------------------------------------

/**
 * Build a WorkspaceConfig by merging layout JSON with the section registry.
 *
 * @param layoutJson  - Persisted layout from sprk_sectionsjson (or SYSTEM_DEFAULT_LAYOUT_JSON)
 * @param registry    - All available section registrations
 * @param context     - Standard SectionFactoryContext passed to each section factory
 * @returns A complete WorkspaceConfig ready for <WorkspaceShell config={...} />
 */
export function buildDynamicWorkspaceConfig(
  layoutJson: LayoutJson,
  registry: readonly SectionRegistration[],
  context: SectionFactoryContext
): WorkspaceConfig {
  // -------------------------------------------------------------------------
  // Step 0: Inject scope from layout JSON into the factory context
  // -------------------------------------------------------------------------
  const effectiveContext: SectionFactoryContext = {
    ...context,
    scope: layoutJson.scope ?? 'my',
  };

  // -------------------------------------------------------------------------
  // Step 1: Validate schema version
  // -------------------------------------------------------------------------
  if (layoutJson.schemaVersion !== SUPPORTED_SCHEMA_VERSION) {
    console.warn(
      `[buildDynamicWorkspaceConfig] Unsupported schemaVersion ${layoutJson.schemaVersion} ` +
        `(expected ${SUPPORTED_SCHEMA_VERSION}). Falling back to system default layout.`
    );
    return buildDynamicWorkspaceConfig(SYSTEM_DEFAULT_LAYOUT_JSON, registry, context);
  }

  // -------------------------------------------------------------------------
  // Step 2: Resolve sections and build rows
  // -------------------------------------------------------------------------
  const allSections: SectionConfig[] = [];
  const rows: WorkspaceRowConfig[] = [];

  for (const jsonRow of layoutJson.rows) {
    const resolvedSectionIds: string[] = [];

    for (const rawEntry of jsonRow.sections) {
      // Task 091 fix 1 — tolerate empty / falsy slot IDs silently.
      // The wizard now allows saving layouts with empty slots (operator
      // removed a section via the X button). Empty slots serialize as ""
      // in sectionsJson; we skip them without a console warning. Applies
      // equally to `SectionInstance` objects with an empty `id`.
      if (!rawEntry) {
        continue;
      }

      // FR-03 (task 012): normalize bare-string entries into SectionInstance
      // at the read boundary. Downstream code always sees the object shape.
      const instance = normalizeSection(rawEntry);

      if (!instance.id) {
        continue;
      }

      const registration = findRegistration(instance.id, registry);

      if (!registration) {
        console.warn(`Unknown section ID: ${instance.id}, skipping`);
        continue;
      }

      // FR-03 (task 012): make per-instance overrides visible to the factory
      // via a per-section-augmented SectionFactoryContext. Factories that
      // embed a `<DataGrid />` read `context.sectionInstance` to:
      //   - swap `configId` (configIdOverride)
      //   - forward per-instance `overrides.pageSize` / `overrides.availableViews`
      // Factories that don't consume `sectionInstance` (get-started, quick-summary,
      // etc.) simply ignore the extra field — additive, back-compat-safe.
      const perSectionContext: SectionFactoryContext = {
        ...effectiveContext,
        sectionInstance: instance,
      };

      // Call the factory to produce the SectionConfig
      const sectionConfig = registration.factory(perSectionContext);

      // FR-03 (task 012): apply `SectionInstance.label` as a title override on
      // the returned SectionConfig. `SectionMetadata.label` is unchanged (the
      // wizard picker still shows the registration label). Empty-string labels
      // are treated as "no override" — same policy as the wizard save contract.
      if (instance.label && instance.label.length > 0) {
        sectionConfig.title = instance.label;
      }

      // Apply defaultHeight from registration based on contentSizing intent.
      //
      // Spec ref: FR-01 (spaarke-dataset-grid-framework-r2). Sections declare
      // whether their `defaultHeight` should be a FLOOR (`'grow'`, default) or a
      // CEILING (`'clamped'`). The framework then applies the correct CSS key.
      //
      // - `'clamped'` (dense grids etc.) → `max-height` + `overflow: hidden` +
      //   `display: flex`. The SectionPanel becomes a fixed-size viewport and the
      //   widget must supply its own inner scroll surface (see
      //   `.claude/patterns/ui/embedded-widget-sizing.md`).
      // - `'grow'` or omitted (back-compat with existing sections) → `min-height`.
      //   The section grows to fit intrinsic content. This preserves current
      //   behavior for every registration that does not set `contentSizing`.
      //
      // In both branches, operator-set styles (`style.minHeight` or `style.maxHeight`
      // supplied by the factory) are NOT overwritten — this preserves the existing
      // "factory wins" contract.
      if (registration.defaultHeight) {
        if (registration.contentSizing === 'clamped') {
          if (!sectionConfig.style?.maxHeight) {
            sectionConfig.style = {
              ...sectionConfig.style,
              maxHeight: registration.defaultHeight,
              overflow: 'hidden',
              display: 'flex',
            };
          }
        } else if (!sectionConfig.style?.minHeight) {
          sectionConfig.style = {
            ...sectionConfig.style,
            minHeight: registration.defaultHeight,
          };
        }
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

    // FR-02: propagate optional row-level `rowHeight` as `maxHeight` +
    // `overflow: 'hidden'` on the row wrapper. When both `rowHeight` (row-level)
    // AND `contentSizing: 'clamped'` (section-level) are set, the row's ceiling
    // wins — the section's `defaultHeight` still applies to its inner card, but
    // the row's `overflow: hidden` prevents any over-constraint on the outer
    // wrapper.
    const rowHeightStyle = jsonRow.rowHeight
      ? { maxHeight: jsonRow.rowHeight, overflow: 'hidden' as const }
      : undefined;

    if (resolvedSectionIds.length <= templateSlotCount) {
      // Normal case: sections fit within the row's column template
      rows.push({
        id: jsonRow.id,
        sectionIds: resolvedSectionIds,
        gridTemplateColumns: jsonRow.columns,
        gridTemplateColumnsSmall: jsonRow.columnsSmall,
        ...(rowHeightStyle ?? {}),
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
        ...(rowHeightStyle ?? {}),
      });

      // Remaining sections go into auto-appended single-column rows.
      // The row-level `rowHeight` ceiling also applies to each overflow row —
      // if operator set a height ceiling, every derived row honors it.
      const overflow = resolvedSectionIds.slice(templateSlotCount);
      for (let i = 0; i < overflow.length; i++) {
        rows.push({
          id: `${jsonRow.id}-overflow-${i + 1}`,
          sectionIds: [overflow[i]],
          gridTemplateColumns: '1fr',
          ...(rowHeightStyle ?? {}),
        });
      }
    }
  }

  // -------------------------------------------------------------------------
  // Step 3: Return the complete WorkspaceConfig
  // -------------------------------------------------------------------------
  return {
    layout: 'rows',
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
