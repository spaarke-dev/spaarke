/**
 * ConditionEditor - Visual expression builder for condition nodes.
 *
 * Provides a user-friendly UI for building condition expressions
 * that match the ConditionNodeExecutor JSON syntax.
 *
 * Condition syntax:
 * {
 *   "condition": { "operator": "eq", "left": "{{var}}", "right": "value" },
 *   "trueBranch": "branchA",
 *   "falseBranch": "branchB"
 * }
 */

import * as React from 'react';
import { useCallback, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Input,
  Label,
  Dropdown,
  Option,
  shorthands,
  Divider,
} from '@fluentui/react-components';
import type { DropdownProps } from '@fluentui/react-components';
import {
  ArrowSplit20Regular,
  Checkmark20Regular,
  Dismiss20Regular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground2,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
  },
  branchRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
  branchField: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  trueBranchLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorPaletteGreenForeground1,
  },
  falseBranchLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorPaletteRedForeground1,
  },
  operatorRow: {
    display: 'grid',
    gridTemplateColumns: '1fr auto 1fr',
    gap: tokens.spacingHorizontalS,
    alignItems: 'end',
  },
  operatorDropdown: {
    minWidth: '100px',
  },
});

/**
 * Supported condition operators.
 * Maps to ConditionNodeExecutor operators in the backend.
 */
const OPERATORS = [
  { value: 'eq', label: 'equals (==)' },
  { value: 'ne', label: 'not equals (!=)' },
  { value: 'gt', label: 'greater than (>)' },
  { value: 'lt', label: 'less than (<)' },
  { value: 'gte', label: 'greater or equal (>=)' },
  { value: 'lte', label: 'less or equal (<=)' },
  { value: 'contains', label: 'contains' },
  { value: 'startsWith', label: 'starts with' },
  { value: 'endsWith', label: 'ends with' },
  { value: 'exists', label: 'exists (not null)' },
] as const;

type OperatorType = (typeof OPERATORS)[number]['value'];

/**
 * Parsed condition expression structure.
 */
interface ConditionExpression {
  condition: {
    operator: OperatorType;
    left: string;
    right?: string;
  };
  trueBranch: string;
  falseBranch: string;
}

/**
 * Default condition for new nodes.
 */
const DEFAULT_CONDITION: ConditionExpression = {
  condition: {
    operator: 'eq',
    left: '',
    right: '',
  },
  trueBranch: 'truePath',
  falseBranch: 'falsePath',
};

interface ConditionEditorProps {
  /**
   * Current condition JSON string from node data.
   */
  conditionJson: string;
  /**
   * Callback when condition changes.
   */
  onChange: (conditionJson: string) => void;
}

/**
 * Parse condition JSON string to structured expression.
 * Returns default condition if parsing fails.
 */
function parseCondition(json: string): ConditionExpression {
  try {
    const parsed = JSON.parse(json);
    // Handle both new format (with condition object) and legacy format
    if (parsed.condition && typeof parsed.condition === 'object') {
      return {
        condition: {
          operator: parsed.condition.operator || 'eq',
          left: parsed.condition.left || '',
          right: parsed.condition.right ?? '',
        },
        trueBranch: parsed.trueBranch || 'truePath',
        falseBranch: parsed.falseBranch || 'falsePath',
      };
    }
    // Legacy format: { field, operator, value }
    if (parsed.field !== undefined) {
      return {
        condition: {
          operator: mapLegacyOperator(parsed.operator || 'equals'),
          left: `{{${parsed.field}}}`,
          right: parsed.value ?? '',
        },
        trueBranch: 'truePath',
        falseBranch: 'falsePath',
      };
    }
    return DEFAULT_CONDITION;
  } catch {
    return DEFAULT_CONDITION;
  }
}

/**
 * Map legacy operator names to new format.
 */
function mapLegacyOperator(op: string): OperatorType {
  const mapping: Record<string, OperatorType> = {
    equals: 'eq',
    '==': 'eq',
    '!=': 'ne',
    '>': 'gt',
    '<': 'lt',
    '>=': 'gte',
    '<=': 'lte',
  };
  return mapping[op] || 'eq';
}

/**
 * Serialize condition expression to JSON string.
 */
function serializeCondition(expr: ConditionExpression): string {
  return JSON.stringify(expr, null, 2);
}

/**
 * ConditionEditor component - Visual expression builder for condition nodes.
 */
