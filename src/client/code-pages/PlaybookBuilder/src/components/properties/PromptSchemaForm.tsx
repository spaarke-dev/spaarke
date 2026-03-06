/**
 * PromptSchemaForm - Level 1 (Form-Based) editor for JSON Prompt Schema.
 *
 * Provides a structured form interface for editing a PromptSchema without
 * requiring the user to see or write JSON. Fields:
 * - Role (optional textarea)
 * - Task (required textarea with validation)
 * - Constraints (dynamic list of strings with add/remove)
 * - Output Fields (repeating rows: name, type, description with add/remove)
 * - Structured Output (checkbox)
 * - Switch to JSON Editor (button)
 *
 * Serialization is handled by the parent — this form works directly with
 * PromptSchema objects and calls onChange on every field edit.
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 * @see ADR-013 - AI Architecture
 */

import { useCallback, useState, useEffect, useMemo, memo } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Textarea,
    Input,
    Dropdown,
    Option,
    Button,
    Checkbox,
    Field,
    Label,
    Text,
    Badge,
    Caption1,
    Tooltip,
} from "@fluentui/react-components";
import type {
    OptionOnSelectData,
    SelectionEvents,
    CheckboxOnChangeData,
} from "@fluentui/react-components";
import { Add20Regular, Delete20Regular, Code20Regular, Link20Regular, LinkDismiss20Regular } from "@fluentui/react-icons";
import type {
    PromptSchema,
    OutputFieldDefinition,
    OutputFieldType,
} from "../../types/promptSchema";
import { OUTPUT_FIELD_TYPES, createDefaultPromptSchema } from "../../types/promptSchema";
import { useCanvasStore } from "../../stores/canvasStore";
import type { PlaybookNode, PlaybookEdge } from "../../types/canvas";

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
    promptArea: {
        minHeight: "80px",
    },
    constraintRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    constraintInput: {
        flex: 1,
    },
    outputFieldRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        backgroundColor: tokens.colorNeutralBackground3,
    },
    outputFieldName: {
        flex: 1,
        minWidth: "100px",
    },
    outputFieldType: {
        minWidth: "110px",
        maxWidth: "110px",
    },
    outputFieldDescription: {
        flex: 2,
        minWidth: "140px",
    },
    outputFieldList: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    addButton: {
        alignSelf: "flex-start",
    },
    choicesRow: {
        display: "flex",
        alignItems: "center",
        flexWrap: "wrap",
        gap: tokens.spacingHorizontalXS,
        ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalS),
    },
    choicesHint: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
        marginRight: tokens.spacingHorizontalXS,
    },
    choicesBadge: {
        cursor: "default",
    },
    choicesLinked: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXXS,
        color: tokens.colorBrandForeground1,
        fontSize: tokens.fontSizeBase100,
    },
    footer: {
        display: "flex",
        justifyContent: "flex-start",
        ...shorthands.padding(tokens.spacingVerticalS, "0"),
        ...shorthands.borderWidth("1px", "0", "0", "0"),
        ...shorthands.borderStyle("solid"),
        ...shorthands.borderColor(tokens.colorNeutralStroke2),
    },
});

// ---------------------------------------------------------------------------
// Type label map for dropdown display
// ---------------------------------------------------------------------------

const TYPE_LABELS: Record<OutputFieldType, string> = {
    string: "String",
    number: "Number",
    boolean: "Boolean",
    array: "Array",
    object: "Object",
};

// ---------------------------------------------------------------------------
// Downstream $choices discovery
// ---------------------------------------------------------------------------

/** Information about a choice field found on a downstream UpdateRecord node. */
interface DownstreamChoiceInfo {
    /** Display label of the downstream node. */
    nodeLabel: string;
    /** Output variable of the downstream node (for $choices reference). */
    nodeOutputVariable: string;
    /** Dataverse field logical name in the fieldMapping. */
    fieldName: string;
    /** Ordered list of choice option labels from the options map. */
    options: string[];
}

