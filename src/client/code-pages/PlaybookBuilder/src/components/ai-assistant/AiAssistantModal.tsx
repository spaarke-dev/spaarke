/**
 * AI Assistant Modal - Floating Draggable Chat Panel
 *
 * A floating, draggable panel for the AI assistant chat interface.
 * Contains chat history, input, and operation feedback sections.
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 * Implements custom drag behavior for positioning within the canvas area.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useCallback, useState, useRef, useEffect } from "react";
import {
    Button,
    Text,
    Dropdown,
    Option,
    Label,
    makeStyles,
    tokens,
    shorthands,
    mergeClasses,
} from "@fluentui/react-components";
import {
    Dismiss20Regular,
    Bot20Regular,
    SubtractCircle20Regular,
    Settings20Regular,
} from "@fluentui/react-icons";
import {
    useAiAssistantStore,
    AI_MODEL_OPTIONS,
    type AiModelSelection,
} from "../../stores/aiAssistantStore";

// ============================================================================
// Types
// ============================================================================

interface Position {
    x: number;
    y: number;
}

interface Size {
    width: number;
    height: number;
}

export interface AiAssistantModalProps {
    /** Initial position of the modal (optional) */
    initialPosition?: Position;
    /** Initial size of the modal (optional) */
    initialSize?: Size;
    /** Minimum width constraint */
    minWidth?: number;
    /** Minimum height constraint */
    minHeight?: number;
    /** Maximum width constraint */
    maxWidth?: number;
    /** Maximum height constraint */
    maxHeight?: number;
    /** Children to render inside the modal body */
    children?: React.ReactNode;
}

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    // Floating container
    container: {
        position: "absolute",
        zIndex: 1000,
        display: "flex",
        flexDirection: "column",
        boxShadow: tokens.shadow16,
        ...shorthands.borderRadius(tokens.borderRadiusLarge),
        ...shorthands.overflow("hidden"),
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
    },
    containerHidden: {
        display: "none",
    },
    // Header (draggable handle)
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
        cursor: "grab",
        userSelect: "none",
    },
    headerDragging: {
        cursor: "grabbing",
    },
    headerTitle: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    headerTitleText: {
        color: tokens.colorNeutralForegroundOnBrand,
        fontWeight: tokens.fontWeightSemibold,
    },
    headerActions: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    headerButton: {
        color: tokens.colorNeutralForegroundOnBrand,
        minWidth: "auto",
        ...shorthands.padding(tokens.spacingVerticalXS),
        ":hover": {
            backgroundColor: tokens.colorBrandBackgroundHover,
        },
    },
    // Body
    body: {
        flex: 1,
        display: "flex",
        flexDirection: "column",
        ...shorthands.overflow("hidden"),
        backgroundColor: tokens.colorNeutralBackground1,
    },
    // Resize handle (bottom-right corner)
    resizeHandle: {
        position: "absolute",
        bottom: 0,
        right: 0,
        width: "16px",
        height: "16px",
        cursor: "se-resize",
        backgroundColor: "transparent",
        "::after": {
            content: '""',
            position: "absolute",
            bottom: "4px",
            right: "4px",
            width: "8px",
            height: "8px",
            ...shorthands.borderRight("2px", "solid", tokens.colorNeutralStroke1),
            ...shorthands.borderBottom("2px", "solid", tokens.colorNeutralStroke1),
        },
    },
    // Minimized state
    minimized: {
        width: "280px !important",
        height: "auto !important",
    },
    minimizedBody: {
        display: "none",
    },
    // Advanced options panel
    advancedOptions: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        ...shorthands.gap(tokens.spacingVerticalXS),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
    },
    advancedOptionsRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    modelLabel: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        flexShrink: 0,
    },
    modelDropdown: {
        minWidth: "180px",
    },
    modelDescription: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        marginLeft: tokens.spacingHorizontalXS,
    },
});

// ============================================================================
// Default Values
// ============================================================================

const DEFAULT_POSITION: Position = { x: 20, y: 20 };
const DEFAULT_SIZE: Size = { width: 380, height: 500 };
const DEFAULT_MIN_WIDTH = 280;
const DEFAULT_MIN_HEIGHT = 200;
const DEFAULT_MAX_WIDTH = 600;
const DEFAULT_MAX_HEIGHT = 800;

// ============================================================================
// Component
// ============================================================================

