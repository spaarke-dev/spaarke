/**
 * UpdateRelatedButton React Component
 *
 * Button component that triggers update of related records
 * based on configured field mapping profiles.
 *
 * Features:
 * - Confirmation dialog before push operation
 * - Progress indicator during API call
 * - Success/error toast with result counts
 * - Dark mode support via Fluent UI v9 tokens
 *
 * @remarks
 * - Uses React 16 APIs per ADR-022
 * - Uses Fluent UI v9 per ADR-021
 * - Calls POST /api/v1/field-mappings/push endpoint (Task 054)
 */

import * as React from "react";
import {
    Button,
    Spinner,
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogContent,
    DialogActions,
    DialogTrigger,
    Toast,
    ToastTitle,
    ToastBody,
    Toaster,
    useToastController,
    useId,
    tokens,
    makeStyles,
    Text,
} from "@fluentui/react-components";
import { ArrowSync20Regular, ArrowSync20Filled, bundleIcon } from "@fluentui/react-icons";

// Bundle regular and filled icons for proper hover behavior
const ArrowSyncIcon = bundleIcon(ArrowSync20Filled, ArrowSync20Regular);

export interface IUpdateRelatedButtonAppProps {
    buttonLabel: string;
    sourceEntityId: string;
    sourceEntityType: string;
    mappingProfileId?: string;
    apiBaseUrl: string;
    webApi: ComponentFramework.WebApi;
    onUpdateComplete: (success: boolean, message: string) => void;
}

/**
 * Response from POST /api/v1/field-mappings/push endpoint
 * Per spec.md Push Mappings Response structure
 */
interface PushMappingsResponse {
    success: boolean;
    targetEntity: string;
    totalRecords: number;
    updatedRecords: number;
    failedRecords: number;
    errors: Array<{ recordId: string; error: string }>;
}

type UpdateState = "idle" | "loading" | "success" | "error";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalS,
    },
    buttonContainer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    button: {
        minWidth: "150px",
    },
    resultText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    errorText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorPaletteRedForeground1,
    },
    successText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorPaletteGreenForeground1,
    },
    dialogContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    versionFooter: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4,
        marginTop: tokens.spacingVerticalS,
    },
    configWarning: {
        color: tokens.colorPaletteYellowForeground1,
        fontSize: tokens.fontSizeBase200,
    },
});

// Control version - update in 4 locations per PCF-V9-PACKAGING.md:
// 1. ControlManifest.Input.xml
// 2. Here (UI footer)
// 3. Solution.xml
// 4. Controls/.../ControlManifest.xml (after build)
const CONTROL_VERSION = "1.0.0";
const BUILD_DATE = "2026-02-01";

