/**
 * RulesList Component
 *
 * Displays a DataGrid of all mapping rules for a profile with:
 * - Source/target field names and types
 * - Type compatibility status indicator
 * - Execution order
 * - Add/Edit/Delete actions
 *
 * ADR Compliance:
 * - ADR-021: Fluent UI v9 with design tokens (no hard-coded colors)
 * - ADR-022: React 16 APIs
 */

import * as React from "react";
import {
    DataGrid,
    DataGridHeader,
    DataGridRow,
    DataGridHeaderCell,
    DataGridBody,
    DataGridCell,
    TableColumnDefinition,
    createTableColumn,
    Button,
    Text,
    Badge,
    Tooltip,
    makeStyles,
    tokens,
    Card,
    CardHeader,
    Toolbar,
    ToolbarButton,
} from "@fluentui/react-components";
import {
    Add20Regular,
    Delete20Regular,
    Edit20Regular,
    ArrowUp20Regular,
    ArrowDown20Regular,
    CheckmarkCircle16Filled,
    ErrorCircle16Filled,
    Warning16Filled,
} from "@fluentui/react-icons";
import {
    IFieldMappingRule,
    FieldType,
    FieldTypeLabels,
    CompatibilityLevel,
} from "../types/FieldMappingTypes";

interface RulesListProps {
    /** List of rules to display */
    rules: IFieldMappingRule[];
    /** Callback when a rule is selected for editing */
    onEditRule: (rule: IFieldMappingRule) => void;
    /** Callback when a rule should be deleted */
    onDeleteRule: (ruleId: string) => void;
    /** Callback to add a new rule */
    onAddRule: () => void;
    /** Callback when rule order changes */
    onReorderRule: (ruleId: string, direction: "up" | "down") => void;
    /** Whether operations are disabled */
    disabled?: boolean;
    /** Type compatibility results per rule */
    compatibilityResults?: Map<string, CompatibilityLevel>;
}

const useStyles = makeStyles({
    card: {
        padding: tokens.spacingHorizontalM,
        flex: 1,
        display: "flex",
        flexDirection: "column",
        minHeight: "200px",
    },
    gridContainer: {
        flex: 1,
        overflow: "auto",
        minHeight: "150px",
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: tokens.spacingVerticalXXL,
        gap: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground3,
    },
    toolbar: {
        marginBottom: tokens.spacingVerticalS,
    },
    actionButtons: {
        display: "flex",
        gap: tokens.spacingHorizontalXS,
    },
    compatibleBadge: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        color: tokens.colorPaletteGreenForeground2,
    },
    warningBadge: {
        backgroundColor: tokens.colorPaletteYellowBackground2,
        color: tokens.colorPaletteYellowForeground2,
    },
    incompatibleBadge: {
        backgroundColor: tokens.colorPaletteRedBackground2,
        color: tokens.colorPaletteRedForeground2,
    },
    invalidRow: {
        backgroundColor: tokens.colorPaletteRedBackground1,
    },
    fieldTypeText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    orderCell: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    requiredBadge: {
        marginLeft: tokens.spacingHorizontalXS,
    },
});

/**
 * Get compatibility status icon and style
 */
const getCompatibilityDisplay = (
    level: CompatibilityLevel | undefined,
    styles: ReturnType<typeof useStyles>
): { icon: React.ReactNode; className: string; label: string } => {
    switch (level) {
        case CompatibilityLevel.Exact:
        case CompatibilityLevel.SafeConversion:
            return {
                icon: <CheckmarkCircle16Filled />,
                className: styles.compatibleBadge,
                label: level === CompatibilityLevel.Exact ? "Exact match" : "Safe conversion",
            };
        case CompatibilityLevel.RequiresResolve:
            return {
                icon: <Warning16Filled />,
                className: styles.warningBadge,
                label: "Requires resolve mode",
            };
        case CompatibilityLevel.Incompatible:
            return {
                icon: <ErrorCircle16Filled />,
                className: styles.incompatibleBadge,
                label: "Incompatible types",
            };
        default:
            return {
                icon: null,
                className: "",
                label: "Unknown",
            };
    }
};

