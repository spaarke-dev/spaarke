/**
 * KnowledgeSourceEditor
 *
 * Editor for sprk_analysisknowledge (Knowledge Source) records.
 * Renders:
 *   - A Textarea for markdown content
 *   - An "Upload File" Button that triggers file upload (stub — actual upload is TODO)
 *
 * ADR-021: makeStyles; design tokens; no hardcoded colors.
 * ADR-022: React 16 APIs.
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Label,
    Textarea,
    Button,
    Text,
    Badge,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IKnowledgeSourceEditorProps {
    /** Current markdown content */
    value: string;
    /** Callback when text changes */
    onChange: (value: string) => void;
    /** Whether the editor is read-only */
    readOnly?: boolean;
}

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
    actionRow: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalXS,
    },
    uploadHint: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
    },
    charCount: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
        marginLeft: "auto",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const KnowledgeSourceEditor: React.FC<IKnowledgeSourceEditorProps> = ({
    value,
    onChange,
    readOnly = false,
}) => {
    const styles = useStyles();
    const [showUploadInfo, setShowUploadInfo] = React.useState<boolean>(false);
    const fileInputRef = React.useRef<HTMLInputElement>(null);
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

    /**
     * Trigger file upload.
     * TODO: Implement actual file upload to BFF API (sprk_analysisknowledge/upload).
     * For now, reads the file content into the textarea as markdown.
     */
    const handleUploadClick = () => {
        fileInputRef.current?.click();
    };

    const handleFileSelected = (ev: React.ChangeEvent<HTMLInputElement>) => {
        const file = ev.target.files?.[0];
        if (!file) return;

        // TODO: Upload file to BFF API endpoint
        // For now: read text content and populate textarea
        const reader = new FileReader();
        reader.onload = (e) => {
            const content = e.target?.result;
            if (typeof content === "string") {
                onChange(content);
                setShowUploadInfo(true);
                // Clear info after 4 seconds
                setTimeout(() => setShowUploadInfo(false), 4000);
            }
        };
        reader.readAsText(file);

        // Reset file input so the same file can be re-selected
        ev.target.value = "";
    };

    return (
        <div className={styles.container}>
            <div className={styles.labelRow}>
                <Label htmlFor="knowledge-editor-textarea" weight="semibold">
                    Knowledge Content
                </Label>
                <Badge appearance="tint" color="success" size="small">
                    Knowledge Source
                </Badge>
            </div>

            {showUploadInfo && (
                <MessageBar intent="success">
                    <MessageBarBody>
                        File content loaded into editor.
                        {/* TODO: Replace with actual upload confirmation */}
                    </MessageBarBody>
                </MessageBar>
            )}

            <Textarea
                id="knowledge-editor-textarea"
                className={styles.textarea}
                value={value}
                onChange={handleChange}
                disabled={readOnly}
                resize="none"
                textarea={{ ref: textareaRef }}
                placeholder="Enter markdown content for this knowledge source. You can also upload a file using the button below."
                aria-label="Knowledge source markdown content editor"
            />

            <div className={styles.actionRow}>
                {/* Hidden file input for upload trigger */}
                <input
                    ref={fileInputRef}
                    type="file"
                    accept=".md,.txt,.pdf,.docx"
                    style={{ display: "none" }}
                    onChange={handleFileSelected}
                    aria-hidden="true"
                />

                <Button
                    appearance="secondary"
                    onClick={handleUploadClick}
                    disabled={readOnly}
                    aria-label="Upload file to populate knowledge source content"
                    data-testid="upload-file-button"
                >
                    Upload File
                </Button>

                <Text className={styles.uploadHint}>
                    Supports .md, .txt, .pdf, .docx
                </Text>

                <Text className={styles.charCount} data-testid="knowledge-char-count">
                    {value.length.toLocaleString()} chars
                </Text>
            </div>
        </div>
    );
};
