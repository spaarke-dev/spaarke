/**
 * SprkChatInput - Chat input area with send button
 *
 * Features:
 * - Textarea with send button
 * - Ctrl+Enter to send (Enter for newline)
 * - Disabled during streaming
 * - Character count indicator
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Textarea,
    Button,
    Text,
} from "@fluentui/react-components";
import { SendRegular } from "@fluentui/react-icons";
import { ISprkChatInputProps } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke1),
        backgroundColor: tokens.colorNeutralBackground1,
    },
    inputRow: {
        display: "flex",
        alignItems: "flex-end",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    textarea: {
        flexGrow: 1,
    },
    footer: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    charCount: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    charCountWarning: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorPaletteRedForeground1,
    },
    hint: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_MAX_CHAR_COUNT = 2000;

/**
 * SprkChatInput - Textarea with send button, Ctrl+Enter shortcut, and character counter.
 *
 * @example
 * ```tsx
 * <SprkChatInput
 *   onSend={(msg) => handleSend(msg)}
 *   disabled={isStreaming}
 *   maxCharCount={2000}
 * />
 * ```
 */
export const SprkChatInput: React.FC<ISprkChatInputProps> = ({
    onSend,
    disabled = false,
    maxCharCount = DEFAULT_MAX_CHAR_COUNT,
    placeholder = "Type a message...",
}) => {
    const styles = useStyles();
    const [value, setValue] = React.useState<string>("");

    const charCount = value.length;
    const isOverLimit = charCount > maxCharCount;
    const canSend = value.trim().length > 0 && !disabled && !isOverLimit;

    const handleSend = React.useCallback(() => {
        const trimmed = value.trim();
        if (trimmed && !disabled && !isOverLimit) {
            onSend(trimmed);
            setValue("");
        }
    }, [value, disabled, isOverLimit, onSend]);

    const handleKeyDown = React.useCallback(
        (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
            if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                handleSend();
            }
        },
        [handleSend]
    );

    const handleChange = React.useCallback(
        (_event: React.ChangeEvent<HTMLTextAreaElement>, data: { value: string }) => {
            setValue(data.value);
        },
        []
    );

    return (
        <div className={styles.root} role="form" aria-label="Chat input">
            <div className={styles.inputRow}>
                <Textarea
                    className={styles.textarea}
                    value={value}
                    onChange={handleChange}
                    onKeyDown={handleKeyDown}
                    placeholder={placeholder}
                    disabled={disabled}
                    resize="vertical"
                    aria-label="Message input"
                    data-testid="chat-input-textarea"
                />
                <Button
                    appearance="primary"
                    icon={<SendRegular />}
                    onClick={handleSend}
                    disabled={!canSend}
                    aria-label="Send message"
                    data-testid="chat-send-button"
                />
            </div>
            <div className={styles.footer}>
                <Text className={styles.hint}>
                    Ctrl+Enter to send
                </Text>
                <Text
                    className={
                        isOverLimit ? styles.charCountWarning : styles.charCount
                    }
                >
                    {charCount}/{maxCharCount}
                </Text>
            </div>
        </div>
    );
};

export default SprkChatInput;
