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

import React, { useState, useEffect, useCallback, useMemo } from "react";
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
} from "@fluentui/react-components";
import {
    NetworkCheck20Regular,
    Grid20Regular,
    Filter20Regular,
    FilterDismiss20Regular,
} from "@fluentui/react-icons";
import { DocumentGraph } from "./components/DocumentGraph";
import { RelationshipGrid } from "./components/RelationshipGrid";
import { ControlPanel, DEFAULT_FILTER_SETTINGS, DOCUMENT_TYPES, type FilterSettings } from "./components/ControlPanel";
import { NodeActionBar } from "./components/NodeActionBar";
import { useVisualizationApi, formatVisualizationError } from "./hooks/useVisualizationApi";
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
import { loginRequest } from "./services/auth/msalConfig";
import type { DocumentNode } from "./types/graph";

const DEFAULT_API_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

type ViewMode = "graph" | "grid";

interface AppProps {
    params: URLSearchParams;
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
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        gap: tokens.spacingHorizontalS,
        flexShrink: 0,
    },
    main: {
        display: "flex",
        flex: 1,
        minHeight: 0,
        overflow: "hidden",
        position: "relative",
    },
    filterPanel: {
        position: "absolute",
        top: tokens.spacingVerticalM,
        left: tokens.spacingHorizontalM,
        zIndex: 50,
        maxHeight: "calc(100% - 32px)",
        overflowY: "auto",
    },
    content: {
        flex: 1,
        overflow: "hidden",
        position: "relative",
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
});

export const App: React.FC<AppProps> = ({ params }) => {
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
    const [viewMode, setViewMode] = useState<ViewMode>("graph");
    const [showFilterPanel, setShowFilterPanel] = useState(false);
    const [filters, setFilters] = useState<FilterSettings>(DEFAULT_FILTER_SETTINGS);
    const [selectedNode, setSelectedNode] = useState<DocumentNode | null>(null);

    // Auth initialization
    useEffect(() => {
        const authProvider = MsalAuthProvider.getInstance();
        let cancelled = false;

        authProvider.initialize()
            .then(() => authProvider.getToken(loginRequest.scopes))
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
    const { nodes, edges, metadata, isLoading, error } = useVisualizationApi({
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
    }, []);

    const handleCloseActionBar = useCallback(() => {
        setSelectedNode(null);
    }, []);

    const handleFilterChange = useCallback((newFilters: FilterSettings) => {
        setFilters(newFilters);
        setSelectedNode(null);
    }, []);

    const isDarkMode = useMemo(
        () => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false,
        []
    );

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
            {/* Toolbar */}
            <div className={styles.toolbar}>
                {/* View toggle */}
                <div className={styles.viewToggle}>
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
                </div>

                <Toolbar>
                    <ToolbarDivider />
                    <Tooltip content={showFilterPanel ? "Hide filters" : "Show filters"} relationship="label">
                        <ToolbarButton
                            icon={showFilterPanel ? <FilterDismiss20Regular /> : <Filter20Regular />}
                            onClick={() => setShowFilterPanel(!showFilterPanel)}
                            aria-label="Toggle filters"
                            aria-pressed={showFilterPanel}
                        />
                    </Tooltip>
                </Toolbar>
            </div>

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
                        <ControlPanel settings={filters} onSettingsChange={handleFilterChange} />
                    </div>
                )}

                <div className={styles.content}>
                    {isLoading ? (
                        <div className={styles.centerState}>
                            <Spinner size="large" label="Loading document relationships..." />
                        </div>
                    ) : viewMode === "graph" ? (
                        <>
                            <DocumentGraph
                                nodes={nodes}
                                edges={edges}
                                isDarkMode={isDarkMode}
                                onNodeSelect={handleNodeSelect}
                                showMinimap
                            />
                            {selectedNode && (
                                <NodeActionBar
                                    nodeData={selectedNode.data}
                                    onClose={handleCloseActionBar}
                                />
                            )}
                        </>
                    ) : (
                        <RelationshipGrid nodes={nodes} isDarkMode={isDarkMode} />
                    )}
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
        </div>
    );
};

export default App;
