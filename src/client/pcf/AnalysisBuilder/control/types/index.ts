/**
 * Type definitions for Analysis Builder PCF control
 */

// ─────────────────────────────────────────────────────────────────────────────
// Playbook Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IPlaybook {
    id: string;
    name: string;
    description: string;
    icon?: string;
    category?: string;
    isDefault?: boolean;
    actionId?: string;
    skillIds?: string[];
    knowledgeIds?: string[];
    toolIds?: string[];
    outputFormat?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Scope Item Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IScopeItem {
    id: string;
    name: string;
    description?: string;
    icon?: string;
    isSelected: boolean;
    category?: string;
}

export interface IAction extends IScopeItem {
    promptTemplate?: string;
}

export interface ISkill extends IScopeItem {
    type: "extraction" | "analysis" | "generation" | "transformation";
}

export interface IKnowledge extends IScopeItem {
    source: "dataverse" | "sharepoint" | "external";
    connectionString?: string;
}

export interface ITool extends IScopeItem {
    toolType: "search" | "calculation" | "api" | "workflow";
    endpoint?: string;
}

export interface IOutputFormat extends IScopeItem {
    format: "markdown" | "json" | "html" | "pdf";
    template?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tab Types
// ─────────────────────────────────────────────────────────────────────────────

export type ScopeTabId = "action" | "skills" | "knowledge" | "tools" | "output";

export interface IScopeTab {
    id: ScopeTabId;
    label: string;
    count: number;
    icon: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Analysis Configuration
// ─────────────────────────────────────────────────────────────────────────────

export interface IAnalysisConfiguration {
    documentId: string;
    documentName?: string;
    containerId?: string;
    fileId?: string;
    playbookId?: string;
    actionId: string;
    skillIds: string[];
    knowledgeIds: string[];
    toolIds: string[];
    outputFormat?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component Props
// ─────────────────────────────────────────────────────────────────────────────

export interface IAnalysisBuilderAppProps {
    documentId: string;
    documentName: string;
    containerId: string;
    fileId: string;
    apiBaseUrl: string;
    webApi: ComponentFramework.WebApi;
    onPlaybookSelect: (playbookId: string) => void;
    onActionSelect: (actionId: string) => void;
    onSkillsSelect: (skillIds: string[]) => void;
    onKnowledgeSelect: (knowledgeIds: string[]) => void;
    onToolsSelect: (toolIds: string[]) => void;
    onExecute: (analysisId: string) => void;
    onCancel: () => void;
}

export interface IPlaybookSelectorProps {
    playbooks: IPlaybook[];
    selectedPlaybookId?: string;
    onSelect: (playbook: IPlaybook) => void;
    isLoading: boolean;
}

export interface IScopeTabsProps {
    activeTab: ScopeTabId;
    tabs: IScopeTab[];
    onTabChange: (tabId: ScopeTabId) => void;
}

export interface IScopeListProps<T extends IScopeItem> {
    items: T[];
    onSelectionChange: (selectedIds: string[]) => void;
    isLoading: boolean;
    emptyMessage?: string;
    multiSelect?: boolean;
}

export interface IFooterActionsProps {
    onSavePlaybook: () => void;
    onSaveAs: () => void;
    onCancel: () => void;
    onExecute: () => void;
    isExecuting: boolean;
    canSave: boolean;
    canExecute: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// API Response Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IApiResponse<T> {
    success: boolean;
    data?: T;
    error?: string;
}

export interface IAnalysisResult {
    analysisId: string;
    status: "created" | "queued" | "processing" | "completed" | "failed";
    workspaceUrl?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// State Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IAnalysisBuilderState {
    // Data
    playbooks: IPlaybook[];
    actions: IAction[];
    skills: ISkill[];
    knowledge: IKnowledge[];
    tools: ITool[];
    outputFormats: IOutputFormat[];

    // Selection
    selectedPlaybook?: IPlaybook;
    selectedActionId?: string;
    selectedSkillIds: string[];
    selectedKnowledgeIds: string[];
    selectedToolIds: string[];
    selectedOutputFormat?: string;

    // UI State
    activeTab: ScopeTabId;
    isLoading: boolean;
    isExecuting: boolean;
    error?: string;
}
