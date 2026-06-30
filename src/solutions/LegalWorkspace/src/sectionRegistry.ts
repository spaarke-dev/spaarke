/**
 * sectionRegistry.ts — Aggregated registry of all workspace section registrations.
 *
 * Single import point for all section metadata. The dynamic config builder
 * (Phase 4) consumes SECTION_REGISTRY to render available sections.
 *
 * Adding a new section requires only:
 *   1. Create a new {name}.registration.ts in sections/
 *   2. Import and add it to the factory body in `createLegalWorkspaceSectionRegistry` below
 *
 * # Registry-as-Composition Factory (R2 Option D, 2026-06-18)
 *
 * Post-R2: the registry is built by `createLegalWorkspaceSectionRegistry(options)`
 * — a factory exposing per-widget customization knobs through a typed
 * `LegalWorkspaceSectionRegistryOptions` interface. The default `SECTION_REGISTRY`
 * const is now `createLegalWorkspaceSectionRegistry()` (no options), preserving
 * standalone-LegalWorkspace bundle behavior byte-identically (FR-25 / NFR-10).
 *
 * Embedding hosts (SpaarkeAi) build a CUSTOM registry by passing options:
 *   `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } })`
 * and feed it into `<LegalWorkspaceApp sections={...} />` via the renderer slot.
 *
 * This replaces the R2 task 002 module-mutation slot pattern (Wave 8) — the
 * `setLegalWorkspaceDailyBriefingNotificationLoader` setter is gone. See
 * `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md`
 * for the full design rationale + speculative-design discipline.
 *
 * Standards: ADR-012 (shared components)
 */

import type {
  SectionRegistration,
  SectionCategory,
  NarrateRequest,
} from "@spaarke/ui-components";
import { SECTION_METADATA_CATALOG } from "@spaarke/ui-components";
import { getStartedRegistration } from "./sections/getStarted.registration";
import { quickSummaryRegistration } from "./sections/quickSummary.registration";
import { latestUpdatesRegistration } from "./sections/latestUpdates.registration";
import { todoRegistration } from "./sections/todo.registration";
import { documentsRegistration } from "./sections/documents.registration";
import { createLegalWorkspaceDailyBriefingRegistration } from "./sections/dailyBriefing/dailyBriefing.registration";
import { calendarRegistration } from "./sections/calendar.registration";
// ai-spaarke-ai-workspace-UI-r1 #4 (2026-06-08): three new Dataverse entity-view
// sections that share <DataverseEntityViewWidget> with the (already-modified)
// documents section. Each is a thin shim with a hardcoded sprk_gridconfiguration
// GUID — see each file's DEPLOYMENT REQUIREMENT note.
import { projectsRegistration } from "./sections/projects.registration";
import { invoicesRegistration } from "./sections/invoices.registration";
import { workAssignmentsRegistration } from "./sections/workAssignments.registration";
import { mattersRegistration } from "./sections/matters.registration";
// spaarkeai-compose-r1 task 040 (2026-06-29): Compose editor section. Mounted
// by the "Compose" workspace layout (system row, task 010). Renders an inline
// Skeleton placeholder in R1; task 042 swaps in the real TipTap widget.
import { composeEditorRegistration } from "./sections/composeEditor.registration";

/**
 * Per-widget customization options for the LegalWorkspace section registry.
 *
 * Each property targets ONE widget's factory and exposes ONLY the knobs that
 * widget's factory accepts. Adding a new knob = add it here + thread it through
 * `createLegalWorkspaceSectionRegistry` below.
 *
 * # Speculative-design discipline (R2 Option D §5)
 *
 * Only widgets that have a real consumer-driven customization need appear here.
 * Examples we explicitly do NOT add today (no concrete consumer):
 *   - `calendar?: { initialFilter?: CalendarFilter; onDayClick?: (date: Date) => void }`
 *   - `smartTodo?: { ... }`
 *   - `paneEventBus?: PaneEventBus` (cross-widget messaging primitive)
 *   - `agentClient?: AgentClient` (Assistant ⇄ Workspace binding)
 *   - `contextProvider?: ContextProvider` (Context → Workspace binding)
 *   - per-widget `visibilityPolicy` (R6 Pillar 9 hook)
 *
 * When the first concrete consumer needs any of those, add a new property here
 * + thread it through the factory. No restructuring required — the architecture
 * absorbs new requirements additively.
 */
export interface LegalWorkspaceSectionRegistryOptions {
  /**
   * Daily Briefing customization. Currently exposes the notification-context
   * loader — used by SpaarkeAi to flow `loadSpaarkeAiNotificationContext` into
   * the BFF `/narrate` envelope so the embedded copy renders real bullets.
   *
   * Standalone consumers omit this → empty-payload contract preserved
   * (BFF returns empty bullets → empty-state UI). FR-25 / NFR-10.
   */
  dailyBriefing?: {
    loadNotificationContext?: () => Promise<NarrateRequest | null>;
  };
}

