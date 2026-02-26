/**
 * SprkChatContextSelector - Document + playbook dropdown selector with multi-document support
 *
 * Allows users to switch the document and playbook context for an active chat session.
 * Supports selecting up to 5 additional documents for multi-document AI context.
 * Uses Fluent UI v9 components (Select, Combobox, Tag, TagGroup, Button).
 *
 * The additional document picker uses a Combobox with type-ahead search/filter,
 * enabling users to quickly find documents in large lists.
 *
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 * @see ADR-022 - React 16 APIs only
 * @see ADR-012 - Shared Component Library
 */

import * as React from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Label,
    Select,
    Combobox,
    Option,
    Button,
    Tag,
    TagGroup,
    Text,
} from "@fluentui/react-components";
import type { ComboboxProps } from "@fluentui/react-components";
import {
    AddRegular,
    DocumentRegular,
    DismissRegular,
    SearchRegular,
} from "@fluentui/react-icons";
import { ISprkChatContextSelectorProps, IDocumentOption } from "./types";

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Default maximum number of additional documents allowed. */
const DEFAULT_MAX_ADDITIONAL_DOCUMENTS = 5;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke1),
        backgroundColor: tokens.colorNeutralBackground2,
    },
    selectorRow: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
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
    additionalDocsSection: {
        display: "flex",
        alignItems: "center",
        flexWrap: "wrap",
        ...shorthands.gap(tokens.spacingHorizontalXS),
        ...shorthands.padding(tokens.spacingVerticalXXS, tokens.spacingHorizontalM),
        paddingBottom: tokens.spacingVerticalXS,
    },
    tagGroup: {
        display: "flex",
        flexWrap: "wrap",
        ...shorthands.gap(tokens.spacingHorizontalXXS),
    },
    addDocGroup: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXXS),
    },
    addDocCombobox: {
        minWidth: "160px",
        maxWidth: "240px",
    },
    countBadge: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        whiteSpace: "nowrap",
        ...shorthands.padding(0, tokens.spacingHorizontalXS),
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SprkChatContextSelector - Compact dropdowns for document and playbook selection
 * with multi-document support for additional context documents.
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
 *   additionalDocumentIds={additionalDocs}
 *   onAdditionalDocumentsChange={(ids) => updateAdditionalDocs(ids)}
 *   maxAdditionalDocuments={5}
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
    additionalDocumentIds = [],
    onAdditionalDocumentsChange,
    maxAdditionalDocuments = DEFAULT_MAX_ADDITIONAL_DOCUMENTS,
}) => {
    const styles = useStyles();

    // ── State for the "add document" picker ──────────────────────────────
    const [isAddingDocument, setIsAddingDocument] = React.useState(false);

    // ── Computed values ──────────────────────────────────────────────────
    const isAtLimit = additionalDocumentIds.length >= maxAdditionalDocuments;

    /** Build a map of document ID to document option for quick lookups. */
    const documentMap = React.useMemo((): Map<string, IDocumentOption> => {
        const map = new Map<string, IDocumentOption>();
        for (const doc of documents) {
            map.set(doc.id, doc);
        }
        return map;
    }, [documents]);

    /**
     * Documents available for adding (not already selected as primary or additional).
     */
    const availableDocuments = React.useMemo((): IDocumentOption[] => {
        const excludedIds = new Set<string>(additionalDocumentIds);
        if (selectedDocumentId) {
            excludedIds.add(selectedDocumentId);
        }
        return documents.filter((doc) => !excludedIds.has(doc.id));
    }, [documents, additionalDocumentIds, selectedDocumentId]);

    // ── Handlers ─────────────────────────────────────────────────────────

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

    /** Add a document to the additional documents list via Combobox selection. */
    const handleAddDocument = React.useCallback<
        NonNullable<ComboboxProps["onOptionSelect"]>
    >(
        (_event, data) => {
            const docId = data.optionValue;
            if (!docId || !onAdditionalDocumentsChange) {
                return;
            }
            if (additionalDocumentIds.includes(docId)) {
                return;
            }
            if (additionalDocumentIds.length >= maxAdditionalDocuments) {
                return;
            }

            onAdditionalDocumentsChange([...additionalDocumentIds, docId]);
            setIsAddingDocument(false);
        },
        [additionalDocumentIds, maxAdditionalDocuments, onAdditionalDocumentsChange]
    );

    /** Remove a document from the additional documents list. */
    const handleRemoveDocument = React.useCallback(
        (docId: string) => {
            if (!onAdditionalDocumentsChange) {
                return;
            }
            onAdditionalDocumentsChange(additionalDocumentIds.filter((id) => id !== docId));
        },
        [additionalDocumentIds, onAdditionalDocumentsChange]
    );

    /** Toggle the add-document picker visibility. */
    const handleToggleAddDocument = React.useCallback(() => {
        setIsAddingDocument((prev) => !prev);
    }, []);

    /** Cancel the add-document picker. */
    const handleCancelAdd = React.useCallback(() => {
        setIsAddingDocument(false);
    }, []);

    // ── Helpers ──────────────────────────────────────────────────────────

    /** Get display name for a document ID, falling back to truncated ID. */
    const getDocumentName = React.useCallback(
        (docId: string): string => {
            const doc = documentMap.get(docId);
            if (doc) {
                return doc.name;
            }
            // Fallback: show first 8 chars of the ID
            return docId.length > 12 ? `${docId.substring(0, 8)}...` : docId;
        },
        [documentMap]
    );

    // ── Determine if additional documents section should be shown ────────
    const showAdditionalDocs =
        onAdditionalDocumentsChange !== undefined && documents.length > 1;
    const hasAdditionalDocs = additionalDocumentIds.length > 0;

    return (
        <div className={styles.root} role="toolbar" aria-label="Chat context">
            {/* Primary selector row: Document + Playbook */}
            <div className={styles.selectorRow}>
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

                {/* Add Document button (shown when multi-doc is enabled) */}
                {showAdditionalDocs && (
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<AddRegular />}
                        onClick={handleToggleAddDocument}
                        disabled={disabled || isAtLimit}
                        aria-label={
                            isAtLimit
                                ? `Maximum ${maxAdditionalDocuments} additional documents reached`
                                : "Add additional document"
                        }
                        title={
                            isAtLimit
                                ? `Maximum ${maxAdditionalDocuments} additional documents`
                                : "Add additional document to context"
                        }
                        data-testid="add-document-button"
                    >
                        {hasAdditionalDocs
                            ? `+${additionalDocumentIds.length} doc${additionalDocumentIds.length !== 1 ? "s" : ""}`
                            : "Add doc"}
                    </Button>
                )}
            </div>

            {/* Additional documents section: tag chips + add picker */}
            {showAdditionalDocs && (hasAdditionalDocs || isAddingDocument) && (
                <div
                    className={styles.additionalDocsSection}
                    data-testid="additional-docs-section"
                >
                    {/* Selected additional documents as removable tags */}
                    {hasAdditionalDocs && (
                        <TagGroup
                            className={styles.tagGroup}
                            role="list"
                            aria-label="Additional documents"
                            data-testid="additional-docs-tags"
                        >
                            {additionalDocumentIds.map((docId) => (
                                <Tag
                                    key={docId}
                                    shape="rounded"
                                    size="small"
                                    appearance="brand"
                                    icon={<DocumentRegular />}
                                    dismissible
                                    dismissIcon={
                                        <DismissRegular
                                            aria-label={`Remove ${getDocumentName(docId)}`}
                                        />
                                    }
                                    value={docId}
                                    onClick={
                                        disabled
                                            ? undefined
                                            : () => handleRemoveDocument(docId)
                                    }
                                    aria-label={`${getDocumentName(docId)} (click to remove)`}
                                    data-testid={`additional-doc-tag-${docId}`}
                                >
                                    {getDocumentName(docId)}
                                </Tag>
                            ))}
                        </TagGroup>
                    )}

                    {/* Inline document picker with search/filter (shown when "Add doc" is clicked) */}
                    {isAddingDocument && !isAtLimit && (
                        <div className={styles.addDocGroup}>
                            <Combobox
                                className={styles.addDocCombobox}
                                placeholder={
                                    availableDocuments.length === 0
                                        ? "No documents available"
                                        : "Search documents..."
                                }
                                onOptionSelect={handleAddDocument}
                                disabled={disabled || availableDocuments.length === 0}
                                aria-label="Search and select additional document"
                                freeform={false}
                                size="small"
                                expandIcon={<SearchRegular />}
                                data-testid="additional-doc-picker"
                            >
                                {availableDocuments.map((doc) => (
                                    <Option key={doc.id} value={doc.id} text={doc.name}>
                                        {doc.name}
                                    </Option>
                                ))}
                            </Combobox>
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<DismissRegular />}
                                onClick={handleCancelAdd}
                                aria-label="Cancel adding document"
                                data-testid="cancel-add-document"
                            />
                        </div>
                    )}

                    {/* Count indicator */}
                    {hasAdditionalDocs && (
                        <Text className={styles.countBadge} data-testid="additional-docs-count">
                            {additionalDocumentIds.length}/{maxAdditionalDocuments}
                        </Text>
                    )}
                </div>
            )}
        </div>
    );
};

export default SprkChatContextSelector;
