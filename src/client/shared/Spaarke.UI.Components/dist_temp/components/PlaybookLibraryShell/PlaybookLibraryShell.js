/**
 * PlaybookLibraryShell — Shared playbook browsing + execution shell.
 *
 * Extracted from AnalysisBuilder/App.tsx (UDSS-020). Provides a 2-tab layout:
 *   Tab 1: Select Playbook — card grid with locked scope preview on selection
 *   Tab 2: Custom Scope — manual action/skills/knowledge/tools selection
 *
 * All Dataverse access is routed through the IDataService prop so the shell
 * remains portable across PCF controls, Code Pages, SPAs, and test harnesses.
 *
 * BFF API calls use the injected `authenticatedFetch` + `bffBaseUrl` props
 * instead of importing from solution-specific modules.
 */
import React from 'react';
import { TabList, Tab, Button, Text, Spinner, MessageBar, MessageBarBody, makeStyles, tokens, } from '@fluentui/react-components';
import { PlaybookCardGrid } from '../Playbook/PlaybookCardGrid';
import { ScopeConfigurator } from '../Playbook/ScopeConfigurator';
import { loadAllData, loadPlaybookScopes } from '../Playbook/playbookService';
import { createAndAssociate } from '../Playbook/analysisService';
import { IntentWizardFlow, INTENT_PLAYBOOK_MAP } from './IntentWizardFlow';
import { DocumentSelector } from './DocumentSelector';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        backgroundColor: tokens.colorNeutralBackground1,
    },
    header: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        paddingTop: tokens.spacingVerticalL,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    tabBar: {
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke1,
        flexShrink: 0,
    },
    content: {
        flex: 1,
        overflow: 'auto',
        paddingTop: tokens.spacingVerticalL,
        paddingBottom: tokens.spacingVerticalL,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        minHeight: 0,
    },
    scopePreview: {
        marginTop: tokens.spacingVerticalL,
    },
    footer: {
        display: 'flex',
        justifyContent: 'flex-end',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        borderTopWidth: '1px',
        borderTopStyle: 'solid',
        borderTopColor: tokens.colorNeutralStroke2,
        backgroundColor: tokens.colorNeutralBackground2,
        flexShrink: 0,
    },
    loading: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flex: 1,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const PlaybookLibraryShell = ({ entityType, entityId, documentIds, allowedPlaybookIds, mode = 'browse', embedded = false, intent, onComplete, onClose, dataService, authenticatedFetch, bffBaseUrl, entityDisplayName, executeButtonLabel = 'Run Analysis', title = 'New Analysis', }) => {
    const styles = useStyles();
    // ---------------------------------------------------------------------------
    // Document selector state
    //
    // When documentIds contains 2+ entries a DocumentSelector bar is shown.
    // The activeDocumentId drives analysis execution instead of the raw entityId.
    // ---------------------------------------------------------------------------
    /** True when the caller supplied 2+ document IDs. */
    const hasMultipleDocuments = (documentIds?.length ?? 0) >= 2;
    /**
     * Derive the initial active document ID:
     *  1. If entityId matches one of the provided documentIds, start with it.
     *  2. Otherwise fall back to the first ID in the list.
     *  3. If documentIds is absent/empty, use entityId as-is.
     */
    const initialDocumentId = React.useMemo(() => {
        if (!documentIds || documentIds.length === 0)
            return entityId;
        if (documentIds.includes(entityId))
            return entityId;
        return documentIds[0];
    }, [documentIds, entityId]);
    const [activeDocumentId, setActiveDocumentId] = React.useState(initialDocumentId);
    /**
     * The document ID to use for analysis execution.
     * When documentIds is provided, this is the user-selected document;
     * otherwise it falls through to the plain entityId prop.
     */
    const effectiveDocumentId = hasMultipleDocuments ? activeDocumentId : entityId;
    // --- Data state ---
    const [isLoading, setIsLoading] = React.useState(true);
    const [error, setError] = React.useState(null);
    const [playbooks, setPlaybooks] = React.useState([]);
    const [actions, setActions] = React.useState([]);
    const [skills, setSkills] = React.useState([]);
    const [knowledge, setKnowledge] = React.useState([]);
    const [tools, setTools] = React.useState([]);
    // --- Selection state ---
    const [activeTab, setActiveTab] = React.useState('playbook');
    const [selectedPlaybook, setSelectedPlaybook] = React.useState(null);
    const [playbookScopes, setPlaybookScopes] = React.useState(null);
    // Custom scope selection (Tab 2)
    const [selectedActionIds, setSelectedActionIds] = React.useState([]);
    const [selectedSkillIds, setSelectedSkillIds] = React.useState([]);
    const [selectedKnowledgeIds, setSelectedKnowledgeIds] = React.useState([]);
    const [selectedToolIds, setSelectedToolIds] = React.useState([]);
    // --- Execution state ---
    const [isExecuting, setIsExecuting] = React.useState(false);
    const [successMessage, setSuccessMessage] = React.useState(null);
    // --- Build a webApi-compatible adapter from IDataService (for playbookService reads) ---
    const webApiAdapter = React.useMemo(() => ({
        retrieveMultipleRecords: (entityName, options) => dataService.retrieveMultipleRecords(entityName, options),
        retrieveRecord: (entityName, id, options) => dataService.retrieveRecord(entityName, id, options),
        createRecord: async (entityName, data) => {
            const id = await dataService.createRecord(entityName, data);
            return { id };
        },
    }), [dataService]);
    // --- Load data on mount ---
    React.useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const data = await loadAllData(webApiAdapter);
                if (cancelled)
                    return;
                // Apply allowlist filter if provided
                let filteredPlaybooks = data.playbooks;
                if (allowedPlaybookIds && allowedPlaybookIds.length > 0) {
                    const allowSet = new Set(allowedPlaybookIds);
                    filteredPlaybooks = data.playbooks.filter(p => allowSet.has(p.id));
                }
                setPlaybooks(filteredPlaybooks);
                setActions(data.actions);
                setSkills(data.skills);
                setKnowledge(data.knowledge);
                setTools(data.tools);
                // Intent mode: auto-select matching playbook.
                // 1. Try INTENT_PLAYBOOK_MAP for a known intent -> playbook ID mapping.
                // 2. Fall back to fuzzy name matching against available playbooks.
                if (mode === 'intent' && intent && filteredPlaybooks.length > 0) {
                    const mappedPlaybookId = INTENT_PLAYBOOK_MAP[intent];
                    let match;
                    if (mappedPlaybookId) {
                        match = filteredPlaybooks.find(p => p.id === mappedPlaybookId);
                    }
                    // Fallback: fuzzy name match
                    if (!match) {
                        const intentLower = intent.toLowerCase();
                        match = filteredPlaybooks.find(p => p.name.toLowerCase().includes(intentLower));
                    }
                    if (match) {
                        try {
                            const scopes = await loadPlaybookScopes(webApiAdapter, match.id);
                            if (!cancelled) {
                                setSelectedPlaybook(match);
                                setPlaybookScopes(scopes);
                            }
                        }
                        catch (err) {
                            console.error('[PlaybookLibraryShell] Failed to load intent playbook scopes:', err);
                        }
                    }
                }
            }
            catch (err) {
                if (cancelled)
                    return;
                setError(err instanceof Error ? err.message : 'Failed to load data');
            }
            finally {
                if (!cancelled)
                    setIsLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [webApiAdapter, allowedPlaybookIds, mode, intent]);
    // --- Playbook selection handler ---
    const handlePlaybookSelect = React.useCallback(async (playbook) => {
        setSelectedPlaybook(playbook);
        try {
            const scopes = await loadPlaybookScopes(webApiAdapter, playbook.id);
            setPlaybookScopes(scopes);
        }
        catch (err) {
            console.error('[PlaybookLibraryShell] Failed to load playbook scopes:', err);
            setPlaybookScopes(null);
        }
    }, [webApiAdapter]);
    // --- Tab change handler ---
    const handleTabSelect = React.useCallback((_event, data) => {
        setActiveTab(data.value);
    }, []);
    // --- Intent mode derived flag ---
    const isIntentMode = mode === 'intent' && !!intent && selectedPlaybook !== null && playbookScopes !== null;
    // --- Can execute? ---
    const canExecute = React.useMemo(() => {
        if (!effectiveDocumentId)
            return false;
        // Intent mode: always ready once playbook + scopes are resolved
        if (isIntentMode)
            return true;
        if (activeTab === 'playbook') {
            return selectedPlaybook !== null && playbookScopes !== null;
        }
        // Custom scope: need at least an action
        return selectedActionIds.length > 0;
    }, [activeTab, selectedPlaybook, playbookScopes, selectedActionIds, effectiveDocumentId, isIntentMode]);
    // --- Run Analysis handler ---
    const handleExecute = React.useCallback(async () => {
        if (!canExecute)
            return;
        setIsExecuting(true);
        setError(null);
        try {
            let config;
            if (activeTab === 'playbook' && selectedPlaybook && playbookScopes) {
                config = {
                    documentId: effectiveDocumentId,
                    playbookId: selectedPlaybook.id,
                    actionId: playbookScopes.actionIds[0] ?? '',
                    skillIds: playbookScopes.skillIds,
                    knowledgeIds: playbookScopes.knowledgeIds,
                    toolIds: playbookScopes.toolIds,
                };
            }
            else {
                config = {
                    documentId: effectiveDocumentId,
                    actionId: selectedActionIds[0] ?? '',
                    skillIds: selectedSkillIds,
                    knowledgeIds: selectedKnowledgeIds,
                    toolIds: selectedToolIds,
                };
            }
            if (!authenticatedFetch || !bffBaseUrl) {
                throw new Error('authenticatedFetch and bffBaseUrl are required to create an analysis.');
            }
            const analysisId = await createAndAssociate(authenticatedFetch, bffBaseUrl, config);
            if (onComplete) {
                onComplete({ analysisId });
            }
            else {
                setSuccessMessage('Analysis created successfully.');
            }
        }
        catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to create analysis');
        }
        finally {
            setIsExecuting(false);
        }
    }, [
        canExecute,
        activeTab,
        selectedPlaybook,
        playbookScopes,
        selectedActionIds,
        selectedSkillIds,
        selectedKnowledgeIds,
        selectedToolIds,
        effectiveDocumentId,
        authenticatedFetch,
        bffBaseUrl,
        onComplete,
    ]);
    // --- Cancel / Close handler ---
    const handleCancel = React.useCallback(() => {
        if (onClose) {
            onClose();
        }
    }, [onClose]);
    // --- Render: success state ---
    if (successMessage) {
        return (React.createElement("div", { className: styles.root },
            !embedded && (React.createElement("div", { className: styles.header },
                React.createElement(Text, { size: 500, weight: "semibold" }, title))),
            React.createElement("div", { className: styles.content },
                React.createElement(MessageBar, { intent: "success" },
                    React.createElement(MessageBarBody, null, successMessage))),
            React.createElement("div", { className: styles.footer },
                React.createElement(Button, { appearance: "primary", onClick: handleCancel }, "Close"))));
    }
    // --- Render: loading state ---
    if (isLoading) {
        return (React.createElement("div", { className: styles.root },
            React.createElement("div", { className: styles.loading },
                React.createElement(Spinner, { size: "large", label: "Loading playbooks..." }))));
    }
    // --- Render: main UI ---
    return (React.createElement("div", { className: styles.root },
        !embedded && (React.createElement("div", { className: styles.header },
            React.createElement(Text, { size: 500, weight: "semibold" }, title),
            entityDisplayName && (React.createElement(Text, { size: 200, style: { color: tokens.colorNeutralForeground3 } }, entityDisplayName)))),
        hasMultipleDocuments && documentIds && (React.createElement(DocumentSelector, { documentIds: documentIds, selectedDocumentId: activeDocumentId, onSelect: setActiveDocumentId, dataService: dataService })),
        isIntentMode ? (React.createElement(React.Fragment, null,
            error && (React.createElement(MessageBar, { intent: "error", style: { margin: tokens.spacingVerticalS } },
                React.createElement(MessageBarBody, null, error))),
            React.createElement("div", { className: styles.content },
                React.createElement(IntentWizardFlow, { playbook: selectedPlaybook, playbookScopes: playbookScopes, actions: actions, skills: skills, knowledge: knowledge, tools: tools, isExecuting: isExecuting, error: error })))) : (React.createElement(React.Fragment, null,
            React.createElement("div", { className: styles.tabBar },
                React.createElement(TabList, { selectedValue: activeTab, onTabSelect: handleTabSelect, size: "medium" },
                    React.createElement(Tab, { value: "playbook" }, "Select Playbook"),
                    React.createElement(Tab, { value: "custom" }, "Custom Scope"))),
            error && (React.createElement(MessageBar, { intent: "error", style: { margin: tokens.spacingVerticalS } },
                React.createElement(MessageBarBody, null, error))),
            React.createElement("div", { className: styles.content }, activeTab === 'playbook' ? (React.createElement(React.Fragment, null,
                React.createElement(PlaybookCardGrid, { playbooks: playbooks, selectedId: selectedPlaybook?.id, onSelect: handlePlaybookSelect, isLoading: false }))) : (React.createElement(ScopeConfigurator, { actions: actions, skills: skills, knowledge: knowledge, tools: tools, selectedActionIds: selectedActionIds, selectedSkillIds: selectedSkillIds, selectedKnowledgeIds: selectedKnowledgeIds, selectedToolIds: selectedToolIds, onActionChange: setSelectedActionIds, onSkillChange: setSelectedSkillIds, onKnowledgeChange: setSelectedKnowledgeIds, onToolChange: setSelectedToolIds }))))),
        React.createElement("div", { className: styles.footer },
            React.createElement(Button, { appearance: "secondary", onClick: handleCancel, disabled: isExecuting }, "Cancel"),
            React.createElement(Button, { appearance: "primary", onClick: handleExecute, disabled: !canExecute || isExecuting }, isExecuting ? 'Creating...' : executeButtonLabel))));
};
export default PlaybookLibraryShell;
//# sourceMappingURL=PlaybookLibraryShell.js.map