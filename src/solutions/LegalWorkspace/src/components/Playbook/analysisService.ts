/**
 * Analysis Service — creates sprk_analysis records via the BFF API.
 *
 * Re-exports from the shared library. LegalWorkspace consumers should
 * import from this file to maintain consistent module boundaries within
 * the solution, while the implementation lives in @spaarke/ui-components.
 *
 * @see ADR-013 — AI features call BFF API, not Dataverse directly from browser.
 */

export {
  createAnalysis,
  associateScopes,
  createAndAssociate,
} from '@spaarke/ui-components/components/Playbook';
