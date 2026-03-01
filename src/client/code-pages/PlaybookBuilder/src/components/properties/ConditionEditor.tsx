/**
 * ConditionEditor — Visual expression builder for condition nodes.
 *
 * Parses/serializes conditionJson to a structured format:
 *   { condition: { operator, left, right? }, trueBranch, falseBranch }
 *
 * Supports operators: eq, ne, gt, lt, gte, lte, contains, startsWith, endsWith, exists.
 * "exists" is unary (no right operand).
 */

import { memo, useMemo, useCallback } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Input,
    Dropdown,
    Option,
    Label,
    Text,
    Divider,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface ConditionExpression {
    condition: {
        operator: OperatorType;
        left: string;
        right?: string;
    };
    trueBranch: string;
    falseBranch: string;
}

type OperatorType =
    | "eq"
    | "ne"
    | "gt"
    | "lt"
    | "gte"
    | "lte"
    | "contains"
    | "startsWith"
    | "endsWith"
    | "exists";

const OPERATORS: { value: OperatorType; label: string }[] = [
    { value: "eq", label: "Equals (==)" },
    { value: "ne", label: "Not Equals (!=)" },
    { value: "gt", label: "Greater Than (>)" },
    { value: "lt", label: "Less Than (<)" },
    { value: "gte", label: "Greater or Equal (>=)" },
    { value: "lte", label: "Less or Equal (<=)" },
    { value: "contains", label: "Contains" },
    { value: "startsWith", label: "Starts With" },
    { value: "endsWith", label: "Ends With" },
    { value: "exists", label: "Exists (not null)" },
];

interface ConditionEditorProps {
    conditionJson: string;
    onConditionChange: (json: string) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("12px"),
    },
    fieldGroup: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap("4px"),
    },
    branchRow: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap("8px"),
    },
    branchDot: {
        width: "10px",
        height: "10px",
        ...shorthands.borderRadius("50%"),
        flexShrink: 0,
    },
    trueDot: {
        backgroundColor: tokens.colorPaletteGreenForeground1,
    },
    falseDot: {
        backgroundColor: tokens.colorPaletteRedForeground1,
    },
    hint: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseCondition(json: string): ConditionExpression {
    try {
        const parsed = JSON.parse(json);
        // Handle new format
        if (parsed.condition && typeof parsed.condition.operator === "string") {
            return {
                condition: {
                    operator: parsed.condition.operator as OperatorType,
                    left: typeof parsed.condition.left === "string" ? parsed.condition.left : "",
                    right: typeof parsed.condition.right === "string" ? parsed.condition.right : undefined,
                },
                trueBranch: typeof parsed.trueBranch === "string" ? parsed.trueBranch : "True",
                falseBranch: typeof parsed.falseBranch === "string" ? parsed.falseBranch : "False",
            };
        }
        // Handle legacy format { field, operator, value }
        if (typeof parsed.field === "string") {
            return {
                condition: {
                    operator: (parsed.operator as OperatorType) || "eq",
                    left: parsed.field,
                    right: typeof parsed.value === "string" ? parsed.value : String(parsed.value ?? ""),
                },
                trueBranch: typeof parsed.trueBranch === "string" ? parsed.trueBranch : "True",
                falseBranch: typeof parsed.falseBranch === "string" ? parsed.falseBranch : "False",
            };
        }
    } catch {
        // malformed JSON
    }
    return {
        condition: { operator: "eq", left: "", right: "" },
        trueBranch: "True",
        falseBranch: "False",
    };
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ConditionEditor = memo(function ConditionEditor({
    conditionJson,
    onConditionChange,
}: ConditionEditorProps) {
    const styles = useStyles();

    const expression = useMemo(() => parseCondition(conditionJson), [conditionJson]);

    const update = useCallback(
        (patch: Partial<ConditionExpression>) => {
            const updated: ConditionExpression = {
                condition: { ...expression.condition, ...(patch.condition ?? {}) },
                trueBranch: patch.trueBranch ?? expression.trueBranch,
                falseBranch: patch.falseBranch ?? expression.falseBranch,
            };
            onConditionChange(JSON.stringify(updated));
        },
        [expression, onConditionChange],
    );

    const needsRightOperand = expression.condition.operator !== "exists";

    return (
        <div className={styles.root}>
            {/* Left operand */}
            <div className={styles.fieldGroup}>
                <Label size="small">Left Operand</Label>
                <Input
                    size="small"
                    placeholder="{{nodeName.output.field}}"
                    value={expression.condition.left}
                    onChange={(_, data) =>
                        update({ condition: { ...expression.condition, left: data.value } })
                    }
                />
                <Text className={styles.hint}>
                    Use template variable syntax: {"{{nodeName.output.fieldName}}"}
                </Text>
            </div>

            {/* Operator */}
            <div className={styles.fieldGroup}>
                <Label size="small">Operator</Label>
                <Dropdown
                    size="small"
                    value={OPERATORS.find((o) => o.value === expression.condition.operator)?.label ?? "Equals (==)"}
                    selectedOptions={[expression.condition.operator]}
                    onOptionSelect={(_, data) =>
                        update({
                            condition: {
                                ...expression.condition,
                                operator: (data.optionValue as OperatorType) ?? "eq",
                            },
                        })
                    }
                >
                    {OPERATORS.map((op) => (
                        <Option key={op.value} value={op.value}>
                            {op.label}
                        </Option>
                    ))}
                </Dropdown>
            </div>

            {/* Right operand (hidden for 'exists') */}
            {needsRightOperand && (
                <div className={styles.fieldGroup}>
                    <Label size="small">Right Operand</Label>
                    <Input
                        size="small"
                        placeholder="Value or {{variable}}"
                        value={expression.condition.right ?? ""}
                        onChange={(_, data) =>
                            update({ condition: { ...expression.condition, right: data.value } })
                        }
                    />
                </div>
            )}

            <Divider />

            {/* Branch names */}
            <div className={styles.fieldGroup}>
                <Label size="small">Branch Names</Label>
                <div className={styles.branchRow}>
                    <div className={`${styles.branchDot} ${styles.trueDot}`} />
                    <Input
                        size="small"
                        placeholder="True branch name"
                        value={expression.trueBranch}
                        onChange={(_, data) => update({ trueBranch: data.value })}
                        style={{ flex: 1 }}
                    />
                </div>
                <div className={styles.branchRow}>
                    <div className={`${styles.branchDot} ${styles.falseDot}`} />
                    <Input
                        size="small"
                        placeholder="False branch name"
                        value={expression.falseBranch}
                        onChange={(_, data) => update({ falseBranch: data.value })}
                        style={{ flex: 1 }}
                    />
                </div>
            </div>
        </div>
    );
});
