/**
 * SummaryPanel — Spaarke AI chat panel for the right column of the workspace.
 *
 * Scaffold implementation: local state only, canned assistant responses.
 * Will be connected to Spaarke AI backend in a follow-up iteration.
 *
 * Layout:
 *   [Header: Sparkle icon + "Spaarke" + New Chat button]
 *   [Message area / Empty state with suggestion chips]
 *   [Input: Textarea + Send button]
 *
 * Behaviour:
 *   - Send adds user message, clears input, after 1s adds canned assistant reply
 *   - New Chat clears messages, returns to empty state
 *   - Suggestion chips populate the input field
 *   - Enter to send, Shift+Enter for newline
 *   - Auto-scroll to bottom on new messages
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported automatically via token system
 */

import * as React from "react";
import {
  makeStyles,
  shorthands,
  tokens,
  Text,
  Button,
  Textarea,
} from "@fluentui/react-components";
import {
  SparkleRegular,
  SendRegular,
  AddRegular,
} from "@fluentui/react-icons";
import type { IWebApi } from "../../types/xrm";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface IChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  timestamp: Date;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const SUGGESTION_CHIPS = [
  "Summarize my portfolio",
  "Show overdue matters",
  "What needs attention today?",
];

const CANNED_RESPONSE =
  "I'm not connected to Spaarke AI yet. This chat interface will be live in the next iteration \u2014 stay tuned!";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  panel: {
    display: "flex",
    flexDirection: "column",
    flex: "1 1 auto",
    minHeight: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderWidth("1px"),
    ...shorthands.borderStyle("solid"),
    ...shorthands.borderColor(tokens.colorNeutralStroke2),
    borderRadius: tokens.borderRadiusMedium,
    overflow: "hidden",
  },

  // ── Header ──────────────────────────────────────────────────────────────
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  headerIcon: {
    color: tokens.colorBrandForeground1,
    fontSize: "20px",
    display: "flex",
    alignItems: "center",
  },
  headerTitle: {
    flex: "1 1 auto",
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  // ── Messages area ───────────────────────────────────────────────────────
  messagesArea: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflowY: "auto",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalM,
  },

  // ── Empty state ─────────────────────────────────────────────────────────
  emptyState: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    textAlign: "center",
  },
  emptyIcon: {
    color: tokens.colorNeutralForeground4,
    fontSize: "40px",
    display: "flex",
    alignItems: "center",
  },
  emptyTitle: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
  },
  emptyDescription: {
    color: tokens.colorNeutralForeground3,
    maxWidth: "280px",
  },
  chipContainer: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
    width: "100%",
    maxWidth: "280px",
    marginTop: tokens.spacingVerticalS,
  },
  chip: {
    justifyContent: "flex-start",
    textAlign: "left",
  },

  // ── Message bubbles ─────────────────────────────────────────────────────
  messageBubbleUser: {
    alignSelf: "flex-end",
    maxWidth: "85%",
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  messageBubbleAssistant: {
    alignSelf: "flex-start",
    maxWidth: "85%",
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
    borderRadius: tokens.borderRadiusMedium,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  messageText: {
    whiteSpace: "pre-wrap",
    wordBreak: "break-word",
  },
  messageTime: {
    marginTop: tokens.spacingVerticalXXS,
    color: tokens.colorNeutralForeground4,
  },
  messageTimeUser: {
    marginTop: tokens.spacingVerticalXXS,
    color: tokens.colorNeutralForegroundOnBrand,
    opacity: "0.7",
  },

  // ── Input area ──────────────────────────────────────────────────────────
  inputArea: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  inputRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-end",
    gap: tokens.spacingHorizontalS,
  },
  textarea: {
    flex: "1 1 auto",
  },
  inputHint: {
    color: tokens.colorNeutralForeground4,
    textAlign: "right",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummaryPanelProps {
  /** Xrm.WebApi reference — will be used for AI context in future iteration */
  webApi: IWebApi;
  /** Current user GUID — will be used for AI context in future iteration */
  userId: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

let messageIdCounter = 0;
function nextMessageId(): string {
  return `msg-${++messageIdCounter}`;
}

export const SummaryPanel: React.FC<ISummaryPanelProps> = ({
  webApi: _webApi,
  userId: _userId,
}) => {
  const styles = useStyles();
  const [messages, setMessages] = React.useState<IChatMessage[]>([]);
  const [inputValue, setInputValue] = React.useState("");
  const messagesEndRef = React.useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom when new messages arrive
  React.useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // ── Send message ──────────────────────────────────────────────────────
  const sendMessage = React.useCallback((text: string) => {
    const trimmed = text.trim();
    if (!trimmed) return;

    const userMessage: IChatMessage = {
      id: nextMessageId(),
      role: "user",
      content: trimmed,
      timestamp: new Date(),
    };

    setMessages((prev) => [...prev, userMessage]);
    setInputValue("");

    // Canned assistant response after 1s delay (scaffold only)
    setTimeout(() => {
      const assistantMessage: IChatMessage = {
        id: nextMessageId(),
        role: "assistant",
        content: CANNED_RESPONSE,
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, assistantMessage]);
    }, 1000);
  }, []);

  // ── Keyboard handler: Enter to send, Shift+Enter for newline ──────────
  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        sendMessage(inputValue);
      }
    },
    [sendMessage, inputValue]
  );

  const handleSendClick = React.useCallback(() => {
    sendMessage(inputValue);
  }, [sendMessage, inputValue]);

  // ── New Chat ──────────────────────────────────────────────────────────
  const handleNewChat = React.useCallback(() => {
    setMessages([]);
    setInputValue("");
  }, []);

  // ── Suggestion chip click → populate input ────────────────────────────
  const handleChipClick = React.useCallback((text: string) => {
    setInputValue(text);
  }, []);

  // ── Format time ───────────────────────────────────────────────────────
  const formatTime = (date: Date): string =>
    date.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });

  const hasMessages = messages.length > 0;

  return (
    <div className={styles.panel} role="region" aria-label="Spaarke Assistant">
      {/* ── Header ──────────────────────────────────────────────────── */}
      <div className={styles.header}>
        <span className={styles.headerIcon} aria-hidden="true">
          <SparkleRegular />
        </span>
        <Text className={styles.headerTitle} size={400}>
          Spaarke
        </Text>
        <Button
          appearance="subtle"
          size="small"
          icon={<AddRegular />}
          onClick={handleNewChat}
          aria-label="New chat"
          title="Start a new conversation"
        />
      </div>

      {/* ── Messages / Empty State ──────────────────────────────────── */}
      {hasMessages ? (
        <div className={styles.messagesArea} role="log" aria-live="polite">
          {messages.map((msg) => (
            <div
              key={msg.id}
              className={
                msg.role === "user"
                  ? styles.messageBubbleUser
                  : styles.messageBubbleAssistant
              }
            >
              <Text size={200} className={styles.messageText}>
                {msg.content}
              </Text>
              <Text
                size={100}
                className={
                  msg.role === "user"
                    ? styles.messageTimeUser
                    : styles.messageTime
                }
              >
                {formatTime(msg.timestamp)}
              </Text>
            </div>
          ))}
          <div ref={messagesEndRef} />
        </div>
      ) : (
        <div className={styles.emptyState}>
          <span className={styles.emptyIcon} aria-hidden="true">
            <SparkleRegular />
          </span>
          <Text className={styles.emptyTitle} size={300}>
            Spaarke Assistant
          </Text>
          <Text className={styles.emptyDescription} size={200}>
            Ask questions about your portfolio, get summaries, or explore your
            legal operations data using natural language.
          </Text>
          <div className={styles.chipContainer}>
            {SUGGESTION_CHIPS.map((chip) => (
              <Button
                key={chip}
                appearance="outline"
                size="small"
                className={styles.chip}
                onClick={() => handleChipClick(chip)}
              >
                {chip}
              </Button>
            ))}
          </div>
        </div>
      )}

      {/* ── Input Area ──────────────────────────────────────────────── */}
      <div className={styles.inputArea}>
        <div className={styles.inputRow}>
          <Textarea
            className={styles.textarea}
            placeholder="Ask Spaarke anything..."
            value={inputValue}
            onChange={(_e, data) => setInputValue(data.value)}
            onKeyDown={handleKeyDown}
            resize="none"
            rows={1}
            aria-label="Chat message input"
          />
          <Button
            appearance="primary"
            size="small"
            icon={<SendRegular />}
            onClick={handleSendClick}
            disabled={!inputValue.trim()}
            aria-label="Send message"
          />
        </div>
        <Text size={100} className={styles.inputHint}>
          Enter to send, Shift+Enter for new line
        </Text>
      </div>
    </div>
  );
};

SummaryPanel.displayName = "SummaryPanel";
