/**
 * FileUploadProgress.tsx
 *
 * Displays per-file upload progress during the orchestrated upload pipeline.
 * Shows each file's current phase with appropriate status indicators.
 *
 * Phases: queued -> uploading (with %) -> creating-record -> profiling -> complete
 * Error state can occur at any phase.
 *
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useMemo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    ProgressBar,
    Spinner,
    Badge,
    Button,
    mergeClasses,
} from "@fluentui/react-components";
import {
    CheckmarkCircleFilled,
    DismissCircleFilled,
    ClockRegular,
    ArrowUploadFilled,
    DatabaseRegular,
    BrainCircuitRegular,
    WarningFilled,
} from "@fluentui/react-icons";

import type { OrchestratorFileProgress, FileUploadPhase } from "../services/uploadOrchestrator";

/** The two collision resolutions a user is offered. See ConflictBehaviorOption for why only two. */
export type ConflictResolution = "rename" | "replace";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFileUploadProgressProps {
    /** Per-file progress entries from the upload orchestrator. */
    fileProgress: OrchestratorFileProgress[];

    /**
     * Invoked when the user resolves a name collision for one file. Omit to render collisions as
     * plain errors (no buttons) — the honest fallback for a host that cannot retry.
     */
    onResolveConflict?: (fileName: string, resolution: ConflictResolution) => void;

    /** File names currently being retried — their buttons are disabled to prevent double-submit. */
    resolvingFileNames?: ReadonlySet<string>;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        width: "100%",
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        paddingBottom: tokens.spacingVerticalXS,
    },
    fileRow: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    fileRowError: {
        backgroundColor: tokens.colorPaletteRedBackground1,
    },
    // A collision is not a failure — nothing was written and the user has a choice. Amber, not red.
    fileRowConflict: {
        backgroundColor: tokens.colorPaletteYellowBackground1,
    },
    fileRowComplete: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
    },
    fileInfo: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    fileName: {
        flex: "1 1 auto",
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    statusIcon: {
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
    },
    statusIconQueued: {
        color: tokens.colorNeutralForeground3,
    },
    statusIconUploading: {
        color: tokens.colorBrandForeground1,
    },
    statusIconCreating: {
        color: tokens.colorPaletteBlueForeground2,
    },
    statusIconProfiling: {
        color: tokens.colorPalettePurpleForeground2,
    },
    statusIconComplete: {
        color: tokens.colorPaletteGreenForeground1,
    },
    statusIconError: {
        color: tokens.colorPaletteRedForeground1,
    },
    progressBar: {
        marginTop: tokens.spacingVerticalXXS,
    },
    statusLabel: {
        color: tokens.colorNeutralForeground3,
    },
    errorMessage: {
        color: tokens.colorPaletteRedForeground1,
    },
    statusIconConflict: {
        color: tokens.colorPaletteYellowForeground1,
    },
    conflictMessage: {
        color: tokens.colorNeutralForeground1,
    },
    conflictActions: {
        display: "flex",
        flexWrap: "wrap",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        marginTop: tokens.spacingVerticalXS,
    },
});

// ---------------------------------------------------------------------------
// Phase display config
// ---------------------------------------------------------------------------

interface PhaseDisplay {
    label: string;
    icon: React.ReactNode;
    styleKey: string;
}

function usePhaseDisplay(phase: FileUploadPhase, styles: ReturnType<typeof useStyles>): PhaseDisplay {
    const displays: Record<FileUploadPhase, PhaseDisplay> = {
        queued: {
            label: "Queued",
            icon: <ClockRegular className={mergeClasses(styles.statusIcon, styles.statusIconQueued)} />,
            styleKey: "statusIconQueued",
        },
        uploading: {
            label: "Uploading",
            icon: <ArrowUploadFilled className={mergeClasses(styles.statusIcon, styles.statusIconUploading)} />,
            styleKey: "statusIconUploading",
        },
        "creating-record": {
            label: "Creating record",
            icon: <DatabaseRegular className={mergeClasses(styles.statusIcon, styles.statusIconCreating)} />,
            styleKey: "statusIconCreating",
        },
        profiling: {
            label: "Profiling",
            icon: <BrainCircuitRegular className={mergeClasses(styles.statusIcon, styles.statusIconProfiling)} />,
            styleKey: "statusIconProfiling",
        },
        complete: {
            label: "Complete",
            icon: <CheckmarkCircleFilled className={mergeClasses(styles.statusIcon, styles.statusIconComplete)} />,
            styleKey: "statusIconComplete",
        },
        error: {
            label: "Error",
            icon: <DismissCircleFilled className={mergeClasses(styles.statusIcon, styles.statusIconError)} />,
            styleKey: "statusIconError",
        },
    };

    return displays[phase];
}

// ---------------------------------------------------------------------------
// FileUploadProgressRow (single file)
// ---------------------------------------------------------------------------

