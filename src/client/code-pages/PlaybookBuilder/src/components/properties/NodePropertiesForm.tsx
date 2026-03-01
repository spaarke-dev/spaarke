/**
 * NodePropertiesForm — Main properties form for a selected playbook node.
 *
 * Renders collapsible Accordion sections:
 *   - Basic (always): Name, Output Variable
 *   - AI Model (aiAnalysis, aiCompletion only): ModelSelector
 *   - Type-Specific Config: DeliverOutputForm | SendEmailForm | CreateTaskForm | AiCompletionForm | WaitForm
 *   - Skills (capability-dependent): ScopeSelector
 *   - Knowledge (capability-dependent): ScopeSelector
 *   - Tools (capability-dependent): ScopeSelector
 *   - Condition (condition nodes only): ConditionEditor
 *   - Runtime Settings: Timeout, Retry Count
 *
 * All changes flow through canvasStore.updateNodeData().
 */

import { memo, useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Accordion,
    AccordionItem,
    AccordionHeader,
    AccordionPanel,
    Input,
    Label,
    SpinButton,
    Button,
    Text,
    Divider,
} from "@fluentui/react-components";
import { Delete20Regular } from "@fluentui/react-icons";
import type { PlaybookNode } from "../../types/canvas";
import { useCanvasStore } from "../../stores/canvasStore";

// Sub-components
import { ModelSelector } from "./ModelSelector";
import { ScopeSelector } from "./ScopeSelector";
import { ConditionEditor } from "./ConditionEditor";
import { DeliverOutputForm } from "./DeliverOutputForm";
import { SendEmailForm } from "./SendEmailForm";
import { CreateTaskForm } from "./CreateTaskForm";
import { AiCompletionForm } from "./AiCompletionForm";
import { WaitForm } from "./WaitForm";
import { NodeValidationBadge } from "./NodeValidationBadge";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface NodePropertiesFormProps {
    node: PlaybookNode;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("0px"),
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding("8px", "12px"),
    },
    headerLeft: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
    },
    typeBadge: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        textTransform: "capitalize" as const,
    },
    accordionPanel: {
        ...shorthands.padding("8px", "12px", "12px"),
    },
    fieldGroup: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("4px"),
        marginBottom: "8px",
    },
    deleteSection: {
        ...shorthands.padding("12px"),
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke2),
    },
});

// ---------------------------------------------------------------------------
// Node type labels
// ---------------------------------------------------------------------------

