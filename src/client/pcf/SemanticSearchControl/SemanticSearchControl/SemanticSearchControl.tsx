/**
 * SemanticSearchControl - Main component for semantic document search.
 *
 * Provides a three-region layout:
 * - Header: Search input with search button
 * - Sidebar: Filter panel (hidden in compact mode)
 * - Main: Search results list with infinite scroll
 *
 * @see ADR-021 for Fluent UI v9 and design token requirements
 */

import * as React from "react";
import { useState, useCallback, useMemo, useEffect, useRef } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Text,
    Link,
    Button,
    Tooltip,
    Dialog,
    DialogSurface,
    DialogBody,
} from "@fluentui/react-components";
import { ChevronRight20Regular } from "@fluentui/react-icons";
import {
    ISemanticSearchControlProps,
    SearchFilters,
    SearchResult,
    SearchScope,
} from "./types";
import {
    SearchInput,
    FilterPanel,
    ResultsList,
    LoadingState,
    EmptyState,
    ErrorState,
} from "./components";
import {
    useSemanticSearch,
    useFilters,
} from "./hooks";
import {
    SemanticSearchApiService,
    MsalAuthProvider,
    NavigationService,
} from "./services";

/**
 * Styles using makeStyles with Fluent design tokens.
 * NO hard-coded colors - all values from tokens (ADR-021).
 */
const useStyles = makeStyles({
    // Root container
    root: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        boxSizing: "border-box",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        fontFamily: tokens.fontFamilyBase,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.overflow("hidden"),
    },

    // Compact mode root adjustments
    rootCompact: {
        maxHeight: "400px",
    },

    // Header region (search input)
    header: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.padding(tokens.spacingHorizontalM),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderBottom(
            tokens.strokeWidthThin,
            "solid",
            tokens.colorNeutralStroke1
        ),
    },

    // Main content area (sidebar + results)
    content: {
        display: "flex",
        flex: 1,
        ...shorthands.overflow("hidden"),
    },

    // Sidebar region (filters) - hidden in compact mode
    sidebar: {
        width: "250px",
        flexShrink: 0,
        boxSizing: "border-box",
        ...shorthands.padding(tokens.spacingHorizontalS),
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.borderRight(
            tokens.strokeWidthThin,
            "solid",
            tokens.colorNeutralStroke1
        ),
        overflowY: "auto",
        overflowX: "hidden",
    },

    // Collapsed sidebar strip
    sidebarCollapsed: {
        width: "36px",
        flexShrink: 0,
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "center",
        paddingTop: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground3,
        ...shorthands.borderRight(
            tokens.strokeWidthThin,
            "solid",
            tokens.colorNeutralStroke1
        ),
    },

    // Main region (results list)
    main: {
        flex: 1,
        display: "flex",
        flexDirection: "column",
        ...shorthands.overflow("hidden"),
    },

    // Footer for compact mode "View all" link
    footer: {
        display: "flex",
        justifyContent: "center",
        ...shorthands.padding(tokens.spacingHorizontalS),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderTop(
            tokens.strokeWidthThin,
            "solid",
            tokens.colorNeutralStroke1
        ),
    },

    // Centered content container for states
    centeredState: {
        flex: 1,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        ...shorthands.padding(tokens.spacingHorizontalL),
    },

    // Version footer (always visible)
    versionFooter: {
        display: "flex",
        justifyContent: "flex-end",
        ...shorthands.padding(tokens.spacingHorizontalXS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderTop(
            tokens.strokeWidthThin,
            "solid",
            tokens.colorNeutralStroke1
        ),
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4,
    },

    // Find Similar iframe dialog
    findSimilarSurface: {
        padding: "0px",
        width: "85vw",
        maxWidth: "85vw",
        height: "85vh",
        maxHeight: "85vh",
        display: "flex",
        flexDirection: "column",
        ...shorthands.overflow("hidden"),
        ...shorthands.borderRadius(tokens.borderRadiusXLarge),
    },
    findSimilarBody: {
        padding: "0px",
        flex: 1,
        minHeight: 0,
        position: "relative" as const,
    },
    findSimilarFrame: {
        position: "absolute" as const,
        top: 0,
        left: 0,
        width: "100%",
        height: "100%",
        border: "none",
        display: "block",
    },
});

/**
 * SemanticSearchControl main component.
 *
 * Renders the complete search UI with configurable layout based on compactMode.
 */
