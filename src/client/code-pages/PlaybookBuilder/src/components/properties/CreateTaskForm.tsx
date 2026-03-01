/**
 * CreateTaskForm - Configuration form for Create Task nodes.
 *
 * Allows users to configure Dataverse task creation:
 * - Subject (supports template variables)
 * - Description (supports template variables)
 * - Due in days (SpinButton)
 * - Priority (Low, Normal, High)
 * - Assign to (owner reference, supports template variables)
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
    Textarea,
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
    descriptionArea: {
        minHeight: "100px",
    },
});

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

const PRIORITY_LEVELS = ["Low", "Normal", "High"] as const;
type PriorityLevel = (typeof PRIORITY_LEVELS)[number];

interface CreateTaskConfig {
    subject: string;
    description: string;
    dueInDays: number;
    priority: PriorityLevel;
    assignTo: string;
}

const DEFAULT_CONFIG: CreateTaskConfig = {
    subject: "",
    description: "",
    dueInDays: 7,
    priority: "Normal",
    assignTo: "",
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseConfig(json: string): CreateTaskConfig {
    try {
        const parsed = JSON.parse(json) as Partial<CreateTaskConfig>;
        return {
            subject: typeof parsed.subject === "string"
                ? parsed.subject
                : DEFAULT_CONFIG.subject,
            description: typeof parsed.description === "string"
                ? parsed.description
                : DEFAULT_CONFIG.description,
            dueInDays: typeof parsed.dueInDays === "number" && parsed.dueInDays >= 0
                ? parsed.dueInDays
                : DEFAULT_CONFIG.dueInDays,
            priority: PRIORITY_LEVELS.includes(parsed.priority as PriorityLevel)
                ? (parsed.priority as PriorityLevel)
                : DEFAULT_CONFIG.priority,
            assignTo: typeof parsed.assignTo === "string"
                ? parsed.assignTo
                : DEFAULT_CONFIG.assignTo,
        };
    } catch {
        return { ...DEFAULT_CONFIG };
    }
}

function serializeConfig(config: CreateTaskConfig): string {
    return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateTaskForm = memo(function CreateTaskForm({
    nodeId,
    configJson,
    onConfigChange,
}: NodeFormProps) {
    const styles = useStyles();
    const config = useMemo(() => parseConfig(configJson), [configJson]);

    const update = useCallback(
        (patch: Partial<CreateTaskConfig>) => {
            onConfigChange(serializeConfig({ ...config, ...patch }));
        },
        [config, onConfigChange],
    );

    // -- Handlers --

    const handleSubjectChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ subject: e.target.value });
        },
        [update],
    );

    const handleDescriptionChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            update({ description: e.target.value });
        },
        [update],
    );

    const handleDueInDaysChange = useCallback(
        (_e: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
            if (data.value !== undefined && data.value !== null) {
                update({ dueInDays: data.value });
            }
        },
        [update],
    );

    const handlePriorityChange: DropdownProps["onOptionSelect"] = useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            if (data.optionValue) {
                update({ priority: data.optionValue as PriorityLevel });
            }
        },
        [update],
    );

    const handleAssignToChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ assignTo: e.target.value });
        },
        [update],
    );

    // -- Render --

    return (
        <div className={styles.form}>
            {/* Subject */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-subject`} size="small" required>
                    Subject
                </Label>
                <Input
                    id={`${nodeId}-subject`}
                    size="small"
                    value={config.subject}
                    onChange={handleSubjectChange}
                    placeholder="e.g., Follow up: {{analysis.output.title}}"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Description */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-description`} size="small">
                    Description
                </Label>
                <Textarea
                    id={`${nodeId}-description`}
                    size="small"
                    className={styles.descriptionArea}
                    value={config.description}
                    onChange={handleDescriptionChange}
                    placeholder={"Task details...\nCan reference: {{nodeName.output.fieldName}}"}
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Due In Days */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-dueInDays`} size="small">
                    Due In (Days)
                </Label>
                <SpinButton
                    id={`${nodeId}-dueInDays`}
                    size="small"
                    value={config.dueInDays}
                    onChange={handleDueInDaysChange}
                    min={0}
                    max={365}
                    step={1}
                />
                <Text className={styles.fieldHint}>
                    Number of days from execution until the task is due (0-365)
                </Text>
            </div>

            {/* Priority */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-priority`} size="small">
                    Priority
                </Label>
                <Dropdown
                    id={`${nodeId}-priority`}
                    size="small"
                    value={config.priority}
                    selectedOptions={[config.priority]}
                    onOptionSelect={handlePriorityChange}
                >
                    {PRIORITY_LEVELS.map((level) => (
                        <Option key={level} value={level}>
                            {level}
                        </Option>
                    ))}
                </Dropdown>
            </div>

            {/* Assign To */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-assignTo`} size="small">
                    Assign To
                </Label>
                <Input
                    id={`${nodeId}-assignTo`}
                    size="small"
                    value={config.assignTo}
                    onChange={handleAssignToChange}
                    placeholder="e.g., {{trigger.output.ownerId}} or user@example.com"
                />
                <Text className={styles.fieldHint}>
                    Owner reference. Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>
        </div>
    );
});
