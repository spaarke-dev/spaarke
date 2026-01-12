/**
 * ModelSelector - AI Model deployment selection for AI nodes
 *
 * Dropdown component for selecting AI model deployments.
 * Only shown for aiAnalysis and aiCompletion node types.
 * Uses Fluent UI v9 Dropdown following ADR-021.
 */

import * as React from 'react';
import { useCallback, useMemo } from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Label,
  Dropdown,
  Option,
  Badge,
  shorthands,
} from '@fluentui/react-components';
import type {
  DropdownProps,
  SelectionEvents,
  OptionOnSelectData,
} from '@fluentui/react-components';
import {
  useModelStore,
  useCanvasStore,
  type ModelDeploymentItem,
  type PlaybookNodeData,
} from '../../stores';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  optionContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  optionHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  optionDescription: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  badge: {
    ...shorthands.padding('2px', '6px'),
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// Badge colors for providers
const providerBadgeColors: Record<string, 'brand' | 'success' | 'warning'> = {
  AzureOpenAI: 'brand',
  OpenAI: 'success',
  Anthropic: 'warning',
};

interface ModelSelectorProps {
  nodeId: string;
  selectedModelId?: string;
  onUpdate: (nodeId: string, data: Partial<PlaybookNodeData>) => void;
}

/**
 * Dropdown component for selecting AI model deployment.
 * Displays model name with provider badge and description.
 */
export const ModelSelector = React.memo(function ModelSelector({
  nodeId,
  selectedModelId,
  onUpdate,
}: ModelSelectorProps) {
  const styles = useStyles();

  // Get chat-capable models from store
  const getChatModels = useModelStore((state) => state.getChatModels);
  const getModelById = useModelStore((state) => state.getModelById);

  const chatModels = useMemo(() => getChatModels(), [getChatModels]);
  const selectedModel = useMemo(
    () => (selectedModelId ? getModelById(selectedModelId) : undefined),
    [selectedModelId, getModelById]
  );

  // Handle model selection change
  const handleModelChange: DropdownProps['onOptionSelect'] = useCallback(
    (_event: SelectionEvents, data: OptionOnSelectData) => {
      onUpdate(nodeId, { modelDeploymentId: data.optionValue || undefined });
    },
    [nodeId, onUpdate]
  );

  return (
    <div className={styles.container}>
      <Label htmlFor={`model-selector-${nodeId}`} size="small">
        AI Model
      </Label>
      <Dropdown
        id={`model-selector-${nodeId}`}
        placeholder="Select a model..."
        value={selectedModel?.name || ''}
        selectedOptions={selectedModelId ? [selectedModelId] : []}
        onOptionSelect={handleModelChange}
        size="small"
      >
        <Option key="none" value="" text="(Default)">
          <div className={styles.optionContent}>
            <span>(Default - GPT-4o)</span>
            <Text className={styles.optionDescription}>
              Uses the default model for this action type
            </Text>
          </div>
        </Option>
        {chatModels.map((model) => (
          <Option key={model.id} value={model.id} text={model.name}>
            <div className={styles.optionContent}>
              <div className={styles.optionHeader}>
                <span>{model.name}</span>
                <Badge
                  appearance="tint"
                  color={providerBadgeColors[model.provider] || 'brand'}
                  size="small"
                  className={styles.badge}
                >
                  {model.provider}
                </Badge>
              </div>
              {model.description && (
                <Text className={styles.optionDescription}>
                  {model.description}
                </Text>
              )}
            </div>
          </Option>
        ))}
      </Dropdown>
      <Text className={styles.hint}>
        {selectedModel
          ? `Context: ${(selectedModel.contextWindow / 1000).toFixed(0)}k tokens`
          : 'Choose an AI model for this node'}
      </Text>
    </div>
  );
});
