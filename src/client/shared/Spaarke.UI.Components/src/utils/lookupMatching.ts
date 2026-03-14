/**
 * lookupMatching.ts
 * Fuzzy-match an AI-generated display name against Dataverse lookup results.
 *
 * Used by the AI pre-fill flow to resolve AI-returned display names
 * (e.g. "Patent", "Jane Smith") to Dataverse lookup IDs so that
 * LookupField can render them as selected chips.
 *
 * @example
 * ```typescript
 * import { findBestLookupMatch } from '@spaarke/ui-components/utils/lookupMatching';
 *
 * const results = await searchMatterTypes(webApi, "Patent");
 * const best = findBestLookupMatch("Patent", results);
 * if (best) {
 *   // best.id = Dataverse GUID, best.name = display name
 * }
 * ```
 */

import type { ILookupItem } from '../types/LookupTypes';

/**
 * Fuzzy-match an AI-generated display name against Dataverse lookup results.
 *
 * Scoring (highest wins, minimum 0.4 to accept):
 *   1.0  — exact match (case-insensitive)
 *   0.8  — one string starts with the other ("Corporate" <-> "Corporate Law")
 *   0.7  — one string is contained in the other ("Trans" in "Transactional")
 *   0.5  — single result from Dataverse contains() filter (already relevant)
 *
 * Returns null if no candidate scores above threshold.
 *
 * @param aiValue - The display name returned by AI (e.g. "Patent", "Jane Smith")
 * @param candidates - Dataverse lookup results to match against
 * @returns The best matching lookup item, or null if no match above threshold
 */
export function findBestLookupMatch(aiValue: string, candidates: ILookupItem[]): ILookupItem | null {
  if (candidates.length === 0) return null;

  const aiLower = aiValue.toLowerCase().trim();

  let bestScore = 0;
  let bestItem: ILookupItem | null = null;

  for (const item of candidates) {
    const dbLower = item.name.toLowerCase().trim();
    let score = 0;

    if (dbLower === aiLower) {
      score = 1.0;
    } else if (dbLower.startsWith(aiLower) || aiLower.startsWith(dbLower)) {
      score = 0.8;
    } else if (dbLower.includes(aiLower) || aiLower.includes(dbLower)) {
      score = 0.7;
    }

    if (score > bestScore) {
      bestScore = score;
      bestItem = item;
    }
  }

  // If no strong match but Dataverse contains() returned exactly one result,
  // trust it — the server-side filter already validated relevance.
  if (bestScore < 0.4 && candidates.length === 1) {
    bestScore = 0.5;
    bestItem = candidates[0];
  }

  return bestScore >= 0.4 ? bestItem : null;
}
