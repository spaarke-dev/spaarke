/**
 * Scope Store — Skills, Knowledge, Tools, and Actions from Dataverse.
 *
 * Zustand v5 store that loads scope items from real Dataverse tables
 * via DataverseClient. Replaces all mock data from the R4 PCF version.
 *
 * Tables queried:
 *   - sprk_analysisskills   → Skills multi-select
 *   - sprk_aiknowledges     → Knowledge multi-select
 *   - sprk_analysistools    → Tool single-select
 *   - sprk_analysisactions  → Action single-select (NEW)
 *
 * @see spec.md Section 8.2 — Scope Tables
 */

import { create } from "zustand";
import { retrieveMultipleRecords } from "../services/dataverseClient";
import type { DataverseRecord } from "../services/dataverseClient";
import type {
    AnalysisSkill,
    AiKnowledge,
    AnalysisTool,
    AnalysisAction,
    ActionTypeCapabilities,
} from "../types/scopeTypes";

const LOG_PREFIX = "[PlaybookBuilder:ScopeStore]";

// ---------------------------------------------------------------------------
// Node type capability map (which scope types each node supports)
// ---------------------------------------------------------------------------

type PlaybookNodeType =
    | "aiAnalysis"
    | "aiCompletion"
    | "condition"
    | "deliverOutput"
    | "createTask"
    | "sendEmail"
    | "wait";

const actionTypeCapabilities: Record<PlaybookNodeType, ActionTypeCapabilities> = {
    aiAnalysis: { allowsSkills: true, allowsKnowledge: true, allowsTools: true },
    aiCompletion: { allowsSkills: true, allowsKnowledge: true, allowsTools: true },
    condition: { allowsSkills: false, allowsKnowledge: false, allowsTools: false },
    deliverOutput: { allowsSkills: false, allowsKnowledge: true, allowsTools: true },
    createTask: { allowsSkills: false, allowsKnowledge: false, allowsTools: false },
    sendEmail: { allowsSkills: false, allowsKnowledge: true, allowsTools: false },
    wait: { allowsSkills: false, allowsKnowledge: false, allowsTools: false },
};

// ---------------------------------------------------------------------------
// Dataverse → typed record mappers
// ---------------------------------------------------------------------------

function mapSkill(record: DataverseRecord): AnalysisSkill {
    return {
        id: (record["sprk_analysisskillid"] as string) ?? "",
        name: (record["sprk_name"] as string) ?? "",
        description: (record["sprk_description"] as string) ?? "",
        category: (record["sprk_category"] as string) ?? "",
    };
}

function mapKnowledge(record: DataverseRecord): AiKnowledge {
    return {
        id: (record["sprk_aiknowledgeid"] as string) ?? "",
        name: (record["sprk_name"] as string) ?? "",
        description: (record["sprk_description"] as string) ?? "",
        sourceType: (record["sprk_type"] as string) ?? "",
    };
}

function mapTool(record: DataverseRecord): AnalysisTool {
    return {
        id: (record["sprk_analysistoolid"] as string) ?? "",
        name: (record["sprk_name"] as string) ?? "",
        description: (record["sprk_description"] as string) ?? "",
        handlerType: (record["sprk_handlertype"] as string) ?? "",
    };
}

function mapAction(record: DataverseRecord): AnalysisAction {
    return {
        id: (record["sprk_analysisactionid"] as string) ?? "",
        name: (record["sprk_name"] as string) ?? "",
        description: (record["sprk_description"] as string) ?? "",
    };
}

// ---------------------------------------------------------------------------
// Store State
// ---------------------------------------------------------------------------

interface ScopeStoreState {
    // Data
    skills: AnalysisSkill[];
    knowledge: AiKnowledge[];
    tools: AnalysisTool[];
    actions: AnalysisAction[];

    // Loading states
    isLoadingSkills: boolean;
    isLoadingKnowledge: boolean;
    isLoadingTools: boolean;
    isLoadingActions: boolean;
    isLoading: boolean;

    // Error states
    skillsError: string | null;
    knowledgeError: string | null;
    toolsError: string | null;
    actionsError: string | null;

    // Actions
    loadSkills: () => Promise<void>;
    loadKnowledge: () => Promise<void>;
    loadTools: () => Promise<void>;
    loadActions: () => Promise<void>;
    loadAllScopes: () => Promise<void>;

