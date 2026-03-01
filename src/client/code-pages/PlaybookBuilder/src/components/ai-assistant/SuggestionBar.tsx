/**
 * Suggestion Bar Component - Quick action suggestions for AI Assistant
 *
 * Shows contextual suggestions based on:
 * - Empty canvas state (getting started)
 * - Current canvas contents (next steps)
 * - Selected node (configure/connect options)
 *
 * Provides a Claude Code-like experience with intelligent suggestions.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useMemo } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Button,
} from "@fluentui/react-components";
import {
    Add20Regular,
    ArrowRight20Regular,
    BrainCircuit20Regular,
    Lightbulb20Regular,
    Settings20Regular,
    Play20Regular,
} from "@fluentui/react-icons";
import { useCanvasStore } from "../../stores/canvasStore";

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
    },
    label: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    suggestions: {
        display: "flex",
        flexWrap: "wrap",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    suggestionButton: {
        minWidth: "auto",
        fontSize: tokens.fontSizeBase200,
        ...shorthands.padding(
            tokens.spacingVerticalXXS,
            tokens.spacingHorizontalS
        ),
    },
});

// ============================================================================
// Types
// ============================================================================

interface Suggestion {
    id: string;
    label: string;
    icon?: React.ReactNode;
    message: string;
}

// ============================================================================
// Props
// ============================================================================

export interface SuggestionBarProps {
    /** Called when user clicks a suggestion */
    onSelectSuggestion: (message: string) => void;
    /** Whether to show the bar (hidden during streaming) */
    isVisible?: boolean;
}

// ============================================================================
// Component
// ============================================================================

export const SuggestionBar: React.FC<SuggestionBarProps> = ({
    onSelectSuggestion,
    isVisible = true,
}) => {
    const styles = useStyles();

    // Get canvas state
    const nodes = useCanvasStore((state) => state.nodes);
    const edges = useCanvasStore((state) => state.edges);
    const selectedNodeId = useCanvasStore((state) => state.selectedNodeId);

    // Generate contextual suggestions based on state
    const suggestions = useMemo((): Suggestion[] => {
        // Empty canvas - getting started
        if (nodes.length === 0) {
            return [
                {
                    id: "start-analysis",
                    label: "Start with AI Analysis",
                    icon: <BrainCircuit20Regular />,
                    message: "Create a new playbook that starts with an AI Analysis node",
                },
                {
                    id: "help",
                    label: "What can I build?",
                    icon: <Lightbulb20Regular />,
                    message:
                        "What types of playbooks can I build? Show me some examples.",
                },
                {
                    id: "template",
                    label: "Use a template",
                    icon: <Add20Regular />,
                    message:
                        "Show me playbook templates I can use as a starting point",
                },
            ];
        }

        // Has nodes but no connections - suggest connecting
        if (nodes.length >= 2 && edges.length === 0) {
            return [
                {
                    id: "connect-nodes",
                    label: "Connect nodes",
                    icon: <ArrowRight20Regular />,
                    message: "Connect my nodes to create a workflow",
                },
                {
                    id: "add-more",
                    label: "Add another node",
                    icon: <Add20Regular />,
                    message: "What node should I add next?",
                },
                {
                    id: "explain",
                    label: "Explain my workflow",
                    icon: <Lightbulb20Regular />,
                    message: "Explain what my current playbook does",
                },
            ];
        }

        // Node is selected - suggest configuration
        if (selectedNodeId) {
            const selectedNode = nodes.find((n) => n.id === selectedNodeId);
            const nodeType = selectedNode?.type || "node";

            return [
                {
                    id: "configure",
                    label: `Configure ${nodeType}`,
                    icon: <Settings20Regular />,
                    message: `Help me configure the selected ${nodeType} node`,
                },
                {
                    id: "connect-selected",
                    label: "Connect to another node",
                    icon: <ArrowRight20Regular />,
                    message: `Connect the ${nodeType} node to another node`,
                },
                {
                    id: "add-skill",
                    label: "Add a skill",
                    icon: <Add20Regular />,
                    message: `Add a skill to the ${nodeType} node`,
                },
            ];
        }

        // Has workflow - suggest next steps
        if (nodes.length > 0 && edges.length > 0) {
            // Check if workflow has an output node
            const hasOutput = nodes.some((n) => n.type === "deliverOutput");

            if (!hasOutput) {
                return [
                    {
                        id: "add-output",
                        label: "Add output step",
                        icon: <Add20Regular />,
                        message: "Add a Deliver Output node to complete my workflow",
                    },
                    {
                        id: "test",
                        label: "Test playbook",
                        icon: <Play20Regular />,
                        message: "Run a test of this playbook",
                    },
                    {
                        id: "suggest-next",
                        label: "What next?",
                        icon: <Lightbulb20Regular />,
                        message: "What should I add next to improve this playbook?",
                    },
                ];
            }

            // Complete workflow
            return [
                {
                    id: "test",
                    label: "Test playbook",
                    icon: <Play20Regular />,
                    message: "Run a test of this playbook",
                },
                {
                    id: "validate",
                    label: "Validate",
                    icon: <Lightbulb20Regular />,
                    message: "Validate this playbook and check for any issues",
                },
                {
                    id: "optimize",
                    label: "Suggestions?",
                    icon: <Settings20Regular />,
                    message: "Are there any improvements you would suggest for this playbook?",
                },
            ];
        }

        // Default suggestions
        return [
            {
                id: "add-node",
                label: "Add a node",
                icon: <Add20Regular />,
                message: "What type of node should I add?",
            },
            {
                id: "help",
                label: "Help",
                icon: <Lightbulb20Regular />,
                message: "What can you help me with?",
            },
        ];
    }, [nodes, edges, selectedNodeId]);

    if (!isVisible || suggestions.length === 0) return null;

    return (
        <div className={styles.container}>
            <div className={styles.label}>
                <Lightbulb20Regular />
                <span>Suggestions</span>
            </div>
            <div className={styles.suggestions}>
                {suggestions.map((suggestion) => (
                    <Button
                        key={suggestion.id}
                        appearance="subtle"
                        size="small"
                        icon={suggestion.icon as React.ReactElement | undefined}
                        className={styles.suggestionButton}
                        onClick={() => onSelectSuggestion(suggestion.message)}
                    >
                        {suggestion.label}
                    </Button>
                ))}
            </div>
        </div>
    );
};

export default SuggestionBar;
