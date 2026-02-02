/**
 * useMappingToast - Hook for displaying field mapping result toasts
 *
 * Provides toast notifications for field mapping operations:
 * - Success: "Applied X field mappings from [EntityName]"
 * - Partial success: "Applied X of Y mappings. Some fields could not be mapped."
 * - Error: "Failed to apply field mappings. Please try again."
 *
 * ADR Compliance:
 * - ADR-021: Uses Fluent UI v9 Toast components with dark mode support
 *
 * @see Task 024 - Add toast notifications for mapping results
 */

import * as React from "react";
import {
    Toast,
    ToastTitle,
    ToastBody,
    ToastIntent,
    useToastController,
    useId
} from "@fluentui/react-components";
import { IFieldMappingApplicationResult } from "../handlers/FieldMappingHandler";

/** Toast display duration in milliseconds */
const TOAST_TIMEOUT = 5000;

/**
 * Toast message configuration
 */
interface IMappingToastMessage {
    title: string;
    body: string;
    intent: ToastIntent;
}

/**
 * Result from useMappingToast hook
 */
export interface IUseMappingToastResult {
    /** Unique toaster ID for the Toaster component */
    toasterId: string;
    /** Show toast for a mapping result */
    showMappingResult: (result: IFieldMappingApplicationResult, entityDisplayName: string) => void;
    /** Show success toast */
    showSuccess: (message: string) => void;
    /** Show warning toast */
    showWarning: (message: string) => void;
    /** Show error toast */
    showError: (message: string) => void;
}

/**
 * Hook for displaying field mapping result toasts
 *
 * @example
 * ```tsx
 * const { toasterId, showMappingResult } = useMappingToast();
 *
 * // After applying mappings:
 * showMappingResult(mappingResult, "Matter");
 *
 * // In render:
 * <Toaster toasterId={toasterId} position="top-end" />
 * ```
 */
export function useMappingToast(): IUseMappingToastResult {
    const toasterId = useId("mapping-toaster");
    const { dispatchToast } = useToastController(toasterId);

    /**
     * Determine the appropriate toast message based on mapping result
     */
    const getToastMessage = React.useCallback(
        (result: IFieldMappingApplicationResult, entityDisplayName: string): IMappingToastMessage | null => {
            // Case 1: No profile found - no toast needed
            if (!result.profileFound) {
                return null;
            }

            // Case 2: Complete success - all mappings applied, no errors
            if (result.errors.length === 0 && result.fieldsMapped > 0) {
                return {
                    title: "Fields Updated",
                    body: `Applied ${result.fieldsMapped} field mapping${result.fieldsMapped > 1 ? "s" : ""} from ${entityDisplayName}`,
                    intent: "success"
                };
            }

            // Case 3: Partial success - some mappings applied but with errors/skipped
            if (result.fieldsMapped > 0 && (result.errors.length > 0 || result.rulesSkipped > 0)) {
                const total = result.fieldsMapped + result.rulesSkipped + result.errors.length;
                return {
                    title: "Partial Update",
                    body: `Applied ${result.fieldsMapped} of ${total} mappings. Some fields could not be mapped.`,
                    intent: "warning"
                };
            }

            // Case 4: Complete failure - no mappings applied despite having a profile
            if (result.errors.length > 0 && result.fieldsMapped === 0) {
                const errorMessage = result.errors[0] || "Failed to apply field mappings. Please try again.";
                return {
                    title: "Mapping Failed",
                    body: errorMessage,
                    intent: "error"
                };
            }

            // Case 5: No fields to map (but no errors) - all fields skipped/already current
            if (result.fieldsMapped === 0 && result.errors.length === 0) {
                return {
                    title: "No Updates",
                    body: "No fields needed updating - all values are current.",
                    intent: "info" as ToastIntent
                };
            }

            return null;
        },
        []
    );

    /**
     * Display a toast for a field mapping result
     */
    const showMappingResult = React.useCallback(
        (result: IFieldMappingApplicationResult, entityDisplayName: string): void => {
            const message = getToastMessage(result, entityDisplayName);

            if (!message) {
                return;
            }

            dispatchToast(
                React.createElement(
                    Toast,
                    null,
                    React.createElement(ToastTitle, null, message.title),
                    React.createElement(ToastBody, null, message.body)
                ),
                {
                    intent: message.intent,
                    timeout: TOAST_TIMEOUT
                }
            );
        },
        [dispatchToast, getToastMessage]
    );

    /**
     * Show a success toast with custom message
     */
    const showSuccess = React.useCallback(
        (message: string): void => {
            dispatchToast(
                React.createElement(
                    Toast,
                    null,
                    React.createElement(ToastTitle, null, "Success"),
                    React.createElement(ToastBody, null, message)
                ),
                {
                    intent: "success",
                    timeout: TOAST_TIMEOUT
                }
            );
        },
        [dispatchToast]
    );

    /**
     * Show a warning toast with custom message
     */
    const showWarning = React.useCallback(
        (message: string): void => {
            dispatchToast(
                React.createElement(
                    Toast,
                    null,
                    React.createElement(ToastTitle, null, "Warning"),
                    React.createElement(ToastBody, null, message)
                ),
                {
                    intent: "warning",
                    timeout: TOAST_TIMEOUT
                }
            );
        },
        [dispatchToast]
    );

    /**
     * Show an error toast with custom message
     */
    const showError = React.useCallback(
        (message: string): void => {
            dispatchToast(
                React.createElement(
                    Toast,
                    null,
                    React.createElement(ToastTitle, null, "Error"),
                    React.createElement(ToastBody, null, message)
                ),
                {
                    intent: "error",
                    timeout: TOAST_TIMEOUT
                }
            );
        },
        [dispatchToast]
    );

    return {
        toasterId,
        showMappingResult,
        showSuccess,
        showWarning,
        showError
    };
}
