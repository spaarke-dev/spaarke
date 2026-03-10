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
    mergeClasses,
} from "@fluentui/react-components";
import {
    CheckmarkCircleFilled,
    DismissCircleFilled,
    ClockRegular,
    ArrowUploadFilled,
    DatabaseRegular,
    BrainCircuitRegular,
} from "@fluentui/react-icons";

import type { OrchestratorFileProgress, FileUploadPhase } from "../services/uploadOrchestrator";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFileUploadProgressProps {
    /** Per-file progress entries from the upload orchestrator. */
    fileProgress: OrchestratorFileProgress[];
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
}: {
    progress: OrchestratorFileProgress;
}): JSX.Element {
    const styles = useStyles();
    const phaseDisplay = usePhaseDisplay(progress.phase, styles);

    const rowClassName = mergeClasses(
        styles.fileRow,
        progress.phase === "error" && styles.fileRowError,
        progress.phase === "complete" && styles.fileRowComplete,
    );

    const showProgressBar = progress.phase === "uploading";
    const showSpinner =
        progress.phase === "uploading" ||
        progress.phase === "creating-record" ||
        progress.phase === "profiling";

    return (
        <div className={rowClassName}>
            <div className={styles.fileInfo}>
                {/* Status icon */}
                {showSpinner ? (
                    <Spinner size="tiny" />
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
                        : progress.phase === "error" ? "danger"
                        : "informative"
                    }
                >
                    {phaseDisplay.label}
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

            {/* Error message */}
            {progress.phase === "error" && progress.errorMessage && (
                <Text size={100} className={styles.errorMessage}>
                    {progress.errorMessage}
                </Text>
            )}
        </div>
    );
}

// ---------------------------------------------------------------------------
// FileUploadProgress (exported)
// ---------------------------------------------------------------------------

export function FileUploadProgress({
    fileProgress,
}: IFileUploadProgressProps): JSX.Element {
    const styles = useStyles();

    const summary = useMemo(() => {
        const total = fileProgress.length;
        const completed = fileProgress.filter((f) => f.phase === "complete").length;
        const errors = fileProgress.filter((f) => f.phase === "error").length;
        const inProgress = total - completed - errors;
        return { total, completed, errors, inProgress };
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
                    {summary.errors > 0 ? ` \u2022 ${summary.errors} failed` : ""}
                </Text>
            </div>

            {/* Per-file rows */}
            {fileProgress.map((fp) => (
                <FileUploadProgressRow key={fp.fileName} progress={fp} />
            ))}
        </div>
    );
}