/**
 * Build a LegalWorkspace section registry, optionally customizing per-widget
 * construction. With no options, returns a registry byte-identical in behavior
 * to the standalone-LegalWorkspace `SECTION_REGISTRY` const (FR-25 / NFR-10).
 *
 * Dev-mode duplicate-ID + metadata-drift guards run for every registry built
 * via this factory — custom registries get the same drift detection as the
 * default one.
 */
export function createLegalWorkspaceSectionRegistry(
  options: LegalWorkspaceSectionRegistryOptions = {},
): readonly SectionRegistration[] {
  const registry: readonly SectionRegistration[] = [
    getStartedRegistration,
    quickSummaryRegistration,
    latestUpdatesRegistration,
    todoRegistration,
    documentsRegistration,
    mattersRegistration,
    projectsRegistration,
    invoicesRegistration,
    workAssignmentsRegistration,
    createLegalWorkspaceDailyBriefingRegistration(options.dailyBriefing ?? {}),
    calendarRegistration,
    composeEditorRegistration,
  ] as const;

  if (process.env.NODE_ENV !== "production") {
    runRegistryDevGuards(registry);
  }

  return registry;
}

// ---------------------------------------------------------------------------
// Development guards: detect duplicate section IDs + drift vs the shared
// SECTION_METADATA_CATALOG (the single source of truth for wizard pickability).
//
// R4 W-3 (task 040, 2026-05-26): the wizard's picker reads
// `SECTION_METADATA_CATALOG` from `@spaarke/ui-components`. Any new section
// added HERE without a matching metadata entry would not appear in the wizard
// (and vice versa: a metadata entry without a registration would render
// nothing). This guard catches both directions of drift in dev builds.
//
// R2 Option D (2026-06-18): extracted into a reusable helper so CUSTOM
// registries built by embedding hosts (SpaarkeAi) get the same drift checks
// as the default registry. Called from inside the factory above.
// ---------------------------------------------------------------------------
function runRegistryDevGuards(
  registry: readonly SectionRegistration[],
): void {
  // (a) Duplicate IDs across registrations.
  const seen = new Set<string>();
  for (const reg of registry) {
    if (seen.has(reg.id)) {
      console.error(
        `[sectionRegistry] Duplicate section ID detected: "${reg.id}". ` +
          "Each registration must have a unique ID.",
      );
    }
    seen.add(reg.id);
  }

  // (b) Registry vs metadata catalog drift.
  const registryIds = new Set(registry.map((r) => r.id));
  const metadataIds = new Set(SECTION_METADATA_CATALOG.map((m) => m.id));

  for (const id of registryIds) {
    if (!metadataIds.has(id)) {
      console.error(
        `[sectionRegistry] Section "${id}" is in SECTION_REGISTRY but missing ` +
          "from SECTION_METADATA_CATALOG in @spaarke/ui-components. " +
          "Add a matching entry to sectionMetadataCatalog.ts so the wizard " +
          "picker includes it.",
      );
    }
  }
  for (const id of metadataIds) {
    if (!registryIds.has(id)) {
      console.error(
        `[sectionRegistry] Section "${id}" is in SECTION_METADATA_CATALOG ` +
          "but has no matching SECTION_REGISTRY entry. " +
          "Either add a registration in sections/ or remove the metadata entry.",
      );
    }
  }
}

/**
 * The default LegalWorkspace section registry — built once with no overrides.
 * Standalone LegalWorkspace imports this directly; embedding consumers
 * (SpaarkeAi) build their own via `createLegalWorkspaceSectionRegistry({...})`.
 *
 * Pre-R2 Option D: this was a literal `as const` array.
 * Post-R2 Option D: this is the no-options factory call — byte-identical
 * behavior preserved (FR-25 / NFR-10).
 */
export const SECTION_REGISTRY: readonly SectionRegistration[] =
  createLegalWorkspaceSectionRegistry();

// ---------------------------------------------------------------------------
// Lookup helpers (continue to operate on the default SECTION_REGISTRY)
// ---------------------------------------------------------------------------

/** Look up a section registration by ID. Returns undefined if not found. */
export function getSectionById(id: string): SectionRegistration | undefined {
  return SECTION_REGISTRY.find((s) => s.id === id);
}

/** Return all section registrations matching a given category. */
export function getSectionsByCategory(
  category: SectionCategory,
): SectionRegistration[] {
  return SECTION_REGISTRY.filter((s) => s.category === category);
}
