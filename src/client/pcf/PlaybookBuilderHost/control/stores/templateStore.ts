/**
 * Template Store - Playbook Templates Management
 *
 * Zustand store for managing playbook templates.
 * Provides API calls to list templates and clone playbooks.
 *
 * @version 2.7.0
 */

import { create } from 'zustand';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

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

  // API base URL (set from context)
  apiBaseUrl: string;

  // Actions
  setApiBaseUrl: (url: string) => void;
  fetchTemplates: (page?: number, nameFilter?: string) => Promise<void>;
  clonePlaybook: (templateId: string, newName?: string) => Promise<ClonedPlaybook>;
  clearError: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// API Client Helper
// ─────────────────────────────────────────────────────────────────────────────

async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
  // Get token from MSAL if available (for authenticated API calls)
  // For now, use cookie-based auth which is handled automatically
  const response = await fetch(url, {
    ...options,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...options.headers,
    },
  });

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || `HTTP ${response.status}: ${response.statusText}`);
  }

  return response;
}

// ─────────────────────────────────────────────────────────────────────────────
// Store
// ─────────────────────────────────────────────────────────────────────────────

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

  apiBaseUrl: '',

  // Set the API base URL (called from PCF context)
  setApiBaseUrl: (url: string) => {
    set({ apiBaseUrl: url.replace(/\/$/, '') }); // Remove trailing slash
  },

  // Fetch templates from the API
  fetchTemplates: async (page = 1, nameFilter?: string) => {
    const { apiBaseUrl, pageSize } = get();

    if (!apiBaseUrl) {
      console.warn('[TemplateStore] API base URL not set');
      set({ error: 'API configuration error' });
      return;
    }

    set({ isLoading: true, error: null });

    try {
      let url = `${apiBaseUrl}/api/ai/playbooks/templates?page=${page}&pageSize=${pageSize}`;
      if (nameFilter) {
        url += `&nameFilter=${encodeURIComponent(nameFilter)}`;
      }

      console.info('[TemplateStore] Fetching templates', { url });

      const response = await fetchWithAuth(url);
      const data: TemplateListResponse = await response.json();

      console.info('[TemplateStore] Templates loaded', {
        count: data.items.length,
        totalCount: data.totalCount,
      });

      set({
        templates: data.items,
        totalCount: data.totalCount,
        currentPage: data.page,
        isLoading: false,
      });
    } catch (error) {
      console.error('[TemplateStore] Failed to fetch templates', error);
      set({
        error: error instanceof Error ? error.message : 'Failed to load templates',
        isLoading: false,
      });
    }
  },

  // Clone a playbook template
  clonePlaybook: async (templateId: string, newName?: string): Promise<ClonedPlaybook> => {
    const { apiBaseUrl } = get();

    if (!apiBaseUrl) {
      throw new Error('API configuration error');
    }

    set({ isCloning: true, cloneError: null });

    try {
      const url = `${apiBaseUrl}/api/ai/playbooks/${templateId}/clone`;
      const body = newName ? JSON.stringify({ newName }) : '{}';

      console.info('[TemplateStore] Cloning playbook', { templateId, newName });

      const response = await fetchWithAuth(url, {
        method: 'POST',
        body,
      });

      const clonedPlaybook = await response.json();

      console.info('[TemplateStore] Playbook cloned', {
        originalId: templateId,
        clonedId: clonedPlaybook.id,
        clonedName: clonedPlaybook.name,
      });

      set({ isCloning: false });

      return {
        id: clonedPlaybook.id,
        name: clonedPlaybook.name,
        description: clonedPlaybook.description,
      };
    } catch (error) {
      console.error('[TemplateStore] Failed to clone playbook', error);
      const errorMessage = error instanceof Error ? error.message : 'Failed to clone playbook';
      set({ cloneError: errorMessage, isCloning: false });
      throw new Error(errorMessage);
    }
  },

  // Clear error state
  clearError: () => {
    set({ error: null, cloneError: null });
  },
}));
