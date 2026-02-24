import * as React from "react";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    makeStyles,
    tokens,
    Text,
    Spinner,
    MessageBar,
    MessageBarBody,
    Menu,
    MenuTrigger,
    MenuPopover,
    MenuList,
    MenuItemCheckbox,
    Button,
} from "@fluentui/react-components";
import { DocumentFlowchart24Regular, Filter20Regular } from "@fluentui/react-icons";
import { IInputs } from "./generated/ManifestTypes";
import { DocumentGraph } from "./components/DocumentGraph";
import { useVisualizationApi, formatVisualizationError } from "./hooks/useVisualizationApi";
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";
import { loginRequest } from "./services/auth/msalConfig";
import type { DocumentNode } from "./types/graph";
import { RELATIONSHIP_TYPES, type RelationshipTypeKey } from "./types/api";

// Control version - must match ControlManifest.Input.xml
const CONTROL_VERSION = "1.0.31";

/**
 * Props for the DocumentRelationshipViewer component
 */
export interface IDocumentRelationshipViewerProps {
    /** PCF context for accessing parameters and platform features */
    context: ComponentFramework.Context<IInputs>;
    /** Callback to notify PCF framework of output changes */
    notifyOutputChanged: () => void;
    /** Callback when user selects a document node */
    onDocumentSelect?: (documentId: string) => void;
    /** MSAL authentication provider for API calls */
    authProvider: MsalAuthProvider;
}

/**
 * Styles using Fluent UI design tokens (ADR-021 compliant)
 * Uses calc(100vh - 180px) pattern like AnalysisWorkspace to fill section
 */
const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        // Fill available vertical space like AnalysisWorkspace pattern
        height: "calc(100vh - 180px)",
        minHeight: "500px",
        // Use viewport width minus form sidebar/chrome (~320px for nav + paddings)
        width: "calc(100vw - 320px)",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalM,
        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
        backgroundColor: tokens.colorNeutralBackground2,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        position: "sticky",
        top: 0,
        zIndex: 10,
    },
    headerTitle: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        textTransform: "uppercase",
        letterSpacing: "0.5px",
        color: tokens.colorNeutralForeground2,
    },
    graphContainer: {
        flex: 1,
        display: "flex",
        position: "relative",
        minHeight: 0, // Important for flex sizing
        width: "100%", // Ensure full horizontal width
    },
    placeholder: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3,
        width: "100%",
        height: "100%",
    },
    footer: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    versionText: {
        color: tokens.colorNeutralForeground4,
    },
    errorContainer: {
        padding: tokens.spacingVerticalL,
        width: "100%",
    },
    filterDropdown: {
        minWidth: "180px",
    },
    filterContainer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
});

/**
 * Resolve theme based on context and system preferences (ADR-021)
 */
function resolveTheme(context?: ComponentFramework.Context<IInputs>) {
    // Check PCF context for dark mode
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme ? webDarkTheme : webLightTheme;
    }

    // Fallback to system preference
    if (typeof window !== "undefined") {
        return window.matchMedia("(prefers-color-scheme: dark)").matches
            ? webDarkTheme
            : webLightTheme;
    }

    return webLightTheme;
}

/**
 * Check if dark mode is enabled
 */
function isDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    if (typeof window !== "undefined") {
        return window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    return false;
}

/**
 * DocumentRelationshipViewer - Interactive graph visualization of related documents.
 *
 * This control displays a force-directed graph showing document relationships
 * based on vector similarity from Azure AI Search.
 *
 * Layout: Fills the section it's placed in (like AnalysisWorkspace pattern).
 * Place this control in a dedicated tab or section for best results.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries
 */
