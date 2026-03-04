/**
 * PromptSchemaEditor - Level 2 JSON editor for JSON Prompt Schema (JPS).
 *
 * Provides advanced users with direct JSON editing and prompt preview:
 * - Edit tab: Textarea-based JSON editor with validation feedback
 * - Preview tab: Read-only rendered prompt text showing what the AI will receive
 * - Footer: Switch back to the form-based editor (Level 1)
 *
 * Lossless toggle: Parsing JSON on switch to form preserves all data.
 * No heavy editor libraries (Monaco, CodeMirror) to keep bundle small (ADR-026).
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see ADR-026 - Single-file build; keep bundle small
 */

import { useState, useCallback, useMemo, memo } from "react";
import {
    makeStyles,
    tokens,
    TabList,
    Tab,
    Textarea,
    Button,
    MessageBar,
    MessageBarBody,
    Text,
    Divider,
} from "@fluentui/react-components";
import type { SelectTabData, SelectTabEvent } from "@fluentui/react-components";
import { ArrowSwap20Regular } from "@fluentui/react-icons";
import type { PromptSchema } from "../../types/promptSchema";
import { validatePromptSchema } from "../../types/promptSchema";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        height: "100%",
    },
    tabContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        flexGrow: 1,
        minHeight: 0,
    },
    editorTextarea: {
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200,
        minHeight: "320px",
        "& textarea": {
            fontFamily: tokens.fontFamilyMonospace,
            fontSize: tokens.fontSizeBase200,
            lineHeight: tokens.lineHeightBase200,
        },
    },
    errorList: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    previewBlock: {
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase300,
        whiteSpace: "pre-wrap",
        wordBreak: "break-word",
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground1,
        borderRadius: tokens.borderRadiusMedium,
        padding: tokens.spacingVerticalM,
        minHeight: "320px",
        maxHeight: "500px",
        overflowY: "auto",
        border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    },
    previewEmpty: {
        color: tokens.colorNeutralForeground3,
        fontStyle: "italic",
    },
    footer: {
        display: "flex",
        justifyContent: "flex-start",
        paddingTop: tokens.spacingVerticalS,
    },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface PromptSchemaEditorProps {
    /** The current prompt schema (null if none configured yet). */
    schema: PromptSchema | null;
    /** Called when the schema is updated from the JSON editor. */
    onChange: (schema: PromptSchema) => void;
    /** Switch back to Level 1 form-based editor. */
    onSwitchToForm: () => void;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

type EditorTab = "edit" | "preview";

/**
 * Renders a simplified client-side preview of the assembled prompt.
 * Shows the prompt sections the AI would receive at execution time.
 */
function renderPromptPreview(schema: PromptSchema | null): string {
    if (!schema) {
        return "(No schema configured)";
    }

    const lines: string[] = [];

    // Role line
    if (schema.instruction.role) {
        lines.push(`[Role] ${schema.instruction.role}`);
        lines.push("");
    }

    // Task description
    if (schema.instruction.task) {
        lines.push(schema.instruction.task);
        lines.push("");
    }

    // Constraints
    if (schema.instruction.constraints && schema.instruction.constraints.length > 0) {
        lines.push("## Constraints");
        schema.instruction.constraints.forEach((constraint, index) => {
            lines.push(`${index + 1}. ${constraint}`);
        });
        lines.push("");
    }

    // Context
    if (schema.instruction.context) {
        lines.push("## Context");
        lines.push(schema.instruction.context);
        lines.push("");
    }

    // Output format
    if (schema.output?.fields && schema.output.fields.length > 0) {
        lines.push("## Output Format");
        lines.push("");
        lines.push("Return a JSON object with the following fields:");
        lines.push("");
        for (const field of schema.output.fields) {
            const descPart = field.description ? ` - ${field.description}` : "";
            const typePart = field.type ? ` (${field.type})` : "";
            const enumPart = field.enum && field.enum.length > 0
                ? ` [values: ${field.enum.join(", ")}]`
                : "";
            const choicesPart = field.$choices ? ` [choices from: ${field.$choices}]` : "";
            lines.push(`  - ${field.name}${typePart}${descPart}${enumPart}${choicesPart}`);
        }
        lines.push("");

        if (schema.output.structuredOutput) {
            lines.push("[Structured Output: enabled - response will use JSON Schema constrained decoding]");
            lines.push("");
        }
    }

    // Examples
    if (schema.examples && schema.examples.length > 0) {
        lines.push("## Examples");
        lines.push("");
        for (let i = 0; i < schema.examples.length; i++) {
            const example = schema.examples[i];
            lines.push(`Example ${i + 1}:`);
            lines.push(`  Input: ${example.input}`);
            lines.push(`  Output: ${JSON.stringify(example.output, null, 2)}`);
            lines.push("");
        }
    }

    // Input configuration
    if (schema.input) {
        if (schema.input.document) {
            lines.push("## Input");
            const doc = schema.input.document;
            const parts: string[] = [];
            if (doc.required) parts.push("required");
            if (doc.maxLength) parts.push(`maxLength: ${doc.maxLength}`);
            if (doc.placeholder) parts.push(`placeholder: "${doc.placeholder}"`);
            lines.push(`  Document: ${parts.length > 0 ? parts.join(", ") : "configured"}`);
            lines.push("");
        }

        if (schema.input.priorOutputs && schema.input.priorOutputs.length > 0) {
            if (!schema.input.document) {
                lines.push("## Input");
            }
            lines.push("  Prior Outputs:");
            for (const ref of schema.input.priorOutputs) {
                const desc = ref.description ? ` - ${ref.description}` : "";
                lines.push(`    - {{${ref.variable}}} [${ref.fields.join(", ")}]${desc}`);
            }
            lines.push("");
        }
    }

    // Scopes
    if (schema.scopes) {
        if (schema.scopes.$skills && schema.scopes.$skills.length > 0) {
            lines.push("## Skills");
            for (const skill of schema.scopes.$skills) {
                if (typeof skill === "string") {
                    lines.push(`  - ${skill}`);
                } else {
                    lines.push(`  - $ref: ${skill.$ref}`);
                }
            }
            lines.push("");
        }

        if (schema.scopes.$knowledge && schema.scopes.$knowledge.length > 0) {
            lines.push("## Knowledge");
            for (const k of schema.scopes.$knowledge) {
                if (k.inline) {
                    const label = k.as ?? "Inline";
                    lines.push(`  [${label}] ${k.inline.substring(0, 100)}${k.inline.length > 100 ? "..." : ""}`);
                } else if (k.$ref) {
                    const label = k.as ?? k.$ref;
                    lines.push(`  - $ref: ${label}`);
                }
            }
            lines.push("");
        }
    }

    return lines.join("\n").trimEnd() || "(Empty schema - add an instruction task to get started)";
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PromptSchemaEditor = memo(function PromptSchemaEditor({
    schema,
    onChange,
    onSwitchToForm,
}: PromptSchemaEditorProps) {
    const styles = useStyles();

    // Local editor state — holds the raw JSON text so partial/invalid edits
    // don't immediately propagate to the parent. Parent onChange is only called
    // when the JSON is valid and passes JPS validation.
    const [jsonText, setJsonText] = useState<string>(() =>
        schema ? JSON.stringify(schema, null, 2) : JSON.stringify({
            $schema: "https://spaarke.com/schemas/jps/v1",
            $version: 1,
            instruction: { task: "" },
        }, null, 2),
    );
    const [selectedTab, setSelectedTab] = useState<EditorTab>("edit");
    const [validationErrors, setValidationErrors] = useState<string[]>([]);

    // Parse the current JSON text for the preview tab
    const parsedSchema = useMemo<PromptSchema | null>(() => {
        try {
            return JSON.parse(jsonText) as PromptSchema;
        } catch {
            return schema;
        }
    }, [jsonText, schema]);

    const previewText = useMemo(
        () => renderPromptPreview(parsedSchema),
        [parsedSchema],
    );

    // -- Handlers --

    const handleTabSelect = useCallback(
        (_event: SelectTabEvent, data: SelectTabData) => {
            setSelectedTab(data.value as EditorTab);
        },
        [],
    );

    const handleJsonChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            const text = e.target.value;
            setJsonText(text);

            // Attempt to parse and validate
            const errors: string[] = [];

            let parsed: unknown;
            try {
                parsed = JSON.parse(text);
            } catch (parseError) {
                const message = parseError instanceof Error
                    ? parseError.message
                    : "Invalid JSON";
                errors.push(`JSON parse error: ${message}`);
                setValidationErrors(errors);
                return;
            }

            // Run JPS validation
            const result = validatePromptSchema(parsed);
            if (!result.valid) {
                setValidationErrors(result.errors);
                return;
            }

            // Valid — clear errors and notify parent
            setValidationErrors([]);
            onChange(parsed as PromptSchema);
        },
        [onChange],
    );

    // -- Render --

    return (
        <div className={styles.root}>
            <TabList
                selectedValue={selectedTab}
                onTabSelect={handleTabSelect}
                size="small"
            >
                <Tab value="edit">Edit</Tab>
                <Tab value="preview">Preview</Tab>
            </TabList>

            <div className={styles.tabContent}>
                {selectedTab === "edit" && (
                    <>
                        <Textarea
                            className={styles.editorTextarea}
                            value={jsonText}
                            onChange={handleJsonChange}
                            resize="vertical"
                            rows={20}
                            placeholder='{\n  "$schema": "https://spaarke.com/schemas/jps/v1",\n  "instruction": {\n    "task": "Describe what the AI should do..."\n  }\n}'
                        />

                        {validationErrors.length > 0 && (
                            <div className={styles.errorList}>
                                {validationErrors.map((error, index) => (
                                    <MessageBar
                                        key={index}
                                        intent="error"
                                    >
                                        <MessageBarBody>{error}</MessageBarBody>
                                    </MessageBar>
                                ))}
                            </div>
                        )}

                        {validationErrors.length === 0 && jsonText.trim() !== "" && (
                            <MessageBar intent="success">
                                <MessageBarBody>Valid JPS schema</MessageBarBody>
                            </MessageBar>
                        )}
                    </>
                )}

                {selectedTab === "preview" && (
                    <>
                        <Text size={200} weight="regular">
                            Rendered prompt preview (as the AI will receive it):
                        </Text>
                        <div className={styles.previewBlock}>
                            {previewText || (
                                <span className={styles.previewEmpty}>
                                    No preview available
                                </span>
                            )}
                        </div>
                    </>
                )}
            </div>

            <Divider />

            <div className={styles.footer}>
                <Button
                    appearance="subtle"
                    size="small"
                    icon={<ArrowSwap20Regular />}
                    onClick={onSwitchToForm}
                >
                    Switch to Form View
                </Button>
            </div>
        </div>
    );
});
