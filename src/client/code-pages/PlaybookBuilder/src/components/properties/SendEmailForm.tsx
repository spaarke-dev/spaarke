/**
 * SendEmailForm - Configuration form for Send Email nodes.
 *
 * Allows users to configure email dispatch settings:
 * - To recipients (supports template variables)
 * - CC recipients (optional, supports template variables)
 * - Subject line (supports template variables)
 * - Body content (supports template variables)
 * - Importance level (Low, Normal, High)
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
} from "@fluentui/react-components";
import type {
    DropdownProps,
    OptionOnSelectData,
    SelectionEvents,
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
    bodyArea: {
        minHeight: "140px",
    },
});

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

const IMPORTANCE_LEVELS = ["Low", "Normal", "High"] as const;
type ImportanceLevel = (typeof IMPORTANCE_LEVELS)[number];

interface SendEmailConfig {
    to: string;
    cc: string;
    subject: string;
    body: string;
    importance: ImportanceLevel;
}

const DEFAULT_CONFIG: SendEmailConfig = {
    to: "",
    cc: "",
    subject: "",
    body: "",
    importance: "Normal",
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseConfig(json: string): SendEmailConfig {
    try {
        const parsed = JSON.parse(json) as Partial<SendEmailConfig>;
        return {
            to: typeof parsed.to === "string" ? parsed.to : DEFAULT_CONFIG.to,
            cc: typeof parsed.cc === "string" ? parsed.cc : DEFAULT_CONFIG.cc,
            subject: typeof parsed.subject === "string"
                ? parsed.subject
                : DEFAULT_CONFIG.subject,
            body: typeof parsed.body === "string" ? parsed.body : DEFAULT_CONFIG.body,
            importance: IMPORTANCE_LEVELS.includes(parsed.importance as ImportanceLevel)
                ? (parsed.importance as ImportanceLevel)
                : DEFAULT_CONFIG.importance,
        };
    } catch {
        return { ...DEFAULT_CONFIG };
    }
}

function serializeConfig(config: SendEmailConfig): string {
    return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SendEmailForm = memo(function SendEmailForm({
    nodeId,
    configJson,
    onConfigChange,
}: NodeFormProps) {
    const styles = useStyles();
    const config = useMemo(() => parseConfig(configJson), [configJson]);

    const update = useCallback(
        (patch: Partial<SendEmailConfig>) => {
            onConfigChange(serializeConfig({ ...config, ...patch }));
        },
        [config, onConfigChange],
    );

    // -- Handlers --

    const handleToChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ to: e.target.value });
        },
        [update],
    );

    const handleCcChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ cc: e.target.value });
        },
        [update],
    );

    const handleSubjectChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ subject: e.target.value });
        },
        [update],
    );

    const handleBodyChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            update({ body: e.target.value });
        },
        [update],
    );

    const handleImportanceChange: DropdownProps["onOptionSelect"] = useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            if (data.optionValue) {
                update({ importance: data.optionValue as ImportanceLevel });
            }
        },
        [update],
    );

    // -- Render --

    return (
        <div className={styles.form}>
            {/* To */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-to`} size="small" required>
                    To
                </Label>
                <Input
                    id={`${nodeId}-to`}
                    size="small"
                    value={config.to}
                    onChange={handleToChange}
                    placeholder="recipient@example.com or {{node.output.email}}"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* CC */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-cc`} size="small">
                    CC
                </Label>
                <Input
                    id={`${nodeId}-cc`}
                    size="small"
                    value={config.cc}
                    onChange={handleCcChange}
                    placeholder="cc@example.com (optional)"
                />
                <Text className={styles.fieldHint}>
                    Optional. Separate multiple addresses with semicolons.
                </Text>
            </div>

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
                    placeholder="e.g., Report: {{analysis.output.title}}"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Body */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-body`} size="small" required>
                    Body
                </Label>
                <Textarea
                    id={`${nodeId}-body`}
                    size="small"
                    className={styles.bodyArea}
                    value={config.body}
                    onChange={handleBodyChange}
                    placeholder={"Hello,\n\nPlease find the results below:\n{{completion.output.result}}\n\nRegards"}
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Importance */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-importance`} size="small">
                    Importance
                </Label>
                <Dropdown
                    id={`${nodeId}-importance`}
                    size="small"
                    value={config.importance}
                    selectedOptions={[config.importance]}
                    onOptionSelect={handleImportanceChange}
                >
                    {IMPORTANCE_LEVELS.map((level) => (
                        <Option key={level} value={level}>
                            {level}
                        </Option>
                    ))}
                </Dropdown>
            </div>
        </div>
    );
});