export const ConditionEditor = React.memo(function ConditionEditor({
  conditionJson,
  onChange,
}: ConditionEditorProps) {
  const styles = useStyles();

  // Parse current condition
  const condition = useMemo(() => parseCondition(conditionJson), [conditionJson]);

  // Handlers for updating condition fields
  const handleLeftChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const updated: ConditionExpression = {
        ...condition,
        condition: { ...condition.condition, left: e.target.value },
      };
      onChange(serializeCondition(updated));
    },
    [condition, onChange]
  );

  const handleRightChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const updated: ConditionExpression = {
        ...condition,
        condition: { ...condition.condition, right: e.target.value },
      };
      onChange(serializeCondition(updated));
    },
    [condition, onChange]
  );

  const handleOperatorChange: DropdownProps['onOptionSelect'] = useCallback(
    (_e, data) => {
      const updated: ConditionExpression = {
        ...condition,
        condition: {
          ...condition.condition,
          operator: (data.optionValue as OperatorType) || 'eq',
        },
      };
      onChange(serializeCondition(updated));
    },
    [condition, onChange]
  );

  const handleTrueBranchChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const updated: ConditionExpression = {
        ...condition,
        trueBranch: e.target.value,
      };
      onChange(serializeCondition(updated));
    },
    [condition, onChange]
  );

  const handleFalseBranchChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const updated: ConditionExpression = {
        ...condition,
        falseBranch: e.target.value,
      };
      onChange(serializeCondition(updated));
    },
    [condition, onChange]
  );

  // Check if operator needs right operand
  const needsRightOperand = condition.condition.operator !== 'exists';

  // Find selected operator label
  const selectedOperator = OPERATORS.find(
    (op) => op.value === condition.condition.operator
  );

  return (
    <div className={styles.container}>
      {/* Expression Section */}
      <div className={styles.section}>
        <div className={styles.sectionHeader}>
          <ArrowSplit20Regular />
          <Text size={200} weight="semibold">
            Expression
          </Text>
        </div>

        {/* Left operand */}
        <div className={styles.field}>
          <Label htmlFor="condition-left" size="small">
            Left Value
          </Label>
          <Input
            id="condition-left"
            size="small"
            value={condition.condition.left}
            onChange={handleLeftChange}
            placeholder="{{node.output.field}}"
          />
          <Text className={styles.fieldHint}>
            Use {'{{variableName}}'} for template variables
          </Text>
        </div>

        {/* Operator */}
        <div className={styles.field}>
          <Label htmlFor="condition-operator" size="small">
            Operator
          </Label>
          <Dropdown
            id="condition-operator"
            size="small"
            className={styles.operatorDropdown}
            value={selectedOperator?.label || 'equals (==)'}
            selectedOptions={[condition.condition.operator]}
            onOptionSelect={handleOperatorChange}
          >
            {OPERATORS.map((op) => (
              <Option key={op.value} value={op.value}>
                {op.label}
              </Option>
            ))}
          </Dropdown>
        </div>

        {/* Right operand (hidden for 'exists' operator) */}
        {needsRightOperand && (
          <div className={styles.field}>
            <Label htmlFor="condition-right" size="small">
              Right Value
            </Label>
            <Input
              id="condition-right"
              size="small"
              value={condition.condition.right || ''}
              onChange={handleRightChange}
              placeholder="value or {{variable}}"
            />
          </div>
        )}
      </div>

      <Divider />

      {/* Branch Names Section */}
      <div className={styles.section}>
        <div className={styles.sectionHeader}>
          <Text size={200} weight="semibold">
            Branch Names
          </Text>
        </div>

        <div className={styles.branchRow}>
          <div className={styles.branchField}>
            <Label
              htmlFor="condition-true-branch"
              size="small"
              className={styles.trueBranchLabel}
            >
              <Checkmark20Regular />
              True Branch
            </Label>
            <Input
              id="condition-true-branch"
              size="small"
              value={condition.trueBranch}
              onChange={handleTrueBranchChange}
              placeholder="truePath"
            />
          </div>

          <div className={styles.branchField}>
            <Label
              htmlFor="condition-false-branch"
              size="small"
              className={styles.falseBranchLabel}
            >
              <Dismiss20Regular />
              False Branch
            </Label>
            <Input
              id="condition-false-branch"
              size="small"
              value={condition.falseBranch}
              onChange={handleFalseBranchChange}
              placeholder="falsePath"
            />
          </div>
        </div>

        <Text className={styles.fieldHint}>
          Branch names are used in the orchestrator to route execution
        </Text>
      </div>
    </div>
  );
});
