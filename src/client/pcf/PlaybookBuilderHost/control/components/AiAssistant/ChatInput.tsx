/**
 * Chat Input Component - Text input with send button for AI chat
 *
 * Features:
 * - Multi-line text input (Textarea)
 * - Send button with icon
 * - Enter to send, Shift+Enter for newline
 * - Disabled state during streaming
 * - Clears input after send
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useCallback, useState } from 'react';
import {
  Textarea,
  Button,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { Send20Regular } from '@fluentui/react-icons';
import { useAiAssistantStore } from '../../stores/aiAssistantStore';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-end',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke1),
  },
  textareaWrapper: {
    flex: 1,
    display: 'flex',
  },
  textarea: {
    width: '100%',
    minHeight: '40px',
    maxHeight: '120px',
    resize: 'none',
  },
  sendButton: {
    minWidth: '40px',
    height: '40px',
  },
  sendButtonDisabled: {
    backgroundColor: tokens.colorNeutralBackgroundDisabled,
    color: tokens.colorNeutralForegroundDisabled,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ChatInputProps {
  /** Callback when user sends a message */
  onSendMessage?: (message: string) => void;
  /** Placeholder text for the input */
  placeholder?: string;
  /** Maximum character length (default: 2000) */
  maxLength?: number;
  /** Disable input manually (in addition to streaming state) */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ChatInput: React.FC<ChatInputProps> = ({
  onSendMessage,
  placeholder = 'Type a message... (Enter to send, Shift+Enter for newline)',
  maxLength = 2000,
  disabled = false,
}) => {
  const styles = useStyles();
  const [message, setMessage] = useState('');

  // Store state - only need isStreaming here
  // Note: addUserMessage is called by sendMessage in the store, so we don't call it here
  const { isStreaming } = useAiAssistantStore();

  // Determine if input should be disabled
  const isDisabled = disabled || isStreaming;

  // Handle message submission
  const handleSend = useCallback(() => {
    const trimmedMessage = message.trim();
    if (!trimmedMessage || isDisabled) return;

    // Call callback - the store's sendMessage will add the user message
    if (onSendMessage) {
      onSendMessage(trimmedMessage);
    }

    // Clear input
    setMessage('');
  }, [message, isDisabled, onSendMessage]);

  // Handle key press (Enter to send, Shift+Enter for newline)
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend]
  );

  // Handle text change
  const handleChange = useCallback(
    (
      _: React.ChangeEvent<HTMLTextAreaElement>,
      data: { value: string }
    ) => {
      if (data.value.length <= maxLength) {
        setMessage(data.value);
      }
    },
    [maxLength]
  );

  // Handle send button click
  const handleSendClick = useCallback(() => {
    handleSend();
  }, [handleSend]);

  // Check if send button should be disabled
  const isSendDisabled = isDisabled || !message.trim();

  return (
    <div className={styles.container}>
      <div className={styles.textareaWrapper}>
        <Textarea
          className={styles.textarea}
          value={message}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          disabled={isDisabled}
          resize="vertical"
          aria-label="Chat message input"
          aria-describedby="chat-input-hint"
        />
      </div>
      <Button
        appearance="primary"
        icon={<Send20Regular />}
        onClick={handleSendClick}
        disabled={isSendDisabled}
        className={styles.sendButton}
        aria-label="Send message"
        title={isStreaming ? 'Wait for response' : 'Send message'}
      />
      {/* Hidden hint for screen readers */}
      <span id="chat-input-hint" hidden>
        Press Enter to send, Shift+Enter for new line
      </span>
    </div>
  );
};

export default ChatInput;
