/**
 * Components - Barrel Export
 */

// Main layout
export { BuilderLayout } from './BuilderLayout';

// Canvas
export { Canvas } from './Canvas';

// Nodes
export {
  BaseNode,
  AiAnalysisNode,
  AiCompletionNode,
  ConditionNode,
  DeliverOutputNode,
  CreateTaskNode,
  SendEmailNode,
  WaitNode,
  nodeTypes,
} from './Nodes';

// Edges
export {
  TrueBranchEdge,
  FalseBranchEdge,
  edgeTypes,
  EDGE_TYPES,
} from './Edges';

// Properties
export { PropertiesPanel, NodePropertiesForm, ScopeSelector, ConditionEditor } from './Properties';

// Execution
export {
  ExecutionOverlay,
  NodeExecutionBadge,
  getNodeExecutionClassName,
} from './Execution';

// Templates
export { TemplateLibraryDialog } from './Templates';

// SaveAsDialog
export { SaveAsDialog, type SaveAsDialogProps } from './SaveAsDialog';

// ScopeBrowser
export {
  ScopeBrowser,
  ScopeList,
  ScopeFormDialog,
  type ScopeBrowserProps,
  type ScopeListProps,
  type ScopeFormDialogProps,
  type ScopeItem,
  type ScopeType,
  type OwnershipType,
  type ScopeFormData,
  type DialogMode,
} from './ScopeBrowser';

// TestModeSelector
export {
  TestModeSelector,
  type TestModeSelectorProps,
} from './TestModeSelector';
