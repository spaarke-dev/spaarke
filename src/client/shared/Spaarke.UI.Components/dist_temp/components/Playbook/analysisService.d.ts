/**
 * Analysis Service — creates sprk_analysis records via the BFF API.
 *
 * Used by the Playbook Library and Analysis Builder code pages (standalone
 * dialogs, React 18). All Dataverse writes are routed through the BFF API
 * using MSAL-authenticated fetch so this works correctly from Code Page
 * iframes where Xrm.WebApi.online.execute is unavailable.
 *
 * @see ADR-013 — AI features call BFF API, not Dataverse directly from browser.
 */
import type { IAnalysisConfig } from './types';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
export type { AuthenticatedFetchFn };
/**
 * Creates an sprk_analysis record via the BFF API, including N:N scope
 * associations (skills, knowledge, tools). This is a single atomic call to
 * POST /api/ai/analysis/create — the server handles all Dataverse writes.
 *
 * @param authenticatedFetch - MSAL-authenticated fetch function (injected from Code Page)
 * @param bffBaseUrl         - Base URL of the BFF API (e.g. "https://spe-api-dev.azurewebsites.net")
 * @param config             - Full analysis configuration including scope IDs
 * @returns The GUID of the newly created sprk_analysis record
 */
export declare function createAndAssociate(authenticatedFetch: AuthenticatedFetchFn, bffBaseUrl: string, config: IAnalysisConfig): Promise<string>;
/**
 * Creates an sprk_analysis record via the BFF API without scope associations.
 *
 * Most callers should use createAndAssociate() instead, which creates the
 * record and associates all scopes in a single server-side transaction.
 *
 * @param authenticatedFetch - MSAL-authenticated fetch function
 * @param bffBaseUrl         - Base URL of the BFF API
 * @param config             - Analysis configuration (skillIds/knowledgeIds/toolIds are ignored)
 * @returns The GUID of the newly created sprk_analysis record
 */
export declare function createAnalysis(authenticatedFetch: AuthenticatedFetchFn, bffBaseUrl: string, config: IAnalysisConfig): Promise<string>;
/**
 * @deprecated Scope associations are now handled server-side by
 * POST /api/ai/analysis/create. This function is retained for API compatibility
 * but is a no-op. Use createAndAssociate() which performs creation and
 * association atomically via the BFF.
 */
export declare function associateScopes(_authenticatedFetch: AuthenticatedFetchFn, _bffBaseUrl: string, _analysisId: string, _skillIds: string[], _knowledgeIds: string[], _toolIds: string[]): Promise<void>;
//# sourceMappingURL=analysisService.d.ts.map