export const DocumentRelationshipViewer: React.FC<IDocumentRelationshipViewerProps> = ({
    context,
    notifyOutputChanged,
    onDocumentSelect,
    authProvider,
}) => {
    const styles = useStyles();
    const theme = resolveTheme(context);
    const darkMode = isDarkMode(context);

    // Get input parameters
    // Use context.page.entityId (the record's GUID on the form) — same pattern as SemanticSearchControl.
    // Note: context.page exists at runtime but isn't in @types/powerapps-component-framework
    const pageContext = (context as unknown as { page?: { entityId?: string; entityTypeName?: string } }).page;
    const pageEntityId = pageContext?.entityId ?? null;
    const documentId = pageEntityId ?? "";
    const apiBaseUrl = context.parameters.apiBaseUrl?.raw ?? "https://spe-api-dev-67e2xz.azurewebsites.net";
    const tenantId = context.parameters.tenantId?.raw ?? "";

    // Selected node state
    const [selectedNodeId, setSelectedNodeId] = React.useState<string | null>(null);

    // Relationship type filter state (empty array = all types)
    const [selectedRelationshipTypes, setSelectedRelationshipTypes] = React.useState<string[]>([]);

    // Access token state for authenticated API calls
    const [accessToken, setAccessToken] = React.useState<string | undefined>(undefined);
    const [authError, setAuthError] = React.useState<string | null>(null);

    // Acquire access token on mount and when auth provider changes
    React.useEffect(() => {
        const acquireToken = async () => {
            try {
                console.log("[DocumentRelationshipViewer] Acquiring access token...");
                const token = await authProvider.getToken(loginRequest.scopes);
                setAccessToken(token);
                setAuthError(null);
                console.log("[DocumentRelationshipViewer] Access token acquired successfully");
            } catch (error) {
                console.error("[DocumentRelationshipViewer] Failed to acquire token:", error);
                setAuthError(error instanceof Error ? error.message : "Authentication failed");
                setAccessToken(undefined);
            }
        };

        void acquireToken();
    }, [authProvider]);

    // Fetch visualization data from API (only when we have a token)
    const {
        nodes,
        edges,
        metadata,
        isLoading,
        error,
    } = useVisualizationApi({
        apiBaseUrl,
        documentId,
        tenantId,
        accessToken,
        threshold: 0.65,
        limit: 25,
        depth: 1,
        relationshipTypes: selectedRelationshipTypes.length > 0 ? selectedRelationshipTypes : undefined,
        enabled: !!documentId && documentId.trim() !== "" && !!tenantId && !!accessToken,
    });

    // Get container dimensions for layout
    const containerRef = React.useRef<HTMLDivElement>(null);
    const [dimensions, setDimensions] = React.useState<{ width: number; height: number } | null>(null);

    // Update dimensions on resize
    React.useEffect(() => {
        const updateDimensions = () => {
            if (containerRef.current) {
                const newWidth = containerRef.current.clientWidth;
                const newHeight = containerRef.current.clientHeight;
                // Only update if we have valid dimensions
                if (newWidth > 0 && newHeight > 0) {
                    setDimensions({
                        width: newWidth,
                        height: newHeight,
                    });
                }
            }
        };

        // Initial update after a small delay to ensure DOM is ready
        const timeoutId = setTimeout(updateDimensions, 50);

        // Also try immediately
        updateDimensions();

        window.addEventListener("resize", updateDimensions);
        return () => {
            clearTimeout(timeoutId);
            window.removeEventListener("resize", updateDimensions);
        };
    }, []);

    // Handle node selection
    const handleNodeSelect = React.useCallback(
        (node: DocumentNode) => {
            setSelectedNodeId(node.id);
            if (onDocumentSelect) {
                // Use documentId if available, otherwise use speFileId or node.id for orphan files
                const selectedId = node.data.documentId ?? node.data.speFileId ?? node.id;
                onDocumentSelect(selectedId);
            }
            notifyOutputChanged();
        },
        [onDocumentSelect, notifyOutputChanged]
    );

    // Handle relationship type filter changes via Menu's onCheckedValueChange
    const handleCheckedValueChange = React.useCallback(
        (_event: unknown, data: { name: string; checkedItems: string[] }) => {
            if (data.name === "filter") {
                setSelectedRelationshipTypes(data.checkedItems);
            }
        },
        []
    );

    // Get all relationship type options for the menu
    const relationshipTypeOptions = React.useMemo(() => {
        return Object.entries(RELATIONSHIP_TYPES).map(([key, label]) => ({
            key: key as RelationshipTypeKey,
            label,
        }));
    }, []);

    // Filter button label
    const filterButtonLabel = React.useMemo(() => {
        if (selectedRelationshipTypes.length === 0) {
            return "All types";
        }
        return `${selectedRelationshipTypes.length} selected`;
    }, [selectedRelationshipTypes.length]);

    // Build checked items object for Menu
    const checkedItems = React.useMemo(() => {
        const items: Record<string, string[]> = { filter: selectedRelationshipTypes };
        return items;
    }, [selectedRelationshipTypes]);

    // Check if we should show the placeholder (missing document or tenant)
    const showPlaceholder = !documentId || documentId.trim() === "";
    const showTenantMissing = documentId && !tenantId;
    const showAuthenticating = !accessToken && !authError;
    // Check if we have measured dimensions
    const hasDimensions = dimensions !== null && dimensions.width > 0 && dimensions.height > 0;
    // Format error message for display - auth errors take priority
    const errorMessage = authError ?? (error ? formatVisualizationError(error) : null);

    return (
        <FluentProvider theme={theme}>
            <div className={styles.container}>
                {/* Header */}
                <div className={styles.header}>
                    <div className={styles.headerTitle}>
                        <DocumentFlowchart24Regular />
                        <Text weight="semibold" size={400}>
                            Document Relationships
                        </Text>
                        {selectedNodeId && (
                            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                                Selected: {selectedNodeId}
                            </Text>
                        )}
                    </div>
                    <div className={styles.filterContainer}>
                        <Menu
                            checkedValues={checkedItems}
                            onCheckedValueChange={handleCheckedValueChange}
                        >
                            <MenuTrigger disableButtonEnhancement>
                                <Button
                                    appearance="subtle"
                                    icon={<Filter20Regular />}
                                    size="small"
                                >
                                    {filterButtonLabel}
                                </Button>
                            </MenuTrigger>
                            <MenuPopover>
                                <MenuList>
                                    {relationshipTypeOptions.map((option) => (
                                        <MenuItemCheckbox
                                            key={option.key}
                                            name="filter"
                                            value={option.key}
                                        >
                                            {option.label}
                                        </MenuItemCheckbox>
                                    ))}
                                </MenuList>
                            </MenuPopover>
                        </Menu>
                    </div>
                </div>

                {/* Graph Container - fills available space */}
                <div className={styles.graphContainer} ref={containerRef}>
                    {showAuthenticating ? (
                        <div className={styles.placeholder}>
                            <Spinner size="large" label="Authenticating..." />
                        </div>
                    ) : isLoading ? (
                        <div className={styles.placeholder}>
                            <Spinner size="large" label="Loading relationships..." />
                        </div>
                    ) : errorMessage ? (
                        <div className={styles.errorContainer}>
                            <MessageBar intent="error">
                                <MessageBarBody>{errorMessage}</MessageBarBody>
                            </MessageBar>
                        </div>
                    ) : showTenantMissing ? (
                        <div className={styles.placeholder}>
                            <DocumentFlowchart24Regular style={{ fontSize: 48 }} />
                            <Text>
                                Configuration required
                            </Text>
                            <Text size={200}>
                                Tenant ID must be configured to load relationships
                            </Text>
                        </div>
                    ) : showPlaceholder ? (
                        <div className={styles.placeholder}>
                            <DocumentFlowchart24Regular style={{ fontSize: 48 }} />
                            <Text>
                                Select a document to view its relationships
                            </Text>
                            <Text size={200}>
                                Graph visualization will appear here
                            </Text>
                        </div>
                    ) : !hasDimensions ? (
                        <div className={styles.placeholder}>
                            <Spinner size="medium" label="Initializing..." />
                        </div>
                    ) : nodes.length === 0 ? (
                        <div className={styles.placeholder}>
                            <DocumentFlowchart24Regular style={{ fontSize: 48 }} />
                            <Text>
                                No related documents found
                            </Text>
                            <Text size={200}>
                                This document has no similar documents above the similarity threshold
                            </Text>
                        </div>
                    ) : (
                        <DocumentGraph
                            nodes={nodes}
                            edges={edges}
                            isDarkMode={darkMode}
                            onNodeSelect={handleNodeSelect}
                            width={dimensions.width}
                            height={dimensions.height}
                            showMinimap={false}
                            compactMode={false}
                        />
                    )}
                </div>

                {/* Footer with stats and version */}
                <div className={styles.footer}>
                    <span>
                        {nodes.length > 0
                            ? `${nodes.length} documents · ${edges.length} relationships`
                            : metadata
                                ? `Searched in ${metadata.searchLatencyMs}ms`
                                : "No data loaded"
                        }
                    </span>
                    <span className={styles.versionText}>
                        v{CONTROL_VERSION}
                    </span>
                </div>
            </div>
        </FluentProvider>
    );
};
