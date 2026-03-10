/**
 * NextStepsStep.tsx
 * Step after Processing — checkbox card selection for optional follow-on
 * actions after file upload.
 *
 * Layout:
 *   ┌─────────────────────────────────────────────────────────────────────┐
 *   │  Next Steps                                                         │
 *   │  Optionally select follow-on actions after uploading your files.    │
 *   │                                                                     │
 *   │  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐      │
 *   │  │ ☐  Send Email   │ │ ☐  Work on      │ │ ☐  Find         │      │
 *   │  │    Share docs   │ │    Analysis      │ │    Similar      │      │
 *   │  └─────────────────┘ └─────────────────┘ └─────────────────┘      │
 *   └─────────────────────────────────────────────────────────────────────┘
 *
 * Dynamic step injection:
 *   - "Send Email" → injects DocumentEmailStep
 *   - "Work on Analysis" → injects inline PlaybookCardGrid (pick playbook →
 *       create analysis → open workspace in new tab)
 *   - "Find Similar" → injects document picker + opens Find Similar in new tab
 *
 * @see ADR-021  - Fluent UI v9 design system
 * @see ADR-006  - Code Pages for standalone dialogs
 */

import * as React from "react";
import {
    Card,
    Text,
    Button,
    Spinner,
    MessageBar,
    MessageBarBody,
    makeStyles,
    tokens,
    mergeClasses,
} from "@fluentui/react-components";
import {
    MailRegular,
    BrainCircuitRegular,
    DocumentSearchRegular,
    CheckboxCheckedRegular,
    CheckboxUncheckedRegular,
    CheckmarkCircleRegular,
} from "@fluentui/react-icons";

import type { IWizardShellHandle, IWizardStepConfig } from "@spaarke/ui-components/components/Wizard";
import {
    PlaybookCardGrid,
    loadAllData,
    loadPlaybookScopes,
    createAndAssociate,
} from "@spaarke/ui-components/components/Playbook";
import type {
    IPlaybook,
    IAction,
    ISkill,
    IKnowledge,
    ITool,
    IPlaybookScopes,
} from "@spaarke/ui-components/components/Playbook";

import type { NextStepActionId, IUploadedFile } from "../types";
import { DocumentEmailStep } from "./DocumentEmailStep";
import type { IDocumentEmailStepProps } from "./DocumentEmailStep";
import { DocumentPicker } from "./DocumentPicker";
import type { UploadedDocumentInfo } from "./SummaryStep";
import type { OrchestratorFileResult } from "../services/uploadOrchestrator";
import { openFindSimilar } from "../services/nextStepLauncher";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface INextStepCardDef {
    id: NextStepActionId;
    label: string;
    description: string;
    icon: React.ReactNode;
}

export interface INextStepsStepProps {
    /** Currently selected next-step action IDs. */
    selectedNextSteps: NextStepActionId[];
    /** Called when the selection changes. */
    onNextStepsChanged: (selected: NextStepActionId[]) => void;
    /** Ref to the WizardShell handle for dynamic step injection. */
    wizardShellRef: React.RefObject<IWizardShellHandle | null>;
    /** Props for the DocumentEmailStep rendered inside the dynamic Send Email step. */
    emailStepProps: IDocumentEmailStepProps;
    /** Map of local file ID -> uploaded document info. */
    uploadedDocumentMap?: Map<string, UploadedDocumentInfo>;
    /** Uploaded files for building document picker options. */
    uploadedFiles?: IUploadedFile[];
    /** SPE container ID for file operations. */
    containerId: string;
    /** BFF API base URL. */
    bffBaseUrl: string;
    /** Token provider for BFF API authentication. */
    bffTokenProvider: () => Promise<string>;
}

// ---------------------------------------------------------------------------
// Card definitions
// ---------------------------------------------------------------------------

