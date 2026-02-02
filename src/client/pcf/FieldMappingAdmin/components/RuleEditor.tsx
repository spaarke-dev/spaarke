/**
 * RuleEditor Component
 *
 * Dialog/panel for creating or editing a single field mapping rule:
 * - Source field selection with type auto-detection
 * - Target field selection with type auto-detection
 * - Real-time type compatibility validation
 * - Required/default value configuration
 * - Cascading source toggle
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with design tokens (no hard-coded colors)
 * - ADR-022: React 16 APIs
 */

import * as React from "react";
import {
    Dialog,
    DialogTrigger,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button,
    Dropdown,
    Option,
    Input,
    Label,
    Text,
    Switch,
    Field,
    Badge,
    Tooltip,
    makeStyles,
    tokens,
    Spinner,
    InfoLabel,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import {
    CheckmarkCircle20Filled,
    ErrorCircle20Filled,
    Warning20Filled,
    Info20Regular,
    Save20Regular,
    Dismiss20Regular,
} from "@fluentui/react-icons";
import {
    IFieldMappingRule,
    FieldType,
    FieldTypeLabels,
    CompatibilityMode,
    CompatibilityModeLabels,
    CompatibilityLevel,
    ITypeCompatibilityResult,
    STRICT_TYPE_COMPATIBILITY,
} from "../types/FieldMappingTypes";

/**
 * Entity field metadata for dropdown population
 */
export interface EntityFieldInfo {
    logicalName: string;
    displayName: string;
    type: FieldType;
    description?: string;
}

interface RuleEditorProps {
    /** Rule being edited (null for new rule) */
    rule: IFieldMappingRule | null;
    /** Whether the dialog is open */
    isOpen: boolean;
    /** Callback when dialog closes */
    onClose: () => void;
    /** Callback when rule is saved */
    onSave: (rule: IFieldMappingRule) => void;
    /** Available source entity fields */
    sourceFields: EntityFieldInfo[];
    /** Available target entity fields */
    targetFields: EntityFieldInfo[];
    /** Whether fields are loading */
    isLoadingFields?: boolean;
    /** Profile ID for new rules */
    profileId: string;
}

const useStyles = makeStyles({
    dialogContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        minWidth: "450px",
    },
    formRow: {
        display: "grid",
        gridTemplateColumns: "1fr 1fr",
        gap: tokens.spacingHorizontalM,
    },
    fieldWithType: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    typeIndicator: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    compatibilitySection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        padding: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusMedium,
    },
    compatibleBg: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
    },
    warningBg: {
        backgroundColor: tokens.colorPaletteYellowBackground1,
    },
    incompatibleBg: {
        backgroundColor: tokens.colorPaletteRedBackground1,
    },
    compatibilityHeader: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    compatibleText: {
        color: tokens.colorPaletteGreenForeground1,
    },
    warningText: {
        color: tokens.colorPaletteYellowForeground1,
    },
    incompatibleText: {
        color: tokens.colorPaletteRedForeground1,
    },
    optionsRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalL,
    },
    switchGroup: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    defaultValueField: {
        marginTop: tokens.spacingVerticalS,
    },
    fullWidth: {
        gridColumn: "1 / -1",
    },
});

/**
 * Validate type compatibility between source and target field types.
 */
const validateTypeCompatibility = (
    sourceType: FieldType,
    targetType: FieldType,
    mode: CompatibilityMode
): ITypeCompatibilityResult => {
    const result: ITypeCompatibilityResult = {
        isCompatible: false,
        level: CompatibilityLevel.Incompatible,
        warnings: [],
        errors: [],
    };

    // Exact match is always compatible
    if (sourceType === targetType) {
        result.isCompatible = true;
        result.level = CompatibilityLevel.Exact;
        return result;
    }

    // Check strict compatibility matrix
    const compatibleTypes = STRICT_TYPE_COMPATIBILITY[sourceType] ?? [];

    if (compatibleTypes.includes(targetType)) {
        result.isCompatible = true;

        // Determine compatibility level
        if (sourceType === FieldType.Text && targetType === FieldType.Memo) {
            result.level = CompatibilityLevel.Exact; // Text to Memo is exact
        } else if (targetType === FieldType.Text) {
            result.level = CompatibilityLevel.SafeConversion; // Any to Text is safe conversion
            result.warnings.push(`Converting ${FieldTypeLabels[sourceType]} to Text - value will be formatted`);
        } else {
            result.level = CompatibilityLevel.SafeConversion;
        }

        return result;
    }

    // Check if Resolve mode would help (future feature)
    if (mode === CompatibilityMode.Resolve) {
        result.level = CompatibilityLevel.RequiresResolve;
        result.warnings.push(`Type resolution from ${FieldTypeLabels[sourceType]} to ${FieldTypeLabels[targetType]} is not yet implemented`);
        return result;
    }

    // Incompatible in Strict mode
    result.errors.push(
        `Cannot convert ${FieldTypeLabels[sourceType]} to ${FieldTypeLabels[targetType]} in Strict mode. ` +
        `Compatible types for ${FieldTypeLabels[sourceType]}: ${compatibleTypes.map((t) => FieldTypeLabels[t]).join(", ") || "none"}`
    );

    return result;
};

