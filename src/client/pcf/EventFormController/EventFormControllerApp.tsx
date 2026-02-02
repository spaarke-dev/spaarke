/**
 * EventFormController React App Component
 *
 * Manages Event form field visibility based on Event Type configuration.
 * Uses FieldVisibilityHandler for form manipulation.
 */

import * as React from "react";
import {
    Text,
    Spinner,
    Badge,
    makeStyles,
    tokens,
    MessageBar,
    MessageBarBody,
    Tooltip
} from "@fluentui/react-components";
import { Checkmark16Regular, Dismiss16Regular, Info16Regular } from "@fluentui/react-icons";
import { IInputs } from "./generated/ManifestTypes";
import {
    applyFieldRules as applyVisibilityRules,
    resetToDefaults,
    isFormContextAvailable,
    EventTypeFieldConfig,
    ApplyRulesResult,
    IFieldRule,
    DEFAULT_FIELD_STATES
} from "./handlers/FieldVisibilityHandler";
import {
    registerSaveHandler,
    unregisterSaveHandler,
    clearValidationNotification
} from "./handlers/SaveValidationHandler";

interface EventFormControllerAppProps {
    context: ComponentFramework.Context<IInputs>;
    eventTypeId: string;
    eventTypeName: string;
    onStatusChange: (status: string) => void;
    version: string;
}

interface EventTypeConfig extends EventTypeFieldConfig {
    // Extended to include optionalFields from base type
}

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingHorizontalS,
        fontSize: tokens.fontSizeBase200
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS
    },
    badge: {
        marginLeft: tokens.spacingHorizontalXS
    },
    fieldList: {
        display: "flex",
        flexWrap: "wrap",
        gap: tokens.spacingHorizontalXS
    },
    footer: {
        marginTop: tokens.spacingVerticalS,
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center"
    },
    versionText: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4
    }
});