const CARD_DEFS: INextStepCardDef[] = [
    {
        id: "send-email",
        label: "Send Email",
        description: "Share uploaded documents via email.",
        icon: <MailRegular fontSize={28} />,
    },
    {
        id: "work-on-analysis",
        label: "Work on Analysis",
        description: "Start an AI analysis on uploaded documents.",
        icon: <BrainCircuitRegular fontSize={28} />,
    },
    {
        id: "find-similar",
        label: "Find Similar",
        description: "Search for similar documents across the tenant.",
        icon: <DocumentSearchRegular fontSize={28} />,
    },
];

// ---------------------------------------------------------------------------
// Dynamic step constants
// ---------------------------------------------------------------------------

export const DYNAMIC_SEND_EMAIL_STEP_ID = "dynamic-send-email";
export const DYNAMIC_WORK_ON_ANALYSIS_STEP_ID = "dynamic-work-on-analysis";
export const DYNAMIC_FIND_SIMILAR_STEP_ID = "dynamic-find-similar";

export const DYNAMIC_STEP_ID_MAP: Record<NextStepActionId, string> = {
    "send-email": DYNAMIC_SEND_EMAIL_STEP_ID,
    "work-on-analysis": DYNAMIC_WORK_ON_ANALYSIS_STEP_ID,
    "find-similar": DYNAMIC_FIND_SIMILAR_STEP_ID,
};

export const DYNAMIC_STEP_LABEL_MAP: Record<NextStepActionId, string> = {
    "send-email": "Send Email",
    "work-on-analysis": "Work on Analysis",
    "find-similar": "Find Similar",
};

const DYNAMIC_CANONICAL_ORDER = [
    DYNAMIC_SEND_EMAIL_STEP_ID,
    DYNAMIC_WORK_ON_ANALYSIS_STEP_ID,
    DYNAMIC_FIND_SIMILAR_STEP_ID,
];

// ---------------------------------------------------------------------------
// Xrm resolution helpers (for inline playbook WebApi + clientUrl)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

function resolveWebApi(): any {
    try {
        if (typeof (window as any).Xrm !== "undefined" && (window as any).Xrm?.WebApi?.retrieveMultipleRecords) return (window as any).Xrm.WebApi;
    } catch { /* */ }
    try {
        const p = (window.parent as any)?.Xrm;
        if (p?.WebApi?.retrieveMultipleRecords) return p.WebApi;
    } catch { /* */ }
    try {
        const t = (window.top as any)?.Xrm;
        if (t?.WebApi?.retrieveMultipleRecords) return t.WebApi;
    } catch { /* */ }
    return undefined;
}

