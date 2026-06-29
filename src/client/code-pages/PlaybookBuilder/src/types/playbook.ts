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
  // R3 P5 (task 042): Resolves the calling user's record memberships for a
  // given entity type via IMembershipResolverService (FR-1B.1). Pairs with
  // server-side ExecutorType.LookupUserMembership = 52 and the
  // LookupUserMembershipNodeExecutor (task 041).
  LookupUserMembership = 'lookupUserMembership',
  // R4 PR 1 (task 004): Tool that scrubs LLM-emitted entity names not present
  // in a maker-supplied allow-list. Pairs with server-side
  // ExecutorType.EntityNameValidator = 141 and the EntityNameValidatorNodeExecutor
  // (task 003). FR-3 / AC-3c.
  EntityNameValidator = 'entityNameValidator',
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
// Executor Dispatch (R7 FR-26 / FR-07)
// ---------------------------------------------------------------------------
//
// Server-side `PlaybookOrchestrationService.ExecuteNodeAsync` reads
// `node.sprk_executortype` directly — single-hop dispatch per FR-07. On the
// canvas, the source of dispatch truth is `node.data.executorType: number`
// (mirrors server `ExecutorType` enum value, 0–143). The full 33-entry catalog
// (value + name + tier + canvasType) lives in `src/config/executorMetadata.ts`
// and mirrors `INodeExecutor.cs ExecutorType` on the server.
//
// Per R7 design.md §3 + project CLAUDE.md "Key Technical Constraints", the canvas
// `PlaybookNodeType` discriminator (13 string values) survives as an internal
// React Flow renderer hint — it does NOT need 1:1 mapping with server `ExecutorType`
// (33 values). Multiple executors may bucket under a single canvas renderer
// (e.g., RuleEngine, Calculation, DataTransform all render as `aiAnalysis`).
// The numeric `executorType` field carries dispatch; `type` carries UI only.

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
  /**
   * R7 FR-26: `sprk_executortype` is the 33-value Choice mirroring server
   * `ExecutorType` enum. Single source of dispatch per FR-07 (single-hop read
   * in PlaybookOrchestrationService).
   */
  sprk_executortype: number;
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

  // Lookup User Membership config (R3 P5 / task 042) — read by buildConfigJson()
  // and the LookupUserMembershipForm (task 043). Field names use `membership`
  // prefix on the canvas to avoid colliding with other executor fields; they
  // are remapped to the server-expected keys (entityType, roles, includeRelated)
  // when serialized into sprk_configjson.
  membershipEntityType?: string;
  membershipRoles?: string[];
  membershipIncludeRelated?: boolean;

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
    type: PlaybookNodeType.LookupUserMembership,
    label: 'Lookup User Membership',
    description: 'Resolve the caller’s record memberships for an entity type',
    icon: 'PeopleTeam',
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
