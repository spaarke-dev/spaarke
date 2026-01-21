/**
 * Clarification Options Component - Interactive multiple-choice UI for AI clarifications
 *
 * Displays clarification questions from the AI assistant with:
 * - Selectable option buttons
 * - "Other" option with free-text input
 * - Context display
 * - Responsive styling matching chat message aesthetics
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useState, useCallback } from 'react';
import {
  Button,
  Input,
  Text,
  makeStyles,
  tokens,
  shorthands,
  mergeClasses,
} from '@fluentui/react-components';
import {
  CheckmarkCircle20Regular,
  Send20Regular,
  Edit20Regular,
} from '@fluentui/react-icons';
import type { ClarificationData } from '../../stores/aiAssistantStore';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // Container for the entire clarification UI
  container: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
    ...shorthands.padding(tokens.spacingVerticalS, '0'),
    maxWidth: '100%',
  },
  // Context text display
  contextText: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    fontStyle: 'italic',
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRadius(tokens.borderRadiusSmall),
  },
  // Options container
  optionsContainer: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  // Individual option button
  optionButton: {
    justifyContent: 'flex-start',
    textAlign: 'left',
    minHeight: '40px',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      ...shorthands.borderColor(tokens.colorBrandStroke1),
    },
    ':active': {
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  // Selected option styling
  optionButtonSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.borderColor(tokens.colorBrandStroke1),
    ':hover': {
      backgroundColor: tokens.colorBrandBackground2Hover,
    },
  },
  // Responded (disabled) option styling
  optionButtonResponded: {
    opacity: 0.7,
    cursor: 'default',
  },
  // Option text
  optionText: {
    flex: 1,
    wordBreak: 'break-word',
  },
  // Option number badge
  optionNumber: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '24px',
    height: '24px',
    minWidth: '24px',
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    ...shorthands.borderRadius(tokens.borderRadiusCircular),
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    marginRight: tokens.spacingHorizontalS,
  },
  optionNumberSelected: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  // Other option section
  otherSection: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
    ...shorthands.padding(tokens.spacingVerticalS, '0'),
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    marginTop: tokens.spacingVerticalXS,
  },
  // Other input row
  otherInputRow: {
    display: 'flex',
    ...shorthands.gap(tokens.spacingHorizontalS),
    alignItems: 'flex-start',
  },
  // Other text input
  otherInput: {
    flex: 1,
    minHeight: '36px',
  },
  // Submit button for other option
  submitButton: {
    minWidth: '80px',
  },
  // Responded indicator
  respondedIndicator: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    color: tokens.colorStatusSuccessForeground1,
    fontSize: tokens.fontSizeBase200,
    marginTop: tokens.spacingVerticalXS,
  },
  // Selected response display
  selectedResponse: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorBrandStroke1),
  },
  selectedResponseText: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ClarificationOptionsProps {
  /** The clarification data from the message */
  clarification: ClarificationData;
  /** Message ID for responding */
  messageId: string;
  /** Callback when user selects an option */
  onRespond: (
    messageId: string,
    response: { selectedOption: number | 'other'; freeText?: string }
  ) => void;
  /** Whether the component is disabled (e.g., during streaming) */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ClarificationOptions: React.FC<ClarificationOptionsProps> = ({
  clarification,
  messageId,
  onRespond,
  disabled = false,
}) => {
  const styles = useStyles();
  const [selectedOption, setSelectedOption] = useState<number | 'other' | null>(null);
  const [otherText, setOtherText] = useState('');
  const [showOtherInput, setShowOtherInput] = useState(false);

  // If already responded, show the selected response
  const hasResponded = clarification.responded === true;

  // Handle option click
  const handleOptionClick = useCallback(
    (index: number) => {
      if (hasResponded || disabled) return;

      setSelectedOption(index);
      setShowOtherInput(false);
      // Immediately send the response
      onRespond(messageId, { selectedOption: index });
    },
    [hasResponded, disabled, messageId, onRespond]
  );

  // Handle other click
  const handleOtherClick = useCallback(() => {
    if (hasResponded || disabled) return;

    setSelectedOption('other');
    setShowOtherInput(true);
  }, [hasResponded, disabled]);

  // Handle other text submission
  const handleOtherSubmit = useCallback(() => {
    if (hasResponded || disabled || !otherText.trim()) return;

    onRespond(messageId, { selectedOption: 'other', freeText: otherText.trim() });
  }, [hasResponded, disabled, otherText, messageId, onRespond]);

  // Handle enter key in other input
  const handleOtherKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleOtherSubmit();
      }
    },
    [handleOtherSubmit]
  );

  // Get the selected response text for display
  const getSelectedResponseText = (): string => {
    if (!hasResponded) return '';
    if (clarification.selectedOption === 'other') {
      return clarification.freeTextResponse ?? '';
    }
    const options = clarification.options ?? [];
    const optionIndex = clarification.selectedOption as number;
    return options[optionIndex] ?? `Option ${optionIndex + 1}`;
  };

  // If responded, show compact view with selected response
  if (hasResponded) {
    return (
      <div className={styles.container}>
        {/* Context if provided */}
        {clarification.context && (
          <Text className={styles.contextText}>{clarification.context}</Text>
        )}

        {/* Show selected response */}
        <div className={styles.selectedResponse}>
          <CheckmarkCircle20Regular />
          <Text className={styles.selectedResponseText}>
            {getSelectedResponseText()}
          </Text>
        </div>
      </div>
    );
  }

  // Show interactive options
  const options = clarification.options ?? [];

  return (
    <div className={styles.container} role="group" aria-label="Clarification options">
      {/* Context if provided */}
      {clarification.context && (
        <Text className={styles.contextText}>{clarification.context}</Text>
      )}

      {/* Options list */}
      {options.length > 0 && (
        <div className={styles.optionsContainer} role="list">
          {options.map((option, index) => (
            <Button
              key={`option-${index}`}
              className={mergeClasses(
                styles.optionButton,
                selectedOption === index && styles.optionButtonSelected,
                disabled && styles.optionButtonResponded
              )}
              appearance="subtle"
              disabled={disabled}
              onClick={() => handleOptionClick(index)}
              aria-label={`Option ${index + 1}: ${option}`}
              role="listitem"
            >
              <span
                className={mergeClasses(
                  styles.optionNumber,
                  selectedOption === index && styles.optionNumberSelected
                )}
              >
                {index + 1}
              </span>
              <Text className={styles.optionText}>{option}</Text>
            </Button>
          ))}
        </div>
      )}

      {/* Other option section */}
      <div className={styles.otherSection}>
        {!showOtherInput ? (
          <Button
            className={mergeClasses(
              styles.optionButton,
              selectedOption === 'other' && styles.optionButtonSelected,
              disabled && styles.optionButtonResponded
            )}
            appearance="subtle"
            disabled={disabled}
            onClick={handleOtherClick}
            icon={<Edit20Regular />}
            aria-label="Enter a different response"
          >
            <Text className={styles.optionText}>Other (type your response)</Text>
          </Button>
        ) : (
          <div className={styles.otherInputRow}>
            <Input
              className={styles.otherInput}
              placeholder="Type your response..."
              value={otherText}
              onChange={(e, data) => setOtherText(data.value)}
              onKeyDown={handleOtherKeyDown}
              disabled={disabled}
              autoFocus
              aria-label="Custom response input"
            />
            <Button
              className={styles.submitButton}
              appearance="primary"
              disabled={disabled || !otherText.trim()}
              onClick={handleOtherSubmit}
              icon={<Send20Regular />}
              aria-label="Submit custom response"
            >
              Send
            </Button>
          </div>
        )}
      </div>
    </div>
  );
};

export default ClarificationOptions;
