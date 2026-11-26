/**
 * discoverMemoNavProps — the nav-prop entries of a memo entity (`sprk_memo`)
 * for `PolymorphicResolverService.applyResolverFields()`.
 *
 * A thin delegate to the shared `discoverNavProps` in `@spaarke/ui-components`
 * (#2909, ADR-012 reuse). This file used to carry its own copy of the metadata
 * query and its own cache; the shared function is the one implementation, so
 * Notepad now shares its cache with every other writer on the page and gets
 * its fixes: the logical name is lowercased for the URL and the cache key
 * (SRFR-050 — a wrong-case name returns HTTP 200 with an empty list), and an
 * empty result is warned about. It still never throws; `[]` on failure.
 *
 * The name is kept because `useSprkMemoRepository` and its tests import it.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts
 */

// DEF-10 (2026-07-04) — deep-path import to skip the services barrel (which
// re-exports `EntityCreationService` → `mammoth` docx parser, ~300 KB).
import {
  discoverNavProps,
  _resetNavPropCacheForTests,
  type INavPropEntry,
} from '@spaarke/ui-components/services/PolymorphicResolverService';

/**
 * Reset the shared nav-prop cache. Test-only surface — production code should
 * never need this.
 */
export function _resetMemoNavPropCacheForTests(): void {
  _resetNavPropCacheForTests();
}

/**
 * Discover the ManyToOne nav-prop entries for the given entity (cached per
 * entity for the page lifetime by the shared function).
 *
 * @param entityLogicalName - e.g. `"sprk_memo"`.
 * @returns Array of `INavPropEntry` for use with `applyResolverFields`. Empty
 *          on any error (logged; not thrown).
 */
export function discoverMemoNavProps(entityLogicalName: string): Promise<INavPropEntry[]> {
  return discoverNavProps(entityLogicalName);
}
