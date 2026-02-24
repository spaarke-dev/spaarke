/**
 * ActionEditor
 *
 * Editor for sprk_analysisaction (Action) records.
 * Renders a Fluent v9 Textarea for the system prompt with:
 *   - Character count display
 *   - Approximate token count (characters ÷ 4, standard GPT approximation)
 *
 * ADR-021: makeStyles for all styling; design tokens; no hardcoded colors.
 * ADR-022: React 16 APIs.
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Label,
    Textarea,
    Text,
    Badge,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IActionEditorProps {
    /** Current system prompt text */
    value: string;
    /** Callback when text changes */
    onChange: (value: string) => void;
    /** Whether the editor is read-only */
    readOnly?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** GPT token approximation: ~4 characters per token */
const CHARS_PER_TOKEN = 4;

/** Warn when approaching typical context limits */
const TOKEN_WARN_THRESHOLD = 2000;
const TOKEN_ERROR_THRESHOLD = 4000;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        width: "100%",
        boxSizing: "border-box",
    },
    labelRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    textarea: {
        width: "100%",
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
    },
    statsRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalXS,
    },
    statLabel: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
    },
    statValue: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

function estimateTokens(text: string): number {
    return Math.ceil(text.length / CHARS_PER_TOKEN);
}

function getTokenBadgeColor(
    tokens: number
): "success" | "warning" | "danger" | "informative" {
    if (tokens >= TOKEN_ERROR_THRESHOLD) return "danger";
    if (tokens >= TOKEN_WARN_THRESHOLD) return "warning";
    return "informative";
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ActionEditor: React.FC<IActionEditorProps> = ({
    value,
    onChange,
    readOnly = false,
}) => {
    const styles = useStyles();
    const textareaRef = React.useRef<HTMLTextAreaElement>(null);

    const charCount = value.length;
    const tokenCount = estimateTokens(value);
    const tokenBadgeColor = getTokenBadgeColor(tokenCount);

    // Calculate height from content rather than DOM measurement.
    // Fluent v9 Griffel CSS makes scrollHeight unreliable, so we compute
    // the needed height from line count and container width instead.
    const calcHeight = React.useCallback((text: string, el: HTMLTextAreaElement): number => {
        // Get available width minus padding (6px left + 12px right from Fluent)
        const width = el.clientWidth || el.parentElement?.clientWidth || 800;
        const usableWidth = Math.max(width - 24, 200);
        // Approximate char width for 14px monospace
        const charWidth = 8.4;
        const charsPerLine = Math.floor(usableWidth / charWidth);
        const lines = text.split("\n");
        let totalLines = 0;
        for (const line of lines) {
            totalLines += Math.max(1, Math.ceil((line.length || 1) / charsPerLine));
        }
        // 20px per line + 24px padding top/bottom
        return totalLines * 20 + 48;
    }, []);

    // Apply calculated height to textarea AND its Fluent wrapper.
    // Must clear max-height — Griffel sets max-height which overrides height
    // even when both use !important (CSS spec: max-height wins over height).
    React.useEffect(() => {
        const el = textareaRef.current;
        if (!el) return;
        const wrapper = el.parentElement;
        const height = Math.max(calcHeight(value, el), 300) + "px";
        el.style.setProperty("height", height, "important");
        el.style.setProperty("max-height", "none", "important");
        el.style.setProperty("overflow", "hidden", "important");
        if (wrapper) {
            wrapper.style.setProperty("height", height, "important");
            wrapper.style.setProperty("max-height", "none", "important");
        }
    }, [value, calcHeight]);

    const handleChange = (
        _ev: React.ChangeEvent<HTMLTextAreaElement>,
        data: { value: string }
    ) => {
        onChange(data.value);
    };

    return (
        <div className={styles.container}>
            <div className={styles.labelRow}>
                <Label htmlFor="action-editor-textarea" weight="semibold">
                    System Prompt
                </Label>
                <Badge
                    appearance="tint"
                    color={tokenBadgeColor}
                    size="small"
                    aria-label={`Estimated token count: ${tokenCount}`}
                >
                    ~{tokenCount.toLocaleString()} tokens
                </Badge>
            </div>

            <Textarea
                id="action-editor-textarea"
                className={styles.textarea}
                value={value}
                onChange={handleChange}
                disabled={readOnly}
                resize="none"
                textarea={{ ref: textareaRef }}
                placeholder="Enter the system prompt for this action. Be specific about the role, context, and expected behavior."
                aria-label="System prompt editor"
                aria-describedby="action-editor-stats"
            />

            <div className={styles.statsRow} id="action-editor-stats" role="status" aria-live="polite">
                <Text className={styles.statLabel}>Characters:</Text>
                <Text className={styles.statValue} data-testid="char-count">
                    {charCount.toLocaleString()}
                </Text>
                <Text className={styles.statLabel}>Approx. tokens:</Text>
                <Text className={styles.statValue} data-testid="token-count">
                    {tokenCount.toLocaleString()}
                </Text>
                {tokenCount >= TOKEN_WARN_THRESHOLD && (
                    <Text
                        className={styles.statLabel}
                        data-testid="token-warning"
                        aria-live="assertive"
                    >
                        {tokenCount >= TOKEN_ERROR_THRESHOLD
                            ? "Warning: prompt may exceed model context limits"
                            : "Approaching token limit — consider condensing"}
                    </Text>
                )}
            </div>
        </div>
    );
};