function getClientUrl(): string | null {
    const tryGetUrl = (xrmObj: any): string | null => {
        try {
            const url = xrmObj?.Utility?.getGlobalContext?.()?.getClientUrl?.();
            if (url) return url.endsWith("/") ? url.slice(0, -1) : url;
        } catch { /* */ }
        return null;
    };
    try { if (typeof (window as any).Xrm !== "undefined") { const u = tryGetUrl((window as any).Xrm); if (u) return u; } } catch { /* */ }
    try { const u = tryGetUrl((window.parent as any)?.Xrm); if (u) return u; } catch { /* */ }
    try { const u = tryGetUrl((window.top as any)?.Xrm); if (u) return u; } catch { /* */ }
    return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalL,
    },
    headerText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    cardRow: {
        display: "grid",
        gridTemplateColumns: "repeat(3, 1fr)",
        gap: tokens.spacingHorizontalM,
    },
    card: {
        cursor: "pointer",
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalL,
        borderTopWidth: "2px",
        borderRightWidth: "2px",
        borderBottomWidth: "2px",
        borderLeftWidth: "2px",
        borderTopStyle: "solid",
        borderRightStyle: "solid",
        borderBottomStyle: "solid",
        borderLeftStyle: "solid",
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        userSelect: "none",
        transition: "border-color 0.1s ease, background-color 0.1s ease",
        boxShadow: "none",
        ":hover": {
            borderTopColor: tokens.colorBrandStroke1,
            borderRightColor: tokens.colorBrandStroke1,
            borderBottomColor: tokens.colorBrandStroke1,
            borderLeftColor: tokens.colorBrandStroke1,
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    cardSelected: {
        borderTopColor: tokens.colorBrandStroke1,
        borderRightColor: tokens.colorBrandStroke1,
        borderBottomColor: tokens.colorBrandStroke1,
        borderLeftColor: tokens.colorBrandStroke1,
        backgroundColor: tokens.colorBrandBackground2,
        ":hover": {
            borderTopColor: tokens.colorBrandStroke1,
            borderRightColor: tokens.colorBrandStroke1,
            borderBottomColor: tokens.colorBrandStroke1,
            borderLeftColor: tokens.colorBrandStroke1,
            backgroundColor: tokens.colorBrandBackground2Hover,
        },
    },
    cardTopRow: {
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalS,
    },
    cardIcon: {
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
    },
    cardIconNeutral: {
        color: tokens.colorNeutralForeground3,
    },
    checkboxIcon: {
        color: tokens.colorBrandForeground1,
        fontSize: "20px",
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
    },
    checkboxIconNeutral: {
        color: tokens.colorNeutralForeground3,
    },
    cardLabel: {
        color: tokens.colorNeutralForeground1,
        marginTop: tokens.spacingVerticalXS,
    },
    cardDescription: {
        color: tokens.colorNeutralForeground2,
    },
    skipMessage: {
        color: tokens.colorNeutralForeground3,
        textAlign: "center",
        paddingTop: tokens.spacingVerticalS,
    },
    dynamicStepRoot: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalL,
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
    },
    loadingCenter: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
    },
    successRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
});

// ---------------------------------------------------------------------------
// CheckboxCard sub-component
// ---------------------------------------------------------------------------

interface ICheckboxCardProps {
    def: INextStepCardDef;
    selected: boolean;
    onToggle: (id: NextStepActionId) => void;
}

const CheckboxCard: React.FC<ICheckboxCardProps> = ({ def, selected, onToggle }) => {
    const styles = useStyles();

    const handleClick = React.useCallback(() => {
        onToggle(def.id);
    }, [def.id, onToggle]);

    const handleKeyDown = React.useCallback(
        (e: React.KeyboardEvent) => {
            if (e.key === " " || e.key === "Enter") {
                e.preventDefault();
                onToggle(def.id);
            }
        },
        [def.id, onToggle]
    );

    return (
        <Card
            className={mergeClasses(styles.card, selected && styles.cardSelected)}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
            role="checkbox"
            aria-checked={selected}
            tabIndex={0}
            aria-label={`${def.label}: ${def.description}${selected ? " — selected" : ""}`}
        >
            <div className={styles.cardTopRow}>
                <span
                    className={mergeClasses(
                        styles.cardIcon,
                        !selected && styles.cardIconNeutral
                    )}
                    aria-hidden="true"
                >
                    {def.icon}
                </span>
                <span
                    className={mergeClasses(
                        styles.checkboxIcon,
                        !selected && styles.checkboxIconNeutral
                    )}
                    aria-hidden="true"
                >
                    {selected ? (
                        <CheckboxCheckedRegular fontSize={22} />
                    ) : (
                        <CheckboxUncheckedRegular fontSize={22} />
                    )}
                </span>
            </div>
            <Text size={300} weight="semibold" className={styles.cardLabel}>
                {def.label}
            </Text>
            <Text size={200} className={styles.cardDescription}>
                {def.description}
            </Text>
        </Card>
    );
};

// ---------------------------------------------------------------------------
// WorkOnAnalysisStepContent — inline playbook selection + analysis creation
// ---------------------------------------------------------------------------

interface IWorkOnAnalysisStepContentProps {
    /** Successfully uploaded file results for document picker. */
    successfulFiles: OrchestratorFileResult[];
    /** SPE container ID for file operations. */
    containerId: string;
}

