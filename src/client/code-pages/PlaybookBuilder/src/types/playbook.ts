/**
 * Shared Dataverse types for the Playbook Builder.
 *
 * Defines enums, interfaces, and mappings for playbook node records
 * stored in the sprk_playbooknode Dataverse table.
 */

// ---------------------------------------------------------------------------
// Node Type Enum
// ---------------------------------------------------------------------------

/**
 * Playbook node types supported by the canvas and execution engine.
 * String values match the canvas store type identifiers.
 */
export enum PlaybookNodeType {
  Start = 'start',
  AiAnalysis = 'aiAnalysis',
  AiCompletion = 'aiCompletion',
  Condition = 'condition',
  DeliverOutput = 'deliverOutput',
  DeliverToIndex = 'deliverToIndex',
  UpdateRecord = 'updateRecord',
  CreateTask = 'createTask',
  SendEmail = 'sendEmail',
  CreateNotification = 'createNotification',
  Wait = 'wait',
}

// ---------------------------------------------------------------------------
// Output Type Enum (R2 — typed output dispatch for PlaybookDispatcher)
// ---------------------------------------------------------------------------

/**
 * Output types for DeliverOutput nodes.
 * Determines how the PlaybookDispatcher presents the final result to the user.
 *
 *   text       — inline text/markdown response in chat
 *   dialog     — open a Code Page dialog (targetPage required)
 *   navigation — navigate to a record or page
 *   download   — generate a downloadable file
 *   insert     — insert content into the current document context
 */
export enum OutputType {
  Text = 'text',
  Dialog = 'dialog',
  Navigation = 'navigation',
  Download = 'download',
  Insert = 'insert',
}

// ---------------------------------------------------------------------------
// Dataverse sprk_nodetype — Coarse Node Category (4 values)
// ---------------------------------------------------------------------------

/**
 * Coarse node category stored as sprk_nodetype choice on sprk_playbooknode.
 * Determines which scopes the orchestrator resolves before execution.
 *
 *   AI Analysis — requires Action record + resolves Skills, Knowledge, Tools scopes
 *   Output      — structural; no Action or scopes needed
 *   Control     — structural; no Action or scopes needed
 *   Workflow    — rule-based actions; scope TBD
 */
export enum DataverseNodeType {
  AIAnalysis = 100_000_000,
  Output = 100_000_001,
  Control = 100_000_002,
  Workflow = 100_000_003,
}

/**
 * Map canvas PlaybookNodeType → Dataverse sprk_nodetype (coarse category).
 */
export const NodeTypeToDataverse: Record<PlaybookNodeType, DataverseNodeType> = {
  [PlaybookNodeType.Start]: DataverseNodeType.Control,
  [PlaybookNodeType.AiAnalysis]: DataverseNodeType.AIAnalysis,
  [PlaybookNodeType.AiCompletion]: DataverseNodeType.AIAnalysis,
  [PlaybookNodeType.Condition]: DataverseNodeType.Control,
  [PlaybookNodeType.DeliverOutput]: DataverseNodeType.Output,
  [PlaybookNodeType.DeliverToIndex]: DataverseNodeType.Output,
  [PlaybookNodeType.UpdateRecord]: DataverseNodeType.Workflow,
  [PlaybookNodeType.CreateTask]: DataverseNodeType.Workflow,
  [PlaybookNodeType.SendEmail]: DataverseNodeType.Workflow,
  [PlaybookNodeType.CreateNotification]: DataverseNodeType.Workflow,
  [PlaybookNodeType.Wait]: DataverseNodeType.Control,
};

// ---------------------------------------------------------------------------
// ActionType — Fine-grained executor dispatch (matches server enum)
// ---------------------------------------------------------------------------

/**
 * Specific action type for executor dispatch.
 * Values match the server-side ActionType enum in INodeExecutor.cs.
 * Stored as __actionType in ConfigJson.
 */