/**
 * Finds choice-typed field mappings on downstream UpdateRecord nodes.
 *
 * Walks outgoing edges from `nodeId`, inspects each target node for
 * UpdateRecord type, parses its configJson for fieldMappings with type
 * "choice", and returns a map keyed by the Dataverse field name.
 *
 * This is used to suggest `$choices` references on AI output fields:
 * when an output field's name matches a downstream choice field name,
 * the form can offer to auto-link them.
 */
function findDownstreamChoiceFields(
    nodeId: string,
    edges: PlaybookEdge[],
    nodes: PlaybookNode[],
): Map<string, DownstreamChoiceInfo> {
    const result = new Map<string, DownstreamChoiceInfo>();

    // Find all edges originating from this node
    const outgoingEdges = edges.filter((e) => e.source === nodeId);

    for (const edge of outgoingEdges) {
        const targetNode = nodes.find((n) => n.id === edge.target);
        if (!targetNode || targetNode.data.type !== "updateRecord") continue;

        const configJson = targetNode.data.configJson;
        if (!configJson) continue;

        try {
            const parsed = JSON.parse(configJson);
            const fieldMappings = Array.isArray(parsed.fieldMappings)
                ? parsed.fieldMappings
                : [];

            for (const mapping of fieldMappings) {
                if (
                    mapping.type === "choice" &&
                    mapping.field &&
                    mapping.options &&
                    typeof mapping.options === "object" &&
                    Object.keys(mapping.options).length > 0
                ) {
                    result.set(mapping.field, {
                        nodeLabel: targetNode.data.label || "Update Record",
                        nodeOutputVariable: targetNode.data.outputVariable || targetNode.id,
                        fieldName: mapping.field,
                        options: Object.keys(mapping.options),
                    });
                }
            }
        } catch {
            // Ignore malformed configJson
        }
    }

    return result;
}

/**
 * Resolves the option labels for an already-linked $choices reference.
 *
 * Parses the reference format "downstream:{nodeVar}.{fieldName}" and
 * looks up the matching DownstreamChoiceInfo from the current map.
 * Returns the option labels or undefined if the reference cannot be resolved.
 */
