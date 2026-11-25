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

// ---------------------------------------------------------------------------
// Request / Response types for POST /api/ai/analysis/create
// ---------------------------------------------------------------------------

interface ICreateAnalysisRequest {
  name: string;
  documentId: string;
  actionId?: string;
  playbookId?: string;
  skillIds: string[];
  knowledgeIds: string[];
  toolIds: string[];
}

interface ICreateAnalysisResponse {
  analysisId: string;
  /**
   * Optional non-fatal notes from the server (field-mapping-server-write-path-r1, task 043): e.g. no fields were copied
   * from the document. The analysis exists; the user is told. A body with no `warnings` member means there is nothing
   * to tell.
   */
  warnings?: unknown;
}

/** What {@link createAndAssociateWithWarnings} returns: the new analysis and the server's non-fatal notes. */
export interface ICreateAnalysisResult {
  analysisId: string;
  /** The server's `warnings` (strings only, empty ones dropped); empty when the 201 body carried none. */
  warnings: string[];
}

// ---------------------------------------------------------------------------
// createAndAssociate — primary entry point for consumers
// ---------------------------------------------------------------------------

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
export async function createAndAssociate(
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string,
  config: IAnalysisConfig
): Promise<string> {
  return (await createAndAssociateWithWarnings(authenticatedFetch, bffBaseUrl, config)).analysisId;
}

/**
 * {@link createAndAssociate}, also returning the optional `warnings` array of the 201 body, so the caller can show the
 * user what the server could not do (task 043: no fields were copied from the document). Same request, same failures.
 */
export async function createAndAssociateWithWarnings(
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string,
  config: IAnalysisConfig
): Promise<ICreateAnalysisResult> {
  const { documentId, documentName, actionId, playbookId, skillIds, knowledgeIds, toolIds } = config;

  const body: ICreateAnalysisRequest = {
    name: `Analysis - ${documentName || 'Document'}`,
    documentId,
    skillIds,
    knowledgeIds,
    toolIds,
  };

  if (actionId) {
    body.actionId = actionId;
  }

  if (playbookId) {
    body.playbookId = playbookId;
  }

  const url = `${bffBaseUrl.replace(/\/$/, '')}/api/ai/analysis/create`;

  const response = await authenticatedFetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const errorText = await response.text().catch(() => response.statusText);
    throw new Error(`Failed to create analysis: ${response.status} ${errorText}`);
  }

  const data: ICreateAnalysisResponse = await response.json();

  if (!data.analysisId) {
    throw new Error('BFF response missing analysisId');
  }

  const serverWarnings = data.warnings;
  const warnings = Array.isArray(serverWarnings)
    ? serverWarnings.filter((w): w is string => typeof w === 'string' && w.trim().length > 0)
    : [];

  return { analysisId: data.analysisId, warnings };
}

// ---------------------------------------------------------------------------
// createAnalysis — low-level helper (BFF, no scope association)
// ---------------------------------------------------------------------------

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
export async function createAnalysis(
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string,
  config: IAnalysisConfig
): Promise<string> {
  return createAndAssociate(authenticatedFetch, bffBaseUrl, {
    ...config,
    skillIds: [],
    knowledgeIds: [],
    toolIds: [],
  });
}

// ---------------------------------------------------------------------------
// associateScopes — stub (association now handled server-side)
// ---------------------------------------------------------------------------

/**
 * @deprecated Scope associations are now handled server-side by
 * POST /api/ai/analysis/create. This function is retained for API compatibility
 * but is a no-op. Use createAndAssociate() which performs creation and
 * association atomically via the BFF.
 */
export async function associateScopes(
  _authenticatedFetch: AuthenticatedFetchFn,
  _bffBaseUrl: string,
  _analysisId: string,
  _skillIds: string[],
  _knowledgeIds: string[],
  _toolIds: string[]
): Promise<void> {
  // No-op: association is now performed atomically by the server in
  // POST /api/ai/analysis/create. Callers that previously called
  // createAnalysis() + associateScopes() should migrate to createAndAssociate().
}
