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
  // server-side ActionType.LookupUserMembership = 52 and the
  // LookupUserMembershipNodeExecutor (task 041).
  LookupUserMembership = 'lookupUserMembership',
  // R4 PR 1 (task 004): Tool that scrubs LLM-emitted entity names not present
  // in a maker-supplied allow-list. Pairs with server-side
  // ActionType.EntityNameValidator = 141 and the EntityNameValidatorNodeExecutor
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
  // chat-routing-redesign-r1 / Phase 5R Wave 5-C (FR-52 / ADR-037): multinode
  // Output composition. Added to sprk_nodetype OptionSet via
  // scripts/dataverse/Add-NodeTypeChoiceOption.ps1.
  DeliverComposite = 100_000_004,
  // R4 task 004 hotfix (2026-06-26, FR-3 / AC-3c): distinct OptionSet value so
  // the MDA "Node Properties" form surfaces EntityNameValidator as its own type
  // (not categorized under Workflow). Added via
  // scripts/dataverse/Add-EntityNameValidatorNodeTypeOption.ps1.
  EntityNameValidator = 100_000_005,
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
  // LookupUserMembership is a data-ops Workflow node — invokes the membership
  // resolver service in-process and binds the resolved IDs to OutputVariable.
  [PlaybookNodeType.LookupUserMembership]: DataverseNodeType.Workflow,
  // EntityNameValidator is a post-LLM Tool — operates purely on text + an
  // allow-list and emits a scrubbed string. R4 task 004 hotfix (2026-06-26):
  // re-pointed from Workflow to its OWN distinct OptionSet value so the MDA
  // "Node Properties" form surfaces it as its own type. See
  // scripts/dataverse/Add-EntityNameValidatorNodeTypeOption.ps1.
  [PlaybookNodeType.EntityNameValidator]: DataverseNodeType.EntityNameValidator,
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
  // R3 P5 (task 042): mirrors server INodeExecutor.cs ActionType.LookupUserMembership.
  // Slot 52 sits alongside QueryDataverse (51, server-only, not yet exposed in the
  // canvas) in the Dataverse-data-ops group.
  LookupUserMembership = 52,
  // R4 PR 1 (task 002 enum / task 003 executor / task 004 form): mirrors server
  // INodeExecutor.cs ActionType.EntityNameValidator. Slot 141 sits in the
  // post-LLM cluster alongside Sanitization (130) and ObservationEmit (140).
  EntityNameValidator = 141,
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
  [PlaybookNodeType.LookupUserMembership]: ActionType.LookupUserMembership,
  [PlaybookNodeType.EntityNameValidator]: ActionType.EntityNameValidator,
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

  // Lookup User Membership config (R3 P5 / task 042) — read by buildConfigJson()
  // and the LookupUserMembershipForm (task 043). Field names use `membership`
  // prefix on the canvas to avoid colliding with other ActionType fields; they
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
