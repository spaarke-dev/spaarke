/**
 * SkillEditor
 *
 * Editor for sprk_analysisskill (Skill) records.
 * Renders a compact Fluent v9 Textarea for the prompt fragment with:
 *   - Injection preview below showing how the fragment will appear
 *
 * ADR-021: makeStyles; design tokens; no hardcoded colors.
 * ADR-022: React 16 APIs.
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Label,
    Textarea,
    Text,
    Divider,
    Badge,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface ISkillEditorProps {
    /** Current prompt fragment text */
    value: string;
    /** Callback when text changes */
    onChange: (value: string) => void;
    /** Whether the editor is read-only */
    readOnly?: boolean;
    /** Skill name for injection preview header */
    skillName?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
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
    previewSection: {
        marginTop: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium,
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke2),
    },
    previewHeader: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        marginBottom: tokens.spacingVerticalXS,
    },
    previewLabel: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        textTransform: "uppercase",
        letterSpacing: "0.05em",
    },
    previewContent: {
        fontFamily: tokens.fontFamilyMonospace,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        whiteSpace: "pre-wrap",
        wordBreak: "break-word",
    },
    emptyPreview: {
        color: tokens.colorNeutralForeground4,
        fontStyle: "italic",
        fontSize: tokens.fontSizeBase200,
    },
    charCount: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const SkillEditor: React.FC<ISkillEditorProps> = ({
    value,
    onChange,
    readOnly = false,
    skillName = "Skill",
}) => {
    const styles = useStyles();
    const textareaRef = React.useRef<HTMLTextAreaElement>(null);

    // Calculate height from content rather than DOM measurement.
    const calcHeight = React.useCallback((text: string, el: HTMLTextAreaElement): number => {
        const width = el.clientWidth || el.parentElement?.clientWidth || 800;
        const usableWidth = Math.max(width - 24, 200);
        const charWidth = 8.4;
        const charsPerLine = Math.floor(usableWidth / charWidth);
        const lines = text.split("\n");
        let totalLines = 0;
        for (const line of lines) {
            totalLines += Math.max(1, Math.ceil((line.length || 1) / charsPerLine));
        }
        return totalLines * 20 + 48;
    }, []);

    // Apply calculated height to textarea AND its Fluent wrapper.
    // Must clear max-height — Griffel sets max-height which overrides height.
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
                <Label htmlFor="skill-editor-textarea" weight="semibold">
                    Prompt Fragment
                </Label>
                <Badge appearance="tint" color="informative" size="small">
                    Skill
                </Badge>
                <Text className={styles.charCount} data-testid="skill-char-count">
                    {value.length.toLocaleString()} chars
                </Text>
            </div>

            <Textarea
                id="skill-editor-textarea"
                className={styles.textarea}
                value={value}
                onChange={handleChange}
                disabled={readOnly}
                resize="none"
                textarea={{ ref: textareaRef }}
                placeholder="Enter the prompt fragment for this skill. This text will be injected into the system prompt when the skill is active."
                aria-label="Skill prompt fragment editor"
                aria-describedby="skill-injection-preview"
            />

            <Divider />

            <div
                className={styles.previewSection}
                id="skill-injection-preview"
                role="region"
                aria-label="Injection preview"
            >
                <div className={styles.previewHeader}>
                    <Text className={styles.previewLabel}>
                        Injection preview — {skillName}
                    </Text>
                </div>

                {value.trim() ? (
                    <pre
                        className={styles.previewContent}
                        data-testid="injection-preview"
                    >
                        {`## Skill: ${skillName}\n\n${value}`}
                    </pre>
                ) : (
                    <Text
                        className={styles.emptyPreview}
                        data-testid="injection-preview-empty"
                    >
                        Enter a prompt fragment above to see the injection preview.
                    </Text>
                )}
            </div>
        </div>
    );
};