export const UpdateRelatedButtonApp: React.FC<IUpdateRelatedButtonAppProps> = (props) => {
    const {
        buttonLabel,
        sourceEntityId,
        sourceEntityType,
        mappingProfileId,
        apiBaseUrl,
        onUpdateComplete,
    } = props;

    const styles = useStyles();
    const toasterId = useId("toaster");
    const { dispatchToast } = useToastController(toasterId);

    const [state, setState] = React.useState<UpdateState>("idle");
    const [isDialogOpen, setIsDialogOpen] = React.useState(false);
    const [lastResult, setLastResult] = React.useState<PushMappingsResponse | null>(null);
    const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

    // Check if control is properly configured
    const isConfigured = React.useMemo(() => {
        return Boolean(sourceEntityId) && Boolean(sourceEntityType) && Boolean(apiBaseUrl);
    }, [sourceEntityId, sourceEntityType, apiBaseUrl]);

    /**
     * Execute the push operation by calling the BFF API
     */
    const executePush = React.useCallback(async () => {
        if (!isConfigured) {
            setErrorMessage("Control is not properly configured");
            setState("error");
            return;
        }

        setState("loading");
        setErrorMessage(null);
        setLastResult(null);

        try {
            // Build request body per spec.md Push Mappings Request structure
            const requestBody = {
                sourceEntity: sourceEntityType,
                sourceRecordId: sourceEntityId,
                targetEntity: mappingProfileId ? undefined : undefined, // Optional - push to all configured targets
            };

            // Call the BFF API endpoint (Task 054)
            const response = await fetch(`${apiBaseUrl}/api/v1/field-mappings/push`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                },
                body: JSON.stringify(requestBody),
                credentials: "include",
            });

            if (!response.ok) {
                let errorText = `HTTP ${response.status}`;
                try {
                    const errorData = await response.json();
                    errorText = errorData.detail || errorData.title || errorText;
                } catch {
                    errorText = await response.text() || errorText;
                }
                throw new Error(errorText);
            }

            const data: PushMappingsResponse = await response.json();
            setLastResult(data);

            if (data.success && data.failedRecords === 0) {
                setState("success");
                showSuccessToast(data);
                onUpdateComplete(true, `Updated ${data.updatedRecords} of ${data.totalRecords} records`);
            } else if (data.success && data.failedRecords > 0) {
                // Partial success
                setState("success");
                showPartialSuccessToast(data);
                onUpdateComplete(true, `Updated ${data.updatedRecords} of ${data.totalRecords} records (${data.failedRecords} failed)`);
            } else {
                setState("error");
                const errorMsg = data.errors && data.errors.length > 0
                    ? data.errors[0].error
                    : "Push operation failed";
                setErrorMessage(errorMsg);
                showErrorToast(errorMsg);
                onUpdateComplete(false, errorMsg);
            }

            // Auto-reset to idle after a delay
            setTimeout(() => {
                setState("idle");
            }, 5000);

        } catch (error) {
            const errorMsg = error instanceof Error ? error.message : "Unknown error occurred";
            setErrorMessage(errorMsg);
            setState("error");
            showErrorToast(errorMsg);
            onUpdateComplete(false, errorMsg);
        }
    }, [sourceEntityId, sourceEntityType, mappingProfileId, apiBaseUrl, onUpdateComplete, isConfigured]);

    /**
     * Handle button click - open confirmation dialog
     */
    const handleButtonClick = React.useCallback(() => {
        if (!isConfigured) {
            setErrorMessage("Source entity ID, type, and API base URL must be configured");
            setState("error");
            return;
        }
        setIsDialogOpen(true);
    }, [isConfigured]);

    /**
     * Handle confirmation - execute push operation
     */
    const handleConfirm = React.useCallback(() => {
        setIsDialogOpen(false);
        executePush();
    }, [executePush]);

    /**
     * Handle cancel - close dialog
     */
    const handleCancel = React.useCallback(() => {
        setIsDialogOpen(false);
    }, []);

    /**
     * Show success toast notification
     */
    const showSuccessToast = React.useCallback((result: PushMappingsResponse) => {
        dispatchToast(
            <Toast>
                <ToastTitle>Update Complete</ToastTitle>
                <ToastBody>
                    Successfully updated {result.updatedRecords} of {result.totalRecords} {result.targetEntity || "related"} records.
                </ToastBody>
            </Toast>,
            { intent: "success", timeout: 5000 }
        );
    }, [dispatchToast]);

    /**
     * Show partial success toast notification
     */
    const showPartialSuccessToast = React.useCallback((result: PushMappingsResponse) => {
        dispatchToast(
            <Toast>
                <ToastTitle>Update Partially Complete</ToastTitle>
                <ToastBody>
                    Updated {result.updatedRecords} of {result.totalRecords} records.
                    {result.failedRecords} record(s) failed.
                </ToastBody>
            </Toast>,
            { intent: "warning", timeout: 8000 }
        );
    }, [dispatchToast]);

    /**
     * Show error toast notification
     */
    const showErrorToast = React.useCallback((message: string) => {
        dispatchToast(
            <Toast>
                <ToastTitle>Update Failed</ToastTitle>
                <ToastBody>{message}</ToastBody>
            </Toast>,
            { intent: "error", timeout: 8000 }
        );
    }, [dispatchToast]);

    /**
     * Get the entity display name for the dialog
     */
    const getEntityDisplayName = React.useCallback((): string => {
        // Map common entity logical names to display names
        const entityDisplayNames: Record<string, string> = {
            "sprk_matter": "Matter",
            "sprk_project": "Project",
            "sprk_invoice": "Invoice",
            "sprk_analysis": "Analysis",
            "account": "Account",
            "contact": "Contact",
            "sprk_workassignment": "Work Assignment",
            "sprk_budget": "Budget",
            "sprk_event": "Event",
        };
        return entityDisplayNames[sourceEntityType] || sourceEntityType;
    }, [sourceEntityType]);

    const isDisabled = state === "loading" || !isConfigured;

    return (
        <div className={styles.container}>
            <Toaster toasterId={toasterId} />

            <div className={styles.buttonContainer}>
                <Button
                    className={styles.button}
                    appearance="primary"
                    icon={state === "loading" ? <Spinner size="tiny" /> : <ArrowSyncIcon />}
                    disabled={isDisabled}
                    onClick={handleButtonClick}
                >
                    {state === "loading" ? "Updating..." : buttonLabel}
                </Button>

                {/* Inline result text */}
                {state === "success" && lastResult && (
                    <Text className={styles.successText}>
                        Updated {lastResult.updatedRecords} of {lastResult.totalRecords} records
                    </Text>
                )}
                {state === "error" && errorMessage && (
                    <Text className={styles.errorText}>
                        {errorMessage}
                    </Text>
                )}
            </div>

            {/* Configuration warning */}
            {!isConfigured && state === "idle" && (
                <Text className={styles.configWarning}>
                    Configuration required: Source entity ID, type, and API URL must be set.
                </Text>
            )}

            {/* Confirmation Dialog */}
            <Dialog open={isDialogOpen} onOpenChange={(_, data) => setIsDialogOpen(data.open)}>
                <DialogSurface>
                    <DialogBody>
                        <DialogTitle>Update Related Records</DialogTitle>
                        <DialogContent className={styles.dialogContent}>
                            <Text>
                                This will update all related records with the current values from this {getEntityDisplayName()}.
                            </Text>
                            <Text>
                                Field mappings configured for this entity type will be applied to all child records.
                            </Text>
                            <Text>
                                <strong>This action cannot be undone.</strong> Are you sure you want to continue?
                            </Text>
                        </DialogContent>
                        <DialogActions>
                            <DialogTrigger disableButtonEnhancement>
                                <Button appearance="secondary" onClick={handleCancel}>
                                    Cancel
                                </Button>
                            </DialogTrigger>
                            <Button appearance="primary" onClick={handleConfirm}>
                                Update Records
                            </Button>
                        </DialogActions>
                    </DialogBody>
                </DialogSurface>
            </Dialog>

            {/* Version footer - MANDATORY per PCF CLAUDE.md */}
            <Text className={styles.versionFooter}>
                v{CONTROL_VERSION} - Built {BUILD_DATE}
            </Text>
        </div>
    );
};
