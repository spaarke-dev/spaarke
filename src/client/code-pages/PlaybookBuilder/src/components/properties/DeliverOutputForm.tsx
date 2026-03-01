/**
 * DeliverOutputForm - Configuration form for Deliver Output nodes.
 *
 * Allows users to configure how a playbook delivers its output:
 * - Output format (Markdown, HTML, Plain Text, JSON)
 * - Template content with variable support ({{nodeName.output.fieldName}})
 * - Target Dataverse field to write output to
 * - Include/exclude metadata toggle
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
    Switch,
    shorthands,
} from "@fluentui/react-components";
import type {
    DropdownProps,
    OptionOnSelectData,
    SelectionEvents,
    SwitchOnChangeData,
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
    templateArea: {
        minHeight: "120px",
    },
    switchRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
});

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

const OUTPUT_FORMATS = ["Markdown", "HTML", "Plain Text", "JSON"] as const;
type OutputFormat = (typeof OUTPUT_FORMATS)[number];

interface DeliverOutputConfig {
    outputFormat: OutputFormat;
    templateContent: string;
    targetField: string;
    includeMetadata: boolean;
}

const DEFAULT_CONFIG: DeliverOutputConfig = {
    outputFormat: "Markdown",
    templateContent: "",
    targetField: "",
    includeMetadata: false,
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseConfig(json: string): DeliverOutputConfig {
    try {
        const parsed = JSON.parse(json) as Partial<DeliverOutputConfig>;
        return {
            outputFormat: OUTPUT_FORMATS.includes(parsed.outputFormat as OutputFormat)
                ? (parsed.outputFormat as OutputFormat)
                : DEFAULT_CONFIG.outputFormat,
            templateContent: typeof parsed.templateContent === "string"
                ? parsed.templateContent
                : DEFAULT_CONFIG.templateContent,
            targetField: typeof parsed.targetField === "string"
                ? parsed.targetField
                : DEFAULT_CONFIG.targetField,
            includeMetadata: typeof parsed.includeMetadata === "boolean"
                ? parsed.includeMetadata
                : DEFAULT_CONFIG.includeMetadata,
        };
    } catch {
        return { ...DEFAULT_CONFIG };
    }
}

function serializeConfig(config: DeliverOutputConfig): string {
    return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DeliverOutputForm = memo(function DeliverOutputForm({
    nodeId,
    configJson,
    onConfigChange,
}: NodeFormProps) {
    const styles = useStyles();
    const config = useMemo(() => parseConfig(configJson), [configJson]);

    const update = useCallback(
        (patch: Partial<DeliverOutputConfig>) => {
            onConfigChange(serializeConfig({ ...config, ...patch }));
        },
        [config, onConfigChange],
    );

    // -- Handlers --

    const handleFormatChange: DropdownProps["onOptionSelect"] = useCallback(
        (_event: SelectionEvents, data: OptionOnSelectData) => {
            if (data.optionValue) {
                update({ outputFormat: data.optionValue as OutputFormat });
            }
        },
        [update],
    );

    const handleTemplateChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            update({ templateContent: e.target.value });
        },
        [update],
    );

    const handleTargetFieldChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            update({ targetField: e.target.value });
        },
        [update],
    );

    const handleMetadataToggle = useCallback(
        (_e: React.ChangeEvent<HTMLInputElement>, data: SwitchOnChangeData) => {
            update({ includeMetadata: data.checked });
        },
        [update],
    );

    // -- Render --

    return (
        <div className={styles.form}>
            {/* Output Format */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-outputFormat`} size="small" required>
                    Output Format
                </Label>
                <Dropdown
                    id={`${nodeId}-outputFormat`}
                    size="small"
                    value={config.outputFormat}
                    selectedOptions={[config.outputFormat]}
                    onOptionSelect={handleFormatChange}
                >
                    {OUTPUT_FORMATS.map((fmt) => (
                        <Option key={fmt} value={fmt}>
                            {fmt}
                        </Option>
                    ))}
                </Dropdown>
                <Text className={styles.fieldHint}>
                    Format used when rendering the output content
                </Text>
            </div>

            {/* Template Content */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-templateContent`} size="small">
                    Template Content
                </Label>
                <Textarea
                    id={`${nodeId}-templateContent`}
                    size="small"
                    className={styles.templateArea}
                    value={config.templateContent}
                    onChange={handleTemplateChange}
                    placeholder={"Use template variables: {{nodeName.output.fieldName}}\n\nExample:\n# Summary\n{{analysis.output.summary}}\n\n## Details\n{{completion.output.result}}"}
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    Supports template variables: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Target Field */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-targetField`} size="small">
                    Target Field
                </Label>
                <Input
                    id={`${nodeId}-targetField`}
                    size="small"
                    value={config.targetField}
                    onChange={handleTargetFieldChange}
                    placeholder="e.g., sprk_workingdocument"
                />
                <Text className={styles.fieldHint}>
                    Dataverse field logical name to write the output to
                </Text>
            </div>

            {/* Include Metadata */}
            <div className={styles.switchRow}>
                <Label htmlFor={`${nodeId}-includeMetadata`} size="small">
                    Include Metadata
                </Label>
                <Switch
                    id={`${nodeId}-includeMetadata`}
                    checked={config.includeMetadata}
                    onChange={handleMetadataToggle}
                />
            </div>
            <Text className={styles.fieldHint}>
                Append execution metadata (timestamps, node IDs) to the output
            </Text>
        </div>
    );
});
