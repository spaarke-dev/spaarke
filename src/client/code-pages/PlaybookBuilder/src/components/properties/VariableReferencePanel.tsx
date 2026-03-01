/**
 * VariableReferencePanel - Sidebar panel showing available template variables.
 *
 * Displays upstream node outputs grouped by source node. Users can click
 * to copy a variable expression to the clipboard for pasting into form fields.
 *
 * Template variable syntax: {{nodeName.output.fieldName}}
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useCallback, useMemo, memo, useState } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    Tooltip,
    Accordion,
    AccordionHeader,
    AccordionItem,
    AccordionPanel,
    Badge,
    shorthands,
    mergeClasses,
} from "@fluentui/react-components";
import {
    Copy20Regular,
    Checkmark20Regular,
    Code20Regular,
} from "@fluentui/react-icons";
import type { NodeReference, VariableEntry } from "../../types/forms";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    panel: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        ...shorthands.padding(tokens.spacingVerticalS, "0"),
    },
    emptyState: {
        color: tokens.colorNeutralForeground3,
        fontStyle: "italic",
        ...shorthands.padding(tokens.spacingVerticalM),
        textAlign: "center",
    },
    variableItem: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalXS),
        borderRadius: tokens.borderRadiusSmall,
        cursor: "pointer",
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    variableInfo: {
        display: "flex",
        flexDirection: "column",
        gap: "2px",
        flex: 1,
        minWidth: 0,
    },
    variableExpression: {
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground1,
        wordBreak: "break-all",
    },
    variableTypeHint: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    copyButton: {
        flexShrink: 0,
    },
    accordionPanel: {
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
    copiedFeedback: {
        color: tokens.colorPaletteGreenForeground1,
    },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface VariableReferencePanelProps {
    /** ID of the currently selected node (used to filter out self). */
    nodeId: string;
    /** All nodes in the playbook canvas. */
    nodes: NodeReference[];
}

// ---------------------------------------------------------------------------
// Well-known output fields by node type
// ---------------------------------------------------------------------------

