/**
 * StreamingIndicator - Visual indicator for active document streaming
 *
 * Displays a subtle overlay/banner when the SprkChat side pane is streaming
 * content into the RichTextEditor. Shows streaming progress (token count),
 * a pulsing animation, and an Escape/Cancel button to abort the stream.
 *
 * Uses Fluent UI v9 semantic tokens exclusively (ADR-021). Supports dark mode
 * via inherited FluentProvider theme context.
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared component library conventions
 */

import {
    makeStyles,
    tokens,
    Button,
    Text,
    ProgressBar,
    mergeClasses,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface StreamingIndicatorProps {
    /** Whether streaming is currently active */
    isStreaming: boolean;
    /** Number of tokens received so far */
    tokenCount: number;
    /** Whether a bulk document replacement is in progress */
    isReplacing?: boolean;
    /** Callback to cancel the active stream */
    onCancel: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    overlay: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
        backgroundColor: tokens.colorNeutralBackground1Hover,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        minHeight: "36px",
        transition: "opacity 200ms ease-in-out, max-height 200ms ease-in-out",
        overflow: "hidden",
    },
    overlayHidden: {
        opacity: 0,
        maxHeight: "0px",
        padding: "0px",
        minHeight: "0px",
        borderBottom: "none",
    },
    overlayVisible: {
        opacity: 1,
        maxHeight: "48px",
    },
    pulse: {
        width: "8px",
        height: "8px",
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: tokens.colorBrandBackground,
        animationName: {
            "0%": { opacity: 1 },
            "50%": { opacity: 0.4 },
            "100%": { opacity: 1 },
        },
        animationDuration: "1.5s",
        animationIterationCount: "infinite",
        animationTimingFunction: "ease-in-out",
        flexShrink: 0,
    },
    label: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        flexShrink: 0,
    },
    tokenCount: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        fontFamily: tokens.fontFamilyMonospace,
        flexShrink: 0,
    },
    progress: {
        flex: 1,
        minWidth: "60px",
        maxWidth: "200px",
    },
    cancelButton: {
        flexShrink: 0,
        marginLeft: "auto",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export function StreamingIndicator({
    isStreaming,
    tokenCount,
    isReplacing = false,
    onCancel,
}: StreamingIndicatorProps): JSX.Element {
    const styles = useStyles();
    const isActive = isStreaming || isReplacing;

    const overlayClass = mergeClasses(
        styles.overlay,
        isActive ? styles.overlayVisible : styles.overlayHidden
    );

    const statusLabel = isReplacing
        ? "Replacing document..."
        : "AI is writing...";

    return (
        <div className={overlayClass} role="status" aria-live="polite">
            {isActive && (
                <>
                    <div className={styles.pulse} aria-hidden="true" />
                    <Text className={styles.label}>{statusLabel}</Text>
                    {isStreaming && (
                        <>
                            <Text className={styles.tokenCount}>
                                {tokenCount} tokens
                            </Text>
                            <div className={styles.progress}>
                                <ProgressBar />
                            </div>
                            <Button
                                className={styles.cancelButton}
                                appearance="subtle"
                                size="small"
                                icon={<DismissRegular />}
                                onClick={onCancel}
                                title="Cancel streaming (Escape)"
                                aria-label="Cancel streaming"
                            >
                                Cancel
                            </Button>
                        </>
                    )}
                </>
            )}
        </div>
    );
}
