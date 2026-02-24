/**
 * ToolEditor
 *
 * Editor for sprk_analysistool (Tool) records.
 * Renders:
 *   - A CodeMirror JSON editor with real-time JSON validation
 *   - A Fluent v9 Dropdown for the handler class name
 *     (populated from GET /api/ai/tools/handlers — AIPL-036 endpoint)
 *   - If handler API fails, falls back to a text input
 *
 * ADR-021: makeStyles; design tokens; no hardcoded colors.
 * ADR-022: React 16 APIs.
 * Bundle: Uses CodeMirror (~300KB) NOT Monaco (~4MB).
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Label,
    Text,
    Badge,
    Dropdown,
    Option,
    Input,
    MessageBar,
    MessageBarBody,
    Spinner,
} from "@fluentui/react-components";

// CodeMirror imports — bundled (not a platform library)
// Minimal individual @codemirror packages — kept small to satisfy < 1MB budget.
import { EditorState } from "@codemirror/state";
import { EditorView, lineNumbers, keymap } from "@codemirror/view";
import { defaultKeymap } from "@codemirror/commands";
import { json } from "@codemirror/lang-json";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IToolEditorProps {
    /** Current tool configuration JSON string */
    value: string;
    /** BFF API base URL for GET /api/ai/tools/handlers */
    apiBaseUrl: string;
    /** Callback when value changes */
    onChange: (value: string) => void;
    /** Whether the editor is read-only */
    readOnly?: boolean;
}

interface IHandlerOption {
    handlerClass: string;
    displayName: string;
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
    handlerSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    handlerControls: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        flexWrap: "wrap",
    },
    dropdown: {
        minWidth: "300px",
    },
    fallbackInput: {
        minWidth: "300px",
    },
    editorSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    editorContainer: {
        ...shorthands.border("1px", "solid", tokens.colorNeutralStroke1),
        borderRadius: tokens.borderRadiusMedium,
        overflow: "hidden",
        minHeight: "300px",
        // We override the background to match Fluent tokens
        backgroundColor: tokens.colorNeutralBackground1,
    },
    editorContainerError: {
        ...shorthands.borderColor(tokens.colorPaletteRedBorder1),
    },
    validationMessage: {
        color: tokens.colorPaletteRedForeground1,
        fontSize: tokens.fontSizeBase100,
    },
    validationSuccess: {
        color: tokens.colorPaletteGreenForeground1,
        fontSize: tokens.fontSizeBase100,
    },
    helperText: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Hooks
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Fetch available handler classes from the BFF API.
 * Falls back to empty array if the request fails.
 */
function useHandlers(apiBaseUrl: string): {
    handlers: IHandlerOption[];
    loading: boolean;
    error: string | null;
    apiAvailable: boolean;
} {
    const [handlers, setHandlers] = React.useState<IHandlerOption[]>([]);
    const [loading, setLoading] = React.useState<boolean>(true);
    const [error, setError] = React.useState<string | null>(null);
    const [apiAvailable, setApiAvailable] = React.useState<boolean>(true);

    React.useEffect(() => {
        let cancelled = false;

        const fetchHandlers = async () => {
            if (!apiBaseUrl) {
                setLoading(false);
                setApiAvailable(false);
                return;
            }

            try {
                const url = `${apiBaseUrl.replace(/\/$/, "")}/api/ai/tools/handlers`;
                const response = await fetch(url, {
                    method: "GET",
                    headers: { "Content-Type": "application/json" },
                    signal: AbortSignal.timeout(5000),
                });

                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }

                const data: { handlerClass: string; displayName?: string }[] =
                    await response.json();

                if (!cancelled) {
                    setHandlers(
                        data.map((h) => ({
                            handlerClass: h.handlerClass,
                            displayName: h.displayName ?? h.handlerClass,
                        }))
                    );
                    setApiAvailable(true);
                }
            } catch (err) {
                if (!cancelled) {
                    const message =
                        err instanceof Error ? err.message : "Unknown error";
                    setError(
                        `Unable to load handler list (${message}). Enter the handler class manually.`
                    );
                    setApiAvailable(false);
                }
            } finally {
                if (!cancelled) {
                    setLoading(false);
                }
            }
        };

        fetchHandlers().catch(() => {
            // Already handled above
        });

        return () => {
            cancelled = true;
        };
    }, [apiBaseUrl]);

    return { handlers, loading, error, apiAvailable };
}

/**
 * Validate a JSON string and return error message or null.
 */
