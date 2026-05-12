/**
 * playbookService.ts
 *
 * Loads playbook-related entities from Dataverse WebAPI.
 * Ported from AnalysisBuilder PCF (AnalysisBuilderApp.tsx lines 182-377).
 *
 * This service is used by the Analysis Launcher code page (not a PCF control),
 * so the webApi parameter is typed as `any` rather than
 * ComponentFramework.WebApi.
 */
import { IPlaybook, IAction, ISkill, IKnowledge, ITool, IPlaybookScopes } from './types';
/**
 * Load all active playbooks from sprk_analysisplaybook.
 *
 * @param webApi - Dataverse WebAPI accessor (Xrm.WebApi or equivalent).
 * @returns Resolved array of IPlaybook records.
 */
export declare function loadPlaybooks(webApi: any): Promise<IPlaybook[]>;
/**
 * Load all active actions from sprk_analysisaction.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of IAction records.
 */
export declare function loadActions(webApi: any): Promise<IAction[]>;
/**
 * Load all active skills from sprk_analysisskill.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of ISkill records.
 */
export declare function loadSkills(webApi: any): Promise<ISkill[]>;
/**
 * Load all active knowledge items from sprk_analysisknowledge.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of IKnowledge records.
 */
export declare function loadKnowledge(webApi: any): Promise<IKnowledge[]>;
/**
 * Load all active tools from sprk_analysistool.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved array of ITool records.
 */
export declare function loadTools(webApi: any): Promise<ITool[]>;
/**
 * Load the scope IDs (skills, knowledge, tools, actions) linked to a specific
 * playbook via its N:N relationships.
 *
 * Uses a single retrieveRecord call with $expand so all four relationship sets
 * are fetched in one round-trip — matching the pattern in AnalysisBuilderApp.
 *
 * @param webApi     - Dataverse WebAPI accessor.
 * @param playbookId - GUID of the sprk_analysisplaybook record.
 * @returns Resolved IPlaybookScopes containing arrays of related entity IDs.
 */
export declare function loadPlaybookScopes(webApi: any, playbookId: string): Promise<IPlaybookScopes>;
/** Result shape returned by loadAllData. */
export interface IPlaybookData {
    playbooks: IPlaybook[];
    actions: IAction[];
    skills: ISkill[];
    knowledge: IKnowledge[];
    tools: ITool[];
}
/**
 * Load all five entity types in parallel.
 *
 * All five requests are dispatched concurrently via Promise.all so total load
 * time is bounded by the slowest individual query rather than the sum.
 *
 * @param webApi - Dataverse WebAPI accessor.
 * @returns Resolved IPlaybookData containing all entity arrays.
 */
export declare function loadAllData(webApi: any): Promise<IPlaybookData>;
//# sourceMappingURL=playbookService.d.ts.map