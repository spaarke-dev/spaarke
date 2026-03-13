/**
 * NodePropertiesDialog — Modal dialog for editing node properties.
 *
 * Opens automatically when a node is selected on the canvas.
 * Reuses the existing NodePropertiesForm for the form content.
 * Changes are applied immediately via updateNodeData (same as before).
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { memo, useCallback } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Dialog,
    DialogSurface,
    DialogBody,
    DialogContent,
    Button,
    Text,
    Badge,
} from "@fluentui/react-components";
import { Dismiss20Regular } from "@fluentui/react-icons";
import { useCanvasStore } from "../../stores/canvasStore";
import { NodePropertiesForm } from "./NodePropertiesForm";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    surface: {
        width: "600px",
        maxWidth: "90vw",
        maxHeight: "85vh",
        ...shorthands.padding("0"),
    },
    titleBar: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding("16px", "20px", "12px"),
    },
    titleLeft: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
    },
    content: {
        ...shorthands.padding("0", "20px", "20px"),
        overflowY: "auto",
    },
    typeBadge: {
        textTransform: "capitalize",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NodePropertiesDialog = memo(function NodePropertiesDialog() {
    const styles = useStyles();

    const selectedNodeId = useCanvasStore((s) => s.selectedNodeId);
    const nodes = useCanvasStore((s) => s.nodes);
    const selectNode = useCanvasStore((s) => s.selectNode);

    const selectedNode = selectedNodeId
        ? nodes.find((n) => n.id === selectedNodeId) ?? null
        : null;

    const handleClose = useCallback(() => {
        selectNode(null);
    }, [selectNode]);

    const isOpen = selectedNode !== null;

    return (
        <Dialog
            open={isOpen}
            onOpenChange={(_e, data) => {
                if (!data.open) handleClose();
            }}
            modalType="non-modal"
        >
            <DialogSurface className={styles.surface}>
                <DialogBody>
                    <div className={styles.titleBar}>
                        <div className={styles.titleLeft}>
                            <Text weight="semibold" size={500}>
                                {selectedNode?.data.label || "Node Properties"}
                            </Text>
                            {selectedNode && (
                                <Badge
                                    size="small"
                                    appearance="outline"
                                    className={styles.typeBadge}
                                >
                                    {selectedNode.data.type}
                                </Badge>
                            )}
                        </div>
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<Dismiss20Regular />}
                            onClick={handleClose}
                        />
                    </div>
                    <DialogContent className={styles.content}>
                        {selectedNode && (
                            <NodePropertiesForm node={selectedNode} />
                        )}
                    </DialogContent>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
});