export const RulesList: React.FC<RulesListProps> = ({
    rules,
    onEditRule,
    onDeleteRule,
    onAddRule,
    onReorderRule,
    disabled = false,
    compatibilityResults,
}) => {
    const styles = useStyles();

    const columns: TableColumnDefinition<IFieldMappingRule>[] = [
        createTableColumn<IFieldMappingRule>({
            columnId: "executionOrder",
            compare: (a, b) => a.executionOrder - b.executionOrder,
            renderHeaderCell: () => "#",
            renderCell: (item) => {
                const index = rules.findIndex((r) => r.id === item.id);
                const isFirst = index === 0;
                const isLast = index === rules.length - 1;

                return (
                    <div className={styles.orderCell}>
                        <Text>{item.executionOrder}</Text>
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<ArrowUp20Regular />}
                            disabled={disabled || isFirst}
                            onClick={() => onReorderRule(item.id, "up")}
                            title="Move up"
                        />
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<ArrowDown20Regular />}
                            disabled={disabled || isLast}
                            onClick={() => onReorderRule(item.id, "down")}
                            title="Move down"
                        />
                    </div>
                );
            },
        }),
        createTableColumn<IFieldMappingRule>({
            columnId: "sourceField",
            compare: (a, b) => a.sourceField.localeCompare(b.sourceField),
            renderHeaderCell: () => "Source Field",
            renderCell: (item) => (
                <div>
                    <Text weight="semibold">{item.sourceField}</Text>
                    <br />
                    <Text className={styles.fieldTypeText}>
                        {FieldTypeLabels[item.sourceFieldType]}
                    </Text>
                </div>
            ),
        }),
        createTableColumn<IFieldMappingRule>({
            columnId: "targetField",
            compare: (a, b) => a.targetField.localeCompare(b.targetField),
            renderHeaderCell: () => "Target Field",
            renderCell: (item) => (
                <div>
                    <Text weight="semibold">{item.targetField}</Text>
                    {item.isRequired && (
                        <Badge
                            appearance="filled"
                            color="danger"
                            size="small"
                            className={styles.requiredBadge}
                        >
                            Required
                        </Badge>
                    )}
                    <br />
                    <Text className={styles.fieldTypeText}>
                        {FieldTypeLabels[item.targetFieldType]}
                    </Text>
                </div>
            ),
        }),
        createTableColumn<IFieldMappingRule>({
            columnId: "compatibility",
            renderHeaderCell: () => "Compatibility",
            renderCell: (item) => {
                const level = compatibilityResults?.get(item.id);
                const display = getCompatibilityDisplay(level, styles);

                return (
                    <Tooltip content={display.label} relationship="label">
                        <Badge
                            appearance="filled"
                            icon={display.icon}
                            className={display.className}
                        >
                            {level === CompatibilityLevel.Exact
                                ? "Exact"
                                : level === CompatibilityLevel.SafeConversion
                                ? "Safe"
                                : level === CompatibilityLevel.RequiresResolve
                                ? "Resolve"
                                : "Invalid"}
                        </Badge>
                    </Tooltip>
                );
            },
        }),
        createTableColumn<IFieldMappingRule>({
            columnId: "options",
            renderHeaderCell: () => "Options",
            renderCell: (item) => (
                <div>
                    {item.isCascadingSource && (
                        <Tooltip content="This field can trigger secondary mappings" relationship="label">
                            <Badge appearance="outline" size="small">
                                Cascading
                            </Badge>
                        </Tooltip>
                    )}
                    {item.defaultValue && (
                        <Tooltip content={`Default: ${item.defaultValue}`} relationship="label">
                            <Badge appearance="outline" size="small">
                                Has Default
                            </Badge>
                        </Tooltip>
                    )}
                </div>
            ),
        }),
        createTableColumn<IFieldMappingRule>({
            columnId: "actions",
            renderHeaderCell: () => "Actions",
            renderCell: (item) => (
                <div className={styles.actionButtons}>
                    <Button
                        appearance="subtle"
                        icon={<Edit20Regular />}
                        onClick={() => onEditRule(item)}
                        disabled={disabled}
                        title="Edit rule"
                    />
                    <Button
                        appearance="subtle"
                        icon={<Delete20Regular />}
                        onClick={() => onDeleteRule(item.id)}
                        disabled={disabled}
                        title="Delete rule"
                    />
                </div>
            ),
        }),
    ];

    const getRowClassName = (item: IFieldMappingRule): string | undefined => {
        const level = compatibilityResults?.get(item.id);
        if (level === CompatibilityLevel.Incompatible) {
            return styles.invalidRow;
        }
        return undefined;
    };

    return (
        <Card className={styles.card}>
            <CardHeader
                header={<Text weight="semibold" size={400}>Mapping Rules</Text>}
                description={
                    <Text size={200}>
                        {rules.length} rule{rules.length !== 1 ? "s" : ""} configured
                        {compatibilityResults &&
                            Array.from(compatibilityResults.values()).filter(
                                (v) => v === CompatibilityLevel.Incompatible
                            ).length > 0 &&
                            ` (${
                                Array.from(compatibilityResults.values()).filter(
                                    (v) => v === CompatibilityLevel.Incompatible
                                ).length
                            } invalid)`}
                    </Text>
                }
            />

            <Toolbar className={styles.toolbar}>
                <ToolbarButton
                    appearance="primary"
                    icon={<Add20Regular />}
                    onClick={onAddRule}
                    disabled={disabled}
                >
                    Add Rule
                </ToolbarButton>
            </Toolbar>

            {rules.length === 0 ? (
                <div className={styles.emptyState}>
                    <Text size={300}>No mapping rules configured</Text>
                    <Text size={200}>
                        Click "Add Rule" to create your first field mapping
                    </Text>
                </div>
            ) : (
                <div className={styles.gridContainer}>
                    <DataGrid
                        items={rules}
                        columns={columns}
                        getRowId={(item) => item.id}
                        sortable
                    >
                        <DataGridHeader>
                            <DataGridRow>
                                {({ renderHeaderCell }) => (
                                    <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                                )}
                            </DataGridRow>
                        </DataGridHeader>
                        <DataGridBody<IFieldMappingRule>>
                            {({ item, rowId }) => (
                                <DataGridRow<IFieldMappingRule>
                                    key={rowId}
                                    className={getRowClassName(item)}
                                >
                                    {({ renderCell }) => (
                                        <DataGridCell>{renderCell(item)}</DataGridCell>
                                    )}
                                </DataGridRow>
                            )}
                        </DataGridBody>
                    </DataGrid>
                </div>
            )}
        </Card>
    );
};

export default RulesList;
