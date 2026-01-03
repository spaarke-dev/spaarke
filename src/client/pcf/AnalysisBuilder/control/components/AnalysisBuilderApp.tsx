/**
 * Analysis Builder App - Main Component
 *
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 *
 * Layout:
 * - Header with title and document name
 * - Playbook selector (card grid)
 * - Scope tabs (Action, Skills, Knowledge, Tools, Output)
 * - Scope item list with checkboxes
 * - Footer actions (Save Playbook, Save As, Cancel, Execute)
 */

import * as React from "react";
import {
    Spinner,
    MessageBar,
    MessageBarBody,
    makeStyles,
    tokens
} from "@fluentui/react-components";
import { PlaybookSelector } from "./PlaybookSelector";
import { ScopeTabs } from "./ScopeTabs";
import { ScopeList } from "./ScopeList";
import { FooterActions } from "./FooterActions";
import {
    IAnalysisBuilderAppProps,
    IPlaybook,
    IAction,
    ISkill,
    IKnowledge,
    ITool,
    IOutputFormat,
    ScopeTabId,
    IScopeTab
} from "../types";
import { logInfo, logError } from "../utils/logger";
import "../css/AnalysisBuilder.css";

// Declare global Xrm object for Dataverse Web API operations
declare const Xrm: {
    WebApi: {
        online: {
            execute: (request: unknown) => Promise<unknown>;
        };
    };
};

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        flex: 1,
        minHeight: 0,
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: "hidden",
        boxSizing: "border-box"
    },
    content: {
        flex: 1,
        overflow: "hidden",
        display: "flex",
        flexDirection: "column",
        minHeight: 0 // Important for flex child overflow
    },
    scrollableContent: {
        flex: 1,
        overflow: "auto",
        display: "flex",
        flexDirection: "column",
        minHeight: 0
    },
    scopeContent: {
        flex: 1,
        overflow: "auto",
        paddingTop: "12px",
        paddingBottom: "12px",
        paddingLeft: "16px",
        paddingRight: "16px",
        minHeight: 0
    },
    loadingContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flex: 1,
        gap: "16px"
    },
    footerWrapper: {
        flexShrink: 0,
        marginTop: "auto",
        backgroundColor: tokens.colorNeutralBackground2
    },
    version: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        textAlign: "center" as const,
        paddingTop: "4px",
        paddingBottom: "4px",
        backgroundColor: tokens.colorNeutralBackground1
    }
});

// Build date for version footer
const BUILD_DATE = new Date().toISOString().split("T")[0];
const VERSION = "2.7.0";