function validateJson(text: string): string | null {
    if (!text.trim()) return null;
    try {
        JSON.parse(text);
        return null;
    } catch (err) {
        return err instanceof Error ? err.message : "Invalid JSON";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CodeMirror Editor sub-component
// ─────────────────────────────────────────────────────────────────────────────

interface ICodeMirrorEditorProps {
    value: string;
    onChange: (value: string) => void;
    readOnly?: boolean;
    containerClassName?: string;
}

const CodeMirrorEditor: React.FC<ICodeMirrorEditorProps> = ({
    value,
    onChange,
    readOnly = false,
    containerClassName,
}) => {
    const editorRef = React.useRef<HTMLDivElement>(null);
    const viewRef = React.useRef<EditorView | null>(null);
    // Track whether the value change came from within CodeMirror
    const internalChangeRef = React.useRef<boolean>(false);

    // Initialize CodeMirror on mount
    React.useEffect(() => {
        if (!editorRef.current) return;

        const updateListener = EditorView.updateListener.of((update) => {
            if (update.docChanged) {
                const newValue = update.state.doc.toString();
                internalChangeRef.current = true;
                onChange(newValue);
            }
        });

        const state = EditorState.create({
            doc: value,
            extensions: [
                // Minimal extension set for JSON editing
                lineNumbers(),
                keymap.of(defaultKeymap),
                json(),
                EditorView.lineWrapping,
                updateListener,
                EditorView.editable.of(!readOnly),
            ],
        });

        const view = new EditorView({
            state,
            parent: editorRef.current,
        });

        viewRef.current = view;

        return () => {
            view.destroy();
            viewRef.current = null;
        };
        // Only initialize once on mount — dependencies intentionally omitted
    }, []);

    // Sync external value changes into CodeMirror (when value changes from outside)
    React.useEffect(() => {
        const view = viewRef.current;
        if (!view) return;

        if (internalChangeRef.current) {
            // Change came from within CodeMirror — don't re-set to avoid cursor jump
            internalChangeRef.current = false;
            return;
        }

        const currentValue = view.state.doc.toString();
        if (currentValue !== value) {
            view.dispatch({
                changes: {
                    from: 0,
                    to: currentValue.length,
                    insert: value,
                },
            });
        }
    }, [value]);

    return <div ref={editorRef} className={containerClassName} data-testid="codemirror-editor" />;
};

// ─────────────────────────────────────────────────────────────────────────────
// Main Component
// ─────────────────────────────────────────────────────────────────────────────

export const ToolEditor: React.FC<IToolEditorProps> = ({
    value,
    apiBaseUrl,
    onChange,
    readOnly = false,
}) => {
    const styles = useStyles();
    const { handlers, loading, error: handlerError, apiAvailable } =
        useHandlers(apiBaseUrl);

    const [handlerClass, setHandlerClass] = React.useState<string>("");
    const [fallbackHandlerText, setFallbackHandlerText] = React.useState<string>("");
    const [jsonError, setJsonError] = React.useState<string | null>(null);

    // Validate JSON on every value change
    React.useEffect(() => {
        setJsonError(validateJson(value));
    }, [value]);

    const handleJsonChange = (newValue: string) => {
        onChange(newValue);
        // Validation will run via the effect above
    };

    const handleHandlerSelect = (
        _ev: React.SyntheticEvent,
        data: { optionValue?: string }
    ) => {
        const selected = data.optionValue ?? "";
        setHandlerClass(selected);
    };

    const handleFallbackInput = (
        _ev: React.ChangeEvent<HTMLInputElement>,
        data: { value: string }
    ) => {
        setFallbackHandlerText(data.value);
    };

    const editorContainerClass = `${styles.editorContainer}${
        jsonError ? ` ${styles.editorContainerError}` : ""
    }`;

    return (
        <div className={styles.container}>
            {/* Handler Class Section */}
            <div className={styles.handlerSection}>
                <div className={styles.labelRow}>
                    <Label weight="semibold">Handler Class</Label>
                    <Badge appearance="tint" color="warning" size="small">
                        Tool
                    </Badge>
                </div>

                <div className={styles.handlerControls}>
                    {loading && (
                        <Spinner size="tiny" label="Loading handlers..." labelPosition="after" />
                    )}

                    {!loading && apiAvailable && (
                        <Dropdown
                            className={styles.dropdown}
                            placeholder="Select a handler class"
                            value={handlerClass}
                            selectedOptions={handlerClass ? [handlerClass] : []}
                            onOptionSelect={handleHandlerSelect}
                            disabled={readOnly || handlers.length === 0}
                            aria-label="Select tool handler class"
                            data-testid="handler-dropdown"
                        >
                            {handlers.map((h) => (
                                <Option key={h.handlerClass} value={h.handlerClass}>
                                    {h.displayName}
                                </Option>
                            ))}
                        </Dropdown>
                    )}

                    {!loading && !apiAvailable && (
                        <Input
                            className={styles.fallbackInput}
                            value={fallbackHandlerText}
                            onChange={handleFallbackInput}
                            disabled={readOnly}
                            placeholder="Enter handler class name (e.g., DocumentSearchToolHandler)"
                            aria-label="Tool handler class name (manual entry)"
                            data-testid="handler-fallback-input"
                        />
                    )}

                    {handlerError && (
                        <Text className={styles.helperText}>{handlerError}</Text>
                    )}
                </div>
            </div>

            {/* JSON Schema Editor Section */}
            <div className={styles.editorSection}>
                <div className={styles.labelRow}>
                    <Label weight="semibold">Tool Configuration (JSON)</Label>
                    {!jsonError && value.trim() && (
                        <Text
                            className={styles.validationSuccess}
                            data-testid="json-valid-indicator"
                            role="status"
                        >
                            Valid JSON
                        </Text>
                    )}
                    {jsonError && (
                        <Text
                            className={styles.validationMessage}
                            data-testid="json-error-indicator"
                            role="alert"
                        >
                            JSON error
                        </Text>
                    )}
                </div>

                <CodeMirrorEditor
                    value={value}
                    onChange={handleJsonChange}
                    readOnly={readOnly}
                    containerClassName={editorContainerClass}
                />

                {jsonError && (
                    <MessageBar intent="error">
                        <MessageBarBody>
                            <Text data-testid="json-error-message">{jsonError}</Text>
                        </MessageBarBody>
                    </MessageBar>
                )}

                <Text className={styles.helperText}>
                    Enter a valid JSON object defining the tool&apos;s parameters and configuration.
                </Text>
            </div>
        </div>
    );
};
