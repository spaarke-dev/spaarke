/**
 * statusDerivation.ts
 *
 * Pure, stateless functions for deriving computed status values from
 * Dataverse entity field data. No side effects, no external dependencies.
 *
 * Status derivation is centralised here so the same logic applies
 * consistently across the MyPortfolio widget, the Updates Feed, and any
 * future consumers that need to classify matters.
 */

import { MatterStatus, GradeLevel } from '../types/enums';
import { IMatter } from '../types/entities';

// ---------------------------------------------------------------------------
// Matter status derivation
// ---------------------------------------------------------------------------

/**
 * Derive the visual health status of a matter from its key metrics.
 *
 * Rules (ordered — first match wins):
 *   Critical — any overdue events OR budget utilisation > 85%
 *   Warning  — budget utilisation > 65%
 *   OnTrack  — all other cases
 *
 * This is a pure function: given the same inputs it always returns the same
 * output and has no side-effects.
 *
 * @param matter - The IMatter entity record from Xrm.WebApi
 * @returns      The derived MatterStatus value
 */
export function deriveMatterStatus(matter: IMatter): MatterStatus {
  if (matter.sprk_overdueeventcount > 0 || matter.sprk_utilizationpercent > 85) {
    return 'Critical';
  }
  if (matter.sprk_utilizationpercent > 65) {
    return 'Warning';
  }
  return 'OnTrack';
}

// ---------------------------------------------------------------------------
// Grade helpers
// ---------------------------------------------------------------------------

/**
 * Validate that a raw string value is a valid GradeLevel (A–F).
 * Returns the grade if valid, undefined otherwise.
 *
 * @param raw - Raw grade string (may be undefined or an unexpected value)
 * @returns    GradeLevel if valid, undefined if not
 */
export function parseGradeLevel(raw: string | undefined): GradeLevel | undefined {
  if (!raw) return undefined;
  const upper = raw.toUpperCase();
  if (upper === 'A' || upper === 'B' || upper === 'C' || upper === 'D' || upper === 'F') {
    return upper as GradeLevel;
  }
  return undefined;
}

/**
 * Extract and validate all three grade fields from an IMatter entity.
 * Returns undefined for any grade that is missing or invalid.
 *
 * @param matter - The IMatter entity
 * @returns       Object with the three optional validated grade values
 */
export function extractMatterGrades(matter: IMatter): {
  budgetControlsGrade: GradeLevel | undefined;
  guidelinesComplianceGrade: GradeLevel | undefined;
  outcomesSuccessGrade: GradeLevel | undefined;
} {
  return {
    budgetControlsGrade: parseGradeLevel(matter.sprk_budgetcontrols_grade),
    guidelinesComplianceGrade: parseGradeLevel(matter.sprk_guidelinescompliance_grade),
    outcomesSuccessGrade: parseGradeLevel(matter.sprk_outcomessuccess_grade),
  };
}

/**
 * Return true when the matter has at least one overdue event.
 * Convenience wrapper that reads the intent from the call site clearly.
 */
export function isMatterOverdue(matter: IMatter): boolean {
  return matter.sprk_overdueeventcount > 0;
}