/**
 * Get compatible target types for a given source type
 */
const getCompatibleTargetTypes = (sourceType: FieldType): FieldType[] => {
    const compatibleTypes = STRICT_TYPE_COMPATIBILITY[sourceType] ?? [];
    if (!compatibleTypes.includes(sourceType)) {
        return [sourceType, ...compatibleTypes];
    }
    return compatibleTypes;
};

export const RuleEditor: React.FC<RuleEditorProps> = ({
    rule,
    isOpen,
    onClose,
    onSave,
    sourceFields,
    targetFields,
    isLoadingFields = false,
    profileId,
}) => {
    const styles = useStyles();
    const isNewRule = !rule;

    // Form state
    const [formData, setFormData] = React.useState<Partial<IFieldMappingRule>>(() =>
        rule ?? {
            id: `new-${Date.now()}`,
            profileId,
            sourceField: "",
            sourceFieldType: FieldType.Text,
            targetField: "",
            targetFieldType: FieldType.Text,
            compatibilityMode: CompatibilityMode.Strict,
            isRequired: false,
            defaultValue: undefined,
            isCascadingSource: false,
            executionOrder: 0,
            isActive: true,
        }
    );

    // Reset form when rule changes
    React.useEffect(() => {
        if (rule) {
            setFormData(rule);
        } else {
            setFormData({
                id: `new-${Date.now()}`,
                profileId,
                sourceField: "",
                sourceFieldType: FieldType.Text,
                targetField: "",
                targetFieldType: FieldType.Text,
                compatibilityMode: CompatibilityMode.Strict,
                isRequired: false,
                defaultValue: undefined,
                isCascadingSource: false,
                executionOrder: 0,
                isActive: true,
            });
        }
    }, [rule, profileId]);

    // Type compatibility validation
    const compatibility = React.useMemo(
        () =>
            validateTypeCompatibility(
                formData.sourceFieldType ?? FieldType.Text,
                formData.targetFieldType ?? FieldType.Text,
                formData.compatibilityMode ?? CompatibilityMode.Strict
            ),
        [formData.sourceFieldType, formData.targetFieldType, formData.compatibilityMode]
    );

    // Handle source field change
    const handleSourceFieldChange = React.useCallback(
        (fieldName: string) => {
            const field = sourceFields.find((f) => f.logicalName === fieldName);
            setFormData((prev) => ({
                ...prev,
                sourceField: fieldName,
                sourceFieldType: field?.type ?? FieldType.Text,
            }));
        },
        [sourceFields]
    );

    // Handle target field change
    const handleTargetFieldChange = React.useCallback(
        (fieldName: string) => {
            const field = targetFields.find((f) => f.logicalName === fieldName);
            setFormData((prev) => ({
                ...prev,
                targetField: fieldName,
                targetFieldType: field?.type ?? FieldType.Text,
            }));
        },
        [targetFields]
    );

    // Handle save
    const handleSave = React.useCallback(() => {
        if (!formData.sourceField || !formData.targetField) {
            return;
        }

        const savedRule: IFieldMappingRule = {
            id: formData.id ?? `new-${Date.now()}`,
            profileId,
            sourceField: formData.sourceField,
            sourceFieldType: formData.sourceFieldType ?? FieldType.Text,
            targetField: formData.targetField,
            targetFieldType: formData.targetFieldType ?? FieldType.Text,
            compatibilityMode: formData.compatibilityMode ?? CompatibilityMode.Strict,
            isRequired: formData.isRequired ?? false,
            defaultValue: formData.defaultValue,
            isCascadingSource: formData.isCascadingSource ?? false,
            executionOrder: formData.executionOrder ?? 0,
            isActive: formData.isActive ?? true,
            name: formData.name,
        };

        onSave(savedRule);
    }, [formData, profileId, onSave]);

    // Validation
    const isValid = Boolean(
        formData.sourceField &&
        formData.targetField &&
        compatibility.isCompatible
    );

    // Get compatibility display
    const getCompatibilityStyle = () => {
        switch (compatibility.level) {
            case CompatibilityLevel.Exact:
            case CompatibilityLevel.SafeConversion:
                return styles.compatibleBg;
            case CompatibilityLevel.RequiresResolve:
                return styles.warningBg;
            default:
                return styles.incompatibleBg;
        }
    };

    const getCompatibilityIcon = () => {
        switch (compatibility.level) {
            case CompatibilityLevel.Exact:
            case CompatibilityLevel.SafeConversion:
                return <CheckmarkCircle20Filled className={styles.compatibleText} />;
            case CompatibilityLevel.RequiresResolve:
                return <Warning20Filled className={styles.warningText} />;
            default:
                return <ErrorCircle20Filled className={styles.incompatibleText} />;
        }
    };

    const getCompatibilityLabel = () => {
        switch (compatibility.level) {
            case CompatibilityLevel.Exact:
                return "Exact type match";
            case CompatibilityLevel.SafeConversion:
                return "Safe type conversion";
            case CompatibilityLevel.RequiresResolve:
                return "Requires resolve mode";
            default:
                return "Incompatible types";
        }
    };

    return (
        <Dialog open={isOpen} onOpenChange={(_, data) => !data.open && onClose()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>
                        {isNewRule ? "Add Mapping Rule" : "Edit Mapping Rule"}
                    </DialogTitle>

                    <DialogContent className={styles.dialogContent}>
                        {isLoadingFields ? (
                            <Spinner label="Loading fields..." />
                        ) : (
                            <>
                                {/* Rule Name (optional) */}
                                <Field label="Rule Name (optional)">
                                    <Input
                                        value={formData.name || ""}
                                        onChange={(_, data) =>
                                            setFormData((prev) => ({ ...prev, name: data.value }))
                                        }
                                        placeholder="e.g., Copy Client to Account"
                                    />
                                </Field>

                                {/* Source and Target Fields */}
                                <div className={styles.formRow}>
                                    <div className={styles.fieldWithType}>
                                        <Field
                                            label={
                                                <InfoLabel info="The field on the source entity to copy from">
                                                    Source Field
                                                </InfoLabel>
                                            }
                                            required
                                        >
                                            <Dropdown
                                                value={
                                                    sourceFields.find((f) => f.logicalName === formData.sourceField)
                                                        ?.displayName || formData.sourceField || ""
                                                }
                                                selectedOptions={formData.sourceField ? [formData.sourceField] : []}
                                                onOptionSelect={(_, data) => {
                                                    if (data.optionValue) {
                                                        handleSourceFieldChange(data.optionValue);
                                                    }
                                                }}
                                                placeholder="Select source field"
                                            >
                                                {sourceFields.map((field) => (
                                                    <Option
                                                        key={field.logicalName}
                                                        value={field.logicalName}
                                                        text={field.displayName}
                                                    >
                                                        <div>
                                                            <Text>{field.displayName}</Text>
                                                            <br />
                                                            <Text size={100}>{FieldTypeLabels[field.type]}</Text>
                                                        </div>
                                                    </Option>
                                                ))}
                                            </Dropdown>
                                        </Field>
                                        {formData.sourceField && (
                                            <div className={styles.typeIndicator}>
                                                <Badge appearance="outline" size="small">
                                                    {FieldTypeLabels[formData.sourceFieldType ?? FieldType.Text]}
                                                </Badge>
                                            </div>
                                        )}
                                    </div>

                                    <div className={styles.fieldWithType}>
                                        <Field
                                            label={
                                                <InfoLabel info="The field on the target entity to copy to">
                                                    Target Field
                                                </InfoLabel>
                                            }
                                            required
                                        >
                                            <Dropdown
                                                value={
                                                    targetFields.find((f) => f.logicalName === formData.targetField)
                                                        ?.displayName || formData.targetField || ""
                                                }
                                                selectedOptions={formData.targetField ? [formData.targetField] : []}
                                                onOptionSelect={(_, data) => {
                                                    if (data.optionValue) {
                                                        handleTargetFieldChange(data.optionValue);
                                                    }
                                                }}
                                                placeholder="Select target field"
                                            >
                                                {targetFields.map((field) => (
                                                    <Option
                                                        key={field.logicalName}
                                                        value={field.logicalName}
                                                        text={field.displayName}
                                                    >
                                                        <div>
                                                            <Text>{field.displayName}</Text>
                                                            <br />
                                                            <Text size={100}>{FieldTypeLabels[field.type]}</Text>
                                                        </div>
                                                    </Option>
                                                ))}
                                            </Dropdown>
                                        </Field>
                                        {formData.targetField && (
                                            <div className={styles.typeIndicator}>
                                                <Badge appearance="outline" size="small">
                                                    {FieldTypeLabels[formData.targetFieldType ?? FieldType.Text]}
                                                </Badge>
                                            </div>
                                        )}
                                    </div>
                                </div>

                                {/* Type Compatibility Status */}
                                {formData.sourceField && formData.targetField && (
                                    <div className={`${styles.compatibilitySection} ${getCompatibilityStyle()}`}>
                                        <div className={styles.compatibilityHeader}>
                                            {getCompatibilityIcon()}
                                            <Text weight="semibold">{getCompatibilityLabel()}</Text>
                                        </div>
                                        {compatibility.warnings.map((warning, i) => (
                                            <Text key={i} size={200}>
                                                {warning}
                                            </Text>
                                        ))}
                                        {compatibility.errors.map((error, i) => (
                                            <Text key={i} size={200}>
                                                {error}
                                            </Text>
                                        ))}
                                    </div>
                                )}

                                {/* Options Row */}
                                <div className={styles.optionsRow}>
                                    <div className={styles.switchGroup}>
                                        <Switch
                                            checked={formData.isRequired ?? false}
                                            onChange={(_, data) =>
                                                setFormData((prev) => ({ ...prev, isRequired: data.checked }))
                                            }
                                        />
                                        <Tooltip
                                            content="If checked, mapping fails when source field is empty"
                                            relationship="label"
                                        >
                                            <Text>Required</Text>
                                        </Tooltip>
                                    </div>

                                    <div className={styles.switchGroup}>
                                        <Switch
                                            checked={formData.isCascadingSource ?? false}
                                            onChange={(_, data) =>
                                                setFormData((prev) => ({ ...prev, isCascadingSource: data.checked }))
                                            }
                                        />
                                        <Tooltip
                                            content="If checked, this field can trigger secondary mappings"
                                            relationship="label"
                                        >
                                            <Text>Cascading Source</Text>
                                        </Tooltip>
                                    </div>

                                    <div className={styles.switchGroup}>
                                        <Switch
                                            checked={formData.isActive ?? true}
                                            onChange={(_, data) =>
                                                setFormData((prev) => ({ ...prev, isActive: data.checked }))
                                            }
                                        />
                                        <Text>Active</Text>
                                    </div>
                                </div>

                                {/* Default Value (shown when not required) */}
                                {!formData.isRequired && (
                                    <Field
                                        label={
                                            <InfoLabel info="Value to use when source field is empty">
                                                Default Value
                                            </InfoLabel>
                                        }
                                        className={styles.defaultValueField}
                                    >
                                        <Input
                                            value={formData.defaultValue || ""}
                                            onChange={(_, data) =>
                                                setFormData((prev) => ({
                                                    ...prev,
                                                    defaultValue: data.value || undefined,
                                                }))
                                            }
                                            placeholder="Optional default value"
                                        />
                                    </Field>
                                )}

                                {/* Execution Order */}
                                <Field
                                    label={
                                        <InfoLabel info="Order for dependent mappings (lower numbers execute first)">
                                            Execution Order
                                        </InfoLabel>
                                    }
                                >
                                    <Input
                                        type="number"
                                        value={String(formData.executionOrder ?? 0)}
                                        onChange={(_, data) =>
                                            setFormData((prev) => ({
                                                ...prev,
                                                executionOrder: parseInt(data.value, 10) || 0,
                                            }))
                                        }
                                        min={0}
                                    />
                                </Field>

                                {/* Incompatible Warning */}
                                {!compatibility.isCompatible && formData.sourceField && formData.targetField && (
                                    <MessageBar intent="error">
                                        <MessageBarBody>
                                            Cannot save rule with incompatible types. Please select compatible fields.
                                        </MessageBarBody>
                                    </MessageBar>
                                )}
                            </>
                        )}
                    </DialogContent>

                    <DialogActions>
                        <Button appearance="secondary" onClick={onClose} icon={<Dismiss20Regular />}>
                            Cancel
                        </Button>
                        <Button
                            appearance="primary"
                            onClick={handleSave}
                            disabled={!isValid || isLoadingFields}
                            icon={<Save20Regular />}
                        >
                            {isNewRule ? "Add Rule" : "Save Changes"}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

export default RuleEditor;
