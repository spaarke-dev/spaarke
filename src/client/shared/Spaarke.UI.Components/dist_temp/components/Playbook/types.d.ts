/**
 * Shared Playbook type definitions.
 *
 * These types are used by both the Analysis Builder code page and the
 * Quick Start Playbook Wizards. They map directly to existing Dataverse
 * entities — no schema changes required.
 */
/** sprk_analysisplaybook */
export interface IPlaybook {
    id: string;
    name: string;
    description: string;
    icon?: string;
    category?: string;
    isDefault?: boolean;
}
/** Base interface for scope items (shared across action, skill, knowledge, tool). */
export interface IScopeItem {
    id: string;
    name: string;
    description?: string;
    icon?: string;
}
/** sprk_analysisaction — single-select scope item. */
export interface IAction extends IScopeItem {
    promptTemplate?: string;
}
/** sprk_analysisskill — multi-select scope item. */
export interface ISkill extends IScopeItem {
    type?: 'extraction' | 'analysis' | 'generation' | 'transformation';
}
/** sprk_analysisknowledge — multi-select scope item. */
export interface IKnowledge extends IScopeItem {
    source?: 'dataverse' | 'sharepoint' | 'external';
}
/** sprk_analysistool — multi-select scope item. */
export interface ITool extends IScopeItem {
    toolType?: 'search' | 'calculation' | 'api' | 'workflow';
}
/** Resolved scope IDs from a playbook's N:N relationships. */
export interface IPlaybookScopes {
    actionIds: string[];
    skillIds: string[];
    knowledgeIds: string[];
    toolIds: string[];
}
/** Tab identifiers for scope configuration UI. */
export type ScopeTabId = 'action' | 'skills' | 'knowledge' | 'tools';
/**
 * Configuration for creating an sprk_analysis record.
 * Passed to AnalysisService.createAnalysis().
 */
export interface IAnalysisConfig {
    /** sprk_documentid lookup — GUID of source document. */
    documentId: string;
    /** Display name for the analysis record. */
    documentName?: string;
    /** SPE container ID (for file access). */
    containerId?: string;
    /** SPE file ID (for file access). */
    fileId?: string;
    /** Optional sprk_Playbook lookup — GUID of selected playbook. */
    playbookId?: string;
    /** sprk_actionid lookup — GUID of selected action (required). */
    actionId: string;
    /** N:N sprk_analysis_skill — GUIDs of selected skills. */
    skillIds: string[];
    /** N:N sprk_analysis_knowledge — GUIDs of selected knowledge. */
    knowledgeIds: string[];
    /** N:N sprk_analysis_tool — GUIDs of selected tools. */
    toolIds: string[];
}
export interface IFollowUpAction {
    id: string;
    label: string;
    description: string;
    icon: string;
    onClick: (analysisId: string) => void;
}
export declare const ENTITY_NAMES: {
    readonly playbook: "sprk_analysisplaybook";
    readonly action: "sprk_analysisaction";
    readonly skill: "sprk_analysisskill";
    readonly knowledge: "sprk_analysisknowledge";
    readonly tool: "sprk_analysistool";
    readonly analysis: "sprk_analysis";
    readonly document: "sprk_document";
};
export declare const RELATIONSHIP_NAMES: {
    /** Playbook → Skills (N:N) */
    readonly playbookSkill: "sprk_playbook_skill";
    /** Playbook → Knowledge (N:N) */
    readonly playbookKnowledge: "sprk_playbook_knowledge";
    /** Playbook → Tools (N:N) */
    readonly playbookTool: "sprk_playbook_tool";
    /** Playbook → Actions (N:N) — note different naming pattern */
    readonly playbookAction: "sprk_analysisplaybook_action";
    /** Analysis → Skills (N:N) */
    readonly analysisSkill: "sprk_analysis_skill";
    /** Analysis → Knowledge (N:N) */
    readonly analysisKnowledge: "sprk_analysis_knowledge";
    /** Analysis → Tools (N:N) */
    readonly analysisTool: "sprk_analysis_tool";
};
export declare const ID_FIELDS: {
    readonly playbook: "sprk_analysisplaybookid";
    readonly action: "sprk_analysisactionid";
    readonly skill: "sprk_analysisskillid";
    readonly knowledge: "sprk_analysisknowledgeid";
    readonly tool: "sprk_analysistoolid";
    readonly analysis: "sprk_analysisid";
};
//# sourceMappingURL=types.d.ts.map