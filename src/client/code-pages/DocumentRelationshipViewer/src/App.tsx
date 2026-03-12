/**
 * DocumentRelationshipViewer App — React Code Page (React 19)
 *
 * Opened as HTML web resource dialog via:
 *   Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_documentrelationshipviewer", data: "documentId=...&tenantId=..." }, { target: 2 })
 *
 * URL params:
 *   documentId  - GUID of the source document (required)
 *   tenantId    - Azure AD tenant ID (required)
 *   apiBaseUrl  - BFF API base URL (optional, defaults to dev)
 *
 * Features:
 *   - Graph view: @xyflow/react v12 force-directed visualization
 *   - Grid view: Fluent v9 DataGrid tabular view (toggle with toolbar button)
 *   - Filter panel: similarity threshold, depth, document type filters
 *   - Node action bar: open record, view file, expand
 */

import React, { useState, useEffect, useCallback, useRef, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Button,
    Text,
    Spinner,
    MessageBar,
    MessageBarTitle,
    MessageBarBody,
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Badge,
    Tooltip,
    SearchBox,
} from "@fluentui/react-components";
import type { SearchBoxChangeEvent, InputOnChangeData } from "@fluentui/react-components";
import {
    NetworkCheck20Regular,
    Grid20Regular,
    Filter20Regular,
    FilterDismiss20Regular,
    ArrowDownload20Regular,
    ArrowSync20Regular,
} from "@fluentui/react-icons";
import { DocumentGraph } from "./components/DocumentGraph";
import { RelationshipGrid, type GridRow } from "./components/RelationshipGrid";
// Network and Timeline views available but not in toolbar yet:
// import { RelationshipNetwork } from "./components/RelationshipNetwork";
// import { RelationshipTimeline } from "./components/RelationshipTimeline";
import { ControlPanel, DEFAULT_FILTER_SETTINGS, DOCUMENT_TYPES, type FilterSettings } from "./components/ControlPanel";
// NodeActionBar removed — node click now opens FilePreviewDialog
import { useVisualizationApi, formatVisualizationError } from "./hooks/useVisualizationApi";
import { initializeAuth, getToken } from "./services/authInit";
import { exportToCsv } from "./services/CsvExportService";
import { createFilePreviewServices } from "./services/FilePreviewServiceAdapter";
import { FilePreviewDialog } from "../../../shared/Spaarke.UI.Components/dist/components/FilePreview";
import type { DocumentNode } from "./types/graph";

const DEFAULT_API_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

type ViewMode = "graph" | "grid" | "network" | "timeline";

interface AppProps {
    params: URLSearchParams;
    isDark?: boolean;
}

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100vh",
        backgroundColor: tokens.colorNeutralBackground1,
        overflow: "hidden",
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        height: "37px",
        padding: `0 ${tokens.spacingHorizontalM}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        gap: tokens.spacingHorizontalS,
        flexShrink: 0,
    },
    main: {
        display: "flex",
        flex: "1 1 0",
        minHeight: 0,
        overflow: "hidden",
        position: "relative",
    },
    filterPanel: {
        position: "absolute",
        top: tokens.spacingVerticalM,
        right: tokens.spacingHorizontalM,
        zIndex: 50,
        maxHeight: "calc(100% - 32px)",
        overflowY: "auto",
    },
    content: {
        flex: "1 1 0",
        overflow: "hidden",
        position: "relative",
        minHeight: 0,
    },
    footer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalM}`,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        flexShrink: 0,
    },
    footerText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    centerState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingHorizontalXXL,
    },
    errorBar: {
        margin: tokens.spacingVerticalM,
    },
    viewToggle: {
        display: "flex",
        gap: tokens.spacingHorizontalXXS,
    },
    viewButton: {
        minWidth: "auto",
    },
    activeViewButton: {
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1,
    },
    searchBox: {
        minWidth: "200px",
        maxWidth: "300px",
    },
    toolbarSpacer: {
        flex: 1,
    },
    gridToolbar: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground1,
        flexShrink: 0,
    },
});

