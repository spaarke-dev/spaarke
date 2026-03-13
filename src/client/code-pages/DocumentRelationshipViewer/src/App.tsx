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

import React, { useState, useEffect, useCallback, useRef } from "react";
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
    Filter20Regular,
    FilterDismiss20Regular,
    ArrowDownload20Regular,
    ArrowSync20Regular,
    Search20Regular,
    Dismiss20Regular,
} from "@fluentui/react-icons";
import { DocumentGraph } from "./components/DocumentGraph";
import { RelationshipGrid, type GridRow } from "./components/RelationshipGrid";
// Network and Timeline views available but not in toolbar yet:
// import { RelationshipNetwork } from "./components/RelationshipNetwork";
// import { RelationshipTimeline } from "./components/RelationshipTimeline";
import { ControlPanel, DEFAULT_FILTER_SETTINGS, DOCUMENT_TYPES, RELATIONSHIP_TYPES, type FilterSettings } from "./components/ControlPanel";
// NodeActionBar removed — node click now opens FilePreviewDialog
import { useVisualizationApi, formatVisualizationError } from "./hooks/useVisualizationApi";
import { initializeAuth, getToken } from "./services/authInit";
import { exportToCsv } from "./services/CsvExportService";
import { authenticatedFetch } from "./services/authInit";
import { FilePreviewDialog } from "./components/FilePreviewDialog";
import type { DocumentNode } from "./types/graph";

/** Inline SVG icon: table/grid view (matches Fluent 20px icon style) */
const TableViewIcon: React.FC = () => (
    <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M3 4.5C3 3.67 3.67 3 4.5 3h11c.83 0 1.5.67 1.5 1.5v11c0 .83-.67 1.5-1.5 1.5h-11C3.67 17 3 16.33 3 15.5v-11zM4.5 4a.5.5 0 00-.5.5V7h12V4.5a.5.5 0 00-.5-.5h-11zM16 8H4v3h12V8zm0 4H4v3.5a.5.5 0 00.5.5h11a.5.5 0 00.5-.5V12z" fill="currentColor"/>
        <path d="M8 8v7M12 8v7" stroke="currentColor" strokeWidth="1"/>
    </svg>
);