export const SemanticSearchControl: React.FC<ISemanticSearchControlProps> = ({
    context,
    notifyOutputChanged,
    onDocumentSelect,
    isDarkMode = false,
}) => {
    const styles = useStyles();

    // Get control properties from context
    const showFilters = context.parameters.showFilters?.raw ?? true;
    const compactMode = context.parameters.compactMode?.raw ?? false;
    const placeholder =
        context.parameters.placeholder?.raw ?? "Search documents...";
    const apiBaseUrl = context.parameters.apiBaseUrl?.raw ?? "";

    // Auto-detect entity context from page (when on a record form)
    // Uses the record's GUID directly instead of relying on bound field
    // Note: context.page exists at runtime but isn't in @types/powerapps-component-framework
    const pageContext = (context as unknown as { page?: { entityId?: string; entityTypeName?: string } }).page;
    const pageEntityId = pageContext?.entityId ?? null;
    const pageEntityTypeName = pageContext?.entityTypeName ?? null;

    // DEBUG: Log page context detection
    console.log("[SemanticSearchControl] Page context detection:", {
        pageContext,
        pageEntityId,
        pageEntityTypeName,
        fullContext: context,
    });

    // Map Dataverse entity logical names to API entity types
    const getEntityTypeFromLogicalName = (logicalName: string | null): string | null => {
        if (!logicalName) return null;
        const mapping: Record<string, string> = {
            "sprk_matter": "matter",
            "sprk_project": "project",
            "sprk_invoice": "invoice",
            "account": "account",
            "contact": "contact",
        };
        return mapping[logicalName.toLowerCase()] ?? null;
    };

    // Determine search scope and entity context
    // Priority: 1) Page context (record form), 2) Parameter binding (fallback)
    const configuredScope = (context.parameters.searchScope?.raw ?? "all") as SearchScope;
    const parameterScopeId = context.parameters.scopeId?.raw ?? null;

    // Use page context when available, otherwise fall back to parameters
    const detectedEntityType = getEntityTypeFromLogicalName(pageEntityTypeName);
    const searchScope: SearchScope = pageEntityId && detectedEntityType
        ? (detectedEntityType as SearchScope)  // Use detected entity type as scope
        : configuredScope;

    // Use page entityId (GUID) when on a record form, otherwise use bound parameter
    const scopeId = pageEntityId ?? parameterScopeId;

    // DEBUG: Log final scope determination
    console.log("[SemanticSearchControl] Scope determination:", {
        detectedEntityType,
        configuredScope,
        parameterScopeId,
        finalSearchScope: searchScope,
        finalScopeId: scopeId,
    });

    // Query input state
    const [queryInput, setQueryInput] = useState("");
    const [hasSearched, setHasSearched] = useState(false);

    // Find Similar dialog state — URL of the web resource to show in the iframe dialog
    const [findSimilarUrl, setFindSimilarUrl] = useState<string | null>(null);

    // Filter pane collapse state
    const [isFilterPaneCollapsed, setIsFilterPaneCollapsed] = useState(false);
    const handleToggleFilterPane = useCallback(() => {
        setIsFilterPaneCollapsed((prev) => !prev);
    }, []);

    // Initialize services (memoized to prevent recreation)
    const authProvider = useMemo(() => MsalAuthProvider.getInstance(), []);
    const apiService = useMemo(
        () => new SemanticSearchApiService(apiBaseUrl, authProvider),
        [apiBaseUrl, authProvider]
    );
    const navigationService = useMemo(() => new NavigationService(), []);

    // Auth initialization state
    const [isAuthInitialized, setIsAuthInitialized] = useState(false);
    const [authError, setAuthError] = useState<string | null>(null);

    // Filter state management — declared BEFORE auth effects so useEffect can reference filters
    const {
        filters,
        setFilters,
        clearFilters,
        hasActiveFilters,
    } = useFilters();

    // Search state management — declared BEFORE auth effects so useEffect can reference search
    const {
        results,
        totalCount,
        isLoading,
        isLoadingMore,
        error,
        hasMore,
        query,
        search,
        loadMore,
        reset,
    } = useSemanticSearch(apiService, searchScope, scopeId);

    // Initialize MSAL on mount
    useEffect(() => {
        const initAuth = async () => {
            try {
                await authProvider.initialize();
                setIsAuthInitialized(true);
            } catch (err) {
                console.error("[SemanticSearchControl] MSAL initialization failed:", err);
                setAuthError(err instanceof Error ? err.message : "Authentication initialization failed");
            }
        };
        void initAuth();
    }, [authProvider]);

    // Auto-load all documents once auth is ready (shows documents without requiring a search query).
    // search and filters intentionally omitted from deps — we only want to fire once on auth ready.
    useEffect(() => {
        if (isAuthInitialized) {
            setHasSearched(true);
            void search("", filters);
        }
    }, [isAuthInitialized]);

    // Execute search — empty query is allowed (returns all documents in scope)
    const handleSearch = useCallback(() => {
        if (!isAuthInitialized) {
            console.warn("[SemanticSearchControl] Cannot search - auth not initialized");
            return;
        }
        setHasSearched(true);
        void search(queryInput, filters);
    }, [queryInput, filters, search, isAuthInitialized]);

    // Handle filter changes - trigger new search even with no query
    const handleFiltersChange = useCallback(
        (newFilters: SearchFilters) => {
            setFilters(newFilters);
            // Re-search whenever initial load has happened (supports filter-only queries)
            if (hasSearched) {
                void search(query, newFilters);
            }
        },
        [setFilters, query, hasSearched, search]
    );

    // Handle retry after error
    const handleRetry = useCallback(() => {
        if (query.trim()) {
            void search(query, filters);
        }
    }, [query, filters, search]);

    // Handle result click
    const handleResultClick = useCallback(
        (result: SearchResult) => {
            onDocumentSelect(result.documentId);
            notifyOutputChanged();
        },
        [onDocumentSelect, notifyOutputChanged]
    );

    // Handle open file — mode is "web" (browser) or "desktop" (Office app).
    // Calls the BFF /api/documents/{id}/open-links endpoint on demand to get the
    // SharePoint webUrl and desktop protocol URL (ms-word://, ms-excel://, etc.)
    // since search results do not pre-populate fileUrl.
    const handleOpenFile = useCallback(
        (result: SearchResult, mode: "web" | "desktop") => {
            apiService.getOpenLinks(result.documentId).then((openLinks) => {
                if (mode === "web") {
                    return window.open(openLinks.webUrl, "_blank", "noopener,noreferrer");
                }
                // Desktop: use protocol URL if available, otherwise fall back to web
                if (openLinks.desktopUrl) {
                    return window.open(openLinks.desktopUrl, "_self");
                }
                return window.open(openLinks.webUrl, "_blank", "noopener,noreferrer");
            }).catch((err) => {
                console.error("[SemanticSearchControl] Failed to open file:", err);
            });
        },
        [apiService]
    );

    // Handle open record
    const handleOpenRecord = useCallback(
        (result: SearchResult, inModal: boolean) => {
            if (inModal) {
                void navigationService.openRecordModal(result);
            } else {
                void navigationService.openRecordNewTab(result);
            }
        },
        [navigationService]
    );

    // Handle view all navigation (when DOM cap reached)
    const handleViewAll = useCallback(() => {
        void navigationService.viewAllResults(query, searchScope, scopeId, filters);
    }, [navigationService, query, searchScope, scopeId, filters]);

    // Handle Find Similar — opens DocumentRelationshipViewer as an in-page iframe dialog
    const handleFindSimilar = useCallback(
        (result: SearchResult) => {
            const url = navigationService.getFindSimilarUrl(result, isDarkMode);
            if (url) setFindSimilarUrl(url);
        },
        [navigationService, isDarkMode]
    );

    // Preview URL cache — avoids redundant BFF calls for the same document
    const previewUrlCache = useRef<Map<string, string>>(new Map());

    // Handle Preview — fetches a read-only preview URL via Graph API preview endpoint
    const handlePreview = useCallback(
        async (result: SearchResult): Promise<string | null> => {
            // Return cached URL if available
            const cached = previewUrlCache.current.get(result.documentId);
            if (cached) return cached;

            try {
                const url = await apiService.getPreviewUrl(result.documentId);
                if (url) {
                    previewUrlCache.current.set(result.documentId, url);
                }
                return url;
            } catch (err) {
                console.error("[SemanticSearchControl] Failed to get preview URL:", err);
                return null;
            }
        },
        [apiService]
    );

    // Handle Add Document — opens document upload dialog custom page
    const handleAddDocument = useCallback(() => {
        void navigationService.openAddDocument(
            scopeId,
            searchScope !== "all" ? searchScope : null
        );
    }, [navigationService, scopeId, searchScope]);

    // Determine what content to show in main area
    const renderMainContent = () => {
        // Auth error state
        if (authError) {
            return (
                <div className={styles.centeredState}>
                    <ErrorState
                        message={authError}
                        retryable={false}
                        onRetry={() => window.location.reload()}
                    />
                </div>
            );
        }

        // Auth initializing state
        if (!isAuthInitialized) {
            return (
                <div className={styles.centeredState}>
                    <LoadingState count={compactMode ? 3 : 5} />
                </div>
            );
        }

        // Initial loading state (skeleton)
        if (isLoading && results.length === 0) {
            return (
                <div className={styles.centeredState}>
                    <LoadingState count={compactMode ? 3 : 5} />
                </div>
            );
        }

        // Error state
        if (error) {
            return (
                <div className={styles.centeredState}>
                    <ErrorState
                        message={error.message}
                        retryable={error.retryable}
                        onRetry={handleRetry}
                    />
                </div>
            );
        }

        // Empty state (after search with no results)
        if (hasSearched && results.length === 0 && !isLoading) {
            return (
                <div className={styles.centeredState}>
                    <EmptyState query={query} hasFilters={hasActiveFilters} />
                </div>
            );
        }

        // Results list
        if (results.length > 0) {
            return (
                <ResultsList
                    results={results}
                    isLoading={isLoading}
                    isLoadingMore={isLoadingMore}
                    hasMore={hasMore}
                    totalCount={totalCount}
                    onLoadMore={loadMore}
                    onResultClick={handleResultClick}
                    onOpenFile={handleOpenFile}
                    onOpenRecord={handleOpenRecord}
                    onFindSimilar={handleFindSimilar}
                    onPreview={handlePreview}
                    onViewAll={handleViewAll}
                    compactMode={compactMode}
                />
            );
        }

        // Initial state (before first search)
        return (
            <div className={styles.centeredState}>
                <Text>Enter a search query to find documents</Text>
            </div>
        );
    };

    // Combine root styles based on mode
    const rootClassName = compactMode
        ? `${styles.root} ${styles.rootCompact}`
        : styles.root;

    return (
        <div className={rootClassName}>
            {/* Header Region: Search Input */}
            <div className={styles.header}>
                <SearchInput
                    value={queryInput}
                    placeholder={placeholder}
                    disabled={isLoading}
                    onValueChange={setQueryInput}
                    onSearch={handleSearch}
                    onAddDocument={handleAddDocument}
                />
            </div>

            {/* Content Region: Sidebar + Main */}
            <div className={styles.content}>
                {/* Sidebar Region: Filters (hidden in compact mode or when disabled) */}
                {showFilters && !compactMode && (
                    isFilterPaneCollapsed ? (
                        <div className={styles.sidebarCollapsed}>
                            <Tooltip content="Expand filters" relationship="label">
                                <Button
                                    appearance="subtle"
                                    size="small"
                                    icon={<ChevronRight20Regular />}
                                    onClick={handleToggleFilterPane}
                                    aria-label="Expand filters"
                                />
                            </Tooltip>
                        </div>
                    ) : (
                        <div className={styles.sidebar}>
                            <FilterPanel
                                filters={filters}
                                searchScope={searchScope}
                                scopeId={scopeId}
                                onFiltersChange={handleFiltersChange}
                                disabled={isLoading}
                                onCollapse={handleToggleFilterPane}
                            />
                        </div>
                    )
                )}

                {/* Main Region: Results */}
                <div className={styles.main}>
                    {renderMainContent()}
                </div>
            </div>

            {/* Footer: View All link (compact mode only) */}
            {compactMode && results.length > 0 && (
                <div className={styles.footer}>
                    <Link onClick={handleViewAll}>
                        View all {totalCount} results →
                    </Link>
                </div>
            )}

            {/* Version Footer (always visible) */}
            <div className={styles.versionFooter}>
                <Text size={100}>v1.0.36 • Built 2026-02-24</Text>
            </div>

            {/* Find Similar — iframe dialog (no Dataverse chrome) */}
            <Dialog
                open={!!findSimilarUrl}
                onOpenChange={(_, data) => { if (!data.open) setFindSimilarUrl(null); }}
            >
                <DialogSurface className={styles.findSimilarSurface}>
                    <DialogBody className={styles.findSimilarBody}>
                        {findSimilarUrl && (
                            <iframe
                                src={findSimilarUrl}
                                title="Document Relationships"
                                className={styles.findSimilarFrame}
                            />
                        )}
                    </DialogBody>
                </DialogSurface>
            </Dialog>
        </div>
    );
};

export default SemanticSearchControl;
