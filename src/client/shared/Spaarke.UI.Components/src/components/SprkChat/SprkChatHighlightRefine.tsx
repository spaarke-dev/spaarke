/**
 * SprkChatHighlightRefine - Text selection and refinement UI
 *
 * Detects text selection within a container element (local DOM) and/or
 * receives cross-pane selections from SprkChatBridge (via the
 * crossPaneSelection prop). Shows a floating "Refine" toolbar allowing the
 * user to submit a refinement instruction or click quick action presets.
 *
 * Cross-pane selections take priority over local DOM selections: when a
 * crossPaneSelection is present, the toolbar shows that text regardless of
 * any local highlight.
 *
 * Quick action presets (Simplify, Expand, Make Concise, Make Formal) are
 * shown as chips that auto-submit when clicked. Custom presets can be
 * provided via the quickActions prop.
 *
 * Source detection: editor selections show a document icon + "Selected in Editor"
 * badge; chat selections show a chat icon + "Selected in Chat" badge.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
 * @see useSelectionListener (receives bridge events -> crossPaneSelection)
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Button,
    Input,
    Spinner,
    Text,
    Badge,
    Tooltip,
} from "@fluentui/react-components";
import {
    TextEditStyle20Regular,
    Dismiss20Regular,
    Document20Regular,
    Chat20Regular,
} from "@fluentui/react-icons";
import {
    ISprkChatHighlightRefineProps,
    IRefineRequest,
    IQuickAction,
    DEFAULT_QUICK_ACTIONS,
} from "./types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Max preview characters shown in the toolbar for any selection */
const PREVIEW_MAX_LENGTH = 150;