export const AnalysisBuilderApp: React.FC<IAnalysisBuilderAppProps> = (props) => {
    const styles = useStyles();
    const {
        documentId,
        documentName,
        webApi,
        onPlaybookSelect,
        onActionSelect,
        onSkillsSelect,
        onKnowledgeSelect,
        onToolsSelect,
        onExecute,
        onCancel
    } = props;

    // State
    const [isLoading, setIsLoading] = React.useState(true);
    const [isExecuting, setIsExecuting] = React.useState(false);
    const [error, setError] = React.useState<string | undefined>();

    // Data state
    const [playbooks, setPlaybooks] = React.useState<IPlaybook[]>([]);
    const [actions, setActions] = React.useState<IAction[]>([]);
    const [skills, setSkills] = React.useState<ISkill[]>([]);
    const [knowledge, setKnowledge] = React.useState<IKnowledge[]>([]);
    const [tools, setTools] = React.useState<ITool[]>([]);
    const [outputFormats, setOutputFormats] = React.useState<IOutputFormat[]>([]);

    // Selection state
    const [selectedPlaybook, setSelectedPlaybook] = React.useState<IPlaybook | undefined>();
    const [selectedActionId, setSelectedActionId] = React.useState<string | undefined>();
    const [selectedSkillIds, setSelectedSkillIds] = React.useState<string[]>([]);
    const [selectedKnowledgeIds, setSelectedKnowledgeIds] = React.useState<string[]>([]);
    const [selectedToolIds, setSelectedToolIds] = React.useState<string[]>([]);
    const [selectedOutputFormat, setSelectedOutputFormat] = React.useState<string | undefined>();

    // UI state
    const [activeTab, setActiveTab] = React.useState<ScopeTabId>("action");

    // Load initial data
    React.useEffect(() => {
        loadInitialData();
    }, []);

    const loadInitialData = async (): Promise<void> => {
        setIsLoading(true);
        setError(undefined);

        try {
            logInfo("AnalysisBuilderApp", "Loading initial data...");

            // Load playbooks, actions, skills, etc. from Dataverse
            await Promise.all([
                loadPlaybooks(),
                loadActions(),
                loadSkills(),
                loadKnowledge(),
                loadTools(),
                loadOutputFormats()
            ]);

            logInfo("AnalysisBuilderApp", "Initial data loaded successfully");
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : "Failed to load data";
            logError("AnalysisBuilderApp", "Error loading data", err);
            setError(errorMessage);
        } finally {
            setIsLoading(false);
        }
    };

    const loadPlaybooks = async (): Promise<void> => {
        try {
            const result = await webApi.retrieveMultipleRecords(
                "sprk_analysisplaybook",
                "?$select=sprk_analysisplaybookid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name"
            );

            const loadedPlaybooks: IPlaybook[] = result.entities.map((entity: ComponentFramework.WebApi.Entity) => ({
                id: entity.sprk_analysisplaybookid as string,
                name: entity.sprk_name as string,
                description: entity.sprk_description as string || "",
                icon: "Lightbulb", // Default icon - field doesn't exist in Dataverse
                isDefault: false
            }));

            setPlaybooks(loadedPlaybooks);

            // Select default playbook if available
            const defaultPlaybook = loadedPlaybooks.find(p => p.isDefault);
            if (defaultPlaybook) {
                handlePlaybookSelect(defaultPlaybook);
            }
        } catch (err) {
            logError("AnalysisBuilderApp", "Error loading playbooks", err);
            // Set sample data for development
            setPlaybooks(getSamplePlaybooks());
        }
    };

    const loadActions = async (): Promise<void> => {
        try {
            const result = await webApi.retrieveMultipleRecords(
                "sprk_analysisaction",
                "?$select=sprk_analysisactionid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name"
            );

            const loadedActions: IAction[] = result.entities.map((entity: ComponentFramework.WebApi.Entity) => ({
                id: entity.sprk_analysisactionid as string,
                name: entity.sprk_name as string,
                description: entity.sprk_description as string || "",
                icon: "Play", // Default icon - field doesn't exist in Dataverse
                isSelected: false
            }));

            setActions(loadedActions);
        } catch (err) {
            logError("AnalysisBuilderApp", "Error loading actions", err);
            setActions(getSampleActions());
        }
    };

    const loadSkills = async (): Promise<void> => {
        try {
            const result = await webApi.retrieveMultipleRecords(
                "sprk_analysisskill",
                "?$select=sprk_analysisskillid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name"
            );

            const loadedSkills: ISkill[] = result.entities.map((entity: ComponentFramework.WebApi.Entity) => ({
                id: entity.sprk_analysisskillid as string,
                name: entity.sprk_name as string,
                description: entity.sprk_description as string || "",
                icon: "Brain", // Default icon - field doesn't exist in Dataverse
                type: "analysis" as const,
                isSelected: false
            }));

            setSkills(loadedSkills);
        } catch (err) {
            logError("AnalysisBuilderApp", "Error loading skills", err);
            setSkills(getSampleSkills());
        }
    };

    const loadKnowledge = async (): Promise<void> => {
        try {
            const result = await webApi.retrieveMultipleRecords(
                "sprk_analysisknowledge",
                "?$select=sprk_analysisknowledgeid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name"
            );

            const loadedKnowledge: IKnowledge[] = result.entities.map((entity: ComponentFramework.WebApi.Entity) => ({
                id: entity.sprk_analysisknowledgeid as string,
                name: entity.sprk_name as string,
                description: entity.sprk_description as string || "",
                icon: "Library", // Default icon - field doesn't exist in Dataverse
                source: "dataverse" as const, // Default source - field doesn't exist in Dataverse
                isSelected: false
            }));

            setKnowledge(loadedKnowledge);
        } catch (err) {
            logError("AnalysisBuilderApp", "Error loading knowledge", err);
            setKnowledge(getSampleKnowledge());
        }
    };

    const loadTools = async (): Promise<void> => {
        try {
            const result = await webApi.retrieveMultipleRecords(
                "sprk_analysistool",
                "?$select=sprk_analysistoolid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name"
            );

            const loadedTools: ITool[] = result.entities.map((entity: ComponentFramework.WebApi.Entity) => ({
                id: entity.sprk_analysistoolid as string,
                name: entity.sprk_name as string,
                description: entity.sprk_description as string || "",
                icon: "Wrench", // Default icon - field doesn't exist in Dataverse
                toolType: "api" as const, // Default type - field doesn't exist in Dataverse
                isSelected: false
            }));

            setTools(loadedTools);
        } catch (err) {
            logError("AnalysisBuilderApp", "Error loading tools", err);
            setTools(getSampleTools());
        }
    };

    const loadOutputFormats = async (): Promise<void> => {
        // Output formats are typically static, but could be loaded from config
        setOutputFormats([
            { id: "markdown", name: "Markdown", description: "Rich text format with headers and formatting", icon: "Document", format: "markdown", isSelected: true },
            { id: "json", name: "JSON", description: "Structured data format", icon: "Code", format: "json", isSelected: false },
            { id: "html", name: "HTML", description: "Web-ready format", icon: "Globe", format: "html", isSelected: false }
        ]);
        setSelectedOutputFormat("markdown");
    };

    // Event handlers
    const handlePlaybookSelect = (playbook: IPlaybook): void => {
        setSelectedPlaybook(playbook);
        onPlaybookSelect(playbook.id);
        logInfo("AnalysisBuilderApp", `Selected playbook: ${playbook.name}`);

        // Apply playbook configuration
        if (playbook.actionId) {
            setSelectedActionId(playbook.actionId);
            onActionSelect(playbook.actionId);
        }
        if (playbook.skillIds) {
            setSelectedSkillIds(playbook.skillIds);
            onSkillsSelect(playbook.skillIds);
        }
        if (playbook.knowledgeIds) {
            setSelectedKnowledgeIds(playbook.knowledgeIds);
            onKnowledgeSelect(playbook.knowledgeIds);
        }
        if (playbook.toolIds) {
            setSelectedToolIds(playbook.toolIds);
            onToolsSelect(playbook.toolIds);
        }
    };

    const handleTabChange = (tabId: ScopeTabId): void => {
        setActiveTab(tabId);
        logInfo("AnalysisBuilderApp", `Tab changed to: ${tabId}`);
    };

    const handleActionSelectionChange = (selectedIds: string[]): void => {
        const actionId = selectedIds[0]; // Single select for action
        setSelectedActionId(actionId);
        onActionSelect(actionId);
    };

    const handleSkillsSelectionChange = (selectedIds: string[]): void => {
        setSelectedSkillIds(selectedIds);
        onSkillsSelect(selectedIds);
    };

    const handleKnowledgeSelectionChange = (selectedIds: string[]): void => {
        setSelectedKnowledgeIds(selectedIds);
        onKnowledgeSelect(selectedIds);
    };

    const handleToolsSelectionChange = (selectedIds: string[]): void => {
        setSelectedToolIds(selectedIds);
        onToolsSelect(selectedIds);
    };

    const handleOutputFormatChange = (selectedIds: string[]): void => {
        setSelectedOutputFormat(selectedIds[0]);
    };

    const handleSavePlaybook = async (): Promise<void> => {
        logInfo("AnalysisBuilderApp", "Save playbook clicked");
        // TODO: Implement save playbook
    };

    const handleSaveAs = async (): Promise<void> => {
        logInfo("AnalysisBuilderApp", "Save As clicked");
        // TODO: Implement save as new playbook
    };

    const handleExecute = async (): Promise<void> => {
        // Validate required fields
        if (!documentId) {
            setError("Document ID is required. Please ensure a document is selected.");
            logError("AnalysisBuilderApp", "Missing documentId", { documentId });
            return;
        }

        if (!selectedActionId) {
            setError("Please select an action before executing.");
            return;
        }

        setIsExecuting(true);
        setError(undefined);

        try {
            logInfo("AnalysisBuilderApp", "Executing analysis...", {
                documentId,
                actionId: selectedActionId,
                skillIds: selectedSkillIds,
                knowledgeIds: selectedKnowledgeIds,
                toolIds: selectedToolIds
            });

            // Create analysis record in Dataverse
            // Lookup fields use @odata.bind syntax
            // Note: statuscode defaults automatically, no need to set it
            const analysisRecord: Record<string, unknown> = {
                "sprk_name": `Analysis - ${documentName || "Document"}`,
                "sprk_documentid@odata.bind": `/sprk_documents(${documentId})`
            };

            // Add action lookup (required)
            if (selectedActionId) {
                analysisRecord["sprk_actionid@odata.bind"] = `/sprk_analysisactions(${selectedActionId})`;
            }

            // Add playbook lookup (optional)
            if (selectedPlaybook) {
                analysisRecord["sprk_playbookid@odata.bind"] = `/sprk_analysisplaybooks(${selectedPlaybook.id})`;
            }

            // Create the analysis record first
            const result = await webApi.createRecord("sprk_analysis", analysisRecord);
            const analysisId = result.id;
            logInfo("AnalysisBuilderApp", `Analysis record created: ${analysisId}`);

            // Associate N:N relationships after record creation using Xrm.WebApi
            // Skills (N:N relationship: sprk_analysis_skill)
            for (const skillId of selectedSkillIds) {
                try {
                    await (Xrm as any).WebApi.online.execute({
                        getMetadata: () => ({
                            boundParameter: undefined,
                            parameterTypes: {},
                            operationType: 2, // Associate
                            operationName: "Associate"
                        }),
                        target: { entityType: "sprk_analysis", id: analysisId },
                        relatedEntities: [{ entityType: "sprk_analysisskill", id: skillId }],
                        relationship: "sprk_analysis_skill"
                    });
                } catch (assocErr) {
                    logError("AnalysisBuilderApp", `Failed to associate skill ${skillId}`, assocErr);
                }
            }

            // Knowledge (N:N relationship: sprk_analysis_knowledge)
            for (const knowledgeId of selectedKnowledgeIds) {
                try {
                    await (Xrm as any).WebApi.online.execute({
                        getMetadata: () => ({
                            boundParameter: undefined,
                            parameterTypes: {},
                            operationType: 2,
                            operationName: "Associate"
                        }),
                        target: { entityType: "sprk_analysis", id: analysisId },
                        relatedEntities: [{ entityType: "sprk_analysisknowledge", id: knowledgeId }],
                        relationship: "sprk_analysis_knowledge"
                    });
                } catch (assocErr) {
                    logError("AnalysisBuilderApp", `Failed to associate knowledge ${knowledgeId}`, assocErr);
                }
            }

            // Tools (N:N relationship: sprk_analysis_tool)
            for (const toolId of selectedToolIds) {
                try {
                    await (Xrm as any).WebApi.online.execute({
                        getMetadata: () => ({
                            boundParameter: undefined,
                            parameterTypes: {},
                            operationType: 2,
                            operationName: "Associate"
                        }),
                        target: { entityType: "sprk_analysis", id: analysisId },
                        relatedEntities: [{ entityType: "sprk_analysistool", id: toolId }],
                        relationship: "sprk_analysis_tool"
                    });
                } catch (assocErr) {
                    logError("AnalysisBuilderApp", `Failed to associate tool ${toolId}`, assocErr);
                }
            }

            logInfo("AnalysisBuilderApp", `Analysis created with associations: ${analysisId}`);
            onExecute(analysisId);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : "Failed to create analysis";
            logError("AnalysisBuilderApp", "Error creating analysis", err);
            setError(errorMessage);
        } finally {
            setIsExecuting(false);
        }
    };

    // Build tabs with counts
    const tabs: IScopeTab[] = [
        { id: "action", label: "Action", count: selectedActionId ? 1 : 0, icon: "Play" },
        { id: "skills", label: "Skills", count: selectedSkillIds.length, icon: "Brain" },
        { id: "knowledge", label: "Knowledge", count: selectedKnowledgeIds.length, icon: "Library" },
        { id: "tools", label: "Tools", count: selectedToolIds.length, icon: "Wrench" },
        { id: "output", label: "Output", count: selectedOutputFormat ? 1 : 0, icon: "Document" }
    ];

    // Render current tab content
    const renderTabContent = (): React.ReactElement => {
        switch (activeTab) {
            case "action":
                return (
                    <ScopeList
                        items={actions.map(a => ({ ...a, isSelected: a.id === selectedActionId }))}
                        onSelectionChange={handleActionSelectionChange}
                        isLoading={isLoading}
                        emptyMessage="No actions available"
                        multiSelect={false}
                    />
                );
            case "skills":
                return (
                    <ScopeList
                        items={skills.map(s => ({ ...s, isSelected: selectedSkillIds.includes(s.id) }))}
                        onSelectionChange={handleSkillsSelectionChange}
                        isLoading={isLoading}
                        emptyMessage="No skills available"
                        multiSelect={true}
                    />
                );
            case "knowledge":
                return (
                    <ScopeList
                        items={knowledge.map(k => ({ ...k, isSelected: selectedKnowledgeIds.includes(k.id) }))}
                        onSelectionChange={handleKnowledgeSelectionChange}
                        isLoading={isLoading}
                        emptyMessage="No knowledge sources available"
                        multiSelect={true}
                    />
                );
            case "tools":
                return (
                    <ScopeList
                        items={tools.map(t => ({ ...t, isSelected: selectedToolIds.includes(t.id) }))}
                        onSelectionChange={handleToolsSelectionChange}
                        isLoading={isLoading}
                        emptyMessage="No tools available"
                        multiSelect={true}
                    />
                );
            case "output":
                return (
                    <ScopeList
                        items={outputFormats.map(o => ({ ...o, isSelected: o.id === selectedOutputFormat }))}
                        onSelectionChange={handleOutputFormatChange}
                        isLoading={isLoading}
                        emptyMessage="No output formats available"
                        multiSelect={false}
                    />
                );
            default:
                return <div>Select a tab</div>;
        }
    };

    // Loading state
    if (isLoading && playbooks.length === 0) {
        return (
            <div className={styles.container}>
                <div className={styles.loadingContainer}>
                    <Spinner size="large" label="Loading analysis configuration..." />
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            {/* Error message */}
            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
            )}

            {/* Scrollable Content Area */}
            <div className={styles.content}>
                <div className={styles.scrollableContent}>
                    {/* Playbook Selector */}
                    <PlaybookSelector
                        playbooks={playbooks}
                        selectedPlaybookId={selectedPlaybook?.id}
                        onSelect={handlePlaybookSelect}
                        isLoading={isLoading}
                    />

                    {/* Scope Tabs */}
                    <ScopeTabs
                        activeTab={activeTab}
                        tabs={tabs}
                        onTabChange={handleTabChange}
                    />

                    {/* Tab Content - scrollable */}
                    <div className={styles.scopeContent}>
                        {renderTabContent()}
                    </div>
                </div>
            </div>

            {/* Footer - fixed at bottom */}
            <div className={styles.footerWrapper}>
                <FooterActions
                    onSavePlaybook={handleSavePlaybook}
                    onSaveAs={handleSaveAs}
                    onCancel={onCancel}
                    onExecute={handleExecute}
                    isExecuting={isExecuting}
                    canSave={!!selectedPlaybook}
                    canExecute={!!selectedActionId}
                />
                {/* Version footer */}
                <div className={styles.version}>
                    v{VERSION} • Built {BUILD_DATE}
                </div>
            </div>
        </div>
    );
};

