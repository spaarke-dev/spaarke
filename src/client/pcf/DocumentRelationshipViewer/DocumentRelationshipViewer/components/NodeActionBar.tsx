/**
 * NodeActionBar - Action bar for selected document nodes
 *
 * Appears when a document node is selected in the graph visualization.
 * Provides actions:
 * - Open Document Record (Dataverse form)
 * - View File in SharePoint (SPE file viewer)
 * - Expand (load next level of related documents)
 *
 * Follows:
 * - ADR-021: Fluent UI v9 exclusively, design tokens for all colors
 * - ADR-022: React 16 compatible APIs
 * - FR-06: Node Action Bar specification
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Card,
    CardHeader,
    Button,
    Tooltip,
    Text,
    Divider,
} from "@fluentui/react-components";
import {
    Open20Regular,
    Globe20Regular,
    ArrowExpand20Regular,
    Dismiss20Regular,
} from "@fluentui/react-icons";
import type { DocumentNodeData } from "../types/graph";

/**
 * Props for NodeActionBar component
 */
export interface NodeActionBarProps {
    /** Selected node data */
    nodeData: DocumentNodeData;
    /** Callback to close the action bar */
    onClose: () => void;
    /** Callback when user clicks Expand */
    onExpand?: (documentId: string) => void;
    /** Whether the node can be expanded (has more levels to load) */
    canExpand?: boolean;
}

/**
 * Styles using Fluent UI v9 design tokens (ADR-021 compliant)
 */
const useStyles = makeStyles({
    container: {
        position: "absolute",
        top: tokens.spacingVerticalM,
        right: tokens.spacingHorizontalM,
        width: "280px",
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow16,
        zIndex: 100,
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    headerContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        overflow: "hidden",
        flex: 1,
    },
    documentName: {
        fontWeight: tokens.fontWeightSemibold,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        color: tokens.colorNeutralForeground1,
    },
    parentEntity: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    closeButton: {
        minWidth: "auto",
        padding: tokens.spacingHorizontalXS,
    },
    actionsContainer: {
        display: "flex",
        flexDirection: "column",
        padding: tokens.spacingVerticalS,
        gap: tokens.spacingVerticalXS,
    },
    actionButton: {
        justifyContent: "flex-start",
        width: "100%",
    },
    divider: {
        margin: `${tokens.spacingVerticalXXS} 0`,
    },
    sourceLabel: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorBrandForeground1,
        fontWeight: tokens.fontWeightSemibold,
        marginTop: tokens.spacingVerticalXXS,
    },
});

/**
 * Xrm.Navigation types for Dataverse navigation
 * Declared globally by Power Platform runtime
 */
declare global {
    interface XrmNavigation {
        openForm(options: {
            entityName: string;
            entityId?: string;
            openInNewWindow?: boolean;
        }): Promise<void>;
    }
    interface Xrm {
        Navigation: XrmNavigation;
    }
    var Xrm: Xrm | undefined;
}

/**
 * NodeActionBar component - Action bar for selected nodes
 */
export const NodeActionBar: React.FC<NodeActionBarProps> = ({
    nodeData,
    onClose,
    onExpand,
    canExpand = true,
}) => {
    const styles = useStyles();
    const isSource = nodeData.isSource ?? false;

    /**
     * Open document record in Dataverse form
     * Uses Xrm.Navigation.openForm per project constraint
     */
    const handleOpenDocumentRecord = React.useCallback(() => {
        if (typeof Xrm !== "undefined" && Xrm.Navigation) {
            Xrm.Navigation.openForm({
                entityName: "sprk_document",
                entityId: nodeData.documentId,
                openInNewWindow: true,
            }).catch((error: Error) => {
                console.error("Failed to open document record:", error);
            });
        } else {
            // Fallback for development/testing outside Dataverse
            console.warn(
                "Xrm.Navigation not available. Document ID:",
                nodeData.documentId
            );
            // Try to construct a URL for testing
            const baseUrl = window.location.origin;
            const formUrl = `${baseUrl}/main.aspx?etn=sprk_document&id=${nodeData.documentId}&pagetype=entityrecord`;
            window.open(formUrl, "_blank");
        }
    }, [nodeData.documentId]);

    /**
     * Open file in SharePoint Embedded viewer
     * Opens fileUrl in new browser tab per project constraint
     */
    const handleViewFile = React.useCallback(() => {
        if (nodeData.fileUrl) {
            window.open(nodeData.fileUrl, "_blank", "noopener,noreferrer");
        } else {
            console.warn("No fileUrl available for document:", nodeData.name);
        }
    }, [nodeData.fileUrl, nodeData.name]);

    /**
     * Expand node to load next level of related documents
     */
    const handleExpand = React.useCallback(() => {
        if (onExpand) {
            onExpand(nodeData.documentId);
        }
    }, [onExpand, nodeData.documentId]);

    return (
        <Card className={styles.container}>
            {/* Header with document name and close button */}
            <div className={styles.header}>
                <div className={styles.headerContent}>
                    <Text className={styles.documentName} title={nodeData.name}>
                        {nodeData.name}
                    </Text>
                    {nodeData.parentEntityName && (
                        <Text
                            className={styles.parentEntity}
                            title={nodeData.parentEntityName}
                        >
                            {nodeData.parentEntityName}
                        </Text>
                    )}
                    {isSource && (
                        <Text className={styles.sourceLabel}>Source Document</Text>
                    )}
                </div>
                <Tooltip content="Close" relationship="label">
                    <Button
                        className={styles.closeButton}
                        appearance="subtle"
                        icon={<Dismiss20Regular />}
                        onClick={onClose}
                        aria-label="Close action bar"
                    />
                </Tooltip>
            </div>

            {/* Action buttons */}
            <div className={styles.actionsContainer}>
                <Tooltip content="Open document record in Dataverse" relationship="description">
                    <Button
                        className={styles.actionButton}
                        appearance="subtle"
                        icon={<Open20Regular />}
                        onClick={handleOpenDocumentRecord}
                    >
                        Open Document Record
                    </Button>
                </Tooltip>

                <Tooltip content="View file in SharePoint" relationship="description">
                    <Button
                        className={styles.actionButton}
                        appearance="subtle"
                        icon={<Globe20Regular />}
                        onClick={handleViewFile}
                        disabled={!nodeData.fileUrl}
                    >
                        View in SharePoint
                    </Button>
                </Tooltip>

                {!isSource && (
                    <>
                        <Divider className={styles.divider} />
                        <Tooltip
                            content="Load related documents for this node"
                            relationship="description"
                        >
                            <Button
                                className={styles.actionButton}
                                appearance="subtle"
                                icon={<ArrowExpand20Regular />}
                                onClick={handleExpand}
                                disabled={!canExpand || !onExpand}
                            >
                                Expand
                            </Button>
                        </Tooltip>
                    </>
                )}
            </div>
        </Card>
    );
};

export default NodeActionBar;
