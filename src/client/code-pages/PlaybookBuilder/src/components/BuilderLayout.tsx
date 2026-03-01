/**
 * BuilderLayout — Main layout for the Playbook Builder Code Page (Task 050)
 *
 * Wires all panels together:
 *   - Top toolbar: save, undo, redo, add node, AI assistant toggle, execution controls
 *   - Left sidebar: Node palette (drag-and-drop node types)
 *   - Center: ReactFlow canvas
 *   - Right sidebar: PropertiesPanel (when node selected)
 *   - Floating: AiAssistantModal (toggleable)
 *   - Overlay: ExecutionOverlay (during execution)
 */

import { useState, useCallback, useEffect, useRef } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Button,
    Text,
    Tooltip,
    Divider,
    mergeClasses,
    Badge,
} from "@fluentui/react-components";
import {
    Save20Regular,
    Add20Regular,
    Bot20Regular,
    Play20Regular,
    PanelRight20Regular,
    PanelLeft20Regular,
    Settings20Regular,
} from "@fluentui/react-icons";
import { ReactFlowProvider } from "@xyflow/react";

import { PlaybookCanvas } from "./canvas/PlaybookCanvas";
import { PropertiesPanel } from "./properties/PropertiesPanel";
import { ExecutionOverlay } from "./execution/ExecutionOverlay";
import { AiAssistantModal } from "./ai-assistant/AiAssistantModal";
import { usePlaybookLoader } from "../hooks/usePlaybookLoader";
import { useCanvasStore } from "../stores/canvasStore";
import { useAiAssistantStore } from "../stores/aiAssistantStore";
import { useExecutionStore } from "../stores/executionStore";
import { useScopeStore } from "../stores/scopeStore";
import { useModelStore } from "../stores/modelStore";
import { syncNodesToDataverse } from "../services/playbookNodeSync";
import { updateRecord } from "../services/dataverseClient";
import { useKeyboardShortcuts } from "../hooks/useKeyboardShortcuts";
import type { PlaybookNodeType } from "../types/canvas";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface BuilderLayoutProps {
    playbookId: string;
}

// ---------------------------------------------------------------------------
// Node palette items
// ---------------------------------------------------------------------------

interface NodePaletteItem {
    type: PlaybookNodeType;
    label: string;
    description: string;
    color: string;
}

