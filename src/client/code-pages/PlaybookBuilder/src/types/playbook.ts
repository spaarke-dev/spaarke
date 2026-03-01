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
    Start = "start",
    AiAnalysis = "aiAnalysis",
    AiCompletion = "aiCompletion",
    Condition = "condition",
    DeliverOutput = "deliverOutput",
    CreateTask = "createTask",
    SendEmail = "sendEmail",
    Wait = "wait",
}

/**
 * Map PlaybookNodeType to Dataverse sprk_nodetype optionset integer values.
 * These are the optionset values stored in the sprk_playbooknode table.
 */
export const NodeTypeOptionSet: Record<PlaybookNodeType, number> = {
    [PlaybookNodeType.Start]: 100000000,
    [PlaybookNodeType.AiAnalysis]: 100000001,
    [PlaybookNodeType.AiCompletion]: 100000002,
    [PlaybookNodeType.Condition]: 100000003,
    [PlaybookNodeType.DeliverOutput]: 100000004,
    [PlaybookNodeType.CreateTask]: 100000005,
    [PlaybookNodeType.SendEmail]: 100000006,
    [PlaybookNodeType.Wait]: 100000007,
};

/**
 * Reverse mapping: optionset integer value back to PlaybookNodeType string.
 */
export const OptionSetToNodeType: Record<number, PlaybookNodeType> = Object.fromEntries(
    Object.entries(NodeTypeOptionSet).map(([type, value]) => [value, type as PlaybookNodeType]),
);

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
    _sprk_toolid_value?: string;
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
    toolId?: string;
    modelDeploymentId?: string;
    timeoutSeconds?: number;
    retryCount?: number;

    // Type-specific config (maps to sprk_configjson)
    conditionJson?: string;

    // Deliver Output config
    deliveryType?: "markdown" | "html" | "text" | "json";
    template?: string;
    includeMetadata?: boolean;
    includeSourceCitations?: boolean;
    maxOutputLength?: number;

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

    // Wait config
    waitType?: "duration" | "until" | "condition";
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
    category: "ai" | "logic" | "output" | "action";
}

/**
 * Metadata for all user-facing node types (excludes Start which is auto-created).
 */
export const NODE_TYPE_INFO: NodeTypeInfo[] = [
    {
        type: PlaybookNodeType.AiAnalysis,
        label: "AI Analysis",
        description: "Run AI analysis with skills, knowledge, and tools",
        icon: "BrainCircuit",
        category: "ai",
    },
    {
        type: PlaybookNodeType.AiCompletion,
        label: "AI Completion",
        description: "Generate text with a system prompt and template",
        icon: "Sparkle",
        category: "ai",
    },
    {
        type: PlaybookNodeType.Condition,
        label: "Condition",
        description: "Branch based on a conditional expression",
        icon: "ArrowSplit",
        category: "logic",
    },
    {
        type: PlaybookNodeType.DeliverOutput,
        label: "Deliver Output",
        description: "Format and deliver results as markdown, HTML, or text",
        icon: "DocumentText",
        category: "output",
    },
    {
        type: PlaybookNodeType.CreateTask,
        label: "Create Task",
        description: "Create a Dataverse task record",
        icon: "TaskListSquare",
        category: "action",
    },
    {
        type: PlaybookNodeType.SendEmail,
        label: "Send Email",
        description: "Send an email with template variable support",
        icon: "Mail",
        category: "action",
    },
    {
        type: PlaybookNodeType.Wait,
        label: "Wait",
        description: "Pause execution for a duration or until a condition",
        icon: "Clock",
        category: "logic",
    },
];