    // Selectors
    getCapabilities: (nodeType: string) => ActionTypeCapabilities;
    getSkillsByIds: (ids: string[]) => AnalysisSkill[];
    getKnowledgeByIds: (ids: string[]) => AiKnowledge[];
    getToolById: (id: string) => AnalysisTool | undefined;
    getActionById: (id: string) => AnalysisAction | undefined;
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useScopeStore = create<ScopeStoreState>()((set, get) => ({
    // Initial data — empty arrays, no mock data
    skills: [],
    knowledge: [],
    tools: [],
    actions: [],

    // Loading states
    isLoadingSkills: false,
    isLoadingKnowledge: false,
    isLoadingTools: false,
    isLoadingActions: false,
    isLoading: false,

    // Error states
    skillsError: null,
    knowledgeError: null,
    toolsError: null,
    actionsError: null,

    // ----- Load Skills from sprk_analysisskills -----
    loadSkills: async () => {
        set({ isLoadingSkills: true, skillsError: null });
        try {
            const result = await retrieveMultipleRecords(
                "sprk_analysisskills",
                "$select=sprk_analysisskillid,sprk_name,sprk_description,sprk_category&$filter=statecode eq 0&$orderby=sprk_name",
            );
            const skills = result.entities.map(mapSkill);
            set({ skills, isLoadingSkills: false });
            console.info(`${LOG_PREFIX} Loaded ${skills.length} skills from Dataverse`);
        } catch (err) {
            const message = err instanceof Error ? err.message : "Failed to load skills";
            console.error(`${LOG_PREFIX} ${message}`, err);
            set({ isLoadingSkills: false, skillsError: message });
        }
    },

    // ----- Load Knowledge from sprk_aiknowledges -----
    loadKnowledge: async () => {
        set({ isLoadingKnowledge: true, knowledgeError: null });
        try {
            const result = await retrieveMultipleRecords(
                "sprk_aiknowledges",
                "$select=sprk_aiknowledgeid,sprk_name,sprk_description,sprk_type&$filter=statecode eq 0&$orderby=sprk_name",
            );
            const knowledge = result.entities.map(mapKnowledge);
            set({ knowledge, isLoadingKnowledge: false });
            console.info(`${LOG_PREFIX} Loaded ${knowledge.length} knowledge sources from Dataverse`);
        } catch (err) {
            const message = err instanceof Error ? err.message : "Failed to load knowledge";
            console.error(`${LOG_PREFIX} ${message}`, err);
            set({ isLoadingKnowledge: false, knowledgeError: message });
        }
    },

    // ----- Load Tools from sprk_analysistools -----
    loadTools: async () => {
        set({ isLoadingTools: true, toolsError: null });
        try {
            const result = await retrieveMultipleRecords(
                "sprk_analysistools",
                "$select=sprk_analysistoolid,sprk_name,sprk_description,sprk_handlertype&$filter=statecode eq 0&$orderby=sprk_name",
            );
            const tools = result.entities.map(mapTool);
            set({ tools, isLoadingTools: false });
            console.info(`${LOG_PREFIX} Loaded ${tools.length} tools from Dataverse`);
        } catch (err) {
            const message = err instanceof Error ? err.message : "Failed to load tools";
            console.error(`${LOG_PREFIX} ${message}`, err);
            set({ isLoadingTools: false, toolsError: message });
        }
    },

    // ----- Load Actions from sprk_analysisactions -----
    loadActions: async () => {
        set({ isLoadingActions: true, actionsError: null });
        try {
            const result = await retrieveMultipleRecords(
                "sprk_analysisactions",
                "$select=sprk_analysisactionid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name",
            );
            const actions = result.entities.map(mapAction);
            set({ actions, isLoadingActions: false });
            console.info(`${LOG_PREFIX} Loaded ${actions.length} actions from Dataverse`);
        } catch (err) {
            const message = err instanceof Error ? err.message : "Failed to load actions";
            console.error(`${LOG_PREFIX} ${message}`, err);
            set({ isLoadingActions: false, actionsError: message });
        }
    },

    // ----- Load All Scopes in Parallel (Promise.all) -----
    loadAllScopes: async () => {
        set({ isLoading: true });
        const store = get();
        try {
            await Promise.all([
                store.loadSkills(),
                store.loadKnowledge(),
                store.loadTools(),
                store.loadActions(),
            ]);
            console.info(`${LOG_PREFIX} All scope data loaded`);
        } finally {
            set({ isLoading: false });
        }
    },

    // ----- Selectors -----

    getCapabilities: (nodeType: string): ActionTypeCapabilities => {
        return (
            actionTypeCapabilities[nodeType as PlaybookNodeType] ?? {
                allowsSkills: false,
                allowsKnowledge: false,
                allowsTools: false,
            }
        );
    },

    getSkillsByIds: (ids: string[]): AnalysisSkill[] => {
        const { skills } = get();
        return skills.filter((s) => ids.includes(s.id));
    },

    getKnowledgeByIds: (ids: string[]): AiKnowledge[] => {
        const { knowledge } = get();
        return knowledge.filter((k) => ids.includes(k.id));
    },

    getToolById: (id: string): AnalysisTool | undefined => {
        const { tools } = get();
        return tools.find((t) => t.id === id);
    },

    getActionById: (id: string): AnalysisAction | undefined => {
        const { actions } = get();
        return actions.find((a) => a.id === id);
    },
}));
