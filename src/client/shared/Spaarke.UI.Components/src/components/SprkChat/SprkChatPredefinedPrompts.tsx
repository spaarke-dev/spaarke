/**
 * SprkChatPredefinedPrompts - Predefined prompt suggestion buttons
 *
 * Displays a set of clickable prompt buttons shown before the user sends their first message.
 * Clicking a prompt inserts the full prompt text as a message.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Button,
    Text,
} from "@fluentui/react-components";
import { LightbulbRegular } from "@fluentui/react-icons";
import { ISprkChatPredefinedPromptsProps } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingVerticalM),
        ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalM),
    },
    heading: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
    },
    promptGrid: {
        display: "flex",
        flexWrap: "wrap",
        justifyContent: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        maxWidth: "100%",
    },
    promptButton: {
        maxWidth: "280px",
        textAlign: "left",
        whiteSpace: "normal",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatPredefinedPrompts - Shows clickable prompt suggestions.
 *
 * @example
 * ```tsx
 * <SprkChatPredefinedPrompts
 *   prompts={[
 *     { key: "summary", label: "Summarize this document", prompt: "Summarize..." },
 *     { key: "review", label: "Review for issues", prompt: "Review..." }
 *   ]}
 *   onSelect={(prompt) => sendMessage(prompt)}
 * />
 * ```
 */
export const SprkChatPredefinedPrompts: React.FC<ISprkChatPredefinedPromptsProps> = ({
    prompts,
    onSelect,
    disabled = false,
}) => {
    const styles = useStyles();

    if (prompts.length === 0) {
        return null;
    }

    return (
        <div className={styles.root} role="region" aria-label="Suggested prompts">
            <div style={{ display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXS }}>
                <LightbulbRegular />
                <Text className={styles.heading}>Try asking</Text>
            </div>

            <div className={styles.promptGrid}>
                {prompts.map((prompt) => (
                    <Button
                        key={prompt.key}
                        appearance="outline"
                        className={styles.promptButton}
                        onClick={() => onSelect(prompt.prompt)}
                        disabled={disabled}
                        data-testid={`predefined-prompt-${prompt.key}`}
                    >
                        {prompt.label}
                    </Button>
                ))}
            </div>
        </div>
    );
};

export default SprkChatPredefinedPrompts;
