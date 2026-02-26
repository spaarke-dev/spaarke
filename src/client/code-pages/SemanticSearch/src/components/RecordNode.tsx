/**
 * RecordNode — Individual record node for ReactFlow drill-down
 *
 * Renders a single search result record showing:
 *   - Entity type icon (document, matter, project, invoice)
 *   - Record name (truncated to 2 lines)
 *   - Similarity badge with green/amber/red color coding
 *   - Parent entity name (if available)
 *   - Click-to-open via onNodeClick → onOpenRecord chain
 *
 * @see DocumentNode.tsx (DocumentRelationshipViewer) — reference pattern
 */

import React from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import {
    makeStyles,
    tokens,
    Text,
    Badge,
} from "@fluentui/react-components";
import {
    DocumentRegular,
    BriefcaseRegular,
    TaskListSquareAddRegular,
    ReceiptRegular,
} from "@fluentui/react-icons";
import type { RecordNodeData, SearchDomain } from "../types";

// =============================================
// Helpers
// =============================================

/** Get an icon component for the record's entity type. */
function getEntityIcon(domain: SearchDomain): React.ReactElement {
    switch (domain) {
        case "documents":
            return <DocumentRegular />;
        case "matters":
            return <BriefcaseRegular />;
        case "projects":
            return <TaskListSquareAddRegular />;
        case "invoices":
            return <ReceiptRegular />;
        default:
            return <DocumentRegular />;
    }
}

/**
 * Get similarity badge color using Fluent v9 palette tokens.
 *   75-100%: green
 *   50-74%: amber/marigold
 *   0-49%:  red
 */
function getSimilarityColor(similarity: number): string {
    const pct = similarity * 100;
    if (pct >= 75) return tokens.colorPaletteGreenBackground3;
    if (pct >= 50) return tokens.colorPaletteMarigoldBackground3;
    return tokens.colorPaletteRedBackground3;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        width: "160px",
        padding: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        cursor: "pointer",
        gap: tokens.spacingVerticalXS,
        boxShadow: tokens.shadow2,
        transitionProperty: "box-shadow",
        transitionDuration: tokens.durationNormal,
        ":hover": {
            boxShadow: tokens.shadow8,
        },
    },
    headerRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    icon: {
        fontSize: tokens.fontSizeBase300,
        flexShrink: 0,
        color: tokens.colorNeutralForeground2,
    },
    name: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        overflow: "hidden",
        textOverflow: "ellipsis",
        display: "-webkit-box",
        WebkitLineClamp: 2,
        WebkitBoxOrient: "vertical",
        lineHeight: tokens.lineHeightBase200,
    },
    badgeRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-end",
    },
    parentName: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
});

// =============================================
// Component
// =============================================

export const RecordNode: React.FC<NodeProps> = ({ data }) => {
    const styles = useStyles();
    const d = data as unknown as RecordNodeData;

    const pct = Math.round(d.similarity * 100);
    const badgeColor = getSimilarityColor(d.similarity);

    return (
        <div
            className={styles.container}
            role="button"
            tabIndex={0}
            aria-label={`${d.recordName}, similarity ${pct}%${d.parentEntityName ? `, ${d.parentEntityName}` : ""}`}
        >
            <Handle
                type="target"
                position={Position.Top}
                style={{ visibility: "hidden" }}
            />

            {/* Header: icon + name */}
            <div className={styles.headerRow}>
                <span className={styles.icon}>
                    {getEntityIcon(d.domain)}
                </span>
                <Text className={styles.name} title={d.recordName}>
                    {d.recordName}
                </Text>
            </div>

            {/* Similarity badge */}
            <div className={styles.badgeRow}>
                <Badge
                    appearance="filled"
                    size="small"
                    style={{ backgroundColor: badgeColor }}
                >
                    {pct}%
                </Badge>
            </div>

            {/* Parent entity name */}
            {d.parentEntityName && (
                <Text className={styles.parentName} title={d.parentEntityName}>
                    {d.parentEntityName}
                </Text>
            )}

            <Handle
                type="source"
                position={Position.Bottom}
                style={{ visibility: "hidden" }}
            />
        </div>
    );
};

export default RecordNode;
