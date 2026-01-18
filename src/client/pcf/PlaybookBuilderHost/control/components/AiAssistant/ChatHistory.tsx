/**
 * Chat History Component - Displays conversation history with AI assistant
 *
 * Shows user and AI messages with:
 * - User messages right-aligned
 * - AI messages left-aligned
 * - Operation badges showing canvas changes
 * - Auto-scroll to latest message
 * - Streaming indicator during AI responses
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useEffect, useRef } from 'react';
import {
  Text,
  Badge,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  makeStyles,
  tokens,
  shorthands,
  mergeClasses,
} from '@fluentui/react-components';
import {
  Bot20Regular,
  Person20Regular,
  CheckmarkCircle12Regular,
  AddCircle12Regular,
  Delete12Regular,
  Edit12Regular,
  Link12Regular,
  DismissRegular,
} from '@fluentui/react-icons';
import {
  useAiAssistantStore,
  type ChatMessage,
  type CanvasOperation,
} from '../../stores/aiAssistantStore';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  // Container
  container: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    ...shorthands.overflow('hidden'),
    backgroundColor: tokens.colorNeutralBackground2,
  },
  // Scrollable message area
  messageList: {
    flex: 1,
    overflowY: 'auto',
    overflowX: 'hidden',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  // Individual message wrapper
  messageWrapper: {
    display: 'flex',
    flexDirection: 'column',
    maxWidth: '85%',
  },
  // User message alignment (right)
  userMessage: {
    alignSelf: 'flex-end',
    alignItems: 'flex-end',
  },
  // Assistant message alignment (left)
  assistantMessage: {
    alignSelf: 'flex-start',
    alignItems: 'flex-start',
  },
  // System message alignment (center)
  systemMessage: {
    alignSelf: 'center',
    alignItems: 'center',
    maxWidth: '90%',
  },
  // Message bubble
  messageBubble: {
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusLarge),
    wordBreak: 'break-word',
    whiteSpace: 'pre-wrap',
  },
  // User bubble styling
  userBubble: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  // Assistant bubble styling
  assistantBubble: {
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1),
  },
  // System bubble styling
  systemBubble: {
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
    fontStyle: 'italic',
    fontSize: tokens.fontSizeBase200,
    textAlign: 'center',
  },
  // Streaming bubble (typing indicator)
  streamingBubble: {
    ...shorthands.borderColor(tokens.colorBrandStroke1),
  },
  // Message header with icon
  messageHeader: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    marginBottom: tokens.spacingVerticalXS,
  },
  messageIcon: {
    color: tokens.colorNeutralForeground3,
  },
  messageIconUser: {
    color: tokens.colorBrandForeground1,
  },
  // Timestamp
  timestamp: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    marginTop: tokens.spacingVerticalXXS,
  },
  // Operation badges container
  operationsContainer: {
    display: 'flex',
    flexWrap: 'wrap',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    marginTop: tokens.spacingVerticalS,
  },
  // Operation badge
  operationBadge: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXXS),
  },
  // Empty state
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    ...shorthands.padding(tokens.spacingVerticalXXL),
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  emptyIcon: {
    fontSize: '32px',
    marginBottom: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground4,
  },
  // Streaming indicator
  streamingIndicator: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderRadius(tokens.borderRadiusLarge),
    ...shorthands.border('1px', 'solid', tokens.colorBrandStroke1),
    alignSelf: 'flex-start',
  },
  streamingText: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  errorBar: {
    marginBottom: tokens.spacingVerticalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Format timestamp for display.
 */
const formatTime = (date: Date): string => {
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
};

/**
 * Get icon and color for operation type.
 */
const getOperationIcon = (type: CanvasOperation['type']) => {
  switch (type) {
    case 'add_node':
      return { icon: <AddCircle12Regular />, color: 'success' as const };
    case 'remove_node':
      return { icon: <Delete12Regular />, color: 'danger' as const };
    case 'update_node':
      return { icon: <Edit12Regular />, color: 'warning' as const };
    case 'add_edge':
      return { icon: <Link12Regular />, color: 'informative' as const };
    case 'remove_edge':
      return { icon: <Delete12Regular />, color: 'subtle' as const };
    default:
      return { icon: <CheckmarkCircle12Regular />, color: 'brand' as const };
  }
};

/**
 * Get operation label for display.
 */