const WorkOnAnalysisStepContent: React.FC<IWorkOnAnalysisStepContentProps> = ({
    successfulFiles,
    containerId,
}) => {
    const styles = useStyles();
    const webApi = React.useMemo(() => resolveWebApi(), []);

    // Data state
    const [isLoading, setIsLoading] = React.useState(true);
    const [error, setError] = React.useState<string | null>(null);
    const [playbooks, setPlaybooks] = React.useState<IPlaybook[]>([]);

    // Selection state
    const [selectedPlaybook, setSelectedPlaybook] = React.useState<IPlaybook | null>(null);
    const [playbookScopes, setPlaybookScopes] = React.useState<IPlaybookScopes | null>(null);
    const [selectedDocumentId, setSelectedDocumentId] = React.useState<string | null>(
        successfulFiles.length === 1 ? (successfulFiles[0].createResult?.documentId ?? null) : null
    );

    // Execution state
    const [isCreating, setIsCreating] = React.useState(false);
    const [successMessage, setSuccessMessage] = React.useState<string | null>(null);

    // Load playbooks on mount
    React.useEffect(() => {
        if (!webApi) {
            setError("Dataverse WebAPI not available.");
            setIsLoading(false);
            return;
        }
        let cancelled = false;
        (async () => {
            try {
                const data = await loadAllData(webApi);
                if (cancelled) return;
                setPlaybooks(data.playbooks);
            } catch (err) {
                if (cancelled) return;
                setError(err instanceof Error ? err.message : "Failed to load playbooks");
            } finally {
                if (!cancelled) setIsLoading(false);
            }
        })();
        return () => { cancelled = true; };
    }, [webApi]);

    // Playbook selection handler
    const handlePlaybookSelect = React.useCallback(async (playbook: IPlaybook) => {
        setSelectedPlaybook(playbook);
        setPlaybookScopes(null);
        try {
            const scopes = await loadPlaybookScopes(webApi, playbook.id);
            setPlaybookScopes(scopes);
        } catch (err) {
            console.error("[WorkOnAnalysis] Failed to load playbook scopes:", err);
        }
    }, [webApi]);

    // Create analysis handler
    const handleCreateAnalysis = React.useCallback(async () => {
        if (!selectedPlaybook || !playbookScopes || !selectedDocumentId) return;
        setIsCreating(true);
        setError(null);
        try {
            const analysisId = await createAndAssociate(webApi, {
                documentId: selectedDocumentId,
                documentName: successfulFiles.find(
                    (f) => f.createResult?.documentId === selectedDocumentId
                )?.fileName ?? "Document",
                playbookId: selectedPlaybook.id,
                actionId: playbookScopes.actionIds[0] ?? "",
                skillIds: playbookScopes.skillIds,
                knowledgeIds: playbookScopes.knowledgeIds,
                toolIds: playbookScopes.toolIds,
            });

            // Open analysis workspace in new tab
            const clientUrl = getClientUrl();
            if (clientUrl) {
                const url = `${clientUrl}/main.aspx?etn=sprk_analysis&id=${analysisId}&pagetype=entityrecord`;
                window.open(url, "_blank", "noopener,noreferrer");
            }
            setSuccessMessage("Analysis created and opened in a new tab.");
        } catch (err) {
            setError(err instanceof Error ? err.message : "Failed to create analysis");
        } finally {
            setIsCreating(false);
        }
    }, [selectedPlaybook, playbookScopes, selectedDocumentId, successfulFiles, webApi]);

    const canCreate = selectedPlaybook !== null && playbookScopes !== null && selectedDocumentId !== null;

    if (isLoading) {
        return (
            <div className={styles.loadingCenter}>
                <Spinner size="medium" label="Loading playbooks..." />
            </div>
        );
    }

    return (
        <div className={styles.dynamicStepRoot}>
            <Text as="h2" size={500} weight="semibold">Work on Analysis</Text>
            <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
                Select a playbook to create an AI analysis on your uploaded document.
            </Text>

            {error && (
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
            )}

            {successMessage && (
                <div className={styles.successRow}>
                    <CheckmarkCircleRegular
                        style={{ color: tokens.colorPaletteGreenForeground1, fontSize: "20px" }}
                    />
                    <Text size={300}>{successMessage}</Text>
                </div>
            )}

            {!successMessage && (
                <>
                    {/* Document picker (multi-file only) */}
                    {successfulFiles.length > 1 && (
                        <DocumentPicker
                            documents={successfulFiles}
                            selectedDocumentId={selectedDocumentId}
                            onSelectionChange={setSelectedDocumentId}
                        />
                    )}
                    {successfulFiles.length === 1 && (
                        <Text size={200} style={{ fontStyle: "italic", color: tokens.colorNeutralForeground3 }}>
                            Document: {successfulFiles[0].fileName}
                        </Text>
                    )}

                    {/* Playbook card grid */}
                    <PlaybookCardGrid
                        playbooks={playbooks}
                        selectedId={selectedPlaybook?.id}
                        onSelect={handlePlaybookSelect}
                        isLoading={false}
                    />

                    {/* Create button */}
                    <div>
                        <Button
                            appearance="primary"
                            icon={<BrainCircuitRegular />}
                            onClick={handleCreateAnalysis}
                            disabled={!canCreate || isCreating}
                        >
                            {isCreating ? "Creating..." : "Create Analysis"}
                        </Button>
                    </div>
                </>
            )}
        </div>
    );
};