export const AiAssistantModal: React.FC<AiAssistantModalProps> = ({
    initialPosition = DEFAULT_POSITION,
    initialSize = DEFAULT_SIZE,
    minWidth = DEFAULT_MIN_WIDTH,
    minHeight = DEFAULT_MIN_HEIGHT,
    maxWidth = DEFAULT_MAX_WIDTH,
    maxHeight = DEFAULT_MAX_HEIGHT,
    children,
}) => {
    const styles = useStyles();

    // Store state
    const {
        isOpen,
        closeModal,
        modelSelection,
        showAdvancedOptions,
        setModelSelection,
        toggleAdvancedOptions,
    } = useAiAssistantStore();

    // Local state
    const [position, setPosition] = useState<Position>(initialPosition);
    const [size, setSize] = useState<Size>(initialSize);
    const [isDragging, setIsDragging] = useState(false);
    const [isResizing, setIsResizing] = useState(false);
    const [isMinimized, setIsMinimized] = useState(false);

    // Refs for tracking drag/resize
    const containerRef = useRef<HTMLDivElement>(null);
    const dragStartRef = useRef<{ mouseX: number; mouseY: number; posX: number; posY: number } | null>(null);
    const resizeStartRef = useRef<{ mouseX: number; mouseY: number; width: number; height: number } | null>(null);

    // ────────────────────────────────────────────────────────────────────────
    // Drag handlers
    // ────────────────────────────────────────────────────────────────────────

    const handleDragStart = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
        // Only start drag from header (not buttons)
        if ((e.target as HTMLElement).tagName === "BUTTON") return;

        e.preventDefault();
        setIsDragging(true);
        dragStartRef.current = {
            mouseX: e.clientX,
            mouseY: e.clientY,
            posX: position.x,
            posY: position.y,
        };
    }, [position]);

    const handleDragMove = useCallback((e: MouseEvent) => {
        if (!isDragging || !dragStartRef.current) return;

        const deltaX = e.clientX - dragStartRef.current.mouseX;
        const deltaY = e.clientY - dragStartRef.current.mouseY;

        setPosition({
            x: Math.max(0, dragStartRef.current.posX + deltaX),
            y: Math.max(0, dragStartRef.current.posY + deltaY),
        });
    }, [isDragging]);

    const handleDragEnd = useCallback(() => {
        setIsDragging(false);
        dragStartRef.current = null;
    }, []);

    // ────────────────────────────────────────────────────────────────────────
    // Resize handlers
    // ────────────────────────────────────────────────────────────────────────

    const handleResizeStart = useCallback((e: React.MouseEvent<HTMLDivElement>) => {
        e.preventDefault();
        e.stopPropagation();
        setIsResizing(true);
        resizeStartRef.current = {
            mouseX: e.clientX,
            mouseY: e.clientY,
            width: size.width,
            height: size.height,
        };
    }, [size]);

    const handleResizeMove = useCallback((e: MouseEvent) => {
        if (!isResizing || !resizeStartRef.current) return;

        const deltaX = e.clientX - resizeStartRef.current.mouseX;
        const deltaY = e.clientY - resizeStartRef.current.mouseY;

        setSize({
            width: Math.min(maxWidth, Math.max(minWidth, resizeStartRef.current.width + deltaX)),
            height: Math.min(maxHeight, Math.max(minHeight, resizeStartRef.current.height + deltaY)),
        });
    }, [isResizing, minWidth, maxWidth, minHeight, maxHeight]);

    const handleResizeEnd = useCallback(() => {
        setIsResizing(false);
        resizeStartRef.current = null;
    }, []);

    // ────────────────────────────────────────────────────────────────────────
    // Global mouse event listeners
    // ────────────────────────────────────────────────────────────────────────

    useEffect(() => {
        if (isDragging) {
            window.addEventListener("mousemove", handleDragMove);
            window.addEventListener("mouseup", handleDragEnd);
            return () => {
                window.removeEventListener("mousemove", handleDragMove);
                window.removeEventListener("mouseup", handleDragEnd);
            };
        }
    }, [isDragging, handleDragMove, handleDragEnd]);

    useEffect(() => {
        if (isResizing) {
            window.addEventListener("mousemove", handleResizeMove);
            window.addEventListener("mouseup", handleResizeEnd);
            return () => {
                window.removeEventListener("mousemove", handleResizeMove);
                window.removeEventListener("mouseup", handleResizeEnd);
            };
        }
    }, [isResizing, handleResizeMove, handleResizeEnd]);

    // ────────────────────────────────────────────────────────────────────────
    // Action handlers
    // ────────────────────────────────────────────────────────────────────────

    const handleMinimize = useCallback(() => {
        setIsMinimized((prev) => !prev);
    }, []);

    const handleClose = useCallback(() => {
        closeModal();
    }, [closeModal]);

    const handleSettingsToggle = useCallback(() => {
        toggleAdvancedOptions();
    }, [toggleAdvancedOptions]);

    const handleModelChange = useCallback(
        (_event: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                setModelSelection(data.optionValue as AiModelSelection);
            }
        },
        [setModelSelection]
    );

    // Get current model display text
    const currentModelOption = AI_MODEL_OPTIONS.find((m) => m.id === modelSelection);
    const modelDisplayText = currentModelOption
        ? `${currentModelOption.name} (${currentModelOption.description})`
        : modelSelection;

    // ────────────────────────────────────────────────────────────────────────
    // Render
    // ────────────────────────────────────────────────────────────────────────

    return (
        <div
            ref={containerRef}
            className={mergeClasses(
                styles.container,
                !isOpen && styles.containerHidden,
                isMinimized && styles.minimized
            )}
            style={{
                left: position.x,
                top: position.y,
                width: isMinimized ? undefined : size.width,
                height: isMinimized ? undefined : size.height,
            }}
            role="dialog"
            aria-modal="false"
            aria-label="AI Assistant"
            aria-hidden={!isOpen}
        >
            {/* Header (Draggable Handle) */}
            <div
                className={mergeClasses(
                    styles.header,
                    isDragging && styles.headerDragging
                )}
                onMouseDown={handleDragStart}
            >
                <div className={styles.headerTitle}>
                    <Bot20Regular />
                    <Text className={styles.headerTitleText} size={300}>
                        AI Assistant
                    </Text>
                </div>
                <div className={styles.headerActions}>
                    <Button
                        appearance="transparent"
                        size="small"
                        icon={<Settings20Regular />}
                        onClick={handleSettingsToggle}
                        className={styles.headerButton}
                        aria-label={showAdvancedOptions ? "Hide settings" : "Show settings"}
                        title={showAdvancedOptions ? "Hide settings" : "Show settings"}
                        aria-pressed={showAdvancedOptions}
                    />
                    <Button
                        appearance="transparent"
                        size="small"
                        icon={<SubtractCircle20Regular />}
                        onClick={handleMinimize}
                        className={styles.headerButton}
                        aria-label={isMinimized ? "Expand" : "Minimize"}
                        title={isMinimized ? "Expand" : "Minimize"}
                    />
                    <Button
                        appearance="transparent"
                        size="small"
                        icon={<Dismiss20Regular />}
                        onClick={handleClose}
                        className={styles.headerButton}
                        aria-label="Close"
                        title="Close"
                    />
                </div>
            </div>

            {/* Advanced Options Panel */}
            {showAdvancedOptions && !isMinimized && (
                <div className={styles.advancedOptions}>
                    <div className={styles.advancedOptionsRow}>
                        <Label className={styles.modelLabel} htmlFor="model-select">
                            Model
                        </Label>
                        <Dropdown
                            id="model-select"
                            className={styles.modelDropdown}
                            value={modelDisplayText}
                            selectedOptions={[modelSelection]}
                            onOptionSelect={handleModelChange}
                            aria-label="Select AI model"
                        >
                            {AI_MODEL_OPTIONS.map((option) => (
                                <Option key={option.id} value={option.id} text={`${option.name} (${option.description})`}>
                                    {option.name}
                                    <span className={styles.modelDescription}>({option.description})</span>
                                </Option>
                            ))}
                        </Dropdown>
                    </div>
                </div>
            )}

            {/* Body */}
            <div className={mergeClasses(styles.body, isMinimized && styles.minimizedBody)}>
                {children}
            </div>

            {/* Resize Handle */}
            {!isMinimized && (
                <div
                    className={styles.resizeHandle}
                    onMouseDown={handleResizeStart}
                    aria-hidden="true"
                />
            )}
        </div>
    );
};

export default AiAssistantModal;
