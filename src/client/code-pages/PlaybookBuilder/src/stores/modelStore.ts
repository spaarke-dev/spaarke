/**
 * Model Store — AI Model Deployments from Dataverse.
 *
 * Zustand v5 store that loads AI model deployments from
 * sprk_aimodeldeployments via DataverseClient.
 * Replaces all mock data from the R4 PCF version.
 *
 * @see spec.md Section 8.2 — Scope Tables
 */

import { create } from "zustand";
import { retrieveMultipleRecords } from "../services/dataverseClient";
import type { DataverseRecord } from "../services/dataverseClient";
import type { AiModelDeployment, AiCapability } from "../types/scopeTypes";

const LOG_PREFIX = "[PlaybookBuilder:ModelStore]";

// ---------------------------------------------------------------------------
// Dataverse → typed record mapper
// ---------------------------------------------------------------------------

function mapModelDeployment(record: DataverseRecord): AiModelDeployment {
    return {
        id: (record["sprk_aimodeldeploymentid"] as string) ?? "",
        name: (record["sprk_name"] as string) ?? "",
        provider: (record["sprk_provider"] as string) ?? "",
        capability: (record["sprk_capability"] as string) ?? "",
        modelId: (record["sprk_modelid"] as string) ?? "",
        contextWindow: (record["sprk_contextwindow"] as number) ?? 0,
        isActive: (record["sprk_isactive"] as boolean) ?? false,
        description: (record["sprk_description"] as string) ?? undefined,
    };
}

// ---------------------------------------------------------------------------
// Store State
// ---------------------------------------------------------------------------

interface ModelStoreState {
    // Data
    models: AiModelDeployment[];

    // Loading state
    isLoading: boolean;

    // Error state
    error: string | null;

    // Actions
    loadModelDeployments: () => Promise<void>;

    // Selectors
    getActiveModels: () => AiModelDeployment[];
    getModelsByCapability: (capability: AiCapability) => AiModelDeployment[];
    getChatModels: () => AiModelDeployment[];
    getModelById: (id: string) => AiModelDeployment | undefined;
    getDefaultModel: () => AiModelDeployment | undefined;
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useModelStore = create<ModelStoreState>()((set, get) => ({
    // Initial data — empty, no mock data
    models: [],

    // Loading state
    isLoading: false,

    // Error state
    error: null,

    // ----- Load Model Deployments from sprk_aimodeldeployments -----
    loadModelDeployments: async () => {
        set({ isLoading: true, error: null });
        try {
            const result = await retrieveMultipleRecords(
                "sprk_aimodeldeployments",
                "$select=sprk_aimodeldeploymentid,sprk_name,sprk_provider,sprk_capability,sprk_modelid,sprk_contextwindow,sprk_isactive,sprk_description&$filter=statecode eq 0&$orderby=sprk_name",
            );
            const models = result.entities.map(mapModelDeployment);
            set({ models, isLoading: false });
            console.info(`${LOG_PREFIX} Loaded ${models.length} model deployments from Dataverse`);
        } catch (err) {
            const message = err instanceof Error ? err.message : "Failed to load model deployments";
            console.error(`${LOG_PREFIX} ${message}`, err);
            set({ isLoading: false, error: message });
        }
    },

    // ----- Selectors -----

    getActiveModels: (): AiModelDeployment[] => {
        return get().models.filter((m) => m.isActive);
    },

    getModelsByCapability: (capability: AiCapability): AiModelDeployment[] => {
        return get().models.filter((m) => m.isActive && m.capability === capability);
    },

    getChatModels: (): AiModelDeployment[] => {
        return get().models.filter((m) => m.isActive && m.capability === "Chat");
    },

    getModelById: (id: string): AiModelDeployment | undefined => {
        return get().models.find((m) => m.id === id);
    },

    getDefaultModel: (): AiModelDeployment | undefined => {
        const chatModels = get().getChatModels();
        // Return the first active chat model as default
        return chatModels.length > 0 ? chatModels[0] : undefined;
    },
}));
