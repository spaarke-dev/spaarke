/**
 * ScopeSelector — Multi-select for Skills, Knowledge, and Tools.
 *
 * Reads from scopeStore, filters by node-type capabilities.
 * Three modes: skills checkbox list, knowledge checkbox list, tools checkbox list.
 * All three scope types use N:N relationships to PlaybookNode.
 */

import { memo, useEffect, useCallback } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Checkbox,
    Radio,
    RadioGroup,
    Spinner,
    Text,
} from "@fluentui/react-components";
import type { RadioGroupOnChangeData } from "@fluentui/react-components";
import { useScopeStore } from "../../stores/scopeStore";
import type { PlaybookNodeType } from "../../types/canvas";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface ScopeSelectorProps {
    nodeType: PlaybookNodeType;
    skillIds: string[];
    knowledgeIds: string[];
    toolIds: string[];
    showSkills?: boolean;
    showKnowledge?: boolean;
    showTools?: boolean;
    onSkillsChange: (ids: string[]) => void;
    onKnowledgeChange: (ids: string[]) => void;
    onToolsChange: (ids: string[]) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("4px"),
        flex: 1,
    },
    checkboxList: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("2px"),
        flex: 1,
        overflowY: "auto",
    },
    empty: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        fontStyle: "italic",
        ...shorthands.padding("4px", "0"),
    },
    disabledMessage: {
        color: tokens.colorNeutralForeground4,
        fontSize: tokens.fontSizeBase200,
        fontStyle: "italic",
        ...shorthands.padding("4px", "0"),
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ScopeSelector = memo(function ScopeSelector({
    nodeType,
    skillIds,
    knowledgeIds,
    toolIds,
    showSkills = false,
    showKnowledge = false,
    showTools = false,
    onSkillsChange,
    onKnowledgeChange,
    onToolsChange,
}: ScopeSelectorProps) {
    const styles = useStyles();

    const skills = useScopeStore((s) => s.skills);
    const knowledge = useScopeStore((s) => s.knowledge);
    const tools = useScopeStore((s) => s.tools);
    const isLoadingSkills = useScopeStore((s) => s.isLoadingSkills);
    const isLoadingKnowledge = useScopeStore((s) => s.isLoadingKnowledge);
    const isLoadingTools = useScopeStore((s) => s.isLoadingTools);
    const getCapabilities = useScopeStore((s) => s.getCapabilities);
    const loadAllScopes = useScopeStore((s) => s.loadAllScopes);

    const capabilities = getCapabilities(nodeType);

    // Lazy load scope data on first render
    useEffect(() => {
        if (skills.length === 0 && knowledge.length === 0 && tools.length === 0) {
            loadAllScopes();
        }
    }, [skills.length, knowledge.length, tools.length, loadAllScopes]);

    const handleSkillToggle = useCallback(
        (skillId: string, checked: boolean) => {
            if (checked) {
                onSkillsChange([...skillIds, skillId]);
            } else {
                onSkillsChange(skillIds.filter((id) => id !== skillId));
            }
        },
        [skillIds, onSkillsChange],
    );

    const handleKnowledgeToggle = useCallback(
        (knowledgeId: string, checked: boolean) => {
            if (checked) {
                onKnowledgeChange([...knowledgeIds, knowledgeId]);
            } else {
                onKnowledgeChange(knowledgeIds.filter((id) => id !== knowledgeId));
            }
        },
        [knowledgeIds, onKnowledgeChange],
    );

    const handleToolToggle = useCallback(
        (toolId: string, checked: boolean) => {
            if (checked) {
                onToolsChange([...toolIds, toolId]);
            } else {
                onToolsChange(toolIds.filter((id) => id !== toolId));
            }
        },
        [toolIds, onToolsChange],
    );

    // Single-select handler for tools (one tool per node)
    const handleToolSelect = useCallback(
        (_e: React.FormEvent<HTMLDivElement>, data: RadioGroupOnChangeData) => {
            if (data.value === "__none__") {
                onToolsChange([]);
            } else {
                onToolsChange([data.value]);
            }
        },
        [onToolsChange],
    );

    // Skills section
    if (showSkills) {
        if (!capabilities.allowsSkills) {
            return <Text className={styles.disabledMessage}>This node type does not support skills.</Text>;
        }
        if (isLoadingSkills) {
            return <Spinner size="tiny" label="Loading skills..." />;
        }
        if (skills.length === 0) {
            return <Text className={styles.empty}>No skills available.</Text>;
        }
        return (
            <div className={styles.checkboxList}>
                {skills.map((skill) => (
                    <Checkbox
                        key={skill.id}
                        size="medium"
                        label={skill.name}
                        checked={skillIds.includes(skill.id)}
                        onChange={(_, data) => handleSkillToggle(skill.id, data.checked === true)}
                    />
                ))}
            </div>
        );
    }

    // Knowledge section
    if (showKnowledge) {
        if (!capabilities.allowsKnowledge) {
            return <Text className={styles.disabledMessage}>This node type does not support knowledge sources.</Text>;
        }
        if (isLoadingKnowledge) {
            return <Spinner size="tiny" label="Loading knowledge..." />;
        }
        if (knowledge.length === 0) {
            return <Text className={styles.empty}>No knowledge sources available.</Text>;
        }
        return (
            <div className={styles.checkboxList}>
                {knowledge.map((k) => (
                    <Checkbox
                        key={k.id}
                        size="medium"
                        label={k.name}
                        checked={knowledgeIds.includes(k.id)}
                        onChange={(_, data) => handleKnowledgeToggle(k.id, data.checked === true)}
                    />
                ))}
            </div>
        );
    }

    // Tools section — single-select (one tool per node)
    if (showTools) {
        if (!capabilities.allowsTools) {
            return <Text className={styles.disabledMessage}>This node type does not support tools.</Text>;
        }
        if (isLoadingTools) {
            return <Spinner size="tiny" label="Loading tools..." />;
        }
        if (tools.length === 0) {
            return <Text className={styles.empty}>No tools available.</Text>;
        }
        return (
            <RadioGroup
                className={styles.checkboxList}
                value={toolIds.length > 0 ? toolIds[0] : "__none__"}
                onChange={handleToolSelect}
            >
                <Radio key="__none__" value="__none__" label="(None)" />
                {tools.map((tool) => (
                    <Radio
                        key={tool.id}
                        value={tool.id}
                        label={tool.name}
                    />
                ))}
            </RadioGroup>
        );
    }

    return null;
});
