/**
 * Scope Types — Type definitions for scope resolution data.
 *
 * Maps Dataverse scope table records to typed interfaces used by
 * scopeStore, modelStore, and ActionSelector.
 */

// ---------------------------------------------------------------------------
// Analysis Skill (sprk_analysisskills)
// ---------------------------------------------------------------------------

export interface AnalysisSkill {
    id: string;
    name: string;
    description: string;
    category: string;
}

// ---------------------------------------------------------------------------
// AI Knowledge (sprk_aiknowledges)
// ---------------------------------------------------------------------------

export interface AiKnowledge {
    id: string;
    name: string;
    description: string;
    sourceType: string;
}

// ---------------------------------------------------------------------------
// Analysis Tool (sprk_analysistools)
// ---------------------------------------------------------------------------

export interface AnalysisTool {
    id: string;
    name: string;
    description: string;
    handlerType: string;
}

// ---------------------------------------------------------------------------
// Analysis Action (sprk_analysisactions)
// ---------------------------------------------------------------------------

export interface AnalysisAction {
    id: string;
    name: string;
    description: string;
}

// ---------------------------------------------------------------------------
// AI Model Deployment (sprk_aimodeldeployments)
// ---------------------------------------------------------------------------

export type AiProvider = "AzureOpenAI" | "OpenAI" | "Anthropic";
export type AiCapability = "Chat" | "Completion" | "Embedding";

export interface AiModelDeployment {
    id: string;
    name: string;
    provider: string;
    capability: string;
    modelId: string;
    contextWindow: number;
    isActive: boolean;
    description?: string;
}

// ---------------------------------------------------------------------------
// Action Type Capabilities (which node types support which scope items)
// ---------------------------------------------------------------------------

export interface ActionTypeCapabilities {
    allowsSkills: boolean;
    allowsKnowledge: boolean;
    allowsTools: boolean;
}
