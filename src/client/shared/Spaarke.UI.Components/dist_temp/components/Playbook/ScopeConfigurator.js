/**
 * ScopeConfigurator — Tabbed scope configuration for analysis.
 *
 * Combines a TabList (Action, Skills, Knowledge, Tools) with ScopeList
 * for each tab. Supports readOnly mode for locked playbook scopes.
 */
import React, { useState, useCallback } from 'react';
import { TabList, Tab, Badge, Text, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { Play24Regular, BrainCircuit24Regular, Library24Regular, Wrench24Regular } from '@fluentui/react-icons';
import { ScopeList } from './ScopeList';
const TAB_CONFIGS = [
    { id: 'action', label: 'Action', icon: React.createElement(Play24Regular, null) },
    { id: 'skills', label: 'Skills', icon: React.createElement(BrainCircuit24Regular, null) },
    { id: 'knowledge', label: 'Knowledge', icon: React.createElement(Library24Regular, null) },
    { id: 'tools', label: 'Tools', icon: React.createElement(Wrench24Regular, null) },
];
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minHeight: 0,
    },
    tabBar: {
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke1,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        flexShrink: 0,
    },
    tabContent: {
        flex: 1,
        overflow: 'auto',
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        minHeight: 0,
    },
    tabLabel: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
    },
    badge: {
        marginLeft: tokens.spacingHorizontalXS,
    },
    readOnlyBanner: {
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalL,
        backgroundColor: tokens.colorNeutralBackground4,
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const ScopeConfigurator = ({ actions, skills, knowledge, tools, selectedActionIds, selectedSkillIds, selectedKnowledgeIds, selectedToolIds, onActionChange, onSkillChange, onKnowledgeChange, onToolChange, isLoading = false, readOnly = false, }) => {
    const styles = useStyles();
    const [activeTab, setActiveTab] = useState('action');
    const handleTabSelect = useCallback((_event, data) => {
        setActiveTab(data.value);
    }, []);
    /** Get the selection count for a given tab. */
    const getCount = (tabId) => {
        switch (tabId) {
            case 'action':
                return selectedActionIds.length;
            case 'skills':
                return selectedSkillIds.length;
            case 'knowledge':
                return selectedKnowledgeIds.length;
            case 'tools':
                return selectedToolIds.length;
        }
    };
    /** Render the ScopeList for the active tab. */
    const renderTabContent = () => {
        if (isLoading) {
            return React.createElement(Spinner, { size: "medium", label: "Loading scope data..." });
        }
        switch (activeTab) {
            case 'action':
                return (React.createElement(ScopeList, { items: actions, selectedIds: selectedActionIds, onSelectionChange: onActionChange, isLoading: false, multiSelect: false, readOnly: readOnly, emptyMessage: "No actions available" }));
            case 'skills':
                return (React.createElement(ScopeList, { items: skills, selectedIds: selectedSkillIds, onSelectionChange: onSkillChange, isLoading: false, readOnly: readOnly, emptyMessage: "No skills available" }));
            case 'knowledge':
                return (React.createElement(ScopeList, { items: knowledge, selectedIds: selectedKnowledgeIds, onSelectionChange: onKnowledgeChange, isLoading: false, readOnly: readOnly, emptyMessage: "No knowledge sources available" }));
            case 'tools':
                return (React.createElement(ScopeList, { items: tools, selectedIds: selectedToolIds, onSelectionChange: onToolChange, isLoading: false, readOnly: readOnly, emptyMessage: "No tools available" }));
        }
    };
    return (React.createElement("div", { className: styles.container },
        readOnly && (React.createElement("div", { className: styles.readOnlyBanner },
            React.createElement(Text, { size: 200 }, "Scope is configured by the selected playbook and cannot be changed."))),
        React.createElement("div", { className: styles.tabBar },
            React.createElement(TabList, { selectedValue: activeTab, onTabSelect: handleTabSelect, size: "medium" }, TAB_CONFIGS.map(tab => {
                const count = getCount(tab.id);
                return (React.createElement(Tab, { key: tab.id, value: tab.id, icon: tab.icon },
                    React.createElement("span", { className: styles.tabLabel },
                        tab.label,
                        count > 0 && (React.createElement(Badge, { appearance: "filled", color: "brand", size: "small", className: styles.badge }, count)))));
            }))),
        React.createElement("div", { className: styles.tabContent }, renderTabContent())));
};
export default ScopeConfigurator;
//# sourceMappingURL=ScopeConfigurator.js.map