/**
 * Template Store - Playbook Templates Management
 *
 * Zustand v5 store for managing playbook templates.
 * Provides API calls to list templates and clone playbooks.
 *
 * Migrated from R4 PCF: uses AuthService.getAccessToken() for Bearer auth
 * instead of cookie-based auth. Uses BFF API endpoints.
 *
 * @version 2.0.0 (Code Page migration)
 */

import { create } from "zustand";
import { getAccessToken } from "../services/authService";

// ============================================================================
// Types
// ============================================================================

export interface PlaybookTemplate {
    id: string;
    name: string;
    description?: string;
    outputTypeId?: string;
    isPublic: boolean;
    isTemplate: boolean;
    ownerId: string;
    modifiedOn: string;
}

export interface TemplateListResponse {
    items: PlaybookTemplate[];
    totalCount: number;
    page: number;
    pageSize: number;
    totalPages: number;
    hasNextPage: boolean;
    hasPreviousPage: boolean;
}

export interface ClonedPlaybook {
    id: string;
    name: string;
    description?: string;
}

interface TemplateState {
    // Data
    templates: PlaybookTemplate[];
    isLoading: boolean;
    error: string | null;
    totalCount: number;
    currentPage: number;
    pageSize: number;

    // Cloning state
    isCloning: boolean;
    cloneError: string | null;

    // API base URL
    apiBaseUrl: string;

    // Actions
    setApiBaseUrl: (url: string) => void;
    fetchTemplates: (page?: number, nameFilter?: string) => Promise<void>;
    clonePlaybook: (templateId: string, newName?: string) => Promise<ClonedPlaybook>;
    clearError: () => void;
}

// ============================================================================
// API Client Helper
// ============================================================================

async function fetchWithBearerAuth(url: string, options: RequestInit = {}): Promise<Response> {
    const token = await getAccessToken();

    const response = await fetch(url, {
        ...options,
        headers: {
            "Content-Type": "application/json",
            "Authorization": `Bearer ${token}`,
            ...options.headers,
        },
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || `HTTP ${response.status}: ${response.statusText}`);
    }

    return response;
}

// ============================================================================
// Store
// ============================================================================

export const useTemplateStore = create<TemplateState>((set, get) => ({
    // Initial state
    templates: [],
    isLoading: false,
    error: null,
    totalCount: 0,
    currentPage: 1,
    pageSize: 20,

    isCloning: false,
    cloneError: null,

    apiBaseUrl: "",

    setApiBaseUrl: (url: string) => {
        set({ apiBaseUrl: url.replace(/\/$/, "") });
    },

    fetchTemplates: async (page = 1, nameFilter?: string) => {
        const { apiBaseUrl, pageSize } = get();

        if (!apiBaseUrl) {
            set({ error: "API configuration error" });
            return;
        }

        set({ isLoading: true, error: null });

        try {
            let url = `${apiBaseUrl}/api/ai/playbooks/templates?page=${page}&pageSize=${pageSize}`;
            if (nameFilter) {
                url += `&nameFilter=${encodeURIComponent(nameFilter)}`;
            }

            const response = await fetchWithBearerAuth(url);
            const data: TemplateListResponse = await response.json();

            set({
                templates: data.items,
                totalCount: data.totalCount,
                currentPage: data.page,
                isLoading: false,
            });
        } catch (error) {
            set({
                error: error instanceof Error ? error.message : "Failed to load templates",
                isLoading: false,
            });
        }
    },

    clonePlaybook: async (templateId: string, newName?: string): Promise<ClonedPlaybook> => {
        const { apiBaseUrl } = get();

        if (!apiBaseUrl) {
            throw new Error("API configuration error");
        }

        set({ isCloning: true, cloneError: null });

        try {
            const url = `${apiBaseUrl}/api/ai/playbooks/${templateId}/clone`;
            const body = newName ? JSON.stringify({ newName }) : "{}";

            const response = await fetchWithBearerAuth(url, {
                method: "POST",
                body,
            });

            const clonedPlaybook = await response.json();

            set({ isCloning: false });

            return {
                id: clonedPlaybook.id,
                name: clonedPlaybook.name,
                description: clonedPlaybook.description,
            };
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : "Failed to clone playbook";
            set({ cloneError: errorMessage, isCloning: false });
            throw new Error(errorMessage);
        }
    },

    clearError: () => {
        set({ error: null, cloneError: null });
    },
}));
