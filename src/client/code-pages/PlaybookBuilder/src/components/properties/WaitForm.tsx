/**
 * WaitForm - Configuration form for Wait nodes.
 *
 * Allows users to configure wait/pause behavior:
 * - Wait type: Duration, Until Date, Until Condition
 * - Duration minutes (shown when waitType = Duration)
 * - Until date (shown when waitType = Until Date)
 * - Condition expression (shown when waitType = Until Condition)
 *
 * Conditional rendering based on waitType selection.
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useCallback, useMemo, memo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Input,
    Label,
    Dropdown,
    Option,
    SpinButton,
} from "@fluentui/react-components";
import type {
    DropdownProps,
    OptionOnSelectData,
    SelectionEvents,
    SpinButtonChangeEvent,
    SpinButtonOnChangeData,
} from "@fluentui/react-components";
import type { NodeFormProps } from "../../types/forms";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    form: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    field: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    fieldHint: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
    },
    conditionalSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusMedium,
        padding: tokens.spacingVerticalS,
    },
});

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

const WAIT_TYPES = ["Duration", "Until Date", "Until Condition"] as const;
type WaitType = (typeof WAIT_TYPES)[number];

interface WaitConfig {
    waitType: WaitType;
    durationMinutes: number;
    untilDate: string;
    conditionExpression: string;
}

const DEFAULT_CONFIG: WaitConfig = {
    waitType: "Duration",
    durationMinutes: 60,
    untilDate: "",
    conditionExpression: "",
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseConfig(json: string): WaitConfig {
    try {
        const parsed = JSON.parse(json) as Partial<WaitConfig>;
        return {
            waitType: WAIT_TYPES.includes(parsed.waitType as WaitType)
                ? (parsed.waitType as WaitType)
                : DEFAULT_CONFIG.waitType,
            durationMinutes: typeof parsed.durationMinutes === "number"
                && parsed.durationMinutes >= 1
                ? parsed.durationMinutes
                : DEFAULT_CONFIG.durationMinutes,
            untilDate: typeof parsed.untilDate === "string"
                ? parsed.untilDate
                : DEFAULT_CONFIG.untilDate,
            conditionExpression: typeof parsed.conditionExpression === "string"
                ? parsed.conditionExpression
                : DEFAULT_CONFIG.conditionExpression,
        };
    } catch {
        return { ...DEFAULT_CONFIG };
    }
}

function serializeConfig(config: WaitConfig): string {
    return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const WaitForm = memo(function WaitForm({
    nodeId,
    configJson,
    onConfigChange,
}: NodeFormProps) {
    const styles = useStyles();
    const config = useMemo(() => parseConfig(configJson), [configJson]);

    const update = useCallback(
        (patch: Partial<WaitConfig>) => {
            onConfigChange(serializeConfig({ ...config, ...patch }));
        },
        [config, onConfigChange],
    );

    // -- Handlers --

    const handleWaitTypeChange: DropdownProps["onOptionSelect"] = useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            if (data.optionValue) {
                update({ waitType: data.optionValue as WaitType });
            }
        },
        [update],
    );

    const handleDurationChange = useCallback(
        (_e: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
            if (data.value !== undefined && data.value !== null) {
                update({ durationMinutes: data.value });
            }
        },
        [update],
    );

    const handleUntilDateChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ untilDate: e.target.value });
        },
        [update],
    );

    const handleConditionChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ conditionExpression: e.target.value });
        },
        [update],
    );

    // -- Render --

    return (
        <div className={styles.form}>
            {/* Wait Type */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-waitType`} size="small" required>
                    Wait Type
                </Label>
                <Dropdown
                    id={`${nodeId}-waitType`}
                    size="small"
                    value={config.waitType}
                    selectedOptions={[config.waitType]}
                    onOptionSelect={handleWaitTypeChange}
                >
                    {WAIT_TYPES.map((wt) => (
                        <Option key={wt} value={wt}>
                            {wt}
                        </Option>
                    ))}
                </Dropdown>
                <Text className={styles.fieldHint}>
                    How the node should pause execution
                </Text>
            </div>

            {/* Conditional fields based on waitType */}
            <div className={styles.conditionalSection}>
                {config.waitType === "Duration" && (
                    <div className={styles.field}>
                        <Label htmlFor={`${nodeId}-durationMinutes`} size="small" required>
                            Duration (Minutes)
                        </Label>
                        <SpinButton
                            id={`${nodeId}-durationMinutes`}
                            size="small"
                            value={config.durationMinutes}
                            onChange={handleDurationChange}
                            min={1}
                            max={525600}
                            step={15}
                        />
                        <Text className={styles.fieldHint}>
                            Wait for a fixed number of minutes (1 to 525,600 = 1 year)
                        </Text>
                    </div>
                )}

                {config.waitType === "Until Date" && (
                    <div className={styles.field}>
                        <Label htmlFor={`${nodeId}-untilDate`} size="small" required>
                            Until Date
                        </Label>
                        <Input
                            id={`${nodeId}-untilDate`}
                            size="small"
                            type="datetime-local"
                            value={config.untilDate}
                            onChange={handleUntilDateChange}
                        />
                        <Text className={styles.fieldHint}>
                            Wait until the specified date and time. Also supports template
                            variables: {"{{nodeName.output.fieldName}}"}
                        </Text>
                    </div>
                )}

                {config.waitType === "Until Condition" && (
                    <div className={styles.field}>
                        <Label htmlFor={`${nodeId}-conditionExpression`} size="small" required>
                            Condition Expression
                        </Label>
                        <Input
                            id={`${nodeId}-conditionExpression`}
                            size="small"
                            value={config.conditionExpression}
                            onChange={handleConditionChange}
                            placeholder="e.g., {{approval.output.status}} == 'approved'"
                        />
                        <Text className={styles.fieldHint}>
                            Wait until this condition evaluates to true. Supports template
                            variables: {"{{nodeName.output.fieldName}}"}
                        </Text>
                    </div>
                )}
            </div>
        </div>
    );
});