function FileUploadProgressRow({
    progress,
    onResolveConflict,
    isResolving,
}: {
    progress: OrchestratorFileProgress;
    onResolveConflict?: (fileName: string, resolution: ConflictResolution) => void;
    isResolving: boolean;
}): JSX.Element {
    const styles = useStyles();
    const phaseDisplay = usePhaseDisplay(progress.phase, styles);

    // A collision only reads as a CHOICE if the host can actually act on it. Without a resolver
    // this row stays a plain error — misleading buttons would be worse than no buttons.
    const isConflict = progress.phase === "error" && progress.nameConflict != null;
    const canResolve = isConflict && onResolveConflict != null;

    const rowClassName = mergeClasses(
        styles.fileRow,
        progress.phase === "error" && (isConflict ? styles.fileRowConflict : styles.fileRowError),
        progress.phase === "complete" && styles.fileRowComplete,
    );

    const showProgressBar = progress.phase === "uploading";
    const showSpinner =
        isResolving ||
        progress.phase === "uploading" ||
        progress.phase === "creating-record" ||
        progress.phase === "profiling";

    return (
        <div className={rowClassName}>
            <div className={styles.fileInfo}>
                {/* Status icon */}
                {showSpinner ? (
                    <Spinner size="tiny" />
                ) : isConflict ? (
                    <WarningFilled
                        className={mergeClasses(styles.statusIcon, styles.statusIconConflict)}
                    />
                ) : (
                    phaseDisplay.icon
                )}

                {/* File name */}
                <Text size={200} weight="semibold" className={styles.fileName}>
                    {progress.fileName}
                </Text>

                {/* Status label / badge */}
                <Badge
                    size="small"
                    appearance="outline"
                    color={
                        progress.phase === "complete" ? "success"
                        : isConflict ? "warning"
                        : progress.phase === "error" ? "danger"
                        : "informative"
                    }
                >
                    {isResolving ? "Retrying" : isConflict ? "Name conflict" : phaseDisplay.label}
                    {progress.phase === "uploading" && progress.uploadPercent > 0
                        ? ` (${progress.uploadPercent}%)`
                        : ""}
                </Badge>
            </div>

            {/* Progress bar for uploading phase */}
            {showProgressBar && (
                <ProgressBar
                    className={styles.progressBar}
                    value={progress.uploadPercent / 100}
                    thickness="medium"
                />
            )}

            {/* Error message — a collision is stated in neutral text, not error red. */}
            {progress.phase === "error" && progress.errorMessage && (
                <Text
                    size={100}
                    className={isConflict ? styles.conflictMessage : styles.errorMessage}
                >
                    {isConflict
                        ? `A file named "${progress.fileName}" already exists here. Nothing has been uploaded or changed — choose how to continue.`
                        : progress.errorMessage}
                </Text>
            )}

            {/* Collision resolution — exactly two options.
                "Keep both" = server stores this file under a non-colliding name (rename).
                "Save as new version" = replace; SharePoint retains the prior content as a version,
                so the existing document is recoverable, not destroyed.
                There is deliberately no third "replace and discard" option — at the Graph level it
                is the same call as replace; a user who wants the old file gone deletes it. */}
            {canResolve && (
                <div className={styles.conflictActions}>
                    <Button
                        size="small"
                        appearance="primary"
                        disabled={isResolving}
                        onClick={() => onResolveConflict(progress.fileName, "rename")}
                    >
                        Keep both
                    </Button>
                    <Button
                        size="small"
                        appearance="secondary"
                        disabled={isResolving}
                        onClick={() => onResolveConflict(progress.fileName, "replace")}
                    >
                        Save as new version
                    </Button>
                    <Text size={100} className={styles.statusLabel}>
                        Keep both uploads this file under a new name. Save as new version keeps the
                        existing document and adds this file as its latest version.
                    </Text>
                </div>
            )}
        </div>
    );
}

// ---------------------------------------------------------------------------
// FileUploadProgress (exported)
// ---------------------------------------------------------------------------

export function FileUploadProgress({
    fileProgress,
    onResolveConflict,
    resolvingFileNames,
}: IFileUploadProgressProps): JSX.Element {
    const styles = useStyles();

    const summary = useMemo(() => {
        const total = fileProgress.length;
        const completed = fileProgress.filter((f) => f.phase === "complete").length;
        const failed = fileProgress.filter(
            (f) => f.phase === "error" && f.nameConflict == null,
        ).length;
        // Counted separately from `failed`: a collision is awaiting a decision, and reporting it as
        // "failed" tells the user something went wrong when nothing has.
        const conflicts = fileProgress.filter(
            (f) => f.phase === "error" && f.nameConflict != null,
        ).length;
        const inProgress = total - completed - failed - conflicts;
        return { total, completed, failed, conflicts, inProgress };
    }, [fileProgress]);

    return (
        <div className={styles.root}>
            {/* Header summary */}
            <div className={styles.header}>
                <Text size={400} weight="semibold">
                    Upload Progress
                </Text>
                <Text size={200} className={styles.statusLabel}>
                    {summary.completed}/{summary.total} complete
                    {summary.failed > 0 ? ` \u2022 ${summary.failed} failed` : ""}
                    {summary.conflicts > 0
                        ? ` \u2022 ${summary.conflicts} needing your choice`
                        : ""}
                </Text>
            </div>

            {/* Per-file rows */}
            {fileProgress.map((fp) => (
                <FileUploadProgressRow
                    key={fp.fileName}
                    progress={fp}
                    onResolveConflict={onResolveConflict}
                    isResolving={resolvingFileNames?.has(fp.fileName) ?? false}
                />
            ))}
        </div>
    );
}
