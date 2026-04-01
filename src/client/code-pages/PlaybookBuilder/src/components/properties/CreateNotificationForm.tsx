/**
 * CreateNotificationForm - Configuration form for Create Notification nodes.
 *
 * Allows users to configure in-app notification creation:
 * - Title (supports template variables)
 * - Body (supports template variables)
 * - Category (Tasks, Documents, Email, Events, Matters, System)
 * - Priority (Low, Normal, High, Urgent)
 * - Action URL (optional deep-link)
 * - Regarding field (record reference, supports template variables)
 *
 * @see ADR-021 - Fluent UI v9 design system (dark mode required)
 */

import { useCallback, useMemo, memo } from 'react';
import { makeStyles, tokens, Text, Input, Label, Textarea, Dropdown, Option } from '@fluentui/react-components';
import type { DropdownProps, OptionOnSelectData, SelectionEvents } from '@fluentui/react-components';
import type { NodeFormProps } from '../../types/forms';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
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
  bodyArea: {
    minHeight: '100px',
  },
});

// ---------------------------------------------------------------------------
// Config shape
// ---------------------------------------------------------------------------

const CATEGORY_OPTIONS = ['Tasks', 'Documents', 'Email', 'Events', 'Matters', 'System'] as const;
type NotificationCategory = (typeof CATEGORY_OPTIONS)[number];

const PRIORITY_OPTIONS = ['Low', 'Normal', 'High', 'Urgent'] as const;
type NotificationPriority = (typeof PRIORITY_OPTIONS)[number];

interface CreateNotificationConfig {
  title: string;
  body: string;
  category: NotificationCategory;
  priority: NotificationPriority;
  actionUrl: string;
  regardingField: string;
}

const DEFAULT_CONFIG: CreateNotificationConfig = {
  title: '',
  body: '',
  category: 'System',
  priority: 'Normal',
  actionUrl: '',
  regardingField: '',
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function parseConfig(json: string): CreateNotificationConfig {
  try {
    const parsed = JSON.parse(json) as Partial<CreateNotificationConfig>;
    return {
      title: typeof parsed.title === 'string' ? parsed.title : DEFAULT_CONFIG.title,
      body: typeof parsed.body === 'string' ? parsed.body : DEFAULT_CONFIG.body,
      category: CATEGORY_OPTIONS.includes(parsed.category as NotificationCategory)
        ? (parsed.category as NotificationCategory)
        : DEFAULT_CONFIG.category,
      priority: PRIORITY_OPTIONS.includes(parsed.priority as NotificationPriority)
        ? (parsed.priority as NotificationPriority)
        : DEFAULT_CONFIG.priority,
      actionUrl: typeof parsed.actionUrl === 'string' ? parsed.actionUrl : DEFAULT_CONFIG.actionUrl,
      regardingField: typeof parsed.regardingField === 'string' ? parsed.regardingField : DEFAULT_CONFIG.regardingField,
    };
  } catch {
    return { ...DEFAULT_CONFIG };
  }
}

function serializeConfig(config: CreateNotificationConfig): string {
  return JSON.stringify(config);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateNotificationForm = memo(function CreateNotificationForm({
  nodeId,
  configJson,
  onConfigChange,
}: NodeFormProps) {
  const styles = useStyles();
  const config = useMemo(() => parseConfig(configJson), [configJson]);

  const update = useCallback(
    (patch: Partial<CreateNotificationConfig>) => {
      onConfigChange(serializeConfig({ ...config, ...patch }));
    },
    [config, onConfigChange]
  );

  // -- Handlers --

  const handleTitleChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ title: e.target.value });
    },
    [update]
  );

  const handleBodyChange = useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      update({ body: e.target.value });
    },
    [update]
  );

  const handleCategoryChange: DropdownProps['onOptionSelect'] = useCallback(
    (_event: SelectionEvents, data: OptionOnSelectData) => {
      if (data.optionValue) {
        update({ category: data.optionValue as NotificationCategory });
      }
    },
    [update]
  );

  const handlePriorityChange: DropdownProps['onOptionSelect'] = useCallback(
    (_event: SelectionEvents, data: OptionOnSelectData) => {
      if (data.optionValue) {
        update({ priority: data.optionValue as NotificationPriority });
      }
    },
    [update]
  );

  const handleActionUrlChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ actionUrl: e.target.value });
    },
    [update]
  );

  const handleRegardingFieldChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      update({ regardingField: e.target.value });
    },
    [update]
  );

  // -- Render --

  return (
    <div className={styles.form}>
      {/* Title */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-title`} size="small" required>
          Title
        </Label>
        <Input
          id={`${nodeId}-title`}
          size="small"
          value={config.title}
          onChange={handleTitleChange}
          placeholder="e.g., New document uploaded: {{analysis.output.title}}"
        />
        <Text className={styles.fieldHint}>Supports template variables: {'{{nodeName.output.fieldName}}'}</Text>
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
          placeholder={'Notification details...\nCan reference: {{nodeName.output.fieldName}}'}
          resize="vertical"
        />
        <Text className={styles.fieldHint}>Supports template variables: {'{{nodeName.output.fieldName}}'}</Text>
      </div>

      {/* Category */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-category`} size="small">
          Category
        </Label>
        <Dropdown
          id={`${nodeId}-category`}
          size="small"
          value={config.category}
          selectedOptions={[config.category]}
          onOptionSelect={handleCategoryChange}
        >
          {CATEGORY_OPTIONS.map(cat => (
            <Option key={cat} value={cat}>
              {cat}
            </Option>
          ))}
        </Dropdown>
        <Text className={styles.fieldHint}>Notification channel category for the Daily Digest grouping</Text>
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
          {PRIORITY_OPTIONS.map(level => (
            <Option key={level} value={level}>
              {level}
            </Option>
          ))}
        </Dropdown>
        <Text className={styles.fieldHint}>Urgent and High priority notifications appear prominently</Text>
      </div>

      {/* Action URL */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-actionUrl`} size="small">
          Action URL
        </Label>
        <Input
          id={`${nodeId}-actionUrl`}
          size="small"
          value={config.actionUrl}
          onChange={handleActionUrlChange}
          placeholder="e.g., /main.aspx?etn=sprk_matter&id={{trigger.output.matterId}}"
        />
        <Text className={styles.fieldHint}>
          Deep-link URL opened when user clicks the notification. Supports template variables.
        </Text>
      </div>

      {/* Regarding Field */}
      <div className={styles.field}>
        <Label htmlFor={`${nodeId}-regardingField`} size="small">
          Regarding Record
        </Label>
        <Input
          id={`${nodeId}-regardingField`}
          size="small"
          value={config.regardingField}
          onChange={handleRegardingFieldChange}
          placeholder="e.g., {{trigger.output.recordId}} or sprk_matter:guid"
        />
        <Text className={styles.fieldHint}>
          Record reference (entity:id) that this notification is about. Supports template variables.
        </Text>
      </div>
    </div>
  );
});