const NODE_TYPE_LABELS: Record<string, string> = {
    start: "Start",
    aiAnalysis: "AI Analysis",
    aiCompletion: "AI Completion",
    condition: "Condition",
    deliverOutput: "Deliver Output",
    createTask: "Create Task",
    sendEmail: "Send Email",
    wait: "Wait",
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NodePropertiesForm = memo(function NodePropertiesForm({
    node,
}: NodePropertiesFormProps) {
    const styles = useStyles();
    const updateNodeData = useCanvasStore((s) => s.updateNodeData);
    const removeNode = useCanvasStore((s) => s.removeNode);

    const nodeType = node.data.type;
    const isAiNode = nodeType === "aiAnalysis" || nodeType === "aiCompletion";
    const isConditionNode = nodeType === "condition";
    const isStartNode = nodeType === "start";

    // Determine which type-specific form to show
    const hasTypeForm = ["deliverOutput", "sendEmail", "createTask", "aiCompletion", "wait"].includes(nodeType);

    // Generic field updater
    const handleUpdate = useCallback(
        (field: string, value: unknown) => {
            updateNodeData(node.id, { [field]: value });
        },
        [node.id, updateNodeData],
    );

    // configJson handler for type-specific forms
    const handleConfigChange = useCallback(
        (json: string) => {
            updateNodeData(node.id, { configJson: json });
        },
        [node.id, updateNodeData],
    );

    const handleDelete = useCallback(() => {
        removeNode(node.id);
    }, [node.id, removeNode]);

    // Default open accordion items
    const defaultOpenItems = useMemo(() => {
        const items = ["basic"];
        if (hasTypeForm) items.push("typeConfig");
        if (isAiNode) items.push("aiModel");
        if (isConditionNode) items.push("condition");
        return items;
    }, [hasTypeForm, isAiNode, isConditionNode]);

    return (
        <div className={styles.root}>
            {/* Header */}
            <div className={styles.header}>
                <div className={styles.headerLeft}>
                    <Text weight="semibold" size={400}>
                        {node.data.label || "Unnamed Node"}
                    </Text>
                    <Text className={styles.typeBadge}>
                        {NODE_TYPE_LABELS[nodeType] ?? nodeType}
                    </Text>
                    <NodeValidationBadge validationErrors={node.data.validationErrors ?? []} />
                </div>
            </div>

            <Divider />

            <Accordion
                multiple
                collapsible
                defaultOpenItems={defaultOpenItems}
            >
                {/* Basic Section — Always shown */}
                <AccordionItem value="basic">
                    <AccordionHeader size="small">Basic</AccordionHeader>
                    <AccordionPanel className={styles.accordionPanel}>
                        <div className={styles.fieldGroup}>
                            <Label size="small" htmlFor={`${node.id}-name`}>Name</Label>
                            <Input
                                id={`${node.id}-name`}
                                size="small"
                                value={node.data.label}
                                onChange={(_, data) => handleUpdate("label", data.value)}
                            />
                        </div>
                        {!isStartNode && (
                            <div className={styles.fieldGroup}>
                                <Label size="small" htmlFor={`${node.id}-outputVar`}>Output Variable</Label>
                                <Input
                                    id={`${node.id}-outputVar`}
                                    size="small"
                                    value={node.data.outputVariable ?? ""}
                                    onChange={(_, data) => handleUpdate("outputVariable", data.value)}
                                    placeholder={`output_${nodeType}`}
                                />
                            </div>
                        )}
                    </AccordionPanel>
                </AccordionItem>

                {/* AI Model Section — Only for AI nodes */}
                {isAiNode && (
                    <AccordionItem value="aiModel">
                        <AccordionHeader size="small">AI Model</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            <ModelSelector
                                modelDeploymentId={node.data.modelDeploymentId}
                                onModelChange={(id) => handleUpdate("modelDeploymentId", id)}
                            />
                        </AccordionPanel>
                    </AccordionItem>
                )}

                {/* Type-Specific Configuration Form */}
                {hasTypeForm && (
                    <AccordionItem value="typeConfig">
                        <AccordionHeader size="small">Configuration</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            {nodeType === "deliverOutput" && (
                                <DeliverOutputForm
                                    nodeId={node.id}
                                    configJson={node.data.configJson ?? "{}"}
                                    onConfigChange={handleConfigChange}
                                />
                            )}
                            {nodeType === "sendEmail" && (
                                <SendEmailForm
                                    nodeId={node.id}
                                    configJson={node.data.configJson ?? "{}"}
                                    onConfigChange={handleConfigChange}
                                />
                            )}
                            {nodeType === "createTask" && (
                                <CreateTaskForm
                                    nodeId={node.id}
                                    configJson={node.data.configJson ?? "{}"}
                                    onConfigChange={handleConfigChange}
                                />
                            )}
                            {nodeType === "aiCompletion" && (
                                <AiCompletionForm
                                    nodeId={node.id}
                                    configJson={node.data.configJson ?? "{}"}
                                    onConfigChange={handleConfigChange}
                                />
                            )}
                            {nodeType === "wait" && (
                                <WaitForm
                                    nodeId={node.id}
                                    configJson={node.data.configJson ?? "{}"}
                                    onConfigChange={handleConfigChange}
                                />
                            )}
                        </AccordionPanel>
                    </AccordionItem>
                )}

                {/* Skills Section */}
                {!isStartNode && (
                    <AccordionItem value="skills">
                        <AccordionHeader size="small">Skills</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            <ScopeSelector
                                nodeType={nodeType}
                                skillIds={node.data.skillIds ?? []}
                                knowledgeIds={node.data.knowledgeIds ?? []}
                                toolId={node.data.toolId}
                                showSkills
                                onSkillsChange={(ids) => handleUpdate("skillIds", ids)}
                                onKnowledgeChange={() => {}}
                                onToolChange={() => {}}
                            />
                        </AccordionPanel>
                    </AccordionItem>
                )}

                {/* Knowledge Section */}
                {!isStartNode && (
                    <AccordionItem value="knowledge">
                        <AccordionHeader size="small">Knowledge</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            <ScopeSelector
                                nodeType={nodeType}
                                skillIds={node.data.skillIds ?? []}
                                knowledgeIds={node.data.knowledgeIds ?? []}
                                toolId={node.data.toolId}
                                showKnowledge
                                onSkillsChange={() => {}}
                                onKnowledgeChange={(ids) => handleUpdate("knowledgeIds", ids)}
                                onToolChange={() => {}}
                            />
                        </AccordionPanel>
                    </AccordionItem>
                )}

                {/* Tools Section */}
                {!isStartNode && (
                    <AccordionItem value="tools">
                        <AccordionHeader size="small">Tools</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            <ScopeSelector
                                nodeType={nodeType}
                                skillIds={node.data.skillIds ?? []}
                                knowledgeIds={node.data.knowledgeIds ?? []}
                                toolId={node.data.toolId}
                                showTools
                                onSkillsChange={() => {}}
                                onKnowledgeChange={() => {}}
                                onToolChange={(id) => handleUpdate("toolId", id)}
                            />
                        </AccordionPanel>
                    </AccordionItem>
                )}

                {/* Condition Section — Only for condition nodes */}
                {isConditionNode && (
                    <AccordionItem value="condition">
                        <AccordionHeader size="small">Condition</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            <ConditionEditor
                                conditionJson={node.data.conditionJson ?? "{}"}
                                onConditionChange={(json) => handleUpdate("conditionJson", json)}
                            />
                        </AccordionPanel>
                    </AccordionItem>
                )}

                {/* Runtime Settings */}
                {!isStartNode && (
                    <AccordionItem value="runtime">
                        <AccordionHeader size="small">Runtime Settings</AccordionHeader>
                        <AccordionPanel className={styles.accordionPanel}>
                            <div className={styles.fieldGroup}>
                                <Label size="small">Timeout (seconds)</Label>
                                <SpinButton
                                    size="small"
                                    min={0}
                                    max={3600}
                                    step={30}
                                    value={node.data.timeoutSeconds ?? 300}
                                    onChange={(_, data) =>
                                        handleUpdate("timeoutSeconds", data.value ?? 300)
                                    }
                                />
                            </div>
                            <div className={styles.fieldGroup}>
                                <Label size="small">Retry Count</Label>
                                <SpinButton
                                    size="small"
                                    min={0}
                                    max={5}
                                    step={1}
                                    value={node.data.retryCount ?? 0}
                                    onChange={(_, data) =>
                                        handleUpdate("retryCount", data.value ?? 0)
                                    }
                                />
                            </div>
                        </AccordionPanel>
                    </AccordionItem>
                )}
            </Accordion>

            {/* Delete button */}
            {!isStartNode && (
                <div className={styles.deleteSection}>
                    <Button
                        appearance="subtle"
                        icon={<Delete20Regular />}
                        onClick={handleDelete}
                        style={{ color: tokens.colorPaletteRedForeground1 }}
                    >
                        Delete Node
                    </Button>
                </div>
            )}
        </div>
    );
});