export enum ActionType {
  AiAnalysis = 0,
  AiCompletion = 1,
  AiEmbedding = 2,
  RuleEngine = 10,
  Calculation = 11,
  DataTransform = 12,
  CreateTask = 20,
  SendEmail = 21,
  UpdateRecord = 22,
  CallWebhook = 23,
  SendTeamsMessage = 24,
  Condition = 30,
  Parallel = 31,
  Wait = 32,
  DeliverOutput = 40,
  DeliverToIndex = 41,
  CreateNotification = 50,
}

/**
 * Map canvas PlaybookNodeType → ActionType (specific executor dispatch).
 */
export const NodeTypeToActionType: Record<PlaybookNodeType, ActionType> = {
  [PlaybookNodeType.Start]: ActionType.Condition,
  [PlaybookNodeType.AiAnalysis]: ActionType.AiAnalysis,
  [PlaybookNodeType.AiCompletion]: ActionType.AiCompletion,
  [PlaybookNodeType.Condition]: ActionType.Condition,
  [PlaybookNodeType.DeliverOutput]: ActionType.DeliverOutput,
  [PlaybookNodeType.DeliverToIndex]: ActionType.DeliverToIndex,
  [PlaybookNodeType.UpdateRecord]: ActionType.UpdateRecord,
  [PlaybookNodeType.CreateTask]: ActionType.CreateTask,
  [PlaybookNodeType.SendEmail]: ActionType.SendEmail,
  [PlaybookNodeType.CreateNotification]: ActionType.CreateNotification,
  [PlaybookNodeType.Wait]: ActionType.Wait,
};

// ---------------------------------------------------------------------------
// Dataverse Record Interfaces
// ---------------------------------------------------------------------------

/**
 * Raw Dataverse record shape for sprk_playbooknode.
 * Field names match Dataverse schema exactly.
 */
export interface PlaybookNodeRecord {
  sprk_playbooknodeid: string;
  sprk_name: string;
  sprk_nodetype: number;
  sprk_executionorder: number;
  sprk_outputvariable?: string;
  sprk_configjson: string;
  sprk_position_x?: number;
  sprk_position_y?: number;
  sprk_isactive: boolean;
  sprk_timeoutseconds?: number;
  sprk_retrycount?: number;
  sprk_conditionjson?: string;
  sprk_dependsonjson?: string;
  _sprk_playbookid_value: string;
  _sprk_actionid_value?: string;
  _sprk_modeldeploymentid_value?: string;
}

/**
 * Extended PlaybookNodeData with type-specific config fields.
 * This is stored in the canvas node's `data` property and
 * gets serialized into sprk_configjson by playbookNodeSync.
 */
export interface PlaybookNodeData {
  label: string;
  type: PlaybookNodeType | string;
  actionId?: string;
  outputVariable?: string;
  isActive?: boolean;
  skillIds?: string[];
  knowledgeIds?: string[];
  toolIds?: string[];
  modelDeploymentId?: string;
  timeoutSeconds?: number;
  retryCount?: number;

  // HITL / autonomous execution flag (R2)
  // When true, PlaybookDispatcher opens confirmation UI before executing.
  // When false, executes autonomously. Default behavior is determined by
  // outputType in PlaybookDispatcher (dialog → true, text → false).
  requiresConfirmation?: boolean;

  // Type-specific config (maps to sprk_configjson)
  conditionJson?: string;

  // Deliver Output config — typed output (R2)
  outputType?: OutputType;
  targetPage?: string;
  prePopulateFields?: Record<string, string>;

  // Deliver Output config
  deliveryType?: 'markdown' | 'html' | 'text' | 'json';
  template?: string;
  includeMetadata?: boolean;
  includeSourceCitations?: boolean;
  maxOutputLength?: number;

  // Deliver to Index config
  indexName?: string;
  indexSource?: 'document' | 'content';
  indexContentVariable?: string;
  indexParentEntityType?: string;
  indexParentEntityId?: string;
  indexParentEntityName?: string;
  indexMetadata?: string;

  // Send Email config
  emailTo?: string[];
  emailCc?: string[];
  emailSubject?: string;
  emailBody?: string;
  emailIsHtml?: boolean;

