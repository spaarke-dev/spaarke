/**
 * SprkChatHighlightRefine - Text selection and refinement UI
 *
 * Detects text selection within a container element and shows a floating
 * "Refine" toolbar. When the user provides an instruction, it triggers
 * a refinement flow via the SSE refine endpoint.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only (useState, useEffect, useRef, useCallback)
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
} from "@fluentui/react-components";
import { TextEditStyle20Regular, Dismiss20Regular } from "@fluentui/react-icons";
import { ISprkChatHighlightRefineProps } from "./types";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
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
        maxWidth: "320px",
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
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SprkChatHighlightRefine - Floating toolbar for text selection refinement.
 *
 * Monitors the contentRef element for text selections. When text is selected,
 * a floating toolbar appears near the selection allowing the user to submit
 * a refinement instruction.
 *
 * @example
 * ```tsx
 * <SprkChatHighlightRefine
 *   contentRef={messageListRef}
 *   onRefine={(text, instruction) => handleRefine(text, instruction)}
 *   isRefining={false}
 * />
 * ```
 */
export const SprkChatHighlightRefine: React.FC<ISprkChatHighlightRefineProps> = ({
    contentRef,
    onRefine,
    isRefining = false,
}) => {
    const [selectedText, setSelectedText] = React.useState<string>("");
    const [instruction, setInstruction] = React.useState<string>("");
    const [toolbarPosition, setToolbarPosition] = React.useState<{
        top: number;
        left: number;
    } | null>(null);
    const [showInput, setShowInput] = React.useState<boolean>(false);

    const styles = useStyles();

    // Listen for selection changes within the container
    React.useEffect(() => {
        const container = contentRef.current;
        if (!container) {
            return;
        }

        const handleSelectionChange = () => {
            const selection = window.getSelection();
            if (!selection || selection.isCollapsed || !selection.rangeCount) {
                // No selection or collapsed selection
                if (!showInput) {
                    setSelectedText("");
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

                setSelectedText(text);
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
    }, [contentRef, showInput]);

    const handleRefineClick = React.useCallback(() => {
        setShowInput(true);
    }, []);

    const handleDismiss = React.useCallback(() => {
        setShowInput(false);
        setInstruction("");
        setSelectedText("");
        setToolbarPosition(null);
        // Clear the text selection
        const selection = window.getSelection();
        if (selection) {
            selection.removeAllRanges();
        }
    }, []);

    const handleSubmitRefine = React.useCallback(() => {
        if (selectedText && instruction.trim()) {
            onRefine(selectedText, instruction.trim());
            setShowInput(false);
            setInstruction("");
        }
    }, [selectedText, instruction, onRefine]);

    const handleInstructionKeyDown = React.useCallback(
        (e: React.KeyboardEvent<HTMLInputElement>) => {
            if (e.key === "Enter") {
                e.preventDefault();
                handleSubmitRefine();
            } else if (e.key === "Escape") {
                handleDismiss();
            }
        },
        [handleSubmitRefine, handleDismiss]
    );

    const handleInstructionChange = React.useCallback(
        (_event: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
            setInstruction(data.value);
        },
        []
    );

    if (!selectedText || !toolbarPosition) {
        return null;
    }

    return (
        <div
            className={styles.toolbar}
            style={{
                top: toolbarPosition.top,
                left: toolbarPosition.left,
            }}
            role="toolbar"
            aria-label="Refine selection"
            data-testid="highlight-refine-toolbar"
        >
            <Text className={styles.selectedText}>
                &ldquo;{selectedText.length > 80
                    ? selectedText.substring(0, 80) + "..."
                    : selectedText}&rdquo;
            </Text>

            {!showInput ? (
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
                    <Button
                        appearance="subtle"
                        icon={<Dismiss20Regular />}
                        onClick={handleDismiss}
                        size="small"
                        aria-label="Dismiss"
                    />
                </div>
            ) : (
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
                            <Button
                                appearance="subtle"
                                icon={<Dismiss20Regular />}
                                onClick={handleDismiss}
                                size="small"
                                aria-label="Cancel refinement"
                            />
                        </>
                    )}
                </div>
            )}
        </div>
    );
};

export default SprkChatHighlightRefine;
