/**
 * PropertiesPanel — Right sidebar wrapper that shows NodePropertiesForm
 * for the currently selected node, or an empty state when nothing is selected.
 *
 * Reads selectedNodeId from canvasStore and finds the corresponding node.
 */

import { memo } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Text,
} from "@fluentui/react-components";
import { Settings20Regular } from "@fluentui/react-icons";
import { useCanvasStore } from "../../stores/canvasStore";
import { NodePropertiesForm } from "./NodePropertiesForm";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    panel: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "300px",
        ...shorthands.borderLeft("1px", "solid", tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground2,
    },
    header: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
        ...shorthands.padding("12px"),
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground3,
    },
    content: {
        flex: 1,
        overflowY: "auto",
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        ...shorthands.gap("8px"),
        ...shorthands.padding("24px"),
        textAlign: "center",
    },
    emptyIcon: {
        color: tokens.colorNeutralForeground4,
        fontSize: "32px",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PropertiesPanel = memo(function PropertiesPanel() {
    const styles = useStyles();

    const selectedNodeId = useCanvasStore((s) => s.selectedNodeId);
    const nodes = useCanvasStore((s) => s.nodes);

    const selectedNode = selectedNodeId
        ? nodes.find((n) => n.id === selectedNodeId) ?? null
        : null;

    return (
        <div className={styles.panel}>
            {/* Header */}
            <div className={styles.header}>
                <Settings20Regular />
                <Text weight="semibold" size={400}>
                    Properties
                </Text>
            </div>

            {/* Content */}
            <div className={styles.content}>
                {selectedNode ? (
                    <NodePropertiesForm node={selectedNode} />
                ) : (
                    <div className={styles.emptyState}>
                        <Settings20Regular className={styles.emptyIcon} />
                        <Text size={300} weight="semibold">
                            No node selected
                        </Text>
                        <Text size={200}>
                            Select a node on the canvas to view and edit its properties
                        </Text>
                    </div>
                )}
            </div>
        </div>
    );
});