// ─────────────────────────────────────────────────────────────────────────────
// Sample Data (for development)
// ─────────────────────────────────────────────────────────────────────────────

function getSamplePlaybooks(): IPlaybook[] {
    return [
        { id: "1", name: "Document Summary", description: "Generate a comprehensive summary", icon: "DocumentText", isDefault: true },
        { id: "2", name: "Contract Analysis", description: "Extract key contract terms", icon: "Certificate", isDefault: false },
        { id: "3", name: "Compliance Check", description: "Check regulatory compliance", icon: "Shield", isDefault: false },
        { id: "4", name: "Custom Analysis", description: "Build your own analysis", icon: "Settings", isDefault: false }
    ];
}

function getSampleActions(): IAction[] {
    return [
        { id: "1", name: "Summarize Document", description: "Create a concise summary of the document content", icon: "TextDocument", isSelected: false },
        { id: "2", name: "Extract Key Information", description: "Identify and extract important data points", icon: "Filter", isSelected: false },
        { id: "3", name: "Generate Report", description: "Create a structured analysis report", icon: "ReportDocument", isSelected: false }
    ];
}

function getSampleSkills(): ISkill[] {
    return [
        { id: "1", name: "Entity Extraction", description: "Identify people, organizations, and locations", icon: "People", type: "extraction", isSelected: false },
        { id: "2", name: "Sentiment Analysis", description: "Determine document tone and sentiment", icon: "Emoji", type: "analysis", isSelected: false }
    ];
}

function getSampleKnowledge(): IKnowledge[] {
    return [
        { id: "1", name: "Company Policies", description: "Internal policy documents", icon: "Library", source: "sharepoint", isSelected: false }
    ];
}

function getSampleTools(): ITool[] {
    return [
        { id: "1", name: "Web Search", description: "Search the web for related information", icon: "Search", toolType: "search", isSelected: false }
    ];
}

export default AnalysisBuilderApp;