export const EventFormControllerApp: React.FC<EventFormControllerAppProps> = ({
    context,
    eventTypeId,
    eventTypeName,
    onStatusChange,
    version
}) => {
    const styles = useStyles();

    const [isLoading, setIsLoading] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);
    const [config, setConfig] = React.useState<EventTypeConfig | null>(null);
    const [appliedRules, setAppliedRules] = React.useState<number>(0);
    const [skippedFields, setSkippedFields] = React.useState<string[]>([]);
    const [wasReset, setWasReset] = React.useState(false);

    // Track previous event type to detect clear action
    const previousEventTypeIdRef = React.useRef<string>("");

    /**
     * Builds IFieldRule array for save validation based on Event Type config
     * Uses DEFAULT_FIELD_STATES as the base for all known fields
     */
    const buildFieldRules = React.useCallback((eventConfig: EventTypeConfig): IFieldRule[] => {
        const rules: IFieldRule[] = [];
        const allFields = Object.keys(DEFAULT_FIELD_STATES);

        for (const fieldName of allFields) {
            const isRequired = eventConfig.requiredFields.includes(fieldName);
            const isHidden = eventConfig.hiddenFields.includes(fieldName);

            rules.push({
                fieldName,
                displayName: undefined, // Will be resolved from form control at validation time
                isVisible: !isHidden,
                isRequired: isRequired && !isHidden // Can't require hidden fields
            });
        }

        return rules;
    }, []);

    // Fetch Event Type configuration when eventTypeId changes
    React.useEffect(() => {
        const previousEventTypeId = previousEventTypeIdRef.current;

        // Detect if Event Type was cleared (had a value, now empty)
        if (!eventTypeId && previousEventTypeId) {
            console.log("[EventFormController] Event Type cleared - resetting to defaults");
            const resetResult = resetToDefaults();
            setConfig(null);
            setAppliedRules(resetResult.rulesApplied);
            setSkippedFields(resetResult.skippedFields);
            setWasReset(true);
            onStatusChange("reset-to-defaults");
            previousEventTypeIdRef.current = "";

            // Unregister save handler and clear notifications when Event Type cleared
            unregisterSaveHandler();
            clearValidationNotification();
            return;
        }

        if (!eventTypeId) {
            setConfig(null);
            setAppliedRules(0);
            setSkippedFields([]);
            setWasReset(false);
            onStatusChange("no-event-type");

            // No Event Type selected - unregister save handler
            unregisterSaveHandler();
            return;
        }

        // Update previous event type for next comparison
        previousEventTypeIdRef.current = eventTypeId;
        setWasReset(false);

        const fetchEventTypeConfig = async () => {
            setIsLoading(true);
            setError(null);
            onStatusChange("loading");

            try {
                // STUB: [API] - S003: Assumes sprk_eventtype entity exists with exact schema (Task 003 - done)
                // Fields: sprk_requiredfields (comma-separated), sprk_hiddenfields (comma-separated)
                const result = await context.webAPI.retrieveRecord(
                    "sprk_eventtype",
                    eventTypeId,
                    "?$select=sprk_name,sprk_requiredfields,sprk_hiddenfields"
                );

                const requiredFieldsRaw = result.sprk_requiredfields || "";
                const hiddenFieldsRaw = result.sprk_hiddenfields || "";

                const eventConfig: EventTypeConfig = {
                    requiredFields: requiredFieldsRaw
                        ? requiredFieldsRaw.split(',').map((f: string) => f.trim()).filter(Boolean)
                        : [],
                    hiddenFields: hiddenFieldsRaw
                        ? hiddenFieldsRaw.split(',').map((f: string) => f.trim()).filter(Boolean)
                        : [],
                    optionalFields: [] // Could be extended in future
                };

                setConfig(eventConfig);

                // Apply field visibility rules via FieldVisibilityHandler
                const applyResult = applyVisibilityRules(eventConfig);
                setAppliedRules(applyResult.rulesApplied);
                setSkippedFields(applyResult.skippedFields);

                // Register save validation handler with field rules
                const fieldRules = buildFieldRules(eventConfig);
                registerSaveHandler(fieldRules);
                console.log(`[EventFormController] Registered save handler with ${fieldRules.filter(r => r.isRequired).length} required fields`);

                if (applyResult.success) {
                    onStatusChange(`applied-${applyResult.rulesApplied}-rules`);
                } else {
                    console.warn("[EventFormController] Some rules failed:", applyResult.errors);
                    onStatusChange(`partial-${applyResult.rulesApplied}-rules`);
                }

            } catch (err) {
                console.error("[EventFormController] Error fetching config:", err);
                setError(err instanceof Error ? err.message : "Failed to load Event Type configuration");
                onStatusChange("error");
            } finally {
                setIsLoading(false);
            }
        };

        fetchEventTypeConfig();
    }, [eventTypeId, buildFieldRules]);

    // Cleanup: unregister save handler on unmount
    React.useEffect(() => {
        return () => {
            unregisterSaveHandler();
        };
    }, []);

    if (isLoading) {
        return (
            <div className={styles.container}>
                <Spinner size="tiny" label="Loading Event Type..." />
            </div>
        );
    }

    if (error) {
        return (
            <div className={styles.container}>
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
            </div>
        );
    }

    // Show message when Event Type cleared and fields reset
    if (!eventTypeId && wasReset) {
        return (
            <div className={styles.container}>
                <div className={styles.header}>
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        Fields reset to defaults
                    </Text>
                    <Badge
                        className={styles.badge}
                        appearance="filled"
                        color="informative"
                        icon={<Checkmark16Regular />}
                    >
                        {appliedRules} fields reset
                    </Badge>
                </div>
                <div className={styles.footer}>
                    <Text className={styles.versionText}>v{version}</Text>
                </div>
            </div>
        );
    }

    if (!eventTypeId) {
        return (
            <div className={styles.container}>
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    Select an Event Type to configure form fields
                </Text>
                <div className={styles.footer}>
                    <Text className={styles.versionText}>v{version}</Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <div className={styles.header}>
                <Text weight="semibold" size={200}>
                    {eventTypeName || "Event Type"}
                </Text>
                <Badge
                    className={styles.badge}
                    appearance="filled"
                    color={appliedRules > 0 ? "success" : "informative"}
                    icon={appliedRules > 0 ? <Checkmark16Regular /> : <Dismiss16Regular />}
                >
                    {appliedRules} rules applied
                </Badge>
                {skippedFields.length > 0 && (
                    <Tooltip
                        content={`Fields not on form: ${skippedFields.join(", ")}`}
                        relationship="label"
                    >
                        <Badge
                            className={styles.badge}
                            appearance="outline"
                            color="warning"
                            icon={<Info16Regular />}
                        >
                            {skippedFields.length} skipped
                        </Badge>
                    </Tooltip>
                )}
            </div>

            {config && (
                <>
                    {config.requiredFields.length > 0 && (
                        <div>
                            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                                Required: {config.requiredFields.join(", ")}
                            </Text>
                        </div>
                    )}
                    {config.hiddenFields.length > 0 && (
                        <div>
                            <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                                Hidden: {config.hiddenFields.join(", ")}
                            </Text>
                        </div>
                    )}
                </>
            )}

            <div className={styles.footer}>
                <Text className={styles.versionText}>v{version}</Text>
            </div>
        </div>
    );
};
