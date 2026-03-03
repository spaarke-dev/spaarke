/**
 * UpdateRecordForm - Configuration form for Update Record nodes.
 *
 * Allows users to configure Dataverse record updates with typed field mappings:
 * - Entity logical name (target table)
 * - Record ID (supports template variables)
 * - Typed field mappings: field name, type (string/choice/boolean/number), value, and
 *   for Choice fields an options map (label → Dataverse int value)
 *
 * Produces ConfigJson matching the server-side UpdateRecordNodeConfig:
 *   { entityLogicalName, recordId, fieldMappings: [...] }
 *
 * The Choice options map serves dual purpose:
 *   1. Downstream coercion: AI string output → Dataverse option value (int)
 *   2. Upstream guidance: Same labels used in AI prompt to constrain output
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useCallback, useMemo, memo, useState } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Text,
    Input,
    Label,
    Button,
    Dropdown,
    Option,
    SpinButton,
    Divider,
} from "@fluentui/react-components";
import type { OptionOnSelectData, SelectionEvents } from "@fluentui/react-components";
import { Add20Regular, Delete20Regular } from "@fluentui/react-icons";
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
    mappingList: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
    mappingCard: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        backgroundColor: tokens.colorNeutralBackground3,
    },
    mappingHeader: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    mappingFieldInput: {
        flex: 1,
    },
    typeDropdown: {
        minWidth: "100px",
        maxWidth: "100px",
    },
    optionsSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
        marginTop: tokens.spacingVerticalXS,
    },
    optionRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    optionLabel: {
        flex: 2,
    },
    optionValue: {
        flex: 1,
        minWidth: "90px",
    },
    addButton: {
        alignSelf: "flex-start",
    },
});

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type FieldMappingType = "string" | "choice" | "boolean" | "number";

interface FieldMapping {
    field: string;
    type: FieldMappingType;
    value: string;
    options?: Record<string, number>;
}

interface ChoiceOption {
    label: string;
    value: number;
}

interface UpdateRecordConfig {
    entityLogicalName: string;
    recordId: string;
    fieldMappings: FieldMapping[];
}

const TYPE_LABELS: Record<FieldMappingType, string> = {
    string: "String",
    choice: "Choice",
    boolean: "Boolean",
    number: "Number",
};

// ---------------------------------------------------------------------------
// Parse / serialize
// ---------------------------------------------------------------------------

function parseConfig(json: string): UpdateRecordConfig {
    try {
        const parsed = JSON.parse(json);
        const entityLogicalName =
            typeof parsed.entityLogicalName === "string" ? parsed.entityLogicalName : "";
        const recordId =
            typeof parsed.recordId === "string" ? parsed.recordId : "";

        // New format: fieldMappings array
        if (Array.isArray(parsed.fieldMappings)) {
            return { entityLogicalName, recordId, fieldMappings: parsed.fieldMappings };
        }

        // Legacy format: fields dict → migrate to fieldMappings (all as string type)
        if (parsed.fields && typeof parsed.fields === "object") {
            const fieldMappings: FieldMapping[] = Object.entries(parsed.fields).map(
                ([field, value]) => ({
                    field,
                    type: "string" as FieldMappingType,
                    value: String(value),
                }),
            );
            return { entityLogicalName, recordId, fieldMappings };
        }

        return { entityLogicalName, recordId, fieldMappings: [] };
    } catch {
        return { entityLogicalName: "", recordId: "", fieldMappings: [] };
    }
}

function serializeConfig(config: UpdateRecordConfig): string {
    // Always output new format with fieldMappings
    const clean: Record<string, unknown> = {
        entityLogicalName: config.entityLogicalName,
        recordId: config.recordId,
        fieldMappings: config.fieldMappings.filter((m) => m.field.trim() !== "").map((m) => {
            const entry: Record<string, unknown> = {
                field: m.field,
                type: m.type,
                value: m.value,
            };
            if (m.type === "choice" && m.options && Object.keys(m.options).length > 0) {
                entry.options = m.options;
            }
            return entry;
        }),
    };
    return JSON.stringify(clean);
}

function optionsToArray(options: Record<string, number> | undefined): ChoiceOption[] {
    if (!options) return [];
    return Object.entries(options).map(([label, value]) => ({ label, value }));
}

function arrayToOptions(arr: ChoiceOption[]): Record<string, number> {
    const result: Record<string, number> = {};
    for (const opt of arr) {
        if (opt.label.trim()) {
            result[opt.label.trim()] = opt.value;
        }
    }
    return result;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const UpdateRecordForm = memo(function UpdateRecordForm({
    nodeId,
    configJson,
    onConfigChange,
}: NodeFormProps) {
    const styles = useStyles();
    const config = useMemo(() => parseConfig(configJson), [configJson]);
    const [mappings, setMappings] = useState<FieldMapping[]>(() => config.fieldMappings);

    // Emit the full config update
    const emitConfig = useCallback(
        (entityLogicalName: string, recordId: string, updatedMappings: FieldMapping[]) => {
            onConfigChange(
                serializeConfig({ entityLogicalName, recordId, fieldMappings: updatedMappings }),
            );
        },
        [onConfigChange],
    );

    // -- Top-level field handlers --

    const handleEntityChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            emitConfig(e.target.value, config.recordId, mappings);
        },
        [config.recordId, mappings, emitConfig],
    );

    const handleRecordIdChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            emitConfig(config.entityLogicalName, e.target.value, mappings);
        },
        [config.entityLogicalName, mappings, emitConfig],
    );

    // -- Mapping handlers --

    const updateMapping = useCallback(
        (index: number, patch: Partial<FieldMapping>) => {
            const updated = [...mappings];
            updated[index] = { ...updated[index], ...patch };
            // Clear options when switching away from choice type
            if (patch.type && patch.type !== "choice") {
                delete updated[index].options;
            }
            setMappings(updated);
            emitConfig(config.entityLogicalName, config.recordId, updated);
        },
        [mappings, config.entityLogicalName, config.recordId, emitConfig],
    );

    const handleAddMapping = useCallback(() => {
        const newMapping: FieldMapping = { field: "", type: "string", value: "" };
        const updated = [...mappings, newMapping];
        setMappings(updated);
    }, [mappings]);

    const handleRemoveMapping = useCallback(
        (index: number) => {
            const updated = mappings.filter((_, i) => i !== index);
            setMappings(updated);
            emitConfig(config.entityLogicalName, config.recordId, updated);
        },
        [mappings, config.entityLogicalName, config.recordId, emitConfig],
    );

    // -- Choice options handlers --

    const handleOptionsChange = useCallback(
        (mappingIndex: number, options: Record<string, number>) => {
            updateMapping(mappingIndex, { options });
        },
        [updateMapping],
    );

    // -- Render --

    return (
        <div className={styles.form}>
            {/* Entity Logical Name */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-entity`} size="small" required>
                    Entity Logical Name
                </Label>
                <Input
                    id={`${nodeId}-entity`}
                    size="small"
                    value={config.entityLogicalName}
                    onChange={handleEntityChange}
                    placeholder="e.g., sprk_document"
                />
                <Text className={styles.fieldHint}>
                    Dataverse table logical name (e.g., sprk_document, sprk_matter, contact)
                </Text>
            </div>

            {/* Record ID */}
            <div className={styles.field}>
                <Label htmlFor={`${nodeId}-recordId`} size="small" required>
                    Record ID
                </Label>
                <Input
                    id={`${nodeId}-recordId`}
                    size="small"
                    value={config.recordId}
                    onChange={handleRecordIdChange}
                    placeholder="e.g., {{document.id}}"
                />
                <Text className={styles.fieldHint}>
                    {"GUID of the record to update. Use {{document.id}} for the current document."}
                </Text>
            </div>

            {/* Field Mappings */}
            <div className={styles.field}>
                <Label size="small" required>
                    Field Mappings
                </Label>
                <Text className={styles.fieldHint}>
                    {"Map Dataverse fields to values with type-aware coercion. " +
                        "Use {{nodeName.text}} or {{nodeName.output.field}} for AI outputs."}
                </Text>

                <div className={styles.mappingList}>
                    {mappings.map((mapping, index) => (
                        <div key={index} className={styles.mappingCard}>
                            {/* Row 1: Field name + Type + Delete */}
                            <div className={styles.mappingHeader}>
                                <Input
                                    className={styles.mappingFieldInput}
                                    size="small"
                                    value={mapping.field}
                                    onChange={(e) =>
                                        updateMapping(index, { field: e.target.value })
                                    }
                                    placeholder="sprk_fieldname"
                                />
                                <Dropdown
                                    className={styles.typeDropdown}
                                    size="small"
                                    value={TYPE_LABELS[mapping.type]}
                                    selectedOptions={[mapping.type]}
                                    onOptionSelect={(
                                        _event: SelectionEvents,
                                        data: OptionOnSelectData,
                                    ) => {
                                        if (data.optionValue) {
                                            updateMapping(index, {
                                                type: data.optionValue as FieldMappingType,
                                            });
                                        }
                                    }}
                                >
                                    <Option value="string">String</Option>
                                    <Option value="choice">Choice</Option>
                                    <Option value="boolean">Boolean</Option>
                                    <Option value="number">Number</Option>
                                </Dropdown>
                                <Button
                                    size="small"
                                    appearance="subtle"
                                    icon={<Delete20Regular />}
                                    onClick={() => handleRemoveMapping(index)}
                                    aria-label="Remove field mapping"
                                />
                            </div>

                            {/* Row 2: Value template */}
                            <Input
                                size="small"
                                value={mapping.value}
                                onChange={(e) =>
                                    updateMapping(index, { value: e.target.value })
                                }
                                placeholder={
                                    mapping.type === "boolean"
                                        ? "{{nodeName.output.isFlag}}"
                                        : mapping.type === "choice"
                                            ? "{{nodeName.output.status}}"
                                            : "{{nodeName.output.field}}"
                                }
                            />

                            {/* Row 3: Choice options (only for choice type) */}
                            {mapping.type === "choice" && (
                                <ChoiceOptionsEditor
                                    options={mapping.options ?? {}}
                                    onChange={(opts) => handleOptionsChange(index, opts)}
                                />
                            )}

                            {/* Type hints */}
                            {mapping.type === "boolean" && (
                                <Text className={styles.fieldHint}>
                                    AI output mapped: yes/true/1/on → true, no/false/0/off → false
                                </Text>
                            )}
                        </div>
                    ))}
                </div>

                <Button
                    className={styles.addButton}
                    size="small"
                    appearance="secondary"
                    icon={<Add20Regular />}
                    onClick={handleAddMapping}
                >
                    Add Field
                </Button>
            </div>
        </div>
    );
});

// ---------------------------------------------------------------------------
// Choice Options Editor sub-component
// ---------------------------------------------------------------------------

interface ChoiceOptionsEditorProps {
    options: Record<string, number>;
    onChange: (options: Record<string, number>) => void;
}

const ChoiceOptionsEditor = memo(function ChoiceOptionsEditor({
    options,
    onChange,
}: ChoiceOptionsEditorProps) {
    const styles = useStyles();
    const [optionsList, setOptionsList] = useState<ChoiceOption[]>(() =>
        optionsToArray(options),
    );

    const handleOptionChange = useCallback(
        (index: number, key: "label" | "value", val: string | number) => {
            const updated = [...optionsList];
            if (key === "label") {
                updated[index] = { ...updated[index], label: val as string };
            } else {
                updated[index] = { ...updated[index], value: val as number };
            }
            setOptionsList(updated);
            onChange(arrayToOptions(updated));
        },
        [optionsList, onChange],
    );

    const handleAddOption = useCallback(() => {
        const updated = [...optionsList, { label: "", value: 100_000_000 }];
        setOptionsList(updated);
    }, [optionsList]);

    const handleRemoveOption = useCallback(
        (index: number) => {
            const updated = optionsList.filter((_, i) => i !== index);
            setOptionsList(updated);
            onChange(arrayToOptions(updated));
        },
        [optionsList, onChange],
    );

    return (
        <div className={styles.optionsSection}>
            <Divider appearance="subtle" />
            <Text className={styles.fieldHint}>
                Choice options: AI output label → Dataverse option value (int).
                Include these same labels in the AI prompt.
            </Text>
            {optionsList.map((opt, index) => (
                <div key={index} className={styles.optionRow}>
                    <Input
                        className={styles.optionLabel}
                        size="small"
                        value={opt.label}
                        onChange={(e) => handleOptionChange(index, "label", e.target.value)}
                        placeholder="Label (e.g., Complete)"
                    />
                    <SpinButton
                        className={styles.optionValue}
                        size="small"
                        value={opt.value}
                        min={0}
                        max={999_999_999}
                        step={1}
                        onChange={(_e, data) =>
                            handleOptionChange(index, "value", data.value ?? 100_000_000)
                        }
                    />
                    <Button
                        size="small"
                        appearance="subtle"
                        icon={<Delete20Regular />}
                        onClick={() => handleRemoveOption(index)}
                        aria-label="Remove option"
                    />
                </div>
            ))}
            <Button
                className={styles.addButton}
                size="small"
                appearance="subtle"
                icon={<Add20Regular />}
                onClick={handleAddOption}
            >
                Add Option
            </Button>
        </div>
    );
});