export const App: React.FC<AppProps> = ({ params, isDark = false }) => {
    const styles = useStyles();

    // Parse URL params
    const documentId = params.get("documentId") ?? "";
    const tenantId = params.get("tenantId") ?? "";
    const apiBaseUrl = params.get("apiBaseUrl") ?? DEFAULT_API_BASE_URL;

    // Auth state
    const [accessToken, setAccessToken] = useState<string | undefined>(undefined);
    const [authError, setAuthError] = useState<string | null>(null);
    const [isAuthInitializing, setIsAuthInitializing] = useState(true);

    // UI state
    const [viewMode, setViewMode] = useState<ViewMode>("grid");
    const [showFilterPanel, setShowFilterPanel] = useState(true);
    const [filters, setFilters] = useState<FilterSettings>(DEFAULT_FILTER_SETTINGS);
    const [selectedNode, setSelectedNode] = useState<DocumentNode | null>(null);
    const [searchQuery, setSearchQuery] = useState("");
    const filteredRowsRef = useRef<GridRow[]>([]);

    // File preview dialog state
    const [previewDocumentId, setPreviewDocumentId] = useState<string | null>(null);
    const [previewDocumentName, setPreviewDocumentName] = useState<string>("");
    const [isPreviewOpen, setIsPreviewOpen] = useState(false);
    const [previewInWorkspace, setPreviewInWorkspace] = useState(false);

    // FilePreviewDialog services adapter (memoized)
    const filePreviewServices = useMemo(() => createFilePreviewServices(apiBaseUrl), [apiBaseUrl]);

    // Auth initialization — uses @spaarke/auth (multi-tenant, no hardcoded values)
    useEffect(() => {
        let cancelled = false;

        initializeAuth()
            .then(() => getToken())
            .then((token) => {
                if (!cancelled) setAccessToken(token);
            })
            .catch((err: unknown) => {
                if (!cancelled) {
                    console.error("[App] Auth failed:", err);
                    setAuthError(err instanceof Error ? err.message : "Authentication failed");
                }
            })
            .finally(() => {
                if (!cancelled) setIsAuthInitializing(false);
            });

        return () => { cancelled = true; };
    }, []);

    // Only send documentTypes filter when user has unchecked some types.
    // When all types are selected (default), omit the param so the BFF doesn't
    // apply a filter — the AI Search index uses business type names (Contract, Invoice)
    // not file extensions (pdf, docx), so sending all extensions would match nothing.
    const effectiveDocumentTypes = filters.documentTypes.length < DOCUMENT_TYPES.length
        ? filters.documentTypes
        : undefined;

    // Fetch visualization data
    const { nodes, edges, metadata, isLoading, error, refetch } = useVisualizationApi({
        apiBaseUrl,
        documentId,
        tenantId,
        accessToken,
        threshold: filters.similarityThreshold,
        limit: filters.maxNodesPerLevel,
        depth: filters.depthLimit,
        documentTypes: effectiveDocumentTypes,
        enabled: !isAuthInitializing && !!documentId && !!tenantId,
    });

    const handleNodeSelect = useCallback((node: DocumentNode) => {
        setSelectedNode(node);
        // Open FilePreviewDialog for the selected node
        if (node.data.documentId) {
            setPreviewDocumentId(node.data.documentId);
            setPreviewDocumentName(node.data.name ?? "Document");
            setIsPreviewOpen(true);
        }
    }, []);

    const handleRowClick = useCallback((documentId: string, documentName: string) => {
        setPreviewDocumentId(documentId);
        setPreviewDocumentName(documentName);
        setIsPreviewOpen(true);
    }, []);

    const handlePreviewClose = useCallback(() => {
        setIsPreviewOpen(false);
        setPreviewDocumentId(null);
    }, []);

    const handleFilterChange = useCallback((newFilters: FilterSettings) => {
        setFilters(newFilters);
        setSelectedNode(null);
    }, []);

    const handleSearchChange = useCallback((_ev: SearchBoxChangeEvent, data: InputOnChangeData) => {
        setSearchQuery(data.value);
    }, []);

    const handleFilteredRowsChange = useCallback((rows: GridRow[]) => {
        filteredRowsRef.current = rows;
    }, []);

    const handleExport = useCallback(() => {
        const sourceNode = nodes.find((n) => n.data.isSource);
        const sourceName = sourceNode?.data.name ?? "document";
        exportToCsv(filteredRowsRef.current, sourceName);
    }, [nodes]);

    // Use theme from URL param (passed by PCF control) instead of system preference
    const isDarkMode = isDark;

    // Missing params error
    if (!documentId || !tenantId) {
        return (
            <div className={styles.root}>
                <div className={styles.centerState}>
                    <Text size={500} weight="semibold">Missing Parameters</Text>
                    <Text>This page requires documentId and tenantId URL parameters.</Text>
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        documentId: {documentId || "(missing)"} • tenantId: {tenantId || "(missing)"}
                    </Text>
                </div>
            </div>
        );
    }

    // Auth initializing
    if (isAuthInitializing) {
        return (
            <div className={styles.root}>
                <div className={styles.centerState}>
                    <Spinner size="large" label="Authenticating..." />
                </div>
            </div>
        );
    }

    const relatedCount = nodes.filter((n) => !n.data.isSource).length;
    const edgeCount = edges.length;

    return (
        <div className={styles.root}>
            {/* Toolbar: [Refresh] | ——spacer—— [Grid] [Graph] [Network] [Timeline] [Settings] */}
            <div className={styles.toolbar}>
                {/* Left: Refresh */}
                <Tooltip content="Refresh data" relationship="label">
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<ArrowSync20Regular />}
                        onClick={refetch}
                        aria-label="Refresh"
                    >
                        Refresh
                    </Button>
                </Tooltip>
                <ToolbarDivider />

                {/* Spacer */}
                <div className={styles.toolbarSpacer} />

                {/* Right: View toggles + Settings */}
                <div className={styles.viewToggle}>
                    <Tooltip content="Grid view" relationship="label">
                        <Button
                            className={`${styles.viewButton} ${viewMode === "grid" ? styles.activeViewButton : ""}`}
                            appearance={viewMode === "grid" ? "primary" : "subtle"}
                            size="small"
                            icon={<Grid20Regular />}
                            onClick={() => setViewMode("grid")}
                            aria-label="Grid view"
                            aria-pressed={viewMode === "grid"}
                        >
                            Grid
                        </Button>
                    </Tooltip>
                    <Tooltip content="Graph view" relationship="label">
                        <Button
                            className={`${styles.viewButton} ${viewMode === "graph" ? styles.activeViewButton : ""}`}
                            appearance={viewMode === "graph" ? "primary" : "subtle"}
                            size="small"
                            icon={<NetworkCheck20Regular />}
                            onClick={() => setViewMode("graph")}
                            aria-label="Graph view"
                            aria-pressed={viewMode === "graph"}
                        >
                            Graph
                        </Button>
                    </Tooltip>
                </div>
                <Tooltip content={showFilterPanel ? "Hide settings" : "Show settings"} relationship="label">
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={showFilterPanel ? <FilterDismiss20Regular /> : <Filter20Regular />}
                        onClick={() => setShowFilterPanel(!showFilterPanel)}
                        aria-label="Toggle settings"
                        aria-pressed={showFilterPanel}
                    />
                </Tooltip>
            </div>

            {/* Grid-specific inline toolbar (search + export) */}
            {viewMode === "grid" && (
                <div className={styles.gridToolbar}>
                    <SearchBox
                        className={styles.searchBox}
                        placeholder="Search documents..."
                        size="small"
                        value={searchQuery}
                        onChange={handleSearchChange}
                        aria-label="Search documents by name"
                    />
                    <Tooltip content="Export filtered rows to CSV" relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<ArrowDownload20Regular />}
                            onClick={handleExport}
                            aria-label="Export to CSV"
                        >
                            Export
                        </Button>
                    </Tooltip>
                </div>
            )}

            {/* Auth or API error */}
            {(authError ?? (error && !isLoading)) && (
                <MessageBar className={styles.errorBar} intent="error">
                    <MessageBarBody>
                        <MessageBarTitle>{authError ? "Authentication Error" : "Load Error"}</MessageBarTitle>
                        {authError ?? formatVisualizationError(error)}
                    </MessageBarBody>
                </MessageBar>
            )}

            {/* Main content */}
            <div className={styles.main}>
                {/* Filter panel (floating, graph view only in this position) */}
                {showFilterPanel && (
                    <div className={styles.filterPanel}>
                        <ControlPanel settings={filters} onSettingsChange={handleFilterChange} viewMode={viewMode} />
                    </div>
                )}

                <div className={styles.content}>
                    {isLoading ? (
                        <div className={styles.centerState}>
                            <Spinner size="large" label="Loading document relationships..." />
                        </div>
                    ) : viewMode === "graph" ? (
                        <DocumentGraph
                            nodes={nodes}
                            edges={edges}
                            isDarkMode={isDarkMode}
                            onNodeSelect={handleNodeSelect}
                            showMinimap
                        />
                    ) : viewMode === "grid" ? (
                        <RelationshipGrid
                            nodes={nodes}
                            isDarkMode={isDarkMode}
                            searchQuery={searchQuery}
                            onFilteredRowsChange={handleFilteredRowsChange}
                            onRowClick={handleRowClick}
                        />
                    ) : null}
                </div>
            </div>

            {/* Footer stats */}
            <div className={styles.footer}>
                <Text className={styles.footerText}>
                    {relatedCount} related documents
                </Text>
                <Text className={styles.footerText}>•</Text>
                <Text className={styles.footerText}>
                    {edgeCount} edges
                </Text>
                {metadata && (
                    <>
                        <Text className={styles.footerText}>•</Text>
                        <Text className={styles.footerText}>
                            {metadata.searchLatencyMs}ms
                        </Text>
                        {metadata.cacheHit && (
                            <>
                                <Text className={styles.footerText}>•</Text>
                                <Badge appearance="tint" color="success" size="small">cached</Badge>
                            </>
                        )}
                    </>
                )}
                <Text className={styles.footerText} style={{ marginLeft: "auto" }}>
                    DocumentRelationshipViewer v1.0.0
                </Text>
            </div>

            {/* File Preview Dialog — shared between grid and graph views */}
            {previewDocumentId && (
                <FilePreviewDialog
                    open={isPreviewOpen}
                    documentName={previewDocumentName}
                    documentId={previewDocumentId}
                    isInWorkspace={previewInWorkspace}
                    services={filePreviewServices}
                    onClose={handlePreviewClose}
                    onWorkspaceFlagChanged={(newFlag: boolean) => setPreviewInWorkspace(newFlag)}
                />
            )}
        </div>
    );
};

export default App;
