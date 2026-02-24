/**
 * NodeActionBar — Action bar for selected document nodes (Code Page version)
 * Same logic as PCF; Xrm is available in Dataverse HTML web resource dialogs.
 */

import React, { useCallback } from "react";
import {
    makeStyles, tokens, Card, Button, Tooltip, Text, Divider,
} from "@fluentui/react-components";
import { Open20Regular, Globe20Regular, ArrowExpand20Regular, Dismiss20Regular } from "@fluentui/react-icons";
import type { DocumentNodeData } from "../types/graph";

export interface NodeActionBarProps {
    nodeData: DocumentNodeData;
    onClose: () => void;
    onExpand?: (documentId: string) => void;
    canExpand?: boolean;
}

const useStyles = makeStyles({
    container: {
        position: "absolute", top: tokens.spacingVerticalM, right: tokens.spacingHorizontalM,
        width: "280px", backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium, boxShadow: tokens.shadow16, zIndex: 100,
    },
    header: {
        display: "flex", alignItems: "center", justifyContent: "space-between",
        padding: tokens.spacingVerticalS, paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalS, borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    headerContent: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalXXS, overflow: "hidden", flex: 1 },
    documentName: { fontWeight: tokens.fontWeightSemibold, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", color: tokens.colorNeutralForeground1 },
    parentEntity: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
    closeButton: { minWidth: "auto", padding: tokens.spacingHorizontalXS },
    actionsContainer: { display: "flex", flexDirection: "column", padding: tokens.spacingVerticalS, gap: tokens.spacingVerticalXS },
    actionButton: { justifyContent: "flex-start", width: "100%" },
    divider: { margin: `${tokens.spacingVerticalXXS} 0` },
    sourceLabel: { fontSize: tokens.fontSizeBase200, color: tokens.colorBrandForeground1, fontWeight: tokens.fontWeightSemibold, marginTop: tokens.spacingVerticalXXS },
});

export const NodeActionBar: React.FC<NodeActionBarProps> = ({
    nodeData, onClose, onExpand, canExpand = true,
}) => {
    const styles = useStyles();
    const isSource = nodeData.isSource ?? false;
    const isOrphanFile = nodeData.isOrphanFile ?? !nodeData.documentId;

    const handleOpenDocumentRecord = useCallback(() => {
        if (!nodeData.documentId) return;
        if (typeof Xrm !== "undefined" && Xrm.Navigation) {
            void Xrm.Navigation.openForm({
                entityName: "sprk_document",
                entityId: nodeData.documentId,
                openInNewWindow: true,
            });
        } else {
            const baseUrl = window.location.origin;
            window.open(`${baseUrl}/main.aspx?etn=sprk_document&id=${nodeData.documentId}&pagetype=entityrecord`, "_blank");
        }
    }, [nodeData.documentId]);

    const handleViewFile = useCallback(() => {
        if (nodeData.fileUrl) window.open(nodeData.fileUrl, "_blank", "noopener,noreferrer");
    }, [nodeData.fileUrl]);

    const handleExpand = useCallback(() => {
        if (onExpand) {
            const expandId = nodeData.documentId ?? nodeData.speFileId;
            if (expandId) onExpand(expandId);
        }
    }, [onExpand, nodeData.documentId, nodeData.speFileId]);

    return (
        <Card className={styles.container}>
            <div className={styles.header}>
                <div className={styles.headerContent}>
                    <Text className={styles.documentName} title={nodeData.name}>{nodeData.name}</Text>
                    {nodeData.parentEntityName && <Text className={styles.parentEntity} title={nodeData.parentEntityName}>{nodeData.parentEntityName}</Text>}
                    {isSource && <Text className={styles.sourceLabel}>Source Document</Text>}
                </div>
                <Tooltip content="Close" relationship="label">
                    <Button className={styles.closeButton} appearance="subtle" icon={<Dismiss20Regular />} onClick={onClose} aria-label="Close action bar" />
                </Tooltip>
            </div>
            <div className={styles.actionsContainer}>
                <Tooltip content={isOrphanFile ? "Not available for files without a document record" : "Open document record in Dataverse"} relationship="description">
                    <Button className={styles.actionButton} appearance="subtle" icon={<Open20Regular />} onClick={handleOpenDocumentRecord} disabled={isOrphanFile}>
                        Open Document Record
                    </Button>
                </Tooltip>
                <Tooltip content="View file in SharePoint" relationship="description">
                    <Button className={styles.actionButton} appearance="subtle" icon={<Globe20Regular />} onClick={handleViewFile} disabled={!nodeData.fileUrl}>
                        View in SharePoint
                    </Button>
                </Tooltip>
                {!isSource && (
                    <>
                        <Divider className={styles.divider} />
                        <Tooltip content="Load related documents for this node" relationship="description">
                            <Button className={styles.actionButton} appearance="subtle" icon={<ArrowExpand20Regular />} onClick={handleExpand} disabled={!canExpand || !onExpand}>
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