/** CSS animation duration in milliseconds */
const ANIMATION_DURATION_MS = 200;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    "@keyframes fadeIn": {
        from: { opacity: 0, transform: "translateY(4px)" },
        to: { opacity: 1, transform: "translateY(0)" },
    },
    "@keyframes fadeOut": {
        from: { opacity: 1, transform: "translateY(0)" },
        to: { opacity: 0, transform: "translateY(4px)" },
    },
    toolbar: {
        position: "absolute",
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
        backgroundColor: tokens.colorNeutralBackground1,
        boxShadow: tokens.shadow8,
        zIndex: 1000,
        maxWidth: "340px",
        animationName: "fadeIn",
        animationDuration: `${ANIMATION_DURATION_MS}ms`,
        animationTimingFunction: "ease-out",
        animationFillMode: "forwards",
    },
    toolbarDismissing: {
        animationName: "fadeOut",
        animationDuration: `${ANIMATION_DURATION_MS}ms`,
        animationTimingFunction: "ease-in",
        animationFillMode: "forwards",
    },
    crossPaneToolbar: {
        position: "sticky",
        top: 0,
        left: 0,
        right: 0,
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border("1px", "solid", tokens.colorBrandStroke1),
        backgroundColor: tokens.colorBrandBackground2,
        boxShadow: tokens.shadow8,
        zIndex: 1000,
        maxWidth: "100%",
        animationName: "fadeIn",
        animationDuration: `${ANIMATION_DURATION_MS}ms`,
        animationTimingFunction: "ease-out",
        animationFillMode: "forwards",
    },
    crossPaneToolbarDismissing: {
        animationName: "fadeOut",
        animationDuration: `${ANIMATION_DURATION_MS}ms`,
        animationTimingFunction: "ease-in",
        animationFillMode: "forwards",
    },
    headerRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    sourceLabel: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    refineRow: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    selectedText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        fontStyle: "italic",
        maxHeight: "40px",
        overflowY: "hidden",
        textOverflow: "ellipsis",
    },
    inputRow: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    quickActionsRow: {
        display: "flex",
        flexWrap: "wrap",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
        ...shorthands.padding(tokens.spacingVerticalXXS, "0"),
    },
    quickActionChip: {
        minWidth: "auto",
        fontSize: tokens.fontSizeBase200,
    },
    sourceIcon: {
        display: "flex",
        alignItems: "center",
        color: tokens.colorBrandForeground1,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SprkChatHighlightRefine - Floating toolbar for text selection refinement.
 *
 * Supports two selection sources:
 * 1. **Local DOM selection** -- text highlighted in the chat message list
 * 2. **Cross-pane selection** -- text selected in the Analysis Workspace editor,
 *    received via SprkChatBridge and passed as the crossPaneSelection prop
 *
 * Cross-pane selections display a sticky toolbar at the top of the container
 * with a document icon and "Selected in Editor" badge. Local selections show a
 * floating toolbar anchored near the selection range with a chat icon and
 * "Selected in Chat" badge.
 *
 * Quick action chips (Simplify, Expand, Make Concise, Make Formal) appear below
 * the input. Clicking a chip auto-submits the refinement with that instruction.
 *
 * @example
 * ```tsx
 * <SprkChatHighlightRefine
 *   contentRef={messageListRef}
 *   onRefine={(text, instruction) => handleRefine(text, instruction)}
 *   onRefineRequest={(req) => handleRefineRequest(req)}
 *   isRefining={false}
 *   crossPaneSelection={crossPaneSelection}
 *   quickActions={[{ key: "simplify", label: "Simplify", instruction: "Simplify this text" }]}
 * />
 * ```
 */
export const SprkChatHighlightRefine: React.FC<ISprkChatHighlightRefineProps> = ({
    contentRef,
    onRefine,
    onRefineRequest,
    isRefining = false,
    crossPaneSelection,
    quickActions,
    onDismiss,
}) => {
    // Local DOM selection state
    const [localSelectedText, setLocalSelectedText] = React.useState<string>("");
    const [instruction, setInstruction] = React.useState<string>("");
    const [toolbarPosition, setToolbarPosition] = React.useState<{
        top: number;
        left: number;
    } | null>(null);
    const [showInput, setShowInput] = React.useState<boolean>(false);
    const [isDismissing, setIsDismissing] = React.useState<boolean>(false);

    const toolbarRef = React.useRef<HTMLDivElement>(null);
    const styles = useStyles();

    // Resolve quick actions: use provided or defaults
    const resolvedQuickActions: IQuickAction[] = quickActions !== undefined
        ? quickActions
        : DEFAULT_QUICK_ACTIONS;

    // Determine which selection source is active.
    // Cross-pane selection takes priority over local DOM selection.
    const hasCrossPaneSelection = !!crossPaneSelection && crossPaneSelection.text.length > 0;
    const activeText = hasCrossPaneSelection ? crossPaneSelection.text : localSelectedText;
    // For refine submission, use the full untruncated text when available
    const activeFullText = hasCrossPaneSelection
        ? crossPaneSelection.fullText
        : localSelectedText;
    // Source identification
    const activeSource: "editor" | "chat" = hasCrossPaneSelection ? "editor" : "chat";

    // Listen for local DOM selection changes within the container
    React.useEffect(() => {
        const container = contentRef.current;
        if (!container) {
            return;
        }

        const handleSelectionChange = () => {
            // If a cross-pane selection is active, don't override it with local selections
            if (hasCrossPaneSelection) {
                return;
            }

            const selection = window.getSelection();
            if (!selection || selection.isCollapsed || !selection.rangeCount) {
                // No selection or collapsed selection
                if (!showInput) {
                    setLocalSelectedText("");
                    setToolbarPosition(null);
                }
                return;
            }

            const range = selection.getRangeAt(0);
            const text = selection.toString().trim();

            // Check that the selection is within our container
            if (!container.contains(range.commonAncestorContainer)) {
                return;
            }

            if (text.length > 0) {
                const rect = range.getBoundingClientRect();
                const containerRect = container.getBoundingClientRect();

                setLocalSelectedText(text);
                setToolbarPosition({
                    top: rect.bottom - containerRect.top + 4,
                    left: rect.left - containerRect.left,
                });
            }
        };

        document.addEventListener("selectionchange", handleSelectionChange);
        return () => {
            document.removeEventListener("selectionchange", handleSelectionChange);
        };
    }, [contentRef, showInput, hasCrossPaneSelection]);

    // Reset instruction input when cross-pane selection changes
    React.useEffect(() => {
        if (hasCrossPaneSelection) {
            // When a new cross-pane selection arrives, reset the input state
            // but keep showInput if the user was already typing
            setInstruction("");
            setShowInput(false);
            setIsDismissing(false);
        }
    }, [hasCrossPaneSelection, crossPaneSelection?.text]);

    // Click-outside handler for local DOM selection toolbar
    React.useEffect(() => {
        if (!localSelectedText || !toolbarPosition || hasCrossPaneSelection) {
            return;
        }

        const handleClickOutside = (e: MouseEvent) => {
            const toolbar = toolbarRef.current;
            if (toolbar && !toolbar.contains(e.target as Node)) {
                handleAnimatedDismiss();
            }
        };

        // Delay attaching to avoid immediate dismiss on the click that created the selection
        const timerId = setTimeout(() => {
            document.addEventListener("mousedown", handleClickOutside);
        }, 100);

        return () => {
            clearTimeout(timerId);
            document.removeEventListener("mousedown", handleClickOutside);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [localSelectedText, toolbarPosition, hasCrossPaneSelection]);

    // Global Escape key handler
    React.useEffect(() => {
        const hasActiveSelection = hasCrossPaneSelection || (localSelectedText && toolbarPosition);
        if (!hasActiveSelection) {
            return;
        }

        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") {
                e.preventDefault();
                handleAnimatedDismiss();
            }
        };

        document.addEventListener("keydown", handleKeyDown);
        return () => {
            document.removeEventListener("keydown", handleKeyDown);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [hasCrossPaneSelection, localSelectedText, toolbarPosition]);

    const handleRefineClick = React.useCallback(() => {
        setShowInput(true);
    }, []);

    /**
     * Animate-out then reset state. Fires onDismiss callback.
     */
    const handleAnimatedDismiss = React.useCallback(() => {
        setIsDismissing(true);
        setTimeout(() => {
            setShowInput(false);
            setInstruction("");
            setLocalSelectedText("");
            setToolbarPosition(null);
            setIsDismissing(false);
            // Clear the local text selection (only affects local DOM, not cross-pane)
            if (!hasCrossPaneSelection) {
                const selection = window.getSelection();
                if (selection) {
                    selection.removeAllRanges();
                }
            }
            if (onDismiss) {
                onDismiss();
            }
        }, ANIMATION_DURATION_MS);
    }, [hasCrossPaneSelection, onDismiss]);

    /**
     * Submit a refinement request. Emits both the legacy onRefine callback
     * and the structured onRefineRequest callback.
     */
    const submitRefine = React.useCallback(
        (instructionText: string, quickActionKey?: string) => {
            if (!activeFullText || !instructionText.trim()) {
                return;
            }

            const trimmedInstruction = instructionText.trim();

            // Legacy callback
            onRefine(activeFullText, trimmedInstruction);

            // Structured callback
            if (onRefineRequest) {
                const request: IRefineRequest = {
                    selectedText: activeFullText,
                    instruction: trimmedInstruction,
                    source: activeSource,
                    quickAction: quickActionKey,
                };
                onRefineRequest(request);
            }

            // Reset state after submission
            setShowInput(false);
            setInstruction("");
        },
        [activeFullText, activeSource, onRefine, onRefineRequest]
    );

    const handleSubmitRefine = React.useCallback(() => {
        submitRefine(instruction);
    }, [instruction, submitRefine]);

    /**
     * Quick action chip clicked: fill instruction and auto-submit.
     */
    const handleQuickAction = React.useCallback(
        (action: IQuickAction) => {
            submitRefine(action.instruction, action.key);
        },
        [submitRefine]
    );

    const handleInstructionKeyDown = React.useCallback(
        (e: React.KeyboardEvent<HTMLInputElement>) => {
            if (e.key === "Enter") {
                e.preventDefault();
                handleSubmitRefine();
            } else if (e.key === "Escape") {
                e.preventDefault();
                handleAnimatedDismiss();
            }
        },
        [handleSubmitRefine, handleAnimatedDismiss]
    );

    const handleInstructionChange = React.useCallback(
        (_event: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
            setInstruction(data.value);
        },
        []
    );

    // Compute the preview text (truncated for display)
    const previewText = activeText.length > PREVIEW_MAX_LENGTH
        ? activeText.substring(0, PREVIEW_MAX_LENGTH) + "\u2026"
        : activeText;

    // Full text for tooltip (only show tooltip if text was truncated)
    const showTooltip = activeText.length > PREVIEW_MAX_LENGTH;
    const tooltipContent = activeFullText.length > 1000
        ? activeFullText.substring(0, 1000) + "\u2026"
        : activeFullText;

    // -----------------------------------------------------------------------
    // Shared sub-renders
    // -----------------------------------------------------------------------

    /** Renders the source icon (document for editor, chat for local) */
    const renderSourceIcon = () => (
        <span className={styles.sourceIcon}>
            {activeSource === "editor" ? (
                <Document20Regular />
            ) : (
                <Chat20Regular />
            )}
        </span>
    );

    /** Renders the quick action chips row */
    const renderQuickActions = () => {
        if (resolvedQuickActions.length === 0 || isRefining) {
            return null;
        }

        return (
            <div
                className={styles.quickActionsRow}
                data-testid="quick-actions-row"
            >
                {resolvedQuickActions.map((action) => (
                    <Button
                        key={action.key}
                        appearance="outline"
                        size="small"
                        className={styles.quickActionChip}
                        onClick={() => handleQuickAction(action)}
                        disabled={!activeFullText || isRefining}
                        data-testid={`quick-action-${action.key}`}
                    >
                        {action.label}
                    </Button>
                ))}
            </div>
        );
    };

    /** Renders the selected text preview, optionally wrapped in a Tooltip */
    const renderPreviewText = () => {
        const textElement = (
            <Text className={styles.selectedText}>
                &ldquo;{previewText}&rdquo;
            </Text>
        );

        if (showTooltip) {
            return (
                <Tooltip
                    content={tooltipContent}
                    relationship="description"
                    positioning="below"
                >
                    {textElement}
                </Tooltip>
            );
        }

        return textElement;
    };

    // -----------------------------------------------------------------------
    // Cross-pane selection: sticky toolbar at top of container
    // -----------------------------------------------------------------------

    if (hasCrossPaneSelection) {
        const crossPaneClassName = isDismissing
            ? `${styles.crossPaneToolbar} ${styles.crossPaneToolbarDismissing}`
            : styles.crossPaneToolbar;

        return (
            <div
                ref={toolbarRef}
                className={crossPaneClassName}
                role="toolbar"
                aria-label="Refine editor selection"
                data-testid="highlight-refine-toolbar-cross-pane"
            >
                <div className={styles.headerRow}>
                    <div className={styles.sourceLabel}>
                        {renderSourceIcon()}
                        <Badge appearance="filled" color="brand" size="small">
                            Selected in Editor
                        </Badge>
                    </div>
                    <Button
                        appearance="subtle"
                        icon={<Dismiss20Regular />}
                        onClick={handleAnimatedDismiss}
                        size="small"
                        aria-label="Dismiss"
                        data-testid="refine-dismiss-button"
                    />
                </div>

                {renderPreviewText()}

                {!showInput ? (
                    <>
                        <div className={styles.refineRow}>
                            <Button
                                appearance="subtle"
                                icon={<TextEditStyle20Regular />}
                                onClick={handleRefineClick}
                                disabled={isRefining}
                                size="small"
                                data-testid="refine-button"
                            >
                                Refine
                            </Button>
                        </div>
                        {renderQuickActions()}
                    </>
                ) : (
                    <>
                        <div className={styles.inputRow}>
                            {isRefining ? (
                                <Spinner size="tiny" label="Refining..." />
                            ) : (
                                <>
                                    <Input
                                        value={instruction}
                                        onChange={handleInstructionChange}
                                        onKeyDown={handleInstructionKeyDown}
                                        placeholder="How should this be refined?"
                                        size="small"
                                        aria-label="Refinement instruction"
                                        data-testid="refine-instruction-input"
                                    />
                                    <Button
                                        appearance="primary"
                                        onClick={handleSubmitRefine}
                                        disabled={!instruction.trim()}
                                        size="small"
                                        data-testid="refine-submit-button"
                                    >
                                        Go
                                    </Button>
                                </>
                            )}
                        </div>
                        {renderQuickActions()}
                    </>
                )}
            </div>
        );
    }

    // -----------------------------------------------------------------------
    // Local DOM selection: floating toolbar anchored to selection range
    // -----------------------------------------------------------------------

    if (!localSelectedText || !toolbarPosition) {
        return null;
    }

    const localClassName = isDismissing
        ? `${styles.toolbar} ${styles.toolbarDismissing}`
        : styles.toolbar;

    return (
        <div
            ref={toolbarRef}
            className={localClassName}
            style={{
                top: toolbarPosition.top,
                left: toolbarPosition.left,
            }}
            role="toolbar"
            aria-label="Refine selection"
            data-testid="highlight-refine-toolbar"
        >
            <div className={styles.headerRow}>
                <div className={styles.sourceLabel}>
                    {renderSourceIcon()}
                    <Badge appearance="tint" color="informative" size="small">
                        Selected in Chat
                    </Badge>
                </div>
                <Button
                    appearance="subtle"
                    icon={<Dismiss20Regular />}
                    onClick={handleAnimatedDismiss}
                    size="small"
                    aria-label="Dismiss"
                    data-testid="refine-dismiss-button"
                />
            </div>

            {renderPreviewText()}

            {!showInput ? (
                <>
                    <div className={styles.refineRow}>
                        <Button
                            appearance="subtle"
                            icon={<TextEditStyle20Regular />}
                            onClick={handleRefineClick}
                            disabled={isRefining}
                            size="small"
                            data-testid="refine-button"
                        >
                            Refine
                        </Button>
                    </div>
                    {renderQuickActions()}
                </>
            ) : (
                <>
                    <div className={styles.inputRow}>
                        {isRefining ? (
                            <Spinner size="tiny" label="Refining..." />
                        ) : (
                            <>
                                <Input
                                    value={instruction}
                                    onChange={handleInstructionChange}
                                    onKeyDown={handleInstructionKeyDown}
                                    placeholder="How should this be refined?"
                                    size="small"
                                    aria-label="Refinement instruction"
                                    data-testid="refine-instruction-input"
                                />
                                <Button
                                    appearance="primary"
                                    onClick={handleSubmitRefine}
                                    disabled={!instruction.trim()}
                                    size="small"
                                    data-testid="refine-submit-button"
                                >
                                    Go
                                </Button>
                            </>
                        )}
                    </div>
                    {renderQuickActions()}
                </>
            )}
        </div>
    );
};

export default SprkChatHighlightRefine;
