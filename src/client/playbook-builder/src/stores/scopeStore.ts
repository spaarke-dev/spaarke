import { create } from 'zustand';
import type { PlaybookNodeType } from './canvasStore';

// Scope item types
export interface ScopeItem {
  id: string;
  name: string;
  description?: string;
}

export interface SkillItem extends ScopeItem {
  category: 'extraction' | 'analysis' | 'generation' | 'classification';
}

export interface KnowledgeItem extends ScopeItem {
  type: 'document' | 'datasource' | 'inline';
}

export interface ToolItem extends ScopeItem {
  handlerType: string;
}

// Action type compatibility settings
export interface ActionTypeCapabilities {
  allowsSkills: boolean;
  allowsKnowledge: boolean;
  allowsTools: boolean;
}

// Compatibility map by node type
const actionTypeCapabilities: Record<PlaybookNodeType, ActionTypeCapabilities> = {
  aiAnalysis: { allowsSkills: true, allowsKnowledge: true, allowsTools: true },
  aiCompletion: { allowsSkills: true, allowsKnowledge: true, allowsTools: true },
  condition: { allowsSkills: false, allowsKnowledge: false, allowsTools: false },
  deliverOutput: { allowsSkills: false, allowsKnowledge: true, allowsTools: true },
  createTask: { allowsSkills: false, allowsKnowledge: false, allowsTools: false },
  sendEmail: { allowsSkills: false, allowsKnowledge: true, allowsTools: false },
  wait: { allowsSkills: false, allowsKnowledge: false, allowsTools: false },
};

// Mock skills data (will be replaced with API calls)
const mockSkills: SkillItem[] = [
  { id: 'skill-1', name: 'Entity Extraction', description: 'Extract named entities from text', category: 'extraction' },
  { id: 'skill-2', name: 'Key Phrase Extraction', description: 'Identify key phrases and topics', category: 'extraction' },
  { id: 'skill-3', name: 'Sentiment Analysis', description: 'Analyze sentiment and tone', category: 'analysis' },
  { id: 'skill-4', name: 'Document Classification', description: 'Classify document type and category', category: 'classification' },
  { id: 'skill-5', name: 'Text Summarization', description: 'Generate concise summaries', category: 'generation' },
  { id: 'skill-6', name: 'Risk Identification', description: 'Identify potential risks and issues', category: 'analysis' },
];

// Mock knowledge data (will be replaced with API calls)
const mockKnowledge: KnowledgeItem[] = [
  { id: 'knowledge-1', name: 'Company Policies', description: 'Internal policy documents', type: 'document' },
  { id: 'knowledge-2', name: 'Legal Templates', description: 'Standard contract templates', type: 'document' },
  { id: 'knowledge-3', name: 'Customer Database', description: 'CRM customer records', type: 'datasource' },
  { id: 'knowledge-4', name: 'Product Catalog', description: 'Product specifications and pricing', type: 'datasource' },
  { id: 'knowledge-5', name: 'Instructions', description: 'Custom inline instructions', type: 'inline' },
];

// Mock tools data (will be replaced with API calls)
const mockTools: ToolItem[] = [
  { id: 'tool-1', name: 'Document Analyzer', description: 'Azure AI Document Intelligence', handlerType: 'DocumentAnalyzer' },
  { id: 'tool-2', name: 'Search Index', description: 'Azure AI Search integration', handlerType: 'SearchIndex' },
  { id: 'tool-3', name: 'GPT-4 Completion', description: 'Azure OpenAI GPT-4 completion', handlerType: 'GptCompletion' },
  { id: 'tool-4', name: 'Word Generator', description: 'Generate Word documents', handlerType: 'WordGenerator' },
  { id: 'tool-5', name: 'PDF Generator', description: 'Generate PDF documents', handlerType: 'PdfGenerator' },
];

interface ScopeState {
  // Available scope items
  skills: SkillItem[];
  knowledge: KnowledgeItem[];
  tools: ToolItem[];

  // Loading states (for future API integration)
  isLoadingSkills: boolean;
  isLoadingKnowledge: boolean;
  isLoadingTools: boolean;

  // Get capabilities for a node type
  getCapabilities: (nodeType: PlaybookNodeType) => ActionTypeCapabilities;

  // Get items by IDs
  getSkillsByIds: (ids: string[]) => SkillItem[];
  getKnowledgeByIds: (ids: string[]) => KnowledgeItem[];
  getToolById: (id: string) => ToolItem | undefined;

  // Future: API fetch methods
  // fetchSkills: () => Promise<void>;
  // fetchKnowledge: () => Promise<void>;
  // fetchTools: () => Promise<void>;
}

/**
 * Zustand store for scope selections (skills, knowledge, tools).
 * Uses mock data for now - will be replaced with API calls.
 */
export const useScopeStore = create<ScopeState>(() => ({
  // Initialize with mock data
  skills: mockSkills,
  knowledge: mockKnowledge,
  tools: mockTools,

  // Loading states
  isLoadingSkills: false,
  isLoadingKnowledge: false,
  isLoadingTools: false,

  // Get capabilities for a node type
  getCapabilities: (nodeType: PlaybookNodeType) => {
    return actionTypeCapabilities[nodeType] || {
      allowsSkills: false,
      allowsKnowledge: false,
      allowsTools: false,
    };
  },

  // Get skills by IDs
  getSkillsByIds: (ids: string[]) => {
    return mockSkills.filter((s) => ids.includes(s.id));
  },

  // Get knowledge by IDs
  getKnowledgeByIds: (ids: string[]) => {
    return mockKnowledge.filter((k) => ids.includes(k.id));
  },

  // Get tool by ID
  getToolById: (id: string) => {
    return mockTools.find((t) => t.id === id);
  },
}));
