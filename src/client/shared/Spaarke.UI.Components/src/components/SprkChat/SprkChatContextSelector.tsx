/**
 * SprkChatContextSelector - Document + playbook dropdown selector
 *
 * Allows users to switch the document and playbook context for an active chat session.
 * Uses Fluent UI v9 Dropdown (via Select for React 16 compatibility).
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Label,
    Select,
} from "@fluentui/react-components";
import { ISprkChatContextSelectorProps } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
        backgroundColor: tokens.colorNeutralBackground2,
        flexWrap: "wrap",
    },
    selectorGroup: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    label: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        whiteSpace: "nowrap",
    },
    select: {
        minWidth: "120px",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatContextSelector - Compact dropdowns for document and playbook selection.
 *
 * @example
 * ```tsx
 * <SprkChatContextSelector
 *   documents={documents}
 *   playbooks={playbooks}
 *   selectedDocumentId={currentDocId}
 *   selectedPlaybookId={currentPlaybookId}
 *   onDocumentChange={(id) => switchContext(id)}
 *   onPlaybookChange={(id) => switchPlaybook(id)}
 * />
 * ```
 */
export const SprkChatContextSelector: React.FC<ISprkChatContextSelectorProps> = ({
    selectedDocumentId,
    selectedPlaybookId,
    documents,
    playbooks,
    onDocumentChange,
    onPlaybookChange,
    disabled = false,
}) => {
    const styles = useStyles();

    const handleDocumentChange = React.useCallback(
        (event: React.ChangeEvent<HTMLSelectElement>) => {
            onDocumentChange(event.target.value);
        },
        [onDocumentChange]
    );

    const handlePlaybookChange = React.useCallback(
        (event: React.ChangeEvent<HTMLSelectElement>) => {
            onPlaybookChange(event.target.value);
        },
        [onPlaybookChange]
    );

    return (
        <div className={styles.root} role="toolbar" aria-label="Chat context">
            {documents.length > 0 && (
                <div className={styles.selectorGroup}>
                    <Label className={styles.label} htmlFor="sprkchat-doc-select">
                        Document:
                    </Label>
                    <Select
                        id="sprkchat-doc-select"
                        className={styles.select}
                        value={selectedDocumentId || ""}
                        onChange={handleDocumentChange}
                        disabled={disabled}
                        aria-label="Select document"
                        data-testid="context-document-select"
                    >
                        <option value="">None</option>
                        {documents.map((doc) => (
                            <option key={doc.id} value={doc.id}>
                                {doc.name}
                            </option>
                        ))}
                    </Select>
                </div>
            )}

            {playbooks.length > 0 && (
                <div className={styles.selectorGroup}>
                    <Label className={styles.label} htmlFor="sprkchat-playbook-select">
                        Playbook:
                    </Label>
                    <Select
                        id="sprkchat-playbook-select"
                        className={styles.select}
                        value={selectedPlaybookId || ""}
                        onChange={handlePlaybookChange}
                        disabled={disabled}
                        aria-label="Select playbook"
                        data-testid="context-playbook-select"
                    >
                        {playbooks.map((pb) => (
                            <option key={pb.id} value={pb.id}>
                                {pb.name}
                            </option>
                        ))}
                    </Select>
                </div>
            )}
        </div>
    );
};

export default SprkChatContextSelector;