// ---------------------------------------------------------------------------
// FindSimilarStepContent — document picker + opens Find Similar in new tab
// ---------------------------------------------------------------------------

interface IFindSimilarStepContentProps {
    successfulFiles: OrchestratorFileResult[];
    containerId: string;
}

const FindSimilarStepContent: React.FC<IFindSimilarStepContentProps> = ({
    successfulFiles,
    containerId,
}) => {
    const styles = useStyles();

    const [selectedDocumentId, setSelectedDocumentId] = React.useState<string | null>(
        successfulFiles.length === 1 ? (successfulFiles[0].createResult?.documentId ?? null) : null
    );

    const handleLaunch = React.useCallback(() => {
        if (selectedDocumentId) {
            openFindSimilar(selectedDocumentId, containerId);
        }
    }, [selectedDocumentId, containerId]);

    return (
        <div className={styles.dynamicStepRoot}>
            <Text as="h2" size={500} weight="semibold">Find Similar</Text>
            <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
                Select a document and search for similar documents across the tenant
                using AI-powered semantic search. The viewer will open in a new tab.
            </Text>
            {successfulFiles.length > 1 && (
                <DocumentPicker
                    documents={successfulFiles}
                    selectedDocumentId={selectedDocumentId}
                    onSelectionChange={setSelectedDocumentId}
                />
            )}
            {successfulFiles.length === 1 && (
                <Text size={200} style={{ fontStyle: "italic", color: tokens.colorNeutralForeground3 }}>
                    Document: {successfulFiles[0].fileName}
                </Text>
            )}
            <div>
                <Button
                    appearance="primary"
                    icon={<DocumentSearchRegular />}
                    onClick={handleLaunch}
                    disabled={!selectedDocumentId}
                >
                    Open Find Similar
                </Button>
            </div>
        </div>
    );
};

// ---------------------------------------------------------------------------
// buildDynamicStepConfig
// ---------------------------------------------------------------------------

interface IDynamicStepBuildOptions {
    emailStepProps: IDocumentEmailStepProps;
    successfulFiles?: OrchestratorFileResult[];
    containerId: string;
}

