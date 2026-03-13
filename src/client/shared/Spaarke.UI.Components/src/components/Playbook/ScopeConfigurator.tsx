/**
 * ScopeConfigurator — Tabbed scope configuration for analysis.
 *
 * Combines a TabList (Action, Skills, Knowledge, Tools) with ScopeList
 * for each tab. Supports readOnly mode for locked playbook scopes.
 */

import React, { useState, useCallback } from "react";
import {
  TabList,
  Tab,
  Badge,
  Text,
  Spinner,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import {
  Play24Regular,
  BrainCircuit24Regular,
  Library24Regular,
  Wrench24Regular,
} from "@fluentui/react-icons";
import { ScopeList } from "./ScopeList";
import type { IAction, ISkill, IKnowledge, ITool, ScopeTabId } from "./types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IScopeConfiguratorProps {
  actions: IAction[];
  skills: ISkill[];
  knowledge: IKnowledge[];
  tools: ITool[];
  selectedActionIds: string[];
  selectedSkillIds: string[];
  selectedKnowledgeIds: string[];
  selectedToolIds: string[];
  onActionChange: (ids: string[]) => void;
  onSkillChange: (ids: string[]) => void;
  onKnowledgeChange: (ids: string[]) => void;
  onToolChange: (ids: string[]) => void;
  isLoading?: boolean;
  readOnly?: boolean;
}

// ---------------------------------------------------------------------------
// Tab configuration
// ---------------------------------------------------------------------------

interface ITabConfig {
  id: ScopeTabId;
  label: string;
  icon: React.ReactElement;
}

const TAB_CONFIGS: ITabConfig[] = [
  { id: "action", label: "Action", icon: <Play24Regular /> },
  { id: "skills", label: "Skills", icon: <BrainCircuit24Regular /> },
  { id: "knowledge", label: "Knowledge", icon: <Library24Regular /> },
  { id: "tools", label: "Tools", icon: <Wrench24Regular /> },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    minHeight: 0,
  },
  tabBar: {
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    flexShrink: 0,
  },
  tabContent: {
    flex: 1,
    overflow: "auto",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    minHeight: 0,
  },
  tabLabel: {
    display: "flex",
    alignItems: "center",
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

export const ScopeConfigurator: React.FC<IScopeConfiguratorProps> = ({
  actions,
  skills,
  knowledge,
  tools,
  selectedActionIds,
  selectedSkillIds,
  selectedKnowledgeIds,
  selectedToolIds,
  onActionChange,
  onSkillChange,
  onKnowledgeChange,
  onToolChange,
  isLoading = false,
  readOnly = false,
}) => {
  const styles = useStyles();
  const [activeTab, setActiveTab] = useState<ScopeTabId>("action");

  const handleTabSelect = useCallback(
    (_event: unknown, data: { value: unknown }) => {
      setActiveTab(data.value as ScopeTabId);
    },
    [],
  );

  /** Get the selection count for a given tab. */
  const getCount = (tabId: ScopeTabId): number => {
    switch (tabId) {
      case "action":
        return selectedActionIds.length;
      case "skills":
        return selectedSkillIds.length;
      case "knowledge":
        return selectedKnowledgeIds.length;
      case "tools":
        return selectedToolIds.length;
    }
  };

  /** Render the ScopeList for the active tab. */
  const renderTabContent = (): React.ReactNode => {
    if (isLoading) {
      return <Spinner size="medium" label="Loading scope data..." />;
    }

    switch (activeTab) {
      case "action":
        return (
          <ScopeList
            items={actions}
            selectedIds={selectedActionIds}
            onSelectionChange={onActionChange}
            isLoading={false}
            multiSelect={false}
            readOnly={readOnly}
            emptyMessage="No actions available"
          />
        );
      case "skills":
        return (
          <ScopeList
            items={skills}
            selectedIds={selectedSkillIds}
            onSelectionChange={onSkillChange}
            isLoading={false}
            readOnly={readOnly}
            emptyMessage="No skills available"
          />
        );
      case "knowledge":
        return (
          <ScopeList
            items={knowledge}
            selectedIds={selectedKnowledgeIds}
            onSelectionChange={onKnowledgeChange}
            isLoading={false}
            readOnly={readOnly}
            emptyMessage="No knowledge sources available"
          />
        );
      case "tools":
        return (
          <ScopeList
            items={tools}
            selectedIds={selectedToolIds}
            onSelectionChange={onToolChange}
            isLoading={false}
            readOnly={readOnly}
            emptyMessage="No tools available"
          />
        );
    }
  };

  return (
    <div className={styles.container}>
      {readOnly && (
        <div className={styles.readOnlyBanner}>
          <Text size={200}>
            Scope is configured by the selected playbook and cannot be changed.
          </Text>
        </div>
      )}

      <div className={styles.tabBar}>
        <TabList
          selectedValue={activeTab}
          onTabSelect={handleTabSelect}
          size="medium"
        >
          {TAB_CONFIGS.map((tab) => {
            const count = getCount(tab.id);
            return (
              <Tab key={tab.id} value={tab.id} icon={tab.icon}>
                <span className={styles.tabLabel}>
                  {tab.label}
                  {count > 0 && (
                    <Badge
                      appearance="filled"
                      color="brand"
                      size="small"
                      className={styles.badge}
                    >
                      {count}
                    </Badge>
                  )}
                </span>
              </Tab>
            );
          })}
        </TabList>
      </div>

      <div className={styles.tabContent}>{renderTabContent()}</div>
    </div>
  );
};

export default ScopeConfigurator;
