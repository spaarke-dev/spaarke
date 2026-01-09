/**
 * Type definitions for the Playbook Builder application.
 * Exports will be added as types are created.
 */

// Host-Builder communication message types
export * from './messages';

// Node types
export type NodeType =
  | 'aiAnalysis'
  | 'aiCompletion'
  | 'condition'
  | 'deliverOutput'
  | 'createTask'
  | 'sendEmail';

// Playbook state
export interface PlaybookDefinition {
  id: string;
  name: string;
  description?: string;
  mode: 'Legacy' | 'NodeBased';
  type: 'AiAnalysis' | 'Workflow' | 'Hybrid';
  nodes: PlaybookNode[];
  canvasLayout?: CanvasLayout;
}

export interface PlaybookNode {
  id: string;
  name: string;
  type: NodeType;
  actionId: string;
  executionOrder: number;
  outputVariable: string;
  position: { x: number; y: number };
  config?: Record<string, unknown>;
  dependsOn?: string[];
  isActive: boolean;
}

export interface CanvasLayout {
  viewport: { x: number; y: number; zoom: number };
}

// Host-Builder communication
export type HostMessage =
  | { type: 'INIT'; payload: InitPayload }
  | { type: 'SAVE_SUCCESS' }
  | { type: 'SAVE_ERROR'; error: string }
  | { type: 'THEME_CHANGE'; isDarkMode: boolean };

export type BuilderMessage =
  | { type: 'READY' }
  | { type: 'SAVE_REQUEST'; playbook: PlaybookDefinition }
  | { type: 'DIRTY_STATE'; isDirty: boolean }
  | { type: 'CLOSE' };

export interface InitPayload {
  playbook: PlaybookDefinition;
  actions: ActionDefinition[];
  authToken: string;
}

export interface ActionDefinition {
  id: string;
  name: string;
  type: number;
  allowsSkills: boolean;
  allowsTools: boolean;
  allowsKnowledge: boolean;
  allowsDelivery: boolean;
}
