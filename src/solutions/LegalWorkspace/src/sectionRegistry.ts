/**
 * sectionRegistry.ts — Aggregated registry of all workspace section registrations.
 *
 * Single import point for all section metadata. The dynamic config builder
 * (Phase 4) consumes SECTION_REGISTRY to render available sections.
 *
 * Adding a new section requires only:
 *   1. Create a new {name}.registration.ts in sections/
 *   2. Import and add it to SECTION_REGISTRY below
 *
 * Standards: ADR-012 (shared components)
 */

import type { SectionRegistration, SectionCategory } from "@spaarke/ui-components";
import { SECTION_METADATA_CATALOG } from "@spaarke/ui-components";
import { getStartedRegistration } from "./sections/getStarted.registration";
import { quickSummaryRegistration } from "./sections/quickSummary.registration";
import { latestUpdatesRegistration } from "./sections/latestUpdates.registration";
import { todoRegistration } from "./sections/todo.registration";
import { documentsRegistration } from "./sections/documents.registration";
import { dailyBriefingRegistration } from "./sections/dailyBriefing/dailyBriefing.registration";
import { calendarRegistration } from "./sections/calendar.registration";

/** All available workspace sections in default display order. */
export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,
  calendarRegistration,
] as const;

// ---------------------------------------------------------------------------
// Development guard: detect duplicate section IDs + drift vs the shared
// SECTION_METADATA_CATALOG (the single source of truth for wizard pickability).
//
// R4 W-3 (task 040, 2026-05-26): the wizard's picker reads
// `SECTION_METADATA_CATALOG` from `@spaarke/ui-components`. Any new section
// added HERE without a matching metadata entry would not appear in the wizard
// (and vice versa: a metadata entry without a registration would render
// nothing). This guard catches both directions of drift in dev builds.
// ---------------------------------------------------------------------------
if (process.env.NODE_ENV !== "production") {
  // (a) Duplicate IDs across registrations.
  const seen = new Set<string>();
  for (const reg of SECTION_REGISTRY) {
    if (seen.has(reg.id)) {
      console.error(
        `[sectionRegistry] Duplicate section ID detected: "${reg.id}". ` +
          "Each registration must have a unique ID.",
      );
    }
    seen.add(reg.id);
  }

  // (b) Registry vs metadata catalog drift.
  const registryIds = new Set(SECTION_REGISTRY.map((r) => r.id));
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

// ---------------------------------------------------------------------------
// Lookup helpers
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
