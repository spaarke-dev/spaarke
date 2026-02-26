/**
 * RelationshipGrid — Grid/table view of document relationships
 *
 * NEW component for Code Page. Renders the same relationship data
 * as the graph view but in a tabular format using Fluent v9 DataGrid.
 *
 * Columns: Document, Relationship, Similarity, Type, Parent, Modified
 */

import React, { useMemo, useCallback } from "react";
import {
    makeStyles,
    tokens,
    DataGrid,
    DataGridHeader,
    DataGridHeaderCell,
    DataGridBody,
    DataGridRow,
    DataGridCell,
    TableColumnDefinition,
    createTableColumn,
    Badge,
    Text,
    Button,
    Tooltip,
    TableCellLayout,
} from "@fluentui/react-components";
import {
    Open20Regular,
    Globe20Regular,
    Document20Regular,
    DocumentPdf20Regular,
    DocumentText20Regular,
    Table20Regular,
    SlideText20Regular,
    Mail20Regular,
    Image20Regular,
    Code20Regular,
    FolderZip20Regular,
    DocumentQuestionMark20Regular,
} from "@fluentui/react-icons";
import type { DocumentNode, DocumentNodeData } from "../types/graph";

export interface RelationshipGridProps {
    /** All nodes from the API (including source node) */
    nodes: DocumentNode[];
    /** Whether dark mode is enabled */
    isDarkMode?: boolean;
}

const useStyles = makeStyles({
    container: {
        width: "100%",
        height: "100%",
        overflow: "auto",
        backgroundColor: tokens.colorNeutralBackground1,
    },
    grid: {
        width: "100%",
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "200px",
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3,
    },
    nameCell: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        overflow: "hidden",
    },
    nameIcon: {
        flexShrink: 0,
        color: tokens.colorBrandForeground1,
    },
    nameText: {
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    sourceBadge: {
        flexShrink: 0,
    },
    similarityHigh: { color: tokens.colorStatusSuccessForeground1, fontWeight: tokens.fontWeightSemibold },
    similarityMed: { color: tokens.colorBrandForeground1, fontWeight: tokens.fontWeightSemibold },
    similarityLow: { color: tokens.colorStatusWarningForeground1 },
    similarityNone: { color: tokens.colorNeutralForeground3 },
    badgeContainer: {
        display: "flex",
        gap: tokens.spacingHorizontalXXS,
        flexWrap: "wrap",
    },
    actionsCell: {
        display: "flex",
        gap: tokens.spacingHorizontalXS,
    },
});

interface GridRow {
    id: string;
    data: DocumentNodeData;
}

const getFileIcon = (fileType: string): React.ReactElement => {
    const type = fileType.toLowerCase();
    switch (type) {
        case "pdf": return <DocumentPdf20Regular />;
        case "docx": case "doc": case "txt": return <DocumentText20Regular />;
        case "xlsx": case "xls": case "csv": return <Table20Regular />;
        case "pptx": case "ppt": return <SlideText20Regular />;
        case "msg": case "eml": return <Mail20Regular />;
        case "jpg": case "jpeg": case "png": case "gif": return <Image20Regular />;
        case "html": case "htm": case "xml": case "json": return <Code20Regular />;
        case "zip": case "rar": return <FolderZip20Regular />;
        case "file": case "unknown": return <DocumentQuestionMark20Regular />;
        default: return <Document20Regular />;
    }
};

const formatDate = (isoDate: string | undefined): string => {
    if (!isoDate) return "—";
    try {
        return new Date(isoDate).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
    } catch { return "—"; }
};

const getRelationshipBadgeColor = (type: string): "brand" | "success" | "warning" | "informative" => {
    switch (type) {
        case "semantic": return "brand";
        case "same_matter": case "same_project": return "success";
        case "same_email": case "same_thread": return "warning";
        default: return "informative";
    }
};