function buildDynamicStepConfig(
    actionId: NextStepActionId,
    options: IDynamicStepBuildOptions
): IWizardStepConfig {
    const stepId = DYNAMIC_STEP_ID_MAP[actionId];
    const stepLabel = DYNAMIC_STEP_LABEL_MAP[actionId];

    if (actionId === "send-email") {
        return {
            id: stepId,
            label: stepLabel,
            canAdvance: () => true,
            isSkippable: true,
            renderContent: () => (
                <DocumentEmailStep {...options.emailStepProps} />
            ),
        };
    }

    if (actionId === "work-on-analysis") {
        return {
            id: stepId,
            label: stepLabel,
            canAdvance: () => true,
            isSkippable: true,
            renderContent: () => (
                <WorkOnAnalysisStepContent
                    successfulFiles={options.successfulFiles ?? []}
                    containerId={options.containerId}
                />
            ),
        };
    }

    // find-similar
    return {
        id: stepId,
        label: stepLabel,
        canAdvance: () => true,
        isSkippable: true,
        renderContent: () => (
            <FindSimilarStepContent
                successfulFiles={options.successfulFiles ?? []}
                containerId={options.containerId}
            />
        ),
    };
}

// ---------------------------------------------------------------------------
// NextStepsStep (exported)
// ---------------------------------------------------------------------------

export const NextStepsStep: React.FC<INextStepsStepProps> = ({
    selectedNextSteps,
    onNextStepsChanged,
    wizardShellRef,
    emailStepProps,
    uploadedDocumentMap,
    uploadedFiles,
    containerId,
}) => {
    const styles = useStyles();

    // Track previous selection to diff adds/removes for dynamic steps
    const prevSelectedRef = React.useRef<NextStepActionId[]>([]);

    const handleToggle = React.useCallback(
        (id: NextStepActionId) => {
            let next: NextStepActionId[];
            if (selectedNextSteps.includes(id)) {
                next = selectedNextSteps.filter((a) => a !== id);
            } else {
                const orderedIds = CARD_DEFS.map((d) => d.id);
                next = orderedIds.filter(
                    (orderedId) => selectedNextSteps.includes(orderedId) || orderedId === id
                );
            }
            onNextStepsChanged(next);
        },
        [selectedNextSteps, onNextStepsChanged]
    );

    // Build successful files list for document picker in dynamic steps
    const successfulFiles = React.useMemo((): OrchestratorFileResult[] => {
        if (!uploadedDocumentMap || !uploadedFiles) return [];
        return uploadedFiles
            .filter((f) => uploadedDocumentMap.has(f.id))
            .map((f) => {
                const info = uploadedDocumentMap.get(f.id)!;
                return {
                    fileName: f.name,
                    success: true,
                    createResult: {
                        success: true,
                        fileName: f.name,
                        recordId: info.documentId,
                        documentId: info.documentId,
                        driveId: info.driveId,
                        itemId: info.itemId,
                    },
                };
            });
    }, [uploadedDocumentMap, uploadedFiles]);

    // Sync dynamic steps with selected actions
    React.useEffect(() => {
        const prev = prevSelectedRef.current;
        const next = selectedNextSteps;

        for (const actionId of next) {
            if (!prev.includes(actionId)) {
                wizardShellRef.current?.addDynamicStep(
                    buildDynamicStepConfig(actionId, { emailStepProps, successfulFiles, containerId }),
                    DYNAMIC_CANONICAL_ORDER
                );
            }
        }

        for (const actionId of prev) {
            if (!next.includes(actionId)) {
                wizardShellRef.current?.removeDynamicStep(DYNAMIC_STEP_ID_MAP[actionId]);
            }
        }

        prevSelectedRef.current = next;
    }, [selectedNextSteps, wizardShellRef, emailStepProps, successfulFiles, containerId]);

    return (
        <div className={styles.root}>
            <div className={styles.headerText}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Next steps
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    Optionally select follow-on actions to perform after uploading your
                    files. You can skip all and handle these later.
                </Text>
            </div>

            <div className={styles.cardRow} role="group" aria-label="Follow-on actions">
                {CARD_DEFS.map((def) => (
                    <CheckboxCard
                        key={def.id}
                        def={def}
                        selected={selectedNextSteps.includes(def.id)}
                        onToggle={handleToggle}
                    />
                ))}
            </div>

            {selectedNextSteps.length === 0 && (
                <Text size={200} className={styles.skipMessage}>
                    No actions selected — click Finish to complete the upload without
                    follow-on steps.
                </Text>
            )}
        </div>
    );
};
