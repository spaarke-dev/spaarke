/**
 * Chat Input Component - Text input with send button for AI chat
 *
 * Features:
 * - Multi-line text input (Textarea)
 * - Send button with icon
 * - Enter to send, Shift+Enter for newline
 * - Slash command support with CommandPalette
 * - Disabled state during streaming
 * - Clears input after send
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useCallback, useState, useRef, useEffect } from "react";
import {
    Textarea,
    Button,
    makeStyles,
    tokens,
    shorthands,
} from "@fluentui/react-components";
import { Send20Regular } from "@fluentui/react-icons";
import { useAiAssistantStore } from "../../stores/aiAssistantStore";
import { CommandPalette } from "./CommandPalette";
import { parseSlashCommand, findCommand, type SlashCommand } from "./commands";

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
    },
    inputRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "flex-end",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    },
    textareaWrapper: {
        flex: 1,
        display: "flex",
        position: "relative",
    },
    textarea: {
        width: "100%",
        minHeight: "40px",
        maxHeight: "120px",
        resize: "none",
    },
    sendButton: {
        minWidth: "40px",
        height: "40px",
    },
    sendButtonDisabled: {
        backgroundColor: tokens.colorNeutralBackgroundDisabled,
        color: tokens.colorNeutralForegroundDisabled,
    },
    hint: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        ...shorthands.padding("0", tokens.spacingHorizontalM, tokens.spacingVerticalXS),
    },
});

// ============================================================================
// Props
// ============================================================================

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

// ============================================================================
// Component
// ============================================================================

export const ChatInput: React.FC<ChatInputProps> = ({
    onSendMessage,
    placeholder = "Type a message or / for commands...",
    maxLength = 2000,
    disabled = false,
}) => {
    const styles = useStyles();
    const [message, setMessage] = useState("");
    const [showPalette, setShowPalette] = useState(false);
    const [commandFilter, setCommandFilter] = useState("");
    const textareaRef = useRef<HTMLTextAreaElement>(null);

    // Store state - only need isStreaming here
    const { isStreaming } = useAiAssistantStore();

    // Determine if input should be disabled
    const isDisabled = disabled || isStreaming;

    // Check if current input starts with a slash command
    const checkForSlashCommand = useCallback((value: string) => {
        // Show palette when input starts with '/' and nothing else or partial command
        if (value.startsWith("/")) {
            const spaceIndex = value.indexOf(" ");
            // If there's no space, we're still typing the command name
            if (spaceIndex === -1) {
                setShowPalette(true);
                setCommandFilter(value.slice(1)); // Remove the '/'
                return true;
            }
        }
        setShowPalette(false);
        setCommandFilter("");
        return false;
    }, []);

    // Handle message submission
    const handleSend = useCallback(() => {
        const trimmedMessage = message.trim();
        if (!trimmedMessage || isDisabled) return;

        // Check if it's a slash command
        const parsed = parseSlashCommand(trimmedMessage);
        if (parsed) {
            // Look up the command object
            const command = findCommand(parsed.command);
            if (command) {
                // Execute the command and get the resulting message (or null)
                const result = command.execute(parsed.args);
                if (result) {
                    // Command returned a message to send to AI
                    if (onSendMessage) {
                        onSendMessage(result);
                    }
                }
                // If result is null, command was handled internally (e.g., help)
            } else {
                // Unknown command - send as regular message
                if (onSendMessage) {
                    onSendMessage(trimmedMessage);
                }
            }
        } else {
            // Regular message - send to AI
            if (onSendMessage) {
                onSendMessage(trimmedMessage);
            }
        }

        // Clear input and palette
        setMessage("");
        setShowPalette(false);
        setCommandFilter("");
    }, [message, isDisabled, onSendMessage]);

    // Handle command selection from palette
    const handleCommandSelect = useCallback((command: SlashCommand, _args: string) => {
        // Build the command text
        const commandText = `/${command.name}`;
        // If command expects arguments, just insert and let user complete
        if (command.argsHint) {
            setMessage(commandText + " ");
            setShowPalette(false);
            setCommandFilter("");
            // Focus textarea
            textareaRef.current?.focus();
        } else {
            // No args needed - execute immediately
            const result = command.execute("");
            if (result && onSendMessage) {
                onSendMessage(result);
            }
            setMessage("");
            setShowPalette(false);
            setCommandFilter("");
        }
    }, [onSendMessage]);

    // Handle palette close
    const handlePaletteClose = useCallback(() => {
        setShowPalette(false);
        setCommandFilter("");
    }, []);

    // Handle key press
    const handleKeyDown = useCallback(
        (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
            // If palette is open, let it handle navigation keys
            if (showPalette) {
                if (["ArrowUp", "ArrowDown", "Tab", "Escape"].includes(e.key)) {
                    // These will be handled by CommandPalette
                    return;
                }
                if (e.key === "Enter" && !e.shiftKey) {
                    // Let palette handle Enter for command selection
                    return;
                }
            }

            // Normal behavior
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                handleSend();
            }
        },
        [handleSend, showPalette]
    );

    // Handle text change
    const handleChange = useCallback(
        (
            _: React.ChangeEvent<HTMLTextAreaElement>,
            data: { value: string }
        ) => {
            if (data.value.length <= maxLength) {
                setMessage(data.value);
                checkForSlashCommand(data.value);
            }
        },
        [maxLength, checkForSlashCommand]
    );

    // Handle send button click
    const handleSendClick = useCallback(() => {
        handleSend();
    }, [handleSend]);

    // Close palette when clicking outside
    useEffect(() => {
        const handleClickOutside = (e: MouseEvent) => {
            if (showPalette) {
                const target = e.target as HTMLElement;
                if (!target.closest("[data-command-palette]") &&
                    !target.closest("textarea")) {
                    setShowPalette(false);
                    setCommandFilter("");
                }
            }
        };

        document.addEventListener("mousedown", handleClickOutside);
        return () => document.removeEventListener("mousedown", handleClickOutside);
    }, [showPalette]);

    // Check if send button should be disabled
    const isSendDisabled = isDisabled || !message.trim();

    return (
        <div className={styles.container}>
            {/* Command Palette - appears above input */}
            {showPalette && (
                <CommandPalette
                    isOpen={showPalette}
                    query={commandFilter}
                    onSelectCommand={handleCommandSelect}
                    onClose={handlePaletteClose}
                />
            )}

            <div className={styles.inputRow}>
                <div className={styles.textareaWrapper}>
                    <Textarea
                        ref={textareaRef}
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
                    title={isStreaming ? "Wait for response" : "Send message"}
                />
            </div>

            {/* Hint text */}
            <div className={styles.hint}>
                Type / for commands, Enter to send, Shift+Enter for new line
            </div>

            {/* Hidden hint for screen readers */}
            <span id="chat-input-hint" hidden>
                Press Enter to send, Shift+Enter for new line. Type slash for commands.
            </span>
        </div>
    );
};

export default ChatInput;
