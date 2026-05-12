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
export declare function findBestLookupMatch(aiValue: string, candidates: ILookupItem[]): ILookupItem | null;
//# sourceMappingURL=lookupMatching.d.ts.map