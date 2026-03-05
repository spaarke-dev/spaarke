/**
 * Analysis Service — creates sprk_analysis records and associates N:N scope relationships.
 *
 * Used by the Playbook Analysis Launcher code page (standalone dialog, React 18).
 * Since this is a code page (not PCF), webApi is typed as `any` and the global
 * Xrm object is used directly for N:N associate operations.
 */

// Global Xrm type declaration — available in Dataverse code page context
declare const Xrm: any;

import { IAnalysisConfig, ENTITY_NAMES, RELATIONSHIP_NAMES } from "./types";

// ---------------------------------------------------------------------------
// createAnalysis
// ---------------------------------------------------------------------------

/**
 * Creates an sprk_analysis record in Dataverse using @odata.bind lookup syntax.
 *
 * @param webApi - Xrm.WebApi or compatible interface (typed as `any` for code page context)
 * @param config - Analysis configuration containing document, action, playbook, and scope IDs
 * @returns The GUID of the newly created sprk_analysis record
 */
export async function createAnalysis(webApi: any, config: IAnalysisConfig): Promise<string> {
    const { documentId, documentName, actionId, playbookId } = config;

    const analysisRecord: Record<string, unknown> = {
        sprk_name: `Analysis - ${documentName || "Document"}`,
        "sprk_documentid@odata.bind": `/sprk_documents(${documentId})`,
        "sprk_actionid@odata.bind": `/sprk_analysisactions(${actionId})`,
    };

    // Playbook lookup is optional
    if (playbookId) {
        analysisRecord["sprk_Playbook@odata.bind"] = `/sprk_analysisplaybooks(${playbookId})`;
    }

    const result = await webApi.createRecord(ENTITY_NAMES.analysis, analysisRecord);
    return result.id as string;
}

// ---------------------------------------------------------------------------
// associateScopes
// ---------------------------------------------------------------------------

/**
 * Associates N:N scope relationships for an sprk_analysis record.
 *
 * Uses Xrm.WebApi.online.execute with operationType: 2 (Associate).
 * Failures for individual associations are caught and re-thrown as an
 * aggregated error so callers can decide whether to surface partial failures.
 *
 * @param analysisId - GUID of the sprk_analysis record to associate scope items with
 * @param skillIds   - GUIDs of sprk_analysisskill records to associate
 * @param knowledgeIds - GUIDs of sprk_analysisknowledge records to associate
 * @param toolIds    - GUIDs of sprk_analysistool records to associate
 */
export async function associateScopes(
    analysisId: string,
    skillIds: string[],
    knowledgeIds: string[],
    toolIds: string[]
): Promise<void> {
    const errors: string[] = [];

    // Helper that performs a single N:N associate call
    async function associate(entityType: string, entityId: string, relationship: string): Promise<void> {
        await Xrm.WebApi.online.execute({
            getMetadata: () => ({
                boundParameter: undefined,
                parameterTypes: {},
                operationType: 2, // Associate
                operationName: "Associate",
            }),
            target: { entityType: ENTITY_NAMES.analysis, id: analysisId },
            relatedEntities: [{ entityType, id: entityId }],
            relationship,
        });
    }

    // Skills (N:N: sprk_analysis_skill)
    for (const skillId of skillIds) {
        try {
            await associate(ENTITY_NAMES.skill, skillId, RELATIONSHIP_NAMES.analysisSkill);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            errors.push(`skill ${skillId}: ${msg}`);
        }
    }

    // Knowledge (N:N: sprk_analysis_knowledge)
    for (const knowledgeId of knowledgeIds) {
        try {
            await associate(ENTITY_NAMES.knowledge, knowledgeId, RELATIONSHIP_NAMES.analysisKnowledge);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            errors.push(`knowledge ${knowledgeId}: ${msg}`);
        }
    }

    // Tools (N:N: sprk_analysis_tool)
    for (const toolId of toolIds) {
        try {
            await associate(ENTITY_NAMES.tool, toolId, RELATIONSHIP_NAMES.analysisTool);
        } catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            errors.push(`tool ${toolId}: ${msg}`);
        }
    }

    if (errors.length > 0) {
        throw new Error(`Failed to associate scope items:\n${errors.join("\n")}`);
    }
}

// ---------------------------------------------------------------------------
// createAndAssociate
// ---------------------------------------------------------------------------

/**
 * Orchestrates analysis record creation followed by N:N scope associations.
 *
 * This is the primary entry point for consumers. It:
 * 1. Creates the sprk_analysis record via createAnalysis()
 * 2. Associates all scope items (skills, knowledge, tools) via associateScopes()
 *
 * @param webApi - Xrm.WebApi or compatible interface (typed as `any` for code page context)
 * @param config - Full analysis configuration including scope IDs
 * @returns The GUID of the newly created sprk_analysis record
 */
export async function createAndAssociate(webApi: any, config: IAnalysisConfig): Promise<string> {
    const analysisId = await createAnalysis(webApi, config);

    await associateScopes(
        analysisId,
        config.skillIds,
        config.knowledgeIds,
        config.toolIds
    );

    return analysisId;
}