const OUTPUT_FIELDS_BY_TYPE: Record<string, Array<{ field: string; typeHint: VariableEntry["typeHint"] }>> = {
    aiAnalysis: [
        { field: "result", typeHint: "string" },
        { field: "summary", typeHint: "string" },
        { field: "entities", typeHint: "array" },
        { field: "confidence", typeHint: "number" },
    ],
    aiCompletion: [
        { field: "result", typeHint: "string" },
        { field: "tokensUsed", typeHint: "number" },
    ],
    condition: [
        { field: "branch", typeHint: "string" },
        { field: "evaluatedValue", typeHint: "boolean" },
    ],
    deliverOutput: [
        { field: "status", typeHint: "string" },
    ],
    createTask: [
        { field: "taskId", typeHint: "string" },
        { field: "status", typeHint: "string" },
    ],
    sendEmail: [
        { field: "messageId", typeHint: "string" },
        { field: "status", typeHint: "string" },
    ],
    wait: [
        { field: "completedAt", typeHint: "string" },
        { field: "status", typeHint: "string" },
    ],
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function buildVariableEntries(
    currentNodeId: string,
    nodes: NodeReference[],
): Map<string, VariableEntry[]> {
    const grouped = new Map<string, VariableEntry[]>();

    for (const node of nodes) {
        // Skip the current node (cannot reference own outputs)
        if (node.id === currentNodeId) continue;

        const label = node.data.label || node.id;
        const nodeName = node.data.outputVariable || label.replace(/\s+/g, "_").toLowerCase();
        const nodeType = node.data.type;
        const fields = OUTPUT_FIELDS_BY_TYPE[nodeType] ?? [
            { field: "result", typeHint: "string" as const },
        ];

        const entries: VariableEntry[] = fields.map((f) => ({
            expression: `{{${nodeName}.output.${f.field}}}`,
            label: f.field,
            typeHint: f.typeHint,
            sourceNodeLabel: label,
        }));

        if (entries.length > 0) {
            grouped.set(label, entries);
        }
    }

    return grouped;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const VariableReferencePanel = memo(function VariableReferencePanel({
    nodeId,
    nodes,
}: VariableReferencePanelProps) {
    const styles = useStyles();
    const [copiedExpression, setCopiedExpression] = useState<string | null>(null);

    const groupedVariables = useMemo(
        () => buildVariableEntries(nodeId, nodes),
        [nodeId, nodes],
    );

    const handleCopy = useCallback(
        async (expression: string) => {
            try {
                await navigator.clipboard.writeText(expression);
                setCopiedExpression(expression);
                // Reset feedback after 2 seconds
                setTimeout(() => setCopiedExpression(null), 2000);
            } catch {
                // Fallback for environments without clipboard API
                const textArea = document.createElement("textarea");
                textArea.value = expression;
                textArea.style.position = "fixed";
                textArea.style.opacity = "0";
                document.body.appendChild(textArea);
                textArea.select();
                document.execCommand("copy");
                document.body.removeChild(textArea);
                setCopiedExpression(expression);
                setTimeout(() => setCopiedExpression(null), 2000);
            }
        },
        [],
    );

    const groupKeys = useMemo(
        () => Array.from(groupedVariables.keys()),
        [groupedVariables],
    );

    if (groupKeys.length === 0) {
        return (
            <div className={styles.panel}>
                <div className={styles.header}>
                    <Code20Regular />
                    <Text size={200} weight="semibold">
                        Template Variables
                    </Text>
                </div>
                <Text className={styles.emptyState}>
                    No upstream nodes available. Add nodes before this one to reference their outputs.
                </Text>
            </div>
        );
    }

    return (
        <div className={styles.panel}>
            <div className={styles.header}>
                <Code20Regular />
                <Text size={200} weight="semibold">
                    Template Variables
                </Text>
            </div>

            <Accordion multiple collapsible defaultOpenItems={groupKeys}>
                {groupKeys.map((groupLabel) => {
                    const entries = groupedVariables.get(groupLabel)!;
                    return (
                        <AccordionItem key={groupLabel} value={groupLabel}>
                            <AccordionHeader size="small">
                                {groupLabel}
                                <Badge
                                    appearance="tint"
                                    size="small"
                                    style={{ marginLeft: tokens.spacingHorizontalXS }}
                                >
                                    {entries.length}
                                </Badge>
                            </AccordionHeader>
                            <AccordionPanel className={styles.accordionPanel}>
                                {entries.map((entry) => {
                                    const isCopied = copiedExpression === entry.expression;
                                    return (
                                        <div
                                            key={entry.expression}
                                            className={styles.variableItem}
                                            onClick={() => handleCopy(entry.expression)}
                                            role="button"
                                            tabIndex={0}
                                            onKeyDown={(e) => {
                                                if (e.key === "Enter" || e.key === " ") {
                                                    e.preventDefault();
                                                    handleCopy(entry.expression);
                                                }
                                            }}
                                        >
                                            <div className={styles.variableInfo}>
                                                <Text className={styles.variableExpression}>
                                                    {entry.expression}
                                                </Text>
                                                <Text className={styles.variableTypeHint}>
                                                    {entry.label} ({entry.typeHint})
                                                </Text>
                                            </div>
                                            <Tooltip
                                                content={isCopied ? "Copied!" : "Click to copy"}
                                                relationship="label"
                                            >
                                                <Button
                                                    className={mergeClasses(
                                                        styles.copyButton,
                                                        isCopied ? styles.copiedFeedback : undefined,
                                                    )}
                                                    appearance="subtle"
                                                    size="small"
                                                    icon={isCopied ? <Checkmark20Regular /> : <Copy20Regular />}
                                                    onClick={(e) => {
                                                        e.stopPropagation();
                                                        handleCopy(entry.expression);
                                                    }}
                                                    aria-label={`Copy ${entry.expression}`}
                                                />
                                            </Tooltip>
                                        </div>
                                    );
                                })}
                            </AccordionPanel>
                        </AccordionItem>
                    );
                })}
            </Accordion>
        </div>
    );
});
