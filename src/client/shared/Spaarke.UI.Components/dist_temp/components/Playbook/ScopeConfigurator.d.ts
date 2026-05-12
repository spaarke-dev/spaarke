/**
 * ScopeConfigurator — Tabbed scope configuration for analysis.
 *
 * Combines a TabList (Action, Skills, Knowledge, Tools) with ScopeList
 * for each tab. Supports readOnly mode for locked playbook scopes.
 */
import React from 'react';
import type { IAction, ISkill, IKnowledge, ITool } from './types';
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
export declare const ScopeConfigurator: React.FC<IScopeConfiguratorProps>;
export default ScopeConfigurator;
//# sourceMappingURL=ScopeConfigurator.d.ts.map