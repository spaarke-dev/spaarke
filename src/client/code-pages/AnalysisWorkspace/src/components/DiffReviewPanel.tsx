/**
 * DiffReviewPanel - Overlay panel for reviewing AI-proposed revisions
 *
 * Renders a slide-in panel over the editor area containing a DiffCompareView
 * from @spaarke/ui-components. When the AI proposes revisions in diff mode,
 * this panel opens with the original and proposed content, allowing the user
 * to Accept, Reject, or Edit the proposed changes before they are applied.
 *
 * Behavior:
 * - Slides in from the right with a semi-transparent backdrop
 * - Accept: pushes current editor content to undo stack, replaces with proposed text
 * - Reject: closes panel, discards proposed content, editor unchanged
 * - Edit: user can modify proposed text before accepting
 * - Escape key dismisses panel (same as Reject)
 * - New chat message while panel is open auto-rejects current diff
 *
 * Task 103: Wire DiffCompareView into Analysis Workspace
 *
 * @see DiffCompareView (@spaarke/ui-components)
 * @see ADR-006  - Code Pages for standalone dialogs
 * @see ADR-012  - Shared component library
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + design tokens)
 */

import { useCallback, useEffect, useRef } from "react";
import {
    makeStyles,
    mergeClasses,
    tokens,
    Text,
    Button,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { DiffCompareView } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DiffReviewPanelProps {
    /** Whether the panel is currently open */
    isOpen: boolean;
    /** Original editor content (before AI revision) */
    originalText: string;
    /** Proposed content from AI revision */
    proposedText: string;
    /** Called when user accepts the proposed text (may be edited) */
    onAccept: (acceptedText: string) => void;
    /** Called when user rejects the proposed changes */
    onReject: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    backdrop: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: tokens.colorNeutralBackgroundAlpha2,
        zIndex: 20,
        transition: "opacity 250ms ease-in-out",
        display: "flex",
        justifyContent: "flex-end",
        overflow: "hidden",
    },
    backdropHidden: {
        opacity: 0,
        pointerEvents: "none",
    },
    backdropVisible: {
        opacity: 1,
        pointerEvents: "auto",
    },
    panel: {
        display: "flex",
        flexDirection: "column",
        width: "90%",
        maxWidth: "900px",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        boxShadow: tokens.shadow64,
        transition: "transform 250ms ease-in-out",
        overflow: "hidden",
    },
    panelHidden: {
        transform: "translateX(100%)",
    },
    panelVisible: {
        transform: "translateX(0)",
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground3,
        flexShrink: 0,
    },
    headerTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    diffContainer: {
        flex: 1,
        overflow: "auto",
        padding: tokens.spacingHorizontalL,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function DiffReviewPanel({
    isOpen,
    originalText,
    proposedText,
    onAccept,
    onReject,
}: DiffReviewPanelProps): JSX.Element {
    const styles = useStyles();
    const panelRef = useRef<HTMLDivElement>(null);

    // ---- Escape key dismisses the panel (same as Reject) ----
    // Note: DiffCompareView has its own Escape handler that calls onReject for
    // non-edit mode and handleEditCancel for edit mode. We add a backdrop-level
    // handler so clicking outside the DiffCompareView and pressing Escape still
    // works. We use bubble phase so DiffCompareView's internal handlers take
    // priority (e.g., cancelling edit mode before dismissing the panel).
    useEffect(() => {
        if (!isOpen) {
            return;
        }

        const handleKeyDown = (e: KeyboardEvent): void => {
            if (e.key === "Escape") {
                // Only dismiss if the event wasn't already handled by DiffCompareView
                if (!e.defaultPrevented) {
                    e.preventDefault();
                    onReject();
                }
            }
        };

        document.addEventListener("keydown", handleKeyDown);
        return () => {
            document.removeEventListener("keydown", handleKeyDown);
        };
    }, [isOpen, onReject]);

    // ---- Focus trap: focus panel when opened ----
    useEffect(() => {
        if (isOpen && panelRef.current) {
            panelRef.current.focus();
        }
    }, [isOpen]);

    // ---- Accept handler: passes accepted text up ----
    const handleAccept = useCallback(
        (acceptedText: string) => {
            onAccept(acceptedText);
        },
        [onAccept],
    );

    // ---- Edit handler: DiffCompareView calls this when user saves edits ----
    const handleEdit = useCallback((_editedText: string) => {
        // The DiffCompareView internally updates its edit buffer.
        // The edited text will be passed via onAccept when the user clicks Accept.
        // No separate action needed here.
    }, []);

    // ---- Render ----
    const backdropClass = mergeClasses(
        styles.backdrop,
        isOpen ? styles.backdropVisible : styles.backdropHidden,
    );
    const panelClass = mergeClasses(
        styles.panel,
        isOpen ? styles.panelVisible : styles.panelHidden,
    );

    return (
        <div
            className={backdropClass}
            role="dialog"
            aria-modal="true"
            aria-label="Review proposed changes"
            data-testid="diff-review-panel-backdrop"
        >
            <div
                ref={panelRef}
                className={panelClass}
                tabIndex={-1}
                data-testid="diff-review-panel"
            >
                {/* Header */}
                <div className={styles.header}>
                    <Text size={400} className={styles.headerTitle}>
                        Review Proposed Changes
                    </Text>
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<DismissRegular />}
                        onClick={onReject}
                        aria-label="Close review panel"
                        data-testid="diff-review-close"
                    />
                </div>

                {/* DiffCompareView content */}
                {isOpen && (
                    <div className={styles.diffContainer}>
                        <DiffCompareView
                            originalText={originalText}
                            proposedText={proposedText}
                            htmlMode={true}
                            mode="side-by-side"
                            onAccept={handleAccept}
                            onReject={onReject}
                            onEdit={handleEdit}
                            title="AI-Proposed Revision"
                            ariaLabel="Review AI-proposed changes to the analysis"
                        />
                    </div>
                )}
            </div>
        </div>
    );
}