  // Create Task config
  taskSubject?: string;
  taskDescription?: string;
  taskRegardingType?: string;
  taskRegardingId?: string;
  taskOwnerId?: string;
  taskDueDate?: string;

  // AI Completion config
  systemPrompt?: string;
  userPromptTemplate?: string;
  temperature?: number;
  maxTokens?: number;

  // Create Notification config
  notificationTitle?: string;
  notificationBody?: string;
  notificationPriority?: 'informational' | 'warning' | 'critical';
  notificationIconType?: string;
  notificationOwnerId?: string;

  // Wait config
  waitType?: 'duration' | 'until' | 'condition';
  waitDurationHours?: number;
  waitUntilDateTime?: string;

  // Extensibility: allow additional properties for React Flow compatibility
  [key: string]: unknown;
}

// ---------------------------------------------------------------------------
// Node Type Metadata
// ---------------------------------------------------------------------------

/**
 * Display metadata for each node type used in the ActionSelector
 * and node palette components.
 */
export interface NodeTypeInfo {
  type: PlaybookNodeType;
  label: string;
  description: string;
  icon: string;
  category: 'ai' | 'logic' | 'output' | 'action';
}

/**
 * Metadata for all user-facing node types (excludes Start which is auto-created).
 */
export const NODE_TYPE_INFO: NodeTypeInfo[] = [
  {
    type: PlaybookNodeType.AiAnalysis,
    label: 'AI Analysis',
    description: 'Run AI analysis with skills, knowledge, and tools',
    icon: 'BrainCircuit',
    category: 'ai',
  },
  {
    type: PlaybookNodeType.AiCompletion,
    label: 'AI Completion',
    description: 'Generate text with a system prompt and template',
    icon: 'Sparkle',
    category: 'ai',
  },
  {
    type: PlaybookNodeType.Condition,
    label: 'Condition',
    description: 'Branch based on a conditional expression',
    icon: 'ArrowSplit',
    category: 'logic',
  },
  {
    type: PlaybookNodeType.DeliverOutput,
    label: 'Deliver Output',
    description: 'Format and deliver results as markdown, HTML, or text',
    icon: 'DocumentText',
    category: 'output',
  },
  {
    type: PlaybookNodeType.DeliverToIndex,
    label: 'Deliver to Index',
    description: 'Queue document for RAG semantic indexing',
    icon: 'DatabaseSearch',
    category: 'output',
  },
  {
    type: PlaybookNodeType.CreateTask,
    label: 'Create Task',
    description: 'Create a Dataverse task record',
    icon: 'TaskListSquare',
    category: 'action',
  },
  {
    type: PlaybookNodeType.SendEmail,
    label: 'Send Email',
    description: 'Send an email with template variable support',
    icon: 'Mail',
    category: 'action',
  },
  {
    type: PlaybookNodeType.CreateNotification,
    label: 'Create Notification',
    description: 'Create an in-app notification for a user',
    icon: 'Alert',
    category: 'action',
  },
  {
    type: PlaybookNodeType.Wait,
    label: 'Wait',
    description: 'Pause execution for a duration or until a condition',
    icon: 'Clock',
    category: 'logic',
  },
];

// ---------------------------------------------------------------------------
// JPS Root-Level Trigger Metadata (R2 — informational, not for dispatch)
// ---------------------------------------------------------------------------

/**
 * Trigger metadata stored at the JPS root level of a playbook definition.
 * Used for execution-time behavior reference only. Queryable matching
 * uses Dataverse fields (sprk_analysisplaybook columns), NOT these values.
 *
 * All fields are optional for backward compatibility.
 */
export interface PlaybookTriggerMetadata {
  /** Natural language phrases for semantic matching (informational). */
  triggerPhrases?: string[];

  /** Record type filter, e.g. "matter", "project". */
  recordType?: string;

  /** Dataverse logical entity name filter, e.g. "sprk_matter". */
  entityType?: string;
}