const NODE_PALETTE: NodePaletteItem[] = [
    { type: "aiAnalysis", label: "AI Analysis", description: "Run AI analysis with skills and knowledge", color: tokens.colorBrandBackground },
    { type: "aiCompletion", label: "AI Completion", description: "Generate AI text completion", color: tokens.colorBrandBackground },
    { type: "condition", label: "Condition", description: "Branch based on expression", color: tokens.colorPaletteYellowBackground3 },
    { type: "deliverOutput", label: "Deliver Output", description: "Format and save results", color: tokens.colorPaletteGreenBackground3 },
    { type: "createTask", label: "Create Task", description: "Create a Dataverse task", color: tokens.colorPaletteBerryBackground2 },
    { type: "sendEmail", label: "Send Email", description: "Send notification email", color: tokens.colorPaletteBerryBackground2 },
    { type: "wait", label: "Wait", description: "Pause for duration or condition", color: tokens.colorPaletteMagentaBackground2 },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("4px"),
        ...shorthands.padding("6px", "12px"),
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground3,
        flexShrink: 0,
    },
    toolbarLeft: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("4px"),
    },
    toolbarCenter: {
        flex: 1,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
    },
    toolbarRight: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("4px"),
    },
    playbookTitle: {
        fontWeight: tokens.fontWeightSemibold,
    },
    dirtyBadge: {
        marginLeft: "4px",
    },
    body: {
        display: "flex",
        flex: 1,
        overflow: "hidden",
    },
    leftSidebar: {
        width: "200px",
        ...shorthands.borderRight("1px", "solid", tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground2,
        display: "flex",
        flexDirection: "column",
        overflowY: "auto",
        flexShrink: 0,
        transition: "width 0.2s ease",
    },
    sidebarCollapsed: {
        width: "0px",
        ...shorthands.borderRight("0px", "solid", "transparent"),
        overflow: "hidden",
    },
    sidebarHeader: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
        ...shorthands.padding("8px", "12px"),
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
        backgroundColor: tokens.colorNeutralBackground3,
    },
    paletteList: {
        ...shorthands.padding("8px"),
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("4px"),
    },
    paletteItem: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
        ...shorthands.padding("8px"),
        ...shorthands.borderRadius("4px"),
        cursor: "grab",
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    paletteColor: {
        width: "8px",
        height: "28px",
        ...shorthands.borderRadius("2px"),
        flexShrink: 0,
    },
    paletteInfo: {
        display: "flex",
        flexDirection: "column",
    },
    paletteName: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
    },
    paletteDesc: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    canvasArea: {
        flex: 1,
        position: "relative",
        overflow: "hidden",
    },
    rightSidebar: {
        flexShrink: 0,
        transition: "width 0.2s ease",
        overflow: "hidden",
    },
    rightSidebarCollapsed: {
        width: "0px",
    },
    loading: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        ...shorthands.gap("12px"),
    },
    error: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        color: tokens.colorPaletteRedForeground1,
        ...shorthands.gap("8px"),
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function BuilderLayout({ playbookId }: BuilderLayoutProps): JSX.Element {
    const styles = useStyles();

    // Playbook loading
    const { isLoading, error, playbookName } = usePlaybookLoader(playbookId);

    // Store hooks
    const isDirty = useCanvasStore((s) => s.isDirty);
    const selectedNodeId = useCanvasStore((s) => s.selectedNodeId);
    const nodes = useCanvasStore((s) => s.nodes);
    const edges = useCanvasStore((s) => s.edges);
    const exportToCanvasJson = useCanvasStore((s) => s.exportToCanvasJson);
    const markSaved = useCanvasStore((s) => s.markSaved);
    const isAiModalOpen = useAiAssistantStore((s) => s.isModalOpen);
    const openAiModal = useAiAssistantStore((s) => s.openModal);
    const closeAiModal = useAiAssistantStore((s) => s.closeModal);
    const isExecuting = useExecutionStore((s) => s.isExecuting);

    // Panel visibility
    const [leftPanelOpen, setLeftPanelOpen] = useState(true);
    const [rightPanelOpen, setRightPanelOpen] = useState(false);
    const [isSaving, setIsSaving] = useState(false);
    const saveTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    // Auto-open properties panel when a node is selected
    useEffect(() => {
        if (selectedNodeId) {
            setRightPanelOpen(true);
        }
    }, [selectedNodeId]);

    // Load scope data on mount
    useEffect(() => {
        useScopeStore.getState().loadAllScopes();
        useModelStore.getState().loadModelDeployments();
    }, []);

    // Save handler
    const handleSave = useCallback(async () => {
        if (!playbookId || isSaving) return;
        setIsSaving(true);
        try {
            // Save canvas JSON to Dataverse
            const canvasJson = exportToCanvasJson();
            await updateRecord("sprk_analysisplaybooks", playbookId, {
                sprk_canvaslayoutjson: canvasJson,
            });
            // Sync nodes to Dataverse records
            await syncNodesToDataverse(playbookId, nodes, edges);
            markSaved();
            console.info("[BuilderLayout] Playbook saved successfully");
        } catch (err) {
            console.error("[BuilderLayout] Save failed:", err);
        } finally {
            setIsSaving(false);
        }
    }, [playbookId, isSaving, exportToCanvasJson, nodes, edges, markSaved]);

    // Auto-save debounced (30 seconds after last change)
    useEffect(() => {
        if (!isDirty || !playbookId) return;
        if (saveTimeoutRef.current) {
            clearTimeout(saveTimeoutRef.current);
        }
        saveTimeoutRef.current = setTimeout(() => {
            handleSave();
        }, 30000);
        return () => {
            if (saveTimeoutRef.current) {
                clearTimeout(saveTimeoutRef.current);
            }
        };
    }, [isDirty, playbookId, handleSave]);

    // Drag start handler for palette items
    const handleDragStart = useCallback(
        (event: React.DragEvent, nodeType: PlaybookNodeType, label: string) => {
            event.dataTransfer.setData("application/reactflow", nodeType);
            event.dataTransfer.setData("application/reactflow-label", label);
            event.dataTransfer.effectAllowed = "move";
        },
        [],
    );

    // Keyboard shortcuts
    useKeyboardShortcuts({ onSave: handleSave });

    // Loading state
    if (isLoading) {
        return (
            <div className={styles.loading}>
                <Text size={400}>Loading playbook...</Text>
            </div>
        );
    }

    // Error state
    if (error) {
        return (
            <div className={styles.error}>
                <Text size={400} weight="semibold">Failed to load playbook</Text>
                <Text>{error}</Text>
            </div>
        );
    }

    return (
        <div className={styles.root}>
            {/* Toolbar */}
            <div className={styles.toolbar}>
                <div className={styles.toolbarLeft}>
                    <Tooltip content={leftPanelOpen ? "Hide node palette" : "Show node palette"} relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<PanelLeft20Regular />}
                            onClick={() => setLeftPanelOpen(!leftPanelOpen)}
                        />
                    </Tooltip>
                    <Divider vertical style={{ height: "20px" }} />
                    <Tooltip content="Save (Ctrl+S)" relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<Save20Regular />}
                            onClick={handleSave}
                            disabled={!isDirty || isSaving}
                        />
                    </Tooltip>
                </div>

                <div className={styles.toolbarCenter}>
                    <Text className={styles.playbookTitle}>
                        {playbookName || "Playbook Builder"}
                    </Text>
                    {isDirty && (
                        <Badge
                            className={styles.dirtyBadge}
                            size="small"
                            appearance="ghost"
                            color="warning"
                        >
                            Unsaved
                        </Badge>
                    )}
                </div>

                <div className={styles.toolbarRight}>
                    <Tooltip content={isExecuting ? "Execution in progress" : "Run playbook"} relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<Play20Regular />}
                            disabled={isExecuting || nodes.length === 0}
                        />
                    </Tooltip>
                    <Tooltip content="AI Assistant" relationship="label">
                        <Button
                            appearance={isAiModalOpen ? "primary" : "subtle"}
                            size="small"
                            icon={<Bot20Regular />}
                            onClick={() => (isAiModalOpen ? closeAiModal() : openAiModal())}
                        />
                    </Tooltip>
                    <Divider vertical style={{ height: "20px" }} />
                    <Tooltip content={rightPanelOpen ? "Hide properties" : "Show properties"} relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<PanelRight20Regular />}
                            onClick={() => setRightPanelOpen(!rightPanelOpen)}
                        />
                    </Tooltip>
                </div>
            </div>

            {/* Body: Left Sidebar + Canvas + Right Sidebar */}
            <div className={styles.body}>
                {/* Left Sidebar — Node Palette */}
                <div
                    className={mergeClasses(
                        styles.leftSidebar,
                        !leftPanelOpen && styles.sidebarCollapsed,
                    )}
                >
                    <div className={styles.sidebarHeader}>
                        <Add20Regular />
                        <Text weight="semibold" size={300}>
                            Node Types
                        </Text>
                    </div>
                    <div className={styles.paletteList}>
                        {NODE_PALETTE.map((item) => (
                            <div
                                key={item.type}
                                className={styles.paletteItem}
                                draggable
                                onDragStart={(e) => handleDragStart(e, item.type, item.label)}
                            >
                                <div
                                    className={styles.paletteColor}
                                    style={{ backgroundColor: item.color }}
                                />
                                <div className={styles.paletteInfo}>
                                    <span className={styles.paletteName}>{item.label}</span>
                                    <span className={styles.paletteDesc}>{item.description}</span>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>

                {/* Canvas (center) */}
                <ReactFlowProvider>
                    <div className={styles.canvasArea}>
                        <PlaybookCanvas />
                        {isExecuting && <ExecutionOverlay />}
                    </div>
                </ReactFlowProvider>

                {/* Right Sidebar — Properties Panel */}
                <div
                    className={mergeClasses(
                        styles.rightSidebar,
                        !rightPanelOpen && styles.rightSidebarCollapsed,
                    )}
                >
                    {rightPanelOpen && <PropertiesPanel />}
                </div>
            </div>

            {/* AI Assistant Modal (floating) */}
            {isAiModalOpen && <AiAssistantModal />}
        </div>
    );
}
