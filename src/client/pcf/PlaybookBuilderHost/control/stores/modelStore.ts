/**
 * Model Store - AI Model Deployments selection
 *
 * Zustand store for managing available AI model deployments.
 * Uses mock data matching the BFF API stub data from ModelEndpoints.cs.
 * Will be replaced with API calls in production.
 */

import { create } from 'zustand';

// AI Provider enum matching BFF API (ModelDeploymentDto.cs)
export type AiProvider = 'AzureOpenAI' | 'OpenAI' | 'Anthropic';

// AI Capability enum matching BFF API
export type AiCapability = 'Chat' | 'Completion' | 'Embedding';

// Model deployment item type
export interface ModelDeploymentItem {
  id: string;
  name: string;
  provider: AiProvider;
  capability: AiCapability;
  modelId: string;
  contextWindow: number;
  description?: string;
  isActive: boolean;
}

// Mock model deployments matching BFF API stub data
const mockModelDeployments: ModelDeploymentItem[] = [
  {
    id: '50000000-0000-0000-0000-000000000001',
    name: 'GPT-4o (Default)',
    provider: 'AzureOpenAI',
    capability: 'Chat',
    modelId: 'gpt-4o',
    contextWindow: 128000,
    description: 'Latest GPT-4 model with improved performance and cost efficiency',
    isActive: true,
  },
  {
    id: '50000000-0000-0000-0000-000000000002',
    name: 'GPT-4o Mini',
    provider: 'AzureOpenAI',
    capability: 'Chat',
    modelId: 'gpt-4o-mini',
    contextWindow: 128000,
    description: 'Smaller, faster GPT-4o variant for simpler tasks',
    isActive: true,
  },
  {
    id: '50000000-0000-0000-0000-000000000003',
    name: 'GPT-4 Turbo',
    provider: 'AzureOpenAI',
    capability: 'Chat',
    modelId: 'gpt-4-turbo',
    contextWindow: 128000,
    description: 'GPT-4 Turbo with vision capabilities',
    isActive: true,
  },
  {
    id: '50000000-0000-0000-0000-000000000004',
    name: 'text-embedding-3-large',
    provider: 'AzureOpenAI',
    capability: 'Embedding',
    modelId: 'text-embedding-3-large',
    contextWindow: 8191,
    description: 'Large embedding model for high-quality vector representations',
    isActive: true,
  },
  {
    id: '50000000-0000-0000-0000-000000000005',
    name: 'Claude 3.5 Sonnet',
    provider: 'Anthropic',
    capability: 'Chat',
    modelId: 'claude-3-5-sonnet-20241022',
    contextWindow: 200000,
    description: "Anthropic's balanced model with excellent reasoning",
    isActive: false, // Not yet configured
  },
];

interface ModelState {
  // Available model deployments
  models: ModelDeploymentItem[];

  // Loading state (for future API integration)
  isLoading: boolean;

  // Error state
  error: string | null;

  // Get active models only
  getActiveModels: () => ModelDeploymentItem[];

  // Get models by capability
  getModelsByCapability: (capability: AiCapability) => ModelDeploymentItem[];

  // Get chat-capable models (for AI analysis/completion nodes)
  getChatModels: () => ModelDeploymentItem[];

  // Get model by ID
  getModelById: (id: string) => ModelDeploymentItem | undefined;
}

/**
 * Zustand store for AI model deployment selections.
 * Filters to show only active, chat-capable models for AI nodes.
 */
export const useModelStore = create<ModelState>(() => ({
  // Initialize with mock data
  models: mockModelDeployments,

  // Loading state
  isLoading: false,

  // Error state
  error: null,

  // Get active models only
  getActiveModels: () => {
    return mockModelDeployments.filter((m) => m.isActive);
  },

  // Get models by capability
  getModelsByCapability: (capability: AiCapability) => {
    return mockModelDeployments.filter(
      (m) => m.isActive && m.capability === capability
    );
  },

  // Get chat-capable models (for AI analysis/completion nodes)
  getChatModels: () => {
    return mockModelDeployments.filter(
      (m) => m.isActive && m.capability === 'Chat'
    );
  },

  // Get model by ID
  getModelById: (id: string) => {
    return mockModelDeployments.find((m) => m.id === id);
  },
}));
