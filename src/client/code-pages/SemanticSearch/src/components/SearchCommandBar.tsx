/**
 * SearchCommandBar — Selection-aware, entity-type-aware command bar
 *
 * Command availability changes based on:
 *   1. Number of selected rows (0, 1, multiple)
 *   2. Active domain (Documents vs Records)
 *
 * Document-only commands hidden for Matters/Projects/Invoices domains.
 *
 * @see spec.md Section 6.6 / FR-09 — command bar specification
 */

import React, { useCallback } from "react";
import {
    makeStyles,
    tokens,
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Tooltip,
} from "@fluentui/react-components";
import {
    DeleteRegular,
    ArrowClockwiseRegular,
    MailRegular,
    OpenRegular,
    DesktopRegular,
    ArrowDownloadRegular,
    DatabaseSearchRegular,
    SaveRegular,
} from "@fluentui/react-icons";
import type { SearchDomain } from "../types";

// =============================================
// Props
// =============================================

export interface SearchCommandBarProps {
    /** IDs of currently selected rows. */
    selectedIds: string[];
    /** Active search domain. */
    activeDomain: SearchDomain;
    /** Delete selected records. */
    onDelete: (ids: string[]) => void;
    /** Refresh search results. */
    onRefresh: () => void;
    /** Email a link to a single record. */
    onEmailLink: (id: string) => void;
    /** Open document in web (Documents only). */
    onOpenInWeb: (id: string) => void;
    /** Open document in desktop app (Documents only). */
    onOpenInDesktop: (id: string) => void;
    /** Download document (Documents only). */
    onDownload: (id: string) => void;
    /** Send documents to AI index (Documents only). */
    onSendToIndex: (ids: string[]) => void;
    /** Save current search to favorites. */
    onSaveSearch: () => void;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    toolbar: {
        gap: tokens.spacingHorizontalXS,
    },
});

// =============================================
// Component
// =============================================

export const SearchCommandBar: React.FC<SearchCommandBarProps> = ({
    selectedIds,
    activeDomain,
    onDelete,
    onRefresh,
    onEmailLink,
    onOpenInWeb,
    onOpenInDesktop,
    onDownload,
    onSendToIndex,
    onSaveSearch,
}) => {
    const styles = useStyles();

    const hasSelection = selectedIds.length > 0;
    const isSingle = selectedIds.length === 1;
    const isDocumentDomain = activeDomain === "documents";

    const handleDelete = useCallback(() => {
        if (hasSelection) onDelete(selectedIds);
    }, [hasSelection, onDelete, selectedIds]);

    const handleEmailLink = useCallback(() => {
        if (isSingle) onEmailLink(selectedIds[0]);
    }, [isSingle, onEmailLink, selectedIds]);

    const handleOpenInWeb = useCallback(() => {
        if (isSingle) onOpenInWeb(selectedIds[0]);
    }, [isSingle, onOpenInWeb, selectedIds]);

    const handleOpenInDesktop = useCallback(() => {
        if (isSingle) onOpenInDesktop(selectedIds[0]);
    }, [isSingle, onOpenInDesktop, selectedIds]);

    const handleDownload = useCallback(() => {
        if (isSingle) onDownload(selectedIds[0]);
    }, [isSingle, onDownload, selectedIds]);

    const handleSendToIndex = useCallback(() => {
        if (hasSelection) onSendToIndex(selectedIds);
    }, [hasSelection, onSendToIndex, selectedIds]);

    return (
        <Toolbar className={styles.toolbar} size="small" aria-label="Search actions">
            {/* Always available */}
            <ToolbarButton
                icon={<ArrowClockwiseRegular />}
                onClick={onRefresh}
            >
                Refresh
            </ToolbarButton>

            <ToolbarDivider />

            {/* Selection-dependent */}
            <Tooltip
                content={hasSelection ? "Delete selected" : "Select items to delete"}
                relationship="label"
            >
                <ToolbarButton
                    icon={<DeleteRegular />}
                    disabled={!hasSelection}
                    onClick={handleDelete}
                >
                    Delete
                </ToolbarButton>
            </Tooltip>

            <Tooltip
                content={isSingle ? "Email a link" : "Select one item to email"}
                relationship="label"
            >
                <ToolbarButton
                    icon={<MailRegular />}
                    disabled={!isSingle}
                    onClick={handleEmailLink}
                >
                    Email a Link
                </ToolbarButton>
            </Tooltip>

            {/* Document-only commands */}
            {isDocumentDomain && (
                <>
                    <ToolbarDivider />

                    <Tooltip
                        content={isSingle ? "Open in browser" : "Select one document"}
                        relationship="label"
                    >
                        <ToolbarButton
                            icon={<OpenRegular />}
                            disabled={!isSingle}
                            onClick={handleOpenInWeb}
                        >
                            Open in Web
                        </ToolbarButton>
                    </Tooltip>

                    <Tooltip
                        content={isSingle ? "Open in desktop app" : "Select one document"}
                        relationship="label"
                    >
                        <ToolbarButton
                            icon={<DesktopRegular />}
                            disabled={!isSingle}
                            onClick={handleOpenInDesktop}
                        >
                            Open in Desktop
                        </ToolbarButton>
                    </Tooltip>

                    <Tooltip
                        content={isSingle ? "Download file" : "Select one document"}
                        relationship="label"
                    >
                        <ToolbarButton
                            icon={<ArrowDownloadRegular />}
                            disabled={!isSingle}
                            onClick={handleDownload}
                        >
                            Download
                        </ToolbarButton>
                    </Tooltip>

                    <Tooltip
                        content={hasSelection ? "Send to AI index" : "Select documents to index"}
                        relationship="label"
                    >
                        <ToolbarButton
                            icon={<DatabaseSearchRegular />}
                            disabled={!hasSelection}
                            onClick={handleSendToIndex}
                        >
                            Send to Index
                        </ToolbarButton>
                    </Tooltip>
                </>
            )}

            <ToolbarDivider />

            {/* Save current search to favorites */}
            <ToolbarButton
                icon={<SaveRegular />}
                onClick={onSaveSearch}
            >
                Save Search
            </ToolbarButton>
        </Toolbar>
    );
};

export default SearchCommandBar;