const getOperationLabel = (op: CanvasOperation): string => {
  if (op.description) return op.description;
  switch (op.type) {
    case 'add_node':
      return `Added node${op.nodeId ? ` ${op.nodeId}` : ''}`;
    case 'remove_node':
      return `Removed node${op.nodeId ? ` ${op.nodeId}` : ''}`;
    case 'update_node':
      return `Updated node${op.nodeId ? ` ${op.nodeId}` : ''}`;
    case 'add_edge':
      return `Added edge${op.edgeId ? ` ${op.edgeId}` : ''}`;
    case 'remove_edge':
      return `Removed edge${op.edgeId ? ` ${op.edgeId}` : ''}`;
    default:
      return 'Canvas updated';
  }
};

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface ChatHistoryProps {
  /** Maximum number of messages to display (default: unlimited) */
  maxMessages?: number;
  /** Show timestamps for messages (default: true) */
  showTimestamps?: boolean;
  /** Show message role icons (default: true) */
  showIcons?: boolean;
  /** Show operation badges (default: true) */
  showOperations?: boolean;
  /** Custom empty state message */
  emptyMessage?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ChatHistory: React.FC<ChatHistoryProps> = ({
  maxMessages,
  showTimestamps = true,
  showIcons = true,
  showOperations = true,
  emptyMessage = 'Start a conversation with the AI assistant',
}) => {
  const styles = useStyles();
  const messageListRef = useRef<HTMLDivElement>(null);

  // Store state
  const { messages, isStreaming, streamingState, error, setError } = useAiAssistantStore();

  // Apply message limit if specified
  const displayMessages = maxMessages
    ? messages.slice(-maxMessages)
    : messages;

  // Auto-scroll to bottom when new messages arrive or during streaming
  useEffect(() => {
    if (messageListRef.current) {
      messageListRef.current.scrollTop = messageListRef.current.scrollHeight;
    }
  }, [messages.length, isStreaming]);

  // Render a single message
  const renderMessage = (message: ChatMessage) => {
    const isUser = message.role === 'user';
    const isAssistant = message.role === 'assistant';
    const isSystem = message.role === 'system';

    return (
      <div
        key={message.id}
        className={mergeClasses(
          styles.messageWrapper,
          isUser && styles.userMessage,
          isAssistant && styles.assistantMessage,
          isSystem && styles.systemMessage
        )}
        role="listitem"
      >
        {/* Message header with icon */}
        {showIcons && !isSystem && (
          <div className={styles.messageHeader}>
            {isUser ? (
              <Person20Regular className={mergeClasses(styles.messageIcon, styles.messageIconUser)} />
            ) : (
              <Bot20Regular className={styles.messageIcon} />
            )}
            <Text size={100} className={styles.messageIcon}>
              {isUser ? 'You' : 'AI Assistant'}
            </Text>
          </div>
        )}

        {/* Message bubble */}
        <div
          className={mergeClasses(
            styles.messageBubble,
            isUser && styles.userBubble,
            isAssistant && styles.assistantBubble,
            isSystem && styles.systemBubble,
            message.isStreaming && styles.streamingBubble
          )}
        >
          <Text size={300}>{message.content}</Text>
        </div>

        {/* Operation badges for assistant messages */}
        {showOperations &&
          isAssistant &&
          message.canvasOperations &&
          message.canvasOperations.length > 0 && (
            <div
              className={styles.operationsContainer}
              role="list"
              aria-label="Canvas operations"
            >
              {message.canvasOperations.map((op, idx) => {
                const { icon, color } = getOperationIcon(op.type);
                return (
                  <Badge
                    key={`${message.id}-op-${idx}`}
                    appearance="filled"
                    color={color}
                    size="small"
                    className={styles.operationBadge}
                    icon={icon}
                  >
                    {getOperationLabel(op)}
                  </Badge>
                );
              })}
            </div>
          )}

        {/* Timestamp */}
        {showTimestamps && !isSystem && (
          <Text className={styles.timestamp}>
            {formatTime(message.timestamp)}
          </Text>
        )}
      </div>
    );
  };

  // Render streaming indicator
  const renderStreamingIndicator = () => {
    if (!isStreaming) return null;

    return (
      <div
        className={styles.streamingIndicator}
        role="status"
        aria-live="polite"
        aria-label="AI is responding"
      >
        <Spinner size="tiny" />
        <Text className={styles.streamingText}>
          {streamingState.currentStep || 'AI is thinking...'}
          {streamingState.operationCount > 0 && (
            <span> ({streamingState.operationCount} changes)</span>
          )}
        </Text>
      </div>
    );
  };

  return (
    <div
      className={styles.container}
      role="log"
      aria-label="Chat history"
      aria-live="polite"
    >
      <div
        ref={messageListRef}
        className={styles.messageList}
        role="list"
        aria-label="Messages"
      >
        {/* Error display */}
        {error && (
          <MessageBar
            className={styles.errorBar}
            intent="error"
          >
            <MessageBarBody>
              <MessageBarTitle>Error</MessageBarTitle>
              {error}
            </MessageBarBody>
            <MessageBarActions
              containerAction={
                <Button
                  appearance="transparent"
                  icon={<DismissRegular />}
                  onClick={() => setError(null)}
                  aria-label="Dismiss error"
                />
              }
            />
          </MessageBar>
        )}

        {/* Empty state */}
        {displayMessages.length === 0 && !isStreaming && !error && (
          <div className={styles.emptyState}>
            <div className={styles.emptyIcon}>
              <Bot20Regular />
            </div>
            <Text size={300} weight="semibold">
              No messages yet
            </Text>
            <Text size={200}>{emptyMessage}</Text>
          </div>
        )}

        {/* Message list */}
        {displayMessages.map(renderMessage)}

        {/* Streaming indicator */}
        {renderStreamingIndicator()}
      </div>
    </div>
  );
};

export default ChatHistory;