/** Inline SVG icon: graph/network view (matches Fluent 20px icon style) */
const GraphViewIcon: React.FC = () => (
    <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
        <circle cx="10" cy="5" r="2.5" stroke="currentColor" strokeWidth="1.2"/>
        <circle cx="5" cy="14" r="2.5" stroke="currentColor" strokeWidth="1.2"/>
        <circle cx="15" cy="14" r="2.5" stroke="currentColor" strokeWidth="1.2"/>
        <line x1="10" y1="7.5" x2="6.5" y2="11.5" stroke="currentColor" strokeWidth="1.2"/>
        <line x1="10" y1="7.5" x2="13.5" y2="11.5" stroke="currentColor" strokeWidth="1.2"/>
        <line x1="7.5" y1="14" x2="12.5" y2="14" stroke="currentColor" strokeWidth="1.2"/>
    </svg>
);

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
        padding: "10px",
        boxSizing: "border-box",
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        height: "47px",
        padding: `0 ${tokens.spacingHorizontalM}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        gap: tokens.spacingHorizontalM,
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
        gap: tokens.spacingHorizontalS,
    },
    viewButton: {
        minWidth: "auto",
    },
    activeViewButton: {
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1,
    },
    toolbarSpacer: {
        flex: 1,
    },
    searchContainer: {
        position: "relative",
        display: "flex",
        alignItems: "center",
    },
    searchBox: {
        width: "400px",
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
    const [isSearchExpanded, setIsSearchExpanded] = useState(false);
    const filteredRowsRef = useRef<GridRow[]>([]);

    // File preview dialog state
    const [previewDocumentId, setPreviewDocumentId] = useState<string | null>(null);
    const [previewDocumentName, setPreviewDocumentName] = useState<string>("");
    const [isPreviewOpen, setIsPreviewOpen] = useState(false);
    const [previewInWorkspace, setPreviewInWorkspace] = useState(false);

    // Preview URL cache with TTL — avoids re-fetching on repeated preview of same document.
    // SPE preview URLs expire after ~15 min; we use 10 min TTL to avoid stale URLs.
    // Max 50 entries (LRU eviction by insertion order).
    const previewUrlCacheRef = useRef<Map<string, { url: string; ts: number }>>(new Map());
    const CACHE_TTL_MS = 10 * 60 * 1000; // 10 minutes
    const CACHE_MAX_SIZE = 50;

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

    // Helper: get from cache if not expired
    const getCachedUrl = useCallback((docId: string): string | null => {
        const entry = previewUrlCacheRef.current.get(docId);
        if (!entry) return null;
        if (Date.now() - entry.ts > CACHE_TTL_MS) {
            previewUrlCacheRef.current.delete(docId);
            return null;
        }
        return entry.url;
    }, []);

    // Helper: put into cache with LRU eviction
    const setCachedUrl = useCallback((docId: string, url: string) => {
        const cache = previewUrlCacheRef.current;
        // Evict oldest entries if at capacity
        if (cache.size >= CACHE_MAX_SIZE) {
            const firstKey = cache.keys().next().value;
            if (firstKey) cache.delete(firstKey);
        }
        cache.set(docId, { url, ts: Date.now() });
    }, []);

    // Prefetch a preview URL into cache (fire-and-forget, e.g. on hover)
    const prefetchPreviewUrl = useCallback((docId: string) => {
        if (!docId || getCachedUrl(docId)) return;
        void (async () => {
            try {
                const res = await authenticatedFetch(`${apiBaseUrl}/api/documents/${docId}/preview-url`);
                if (!res.ok) return;
                const data = await res.json();
                const url = data.previewUrl ?? data.url;
                if (url) setCachedUrl(docId, url);
            } catch { /* silent prefetch failure */ }
        })();
    }, [apiBaseUrl, getCachedUrl, setCachedUrl]);

    // FilePreviewDialog callbacks — checks cache first
    const fetchPreviewUrl = useCallback(async (): Promise<string | null> => {
        if (!previewDocumentId) return null;
        // Return cached URL immediately if available
        const cached = getCachedUrl(previewDocumentId);
        if (cached) return cached;
        try {
            const res = await authenticatedFetch(`${apiBaseUrl}/api/documents/${previewDocumentId}/preview-url`);
            if (!res.ok) return null;
            const data = await res.json();
            const url = data.previewUrl ?? data.url ?? null;
            if (url) setCachedUrl(previewDocumentId, url);
            return url;
        } catch {
            console.error("[FilePreview] Failed to get preview URL:", previewDocumentId);
            return null;
        }
    }, [previewDocumentId, apiBaseUrl, getCachedUrl, setCachedUrl]);

    const handleOpenFile = useCallback((mode: "desktop" | "web") => {
        if (!previewDocumentId) return;
        void (async () => {
            try {
                const res = await authenticatedFetch(`${apiBaseUrl}/api/documents/${encodeURIComponent(previewDocumentId)}/open-links`);
                if (!res.ok) return;
                const links = await res.json();
                // Desktop protocol available (Word, Excel, PowerPoint) — open in native app
                if (links.desktopUrl) {
                    window.open(links.desktopUrl, "_self");
                    return;
                }
                // No desktop protocol — download via BFF /content endpoint and let OS open
                // (e.g. Adobe Acrobat for PDFs). SPE webUrl requires SharePoint permissions
                // users may not have, so we stream through the BFF.
                try {
                    const contentUrl = `${apiBaseUrl}/api/documents/${encodeURIComponent(previewDocumentId)}/content`;
                    const response = await authenticatedFetch(contentUrl);
                    if (response.ok) {
                        const blob = await response.blob();
                        const objectUrl = URL.createObjectURL(blob);
                        const a = document.createElement("a");
                        a.href = objectUrl;
                        a.download = links.fileName ?? previewDocumentName ?? "document";
                        document.body.appendChild(a);
                        a.click();
                        document.body.removeChild(a);
                        URL.revokeObjectURL(objectUrl);
                        return;
                    }
                } catch (err) {
                    console.error("[FilePreview] Download failed, falling back:", err);
                }
                // Final fallback to webUrl
                if (links.webUrl) {
                    window.open(links.webUrl, "_blank", "noopener,noreferrer");
                }
            } catch {
                console.error("[FilePreview] Failed to get open links:", previewDocumentId);
            }
        })();
    }, [previewDocumentId, previewDocumentName, apiBaseUrl]);

    const handleOpenRecord = useCallback(() => {
        if (!previewDocumentId) return;
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        if (xrm?.Navigation?.openForm) {
            xrm.Navigation.openForm({ entityName: "sprk_document", entityId: previewDocumentId });
        } else {
            const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? window.location.origin;
            window.open(`${clientUrl}/main.aspx?etn=sprk_document&id=${previewDocumentId}&pagetype=entityrecord`, "_blank");
        }
    }, [previewDocumentId]);

    const handleEmailDocument = useCallback(() => {
        // Email not yet implemented in viewer context — stub
        console.log("[FilePreview] Email not implemented for viewer context");
    }, []);

    const handleCopyLink = useCallback(() => {
        if (!previewDocumentId) return;
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? window.location.origin;
        const url = `${clientUrl}/main.aspx?etn=sprk_document&id=${previewDocumentId}&pagetype=entityrecord`;
        void navigator.clipboard.writeText(url);
    }, [previewDocumentId]);

    const handleToggleWorkspace = useCallback(() => {
        setPreviewInWorkspace((prev) => !prev);
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

    // Client-side filtering: document types, relationship types, and search query.
    // NOTE: These useMemo hooks MUST be before any early returns to satisfy
    // React's Rules of Hooks (hooks must run in the same order every render).

    // Map document type checkbox keys to file extensions they cover
    const docTypeExtensions = React.useMemo(() => {
        const map: Record<string, string[]> = {
            pdf: ["pdf"],
            docx: ["docx", "doc", "rtf"],
            xlsx: ["xlsx", "xls", "csv"],
            pptx: ["pptx", "ppt"],
            txt: ["txt"],
            other: ["msg", "eml", "html", "htm", "xml", "json", "zip", "rar", "7z", "jpg", "jpeg", "png", "gif", "svg", "mp4", "avi", "mov", "unknown", "file"],
        };
        const allowed = new Set<string>();
        for (const key of filters.documentTypes) {
            for (const ext of map[key] ?? []) allowed.add(ext);
        }
        return allowed;
    }, [filters.documentTypes]);

    const allDocTypesSelected = filters.documentTypes.length >= DOCUMENT_TYPES.length;
    const allRelTypesSelected = filters.relationshipTypes.length >= RELATIONSHIP_TYPES.length;

    // Step 1: Filter edges by relationship type
    const typeFilteredEdges = React.useMemo(() => {
        if (allRelTypesSelected) return edges;
        const allowedRels = new Set<string>(filters.relationshipTypes);
        return edges.filter((e) => {
            const relType = e.data?.relationshipType;
            return relType ? allowedRels.has(relType) : true;
        });
    }, [edges, filters.relationshipTypes, allRelTypesSelected]);

    // Step 2: Filter nodes by document type, then remove disconnected non-source nodes
    const typeFilteredNodes = React.useMemo(() => {
        if (allDocTypesSelected && allRelTypesSelected) return nodes;
        const connectedIds = new Set<string>();
        for (const e of typeFilteredEdges) {
            connectedIds.add(e.source);
            connectedIds.add(e.target);
        }
        return nodes.filter((n) => {
            // Always keep source node
            if (n.data.isSource) return true;
            // Filter by document type (file extension)
            if (!allDocTypesSelected) {
                const ft = (n.data.fileType ?? "unknown").toLowerCase();
                if (!docTypeExtensions.has(ft)) return false;
            }
            // If relationship types are filtered, only keep nodes still connected
            if (!allRelTypesSelected && !connectedIds.has(n.id)) return false;
            return true;
        });
    }, [nodes, typeFilteredEdges, allDocTypesSelected, allRelTypesSelected, docTypeExtensions]);

    // Step 3: Apply search query filter on top
    const filteredGraphNodes = React.useMemo(() => {
        const base = typeFilteredNodes;
        if (!searchQuery || searchQuery.trim() === "") return base;
        const query = searchQuery.toLowerCase();
        return base.filter((n) => n.data.isSource || n.data.name.toLowerCase().includes(query));
    }, [typeFilteredNodes, searchQuery]);

    const filteredGraphEdges = React.useMemo(() => {
        const base = typeFilteredEdges;
        const visibleIds = new Set(filteredGraphNodes.map((n) => n.id));
        return base.filter((e) => visibleIds.has(e.source) && visibleIds.has(e.target));
    }, [typeFilteredEdges, filteredGraphNodes]);

    const relatedCount = filteredGraphNodes.filter((n) => !n.data.isSource).length;
    const edgeCount = filteredGraphEdges.length;

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

    return (
        <div className={styles.root}>
            {/* Toolbar: [Refresh] [Search] [Export] | ——spacer—— [Grid] [Graph] [Settings] */}
            <div className={styles.toolbar}>
                {/* Left: Refresh + Search + Export */}
                <Tooltip content="Refresh the page" relationship="label">
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<ArrowSync20Regular />}
                        onClick={refetch}
                        aria-label="Refresh the page"
                    />
                </Tooltip>
                <div className={styles.searchContainer}>
                    {isSearchExpanded ? (
                        <>
                            <SearchBox
                                className={styles.searchBox}
                                placeholder="Search documents..."
                                size="small"
                                value={searchQuery}
                                onChange={handleSearchChange}
                                aria-label="Filter the list of documents"
                                autoFocus
                            />
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<Dismiss20Regular />}
                                onClick={() => { setIsSearchExpanded(false); setSearchQuery(""); }}
                                aria-label="Close search"
                            />
                        </>
                    ) : (
                        <Tooltip content="Filter the list of documents" relationship="label">
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<Search20Regular />}
                                onClick={() => setIsSearchExpanded(true)}
                                aria-label="Filter the list of documents"
                            />
                        </Tooltip>
                    )}
                </div>
                {viewMode === "grid" && (
                    <Tooltip content="Export the list of documents" relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<ArrowDownload20Regular />}
                            onClick={handleExport}
                            aria-label="Export the list of documents"
                        />
                    </Tooltip>
                )}
                <ToolbarDivider />

                {/* Spacer */}
                <div className={styles.toolbarSpacer} />

                {/* Right: View toggles + Settings */}
                <div className={styles.viewToggle}>
                    <Tooltip content="View documents in grid" relationship="label">
                        <Button
                            className={`${styles.viewButton} ${viewMode === "grid" ? styles.activeViewButton : ""}`}
                            appearance={viewMode === "grid" ? "primary" : "subtle"}
                            size="small"
                            icon={<TableViewIcon />}
                            onClick={() => setViewMode("grid")}
                            aria-label="View documents in grid"
                            aria-pressed={viewMode === "grid"}
                        />
                    </Tooltip>
                    <Tooltip content="View documents in node graph visual" relationship="label">
                        <Button
                            className={`${styles.viewButton} ${viewMode === "graph" ? styles.activeViewButton : ""}`}
                            appearance={viewMode === "graph" ? "primary" : "subtle"}
                            size="small"
                            icon={<GraphViewIcon />}
                            onClick={() => setViewMode("graph")}
                            aria-label="View documents in node graph visual"
                            aria-pressed={viewMode === "graph"}
                        />
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
                            nodes={filteredGraphNodes}
                            edges={filteredGraphEdges}
                            isDarkMode={isDarkMode}
                            onNodeSelect={handleNodeSelect}
                            showMinimap
                        />
                    ) : viewMode === "grid" ? (
                        <RelationshipGrid
                            nodes={filteredGraphNodes}
                            isDarkMode={isDarkMode}
                            searchQuery={searchQuery}
                            onFilteredRowsChange={handleFilteredRowsChange}
                            onRowClick={handleRowClick}
                            onRowHover={prefetchPreviewUrl}
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
                    onClose={handlePreviewClose}
                    fetchPreviewUrl={fetchPreviewUrl}
                    onOpenFile={handleOpenFile}
                    onOpenRecord={handleOpenRecord}
                    onEmailDocument={handleEmailDocument}
                    onCopyLink={handleCopyLink}
                    onToggleWorkspace={handleToggleWorkspace}
                    isInWorkspace={previewInWorkspace}
                />
            )}
        </div>
    );
};

export default App;
