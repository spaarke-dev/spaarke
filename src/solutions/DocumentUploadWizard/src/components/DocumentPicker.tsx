/**
 * DocumentPicker.tsx
 * Single-select list of uploaded documents for the success screen.
 *
 * Used when "Work on Analysis" is clicked and multiple documents were
 * uploaded -- the user picks which document to analyze.
 *
 * Layout:
 *   ┌───────────────────────────────────────────────────────────────┐
 *   │  ○  Contract_v2.docx                          ✅ Uploaded     │
 *   │  ● NDA_Final.pdf                              ✅ Uploaded     │
 *   │  ○  Engagement_Letter.pdf                     ✅ Uploaded     │
 *   └───────────────────────────────────────────────────────────────┘
 *
 * Constraints:
 *   - Fluent UI v9 exclusively (RadioGroup) — ADR-021
 *   - makeStyles with semantic tokens, dark mode support
 *   - No domain-specific imports beyond OrchestratorFileResult
 *
 * @see ADR-021  - Fluent UI v9 design system
 */

import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Text,
    RadioGroup,
    Radio,
    Badge,
} from "@fluentui/react-components";
import {
    DocumentRegular,
    DocumentPdfRegular,
    ImageRegular,
    CheckmarkCircleRegular,
} from "@fluentui/react-icons";

import type { OrchestratorFileResult } from "../services/uploadOrchestrator";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDocumentPickerProps {
    /** Successfully uploaded documents to display for selection. */
    documents: OrchestratorFileResult[];
    /** Currently selected document ID (null if none selected). */
    selectedDocumentId: string | null;
    /** Called when the user selects a document. */
    onSelectionChange: (documentId: string | null) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        width: "100%",
    },
    radioGroup: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    radioItem: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground2,
        border: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    docIcon: {
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
        display: "flex",
        alignItems: "center",
    },
    docName: {
        flex: "1 1 auto",
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        color: tokens.colorNeutralForeground1,
    },
    statusBadge: {
        flexShrink: 0,
    },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Returns a file type icon based on file extension. */
function getDocumentIcon(fileName: string): JSX.Element {
    const ext = fileName.split(".").pop()?.toLowerCase() ?? "";
    if (ext === "pdf") return <DocumentPdfRegular />;
    if (["jpg", "jpeg", "png", "gif", "bmp", "svg", "webp"].includes(ext)) {
        return <ImageRegular />;
    }
    return <DocumentRegular />;
}

// ---------------------------------------------------------------------------
// DocumentPicker (exported)
// ---------------------------------------------------------------------------

export function DocumentPicker({
    documents,
    selectedDocumentId,
    onSelectionChange,
}: IDocumentPickerProps): JSX.Element {
    const styles = useStyles();

    const handleChange = useCallback(
        (_ev: unknown, data: { value: string }) => {
            onSelectionChange(data.value || null);
        },
        [onSelectionChange]
    );

    return (
        <div className={styles.root}>
            <RadioGroup
                className={styles.radioGroup}
                value={selectedDocumentId ?? ""}
                onChange={handleChange}
                aria-label="Select a document to analyze"
            >
                {documents.map((doc) => {
                    const docId = doc.createResult?.documentId ?? doc.fileName;
                    return (
                        <div key={docId} className={styles.radioItem}>
                            <Radio value={docId} label="" />
                            <span className={styles.docIcon} aria-hidden="true">
                                {getDocumentIcon(doc.fileName)}
                            </span>
                            <Text
                                size={200}
                                weight="semibold"
                                className={styles.docName}
                                title={doc.fileName}
                            >
                                {doc.fileName}
                            </Text>
                            <span className={styles.statusBadge}>
                                <Badge
                                    appearance="filled"
                                    color="success"
                                    size="small"
                                    icon={<CheckmarkCircleRegular />}
                                >
                                    Uploaded
                                </Badge>
                            </span>
                        </div>
                    );
                })}
            </RadioGroup>
        </div>
    );
}