export const RelationshipGrid: React.FC<RelationshipGridProps> = ({ nodes }) => {
    const styles = useStyles();

    // Filter out hub nodes (matter/project/invoice/email) — show only documents
    const rows = useMemo((): GridRow[] => {
        return nodes
            .filter((n) => {
                const nodeType = n.data.nodeType;
                return nodeType !== "matter" && nodeType !== "project" && nodeType !== "invoice" && nodeType !== "email";
            })
            .map((n) => ({ id: n.id, data: n.data }));
    }, [nodes]);

    const handleOpenRecord = useCallback((data: DocumentNodeData) => {
        if (!data.recordUrl && !data.documentId) return;
        if (data.recordUrl) {
            window.open(data.recordUrl, "_blank", "noopener,noreferrer");
        } else if (data.documentId) {
            const baseUrl = window.location.origin;
            window.open(`${baseUrl}/main.aspx?etn=sprk_document&id=${data.documentId}&pagetype=entityrecord`, "_blank");
        }
    }, []);

    const handleViewFile = useCallback((data: DocumentNodeData) => {
        if (data.fileUrl) window.open(data.fileUrl, "_blank", "noopener,noreferrer");
    }, []);

    const columns: TableColumnDefinition<GridRow>[] = useMemo(() => [
        createTableColumn<GridRow>({
            columnId: "name",
            compare: (a, b) => a.data.name.localeCompare(b.data.name),
            renderHeaderCell: () => "Document",
            renderCell: (row) => (
                <TableCellLayout>
                    <div className={styles.nameCell}>
                        <span className={styles.nameIcon}>{getFileIcon(row.data.fileType ?? "file")}</span>
                        <Text className={styles.nameText} title={row.data.name}>{row.data.name}</Text>
                        {row.data.isSource && (
                            <Badge className={styles.sourceBadge} appearance="filled" color="brand" size="small">Source</Badge>
                        )}
                    </div>
                </TableCellLayout>
            ),
        }),
        createTableColumn<GridRow>({
            columnId: "relationship",
            compare: (a, b) => (a.data.relationshipLabel ?? "").localeCompare(b.data.relationshipLabel ?? ""),
            renderHeaderCell: () => "Relationship",
            renderCell: (row) => (
                <TableCellLayout>
                    {row.data.isSource ? (
                        <Badge appearance="outline" color="brand" size="small">Source</Badge>
                    ) : row.data.isOrphanFile ? (
                        <Badge appearance="outline" color="warning" size="small">File only</Badge>
                    ) : row.data.relationshipTypes && row.data.relationshipTypes.length > 0 ? (
                        <div className={styles.badgeContainer}>
                            {row.data.relationshipTypes.map((rel) => (
                                <Badge key={rel.type} appearance="outline" color={getRelationshipBadgeColor(rel.type)} size="small">{rel.label}</Badge>
                            ))}
                        </div>
                    ) : row.data.relationshipLabel ? (
                        <Badge appearance="outline" color="informative" size="small">{row.data.relationshipLabel}</Badge>
                    ) : (
                        <Text>—</Text>
                    )}
                </TableCellLayout>
            ),
        }),
        createTableColumn<GridRow>({
            columnId: "similarity",
            compare: (a, b) => (b.data.similarity ?? 0) - (a.data.similarity ?? 0),
            renderHeaderCell: () => "Similarity",
            renderCell: (row) => {
                if (row.data.isSource) return <TableCellLayout><Text className={styles.similarityNone}>—</Text></TableCellLayout>;
                const sim = row.data.similarity ?? 0;
                const pct = `${Math.round(sim * 100)}%`;
                const cls = sim >= 0.9 ? styles.similarityHigh : sim >= 0.75 ? styles.similarityMed : sim >= 0.65 ? styles.similarityLow : styles.similarityNone;
                return <TableCellLayout><Text className={cls}>{pct}</Text></TableCellLayout>;
            },
        }),
        createTableColumn<GridRow>({
            columnId: "type",
            compare: (a, b) => (a.data.documentType ?? "").localeCompare(b.data.documentType ?? ""),
            renderHeaderCell: () => "Type",
            renderCell: (row) => (
                <TableCellLayout>
                    <Text>{row.data.documentType ?? row.data.fileType?.toUpperCase() ?? "—"}</Text>
                </TableCellLayout>
            ),
        }),
        createTableColumn<GridRow>({
            columnId: "parent",
            compare: (a, b) => (a.data.parentEntityName ?? "").localeCompare(b.data.parentEntityName ?? ""),
            renderHeaderCell: () => "Parent Entity",
            renderCell: (row) => (
                <TableCellLayout>
                    <Text>{row.data.parentEntityName ?? "—"}</Text>
                </TableCellLayout>
            ),
        }),
        createTableColumn<GridRow>({
            columnId: "modified",
            compare: (a, b) => (a.data.modifiedOn ?? "").localeCompare(b.data.modifiedOn ?? ""),
            renderHeaderCell: () => "Modified",
            renderCell: (row) => (
                <TableCellLayout>
                    <Text>{formatDate(row.data.modifiedOn)}</Text>
                </TableCellLayout>
            ),
        }),
        createTableColumn<GridRow>({
            columnId: "actions",
            renderHeaderCell: () => "",
            renderCell: (row) => (
                <TableCellLayout>
                    <div className={styles.actionsCell}>
                        <Tooltip content="Open document record" relationship="label">
                            <Button
                                size="small"
                                appearance="subtle"
                                icon={<Open20Regular />}
                                onClick={() => handleOpenRecord(row.data)}
                                disabled={row.data.isOrphanFile || (!row.data.recordUrl && !row.data.documentId)}
                                aria-label="Open record"
                            />
                        </Tooltip>
                        <Tooltip content="View in SharePoint" relationship="label">
                            <Button
                                size="small"
                                appearance="subtle"
                                icon={<Globe20Regular />}
                                onClick={() => handleViewFile(row.data)}
                                disabled={!row.data.fileUrl}
                                aria-label="View file"
                            />
                        </Tooltip>
                    </div>
                </TableCellLayout>
            ),
        }),
    ], [styles, handleOpenRecord, handleViewFile]);

    if (rows.length === 0) {
        return (
            <div className={styles.container}>
                <div className={styles.emptyState}>
                    <Text size={400}>No documents to display</Text>
                    <Text size={200}>Load a document with AI embeddings to see related documents</Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <DataGrid
                className={styles.grid}
                items={rows}
                columns={columns}
                sortable
                getRowId={(row) => row.id}
                focusMode="composite"
            >
                <DataGridHeader>
                    <DataGridRow>
                        {({ renderHeaderCell }) => (
                            <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                        )}
                    </DataGridRow>
                </DataGridHeader>
                <DataGridBody<GridRow>>
                    {({ item, rowId }) => (
                        <DataGridRow<GridRow> key={rowId}>
                            {({ renderCell }) => (
                                <DataGridCell>{renderCell(item)}</DataGridCell>
                            )}
                        </DataGridRow>
                    )}
                </DataGridBody>
            </DataGrid>
        </div>
    );
};

export default RelationshipGrid;
