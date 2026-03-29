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
import { getStartedRegistration } from "./sections/getStarted.registration";
import { quickSummaryRegistration } from "./sections/quickSummary.registration";
import { latestUpdatesRegistration } from "./sections/latestUpdates.registration";
import { todoRegistration } from "./sections/todo.registration";
import { documentsRegistration } from "./sections/documents.registration";

/** All available workspace sections in default display order. */
export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
] as const;

// ---------------------------------------------------------------------------
// Development guard: detect duplicate section IDs at module load time.
// ---------------------------------------------------------------------------
if (process.env.NODE_ENV !== "production") {
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