function resolveLinkedOptions(
    choicesRef: string,
    downstreamChoices: Map<string, DownstreamChoiceInfo>,
): string[] | undefined {
    // Format: "downstream:{nodeVar}.{fieldName}"
    const match = choicesRef.match(/^downstream:[^.]+\.(.+)$/);
    if (!match) return undefined;

    const fieldName = match[1];
    const info = downstreamChoices.get(fieldName);
    return info?.options;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface PromptSchemaFormProps {
    /** Current schema value (null triggers creation of a default). */
    schema: PromptSchema | null;
    /** Callback invoked with the updated schema on every field change. */
    onChange: (schema: PromptSchema) => void;
    /** Switch to the JSON editor view. */
    onSwitchToEditor: () => void;
    /** Node ID of the AI node being edited — enables downstream $choices discovery. */
    nodeId?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PromptSchemaForm = memo(function PromptSchemaForm({
    schema,
    onChange,
    onSwitchToEditor,
    nodeId,
}: PromptSchemaFormProps) {
    const styles = useStyles();

    // -- Local state derived from props --
    const [local, setLocal] = useState<PromptSchema>(
        () => schema ?? createDefaultPromptSchema(),
    );
    // taskTouched removed — Task field is now optional (override only)

    // -- Downstream $choices discovery from canvas state --
    const canvasNodes = useCanvasStore((s) => s.nodes);
    const canvasEdges = useCanvasStore((s) => s.edges);

    const downstreamChoices = useMemo(() => {
        if (!nodeId) return new Map<string, DownstreamChoiceInfo>();
        return findDownstreamChoiceFields(nodeId, canvasEdges, canvasNodes);
    }, [nodeId, canvasEdges, canvasNodes]);

    // Sync from parent when schema identity changes (e.g., on first load or
    // external update). We deliberately compare by JSON identity to avoid
    // infinite loops while still accepting external updates.
    useEffect(() => {
        if (schema) {
            setLocal(schema);
        }
    }, [schema]);

    // -- Emit helper: updates local state and notifies parent --
    const emit = useCallback(
        (next: PromptSchema) => {
            setLocal(next);
            onChange(next);
        },
        [onChange],
    );

    // -----------------------------------------------------------------------
    // Role
    // -----------------------------------------------------------------------

    const handleRoleChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            const role = e.target.value;
            emit({
                ...local,
                instruction: {
                    ...local.instruction,
                    role: role || undefined,
                },
            });
        },
        [local, emit],
    );

    // -----------------------------------------------------------------------
    // Task
    // -----------------------------------------------------------------------

    const handleTaskChange = useCallback(
        (e: React.ChangeEvent<HTMLTextAreaElement>) => {
            emit({
                ...local,
                instruction: {
                    ...local.instruction,
                    task: e.target.value,
                },
            });
        },
        [local, emit],
    );

    // handleTaskBlur removed — Task field no longer validates on blur

    const taskError = undefined;

    // -----------------------------------------------------------------------
    // Constraints
    // -----------------------------------------------------------------------

    const constraints = local.instruction.constraints ?? [];

    const handleConstraintChange = useCallback(
        (index: number, value: string) => {
            const updated = [...constraints];
            updated[index] = value;
            emit({
                ...local,
                instruction: {
                    ...local.instruction,
                    constraints: updated,
                },
            });
        },
        [local, constraints, emit],
    );

    const handleRemoveConstraint = useCallback(
        (index: number) => {
            const updated = constraints.filter((_, i) => i !== index);
            emit({
                ...local,
                instruction: {
                    ...local.instruction,
                    constraints: updated.length > 0 ? updated : undefined,
                },
            });
        },
        [local, constraints, emit],
    );

    const handleAddConstraint = useCallback(() => {
        emit({
            ...local,
            instruction: {
                ...local.instruction,
                constraints: [...constraints, ""],
            },
        });
    }, [local, constraints, emit]);

    // -----------------------------------------------------------------------
    // Output Fields
    // -----------------------------------------------------------------------

    const outputFields: OutputFieldDefinition[] = local.output?.fields ?? [];

    const handleOutputFieldChange = useCallback(
        (index: number, patch: Partial<OutputFieldDefinition>) => {
            const updated = [...outputFields];
            updated[index] = { ...updated[index], ...patch };
            emit({
                ...local,
                output: {
                    ...local.output,
                    fields: updated,
                },
            });
        },
        [local, outputFields, emit],
    );

    const handleRemoveOutputField = useCallback(
        (index: number) => {
            const updated = outputFields.filter((_, i) => i !== index);
            emit({
                ...local,
                output:
                    updated.length > 0
                        ? { ...local.output, fields: updated }
                        : local.output?.structuredOutput
                            ? { ...local.output, fields: updated }
                            : undefined,
            });
        },
        [local, outputFields, emit],
    );

    const handleAddOutputField = useCallback(() => {
        const newField: OutputFieldDefinition = {
            name: "",
            type: "string",
            description: "",
        };
        emit({
            ...local,
            output: {
                ...local.output,
                fields: [...outputFields, newField],
            },
        });
    }, [local, outputFields, emit]);

    // -----------------------------------------------------------------------
    // $choices linking
    // -----------------------------------------------------------------------

    /**
     * Toggles the $choices reference on an output field.
     *
     * When linking: sets $choices to "downstream:{nodeOutputVariable}.{fieldName}"
     * When unlinking: removes $choices from the field.
     */
    const handleToggleChoices = useCallback(
        (index: number, choiceInfo: DownstreamChoiceInfo | undefined) => {
            const field = outputFields[index];
            if (!field) return;

            const updated = [...outputFields];
            if (field.$choices) {
                // Unlink — remove $choices
                const { $choices: _, ...rest } = updated[index];
                updated[index] = rest as OutputFieldDefinition;
            } else if (choiceInfo) {
                // Link — set $choices reference
                updated[index] = {
                    ...updated[index],
                    $choices: `downstream:${choiceInfo.nodeOutputVariable}.${choiceInfo.fieldName}`,
                };
            }
            emit({
                ...local,
                output: {
                    ...local.output,
                    fields: updated,
                },
            });
        },
        [local, outputFields, emit],
    );

    // -----------------------------------------------------------------------
    // Structured Output
    // -----------------------------------------------------------------------

    const structuredOutput = local.output?.structuredOutput ?? false;

    const handleStructuredOutputChange = useCallback(
        (_e: React.ChangeEvent<HTMLInputElement>, data: CheckboxOnChangeData) => {
            const checked = data.checked === true;
            emit({
                ...local,
                output: {
                    ...local.output,
                    fields: outputFields,
                    structuredOutput: checked || undefined,
                },
            });
        },
        [local, outputFields, emit],
    );

    // -----------------------------------------------------------------------
    // Render
    // -----------------------------------------------------------------------

    return (
        <div className={styles.form}>
            {/* Role */}
            <div className={styles.field}>
                <Label size="small">Role</Label>
                <Textarea
                    size="small"
                    className={styles.promptArea}
                    value={local.instruction.role ?? ""}
                    onChange={handleRoleChange}
                    placeholder="e.g., You are a document analysis assistant..."
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    System-level identity that defines the AI's persona
                </Text>
            </div>

            {/* Task */}
            <Field
                validationMessage={taskError}
                validationState={taskError ? "error" : "none"}
            >
                <Label size="small">
                    Task
                </Label>
                <Textarea
                    size="small"
                    className={styles.promptArea}
                    value={local.instruction.task}
                    onChange={handleTaskChange}
                    placeholder="Inherited from action — override here if needed"
                    resize="vertical"
                />
                <Text className={styles.fieldHint}>
                    Override the action's task instruction for this node (optional)
                </Text>
            </Field>

            {/* Constraints */}
            <div className={styles.field}>
                <Label size="small">Constraints</Label>
                {constraints.map((constraint, index) => (
                    <div key={index} className={styles.constraintRow}>
                        <Input
                            className={styles.constraintInput}
                            size="small"
                            value={constraint}
                            onChange={(e) =>
                                handleConstraintChange(index, e.target.value)
                            }
                            placeholder="e.g., Only use information present in the document"
                        />
                        <Button
                            size="small"
                            appearance="subtle"
                            icon={<Delete20Regular />}
                            onClick={() => handleRemoveConstraint(index)}
                            aria-label="Remove constraint"
                        />
                    </div>
                ))}
                <Button
                    className={styles.addButton}
                    size="small"
                    appearance="secondary"
                    icon={<Add20Regular />}
                    onClick={handleAddConstraint}
                >
                    Add Constraint
                </Button>
            </div>

            {/* Output Fields */}
            <div className={styles.field}>
                <Label size="small">Output Fields</Label>
                <Text className={styles.fieldHint}>
                    Define the structure of the AI's JSON output
                </Text>
                <div className={styles.outputFieldList}>
                    {outputFields.map((field, index) => {
                        // Check for matching downstream choice field
                        const matchingChoice =
                            field.type === "string" && field.name
                                ? downstreamChoices.get(field.name)
                                : undefined;
                        const isLinked = !!field.$choices;

                        // If already linked, resolve the options from the reference
                        const linkedOptions = isLinked
                            ? resolveLinkedOptions(field.$choices!, downstreamChoices)
                            : undefined;

                        return (
                            <div key={index}>
                                <div className={styles.outputFieldRow}>
                                    <Input
                                        className={styles.outputFieldName}
                                        size="small"
                                        value={field.name}
                                        onChange={(e) =>
                                            handleOutputFieldChange(index, {
                                                name: e.target.value,
                                            })
                                        }
                                        placeholder="Field name"
                                    />
                                    <Dropdown
                                        className={styles.outputFieldType}
                                        size="small"
                                        value={TYPE_LABELS[field.type]}
                                        selectedOptions={[field.type]}
                                        onOptionSelect={(
                                            _event: SelectionEvents,
                                            data: OptionOnSelectData,
                                        ) => {
                                            if (data.optionValue) {
                                                handleOutputFieldChange(index, {
                                                    type: data.optionValue as OutputFieldType,
                                                });
                                            }
                                        }}
                                    >
                                        {OUTPUT_FIELD_TYPES.map((t) => (
                                            <Option key={t} value={t}>
                                                {TYPE_LABELS[t]}
                                            </Option>
                                        ))}
                                    </Dropdown>
                                    <Input
                                        className={styles.outputFieldDescription}
                                        size="small"
                                        value={field.description ?? ""}
                                        onChange={(e) =>
                                            handleOutputFieldChange(index, {
                                                description: e.target.value || undefined,
                                            })
                                        }
                                        placeholder="Description"
                                    />
                                    <Button
                                        size="small"
                                        appearance="subtle"
                                        icon={<Delete20Regular />}
                                        onClick={() => handleRemoveOutputField(index)}
                                        aria-label="Remove output field"
                                    />
                                </div>

                                {/* $choices: show suggestion when a matching downstream choice exists */}
                                {matchingChoice && !isLinked && (
                                    <div className={styles.choicesRow}>
                                        <Caption1 className={styles.choicesHint}>
                                            Values from: {matchingChoice.nodeLabel}
                                        </Caption1>
                                        {matchingChoice.options.map((opt) => (
                                            <Badge
                                                key={opt}
                                                size="small"
                                                appearance="outline"
                                                className={styles.choicesBadge}
                                            >
                                                {opt}
                                            </Badge>
                                        ))}
                                        <Tooltip
                                            content="Link to downstream choice field — constrains AI output to these values"
                                            relationship="label"
                                        >
                                            <Button
                                                size="small"
                                                appearance="subtle"
                                                icon={<Link20Regular />}
                                                onClick={() =>
                                                    handleToggleChoices(index, matchingChoice)
                                                }
                                                aria-label="Link to downstream choices"
                                            />
                                        </Tooltip>
                                    </div>
                                )}

                                {/* $choices: show linked state with resolved labels */}
                                {isLinked && (
                                    <div className={styles.choicesRow}>
                                        <Caption1 className={styles.choicesLinked}>
                                            $choices: {field.$choices}
                                        </Caption1>
                                        {linkedOptions && linkedOptions.length > 0 && (
                                            <>
                                                {linkedOptions.map((opt) => (
                                                    <Badge
                                                        key={opt}
                                                        size="small"
                                                        appearance="filled"
                                                        color="brand"
                                                        className={styles.choicesBadge}
                                                    >
                                                        {opt}
                                                    </Badge>
                                                ))}
                                            </>
                                        )}
                                        <Tooltip
                                            content="Remove $choices link"
                                            relationship="label"
                                        >
                                            <Button
                                                size="small"
                                                appearance="subtle"
                                                icon={<LinkDismiss20Regular />}
                                                onClick={() =>
                                                    handleToggleChoices(index, undefined)
                                                }
                                                aria-label="Unlink downstream choices"
                                            />
                                        </Tooltip>
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
                <Button
                    className={styles.addButton}
                    size="small"
                    appearance="secondary"
                    icon={<Add20Regular />}
                    onClick={handleAddOutputField}
                >
                    Add Field
                </Button>
            </div>

            {/* Structured Output */}
            <Checkbox
                checked={structuredOutput}
                onChange={handleStructuredOutputChange}
                label="Use Structured Output (guaranteed JSON)"
            />

            {/* Switch to JSON Editor */}
            <div className={styles.footer}>
                <Button
                    size="small"
                    appearance="secondary"
                    icon={<Code20Regular />}
                    onClick={onSwitchToEditor}
                >
                    Switch to JSON Editor
                </Button>
            </div>
        </div>
    );
});
