/**
 * DocumentSelector — single-select document switcher for PlaybookLibraryShell.
 *
 * Rendered at the top of the PlaybookLibrary when two or more document IDs
 * are passed via the `documentIds` parameter. Fetches document names from
 * Dataverse and presents a RadioGroup so the user can switch the active
 * document before running an analysis.
 *
 * Single-select MVP: only one document can be active at a time.
 *
 * @see ADR-021 — Fluent v9 tokens only; dark mode via FluentProvider cascade.
 * @see ADR-012 — Shared component; IDataService for all Dataverse access.
 */
import React from 'react';
import type { IDataService } from '../../types/serviceInterfaces';
/** A resolved document item (id + display name). */
export interface IDocumentItem {
    id: string;
    name: string;
}
export interface IDocumentSelectorProps {
    /** Ordered list of document IDs to display. Must have length >= 2. */
    documentIds: string[];
    /** Currently selected document ID. */
    selectedDocumentId: string;
    /** Called when the user selects a different document. */
    onSelect: (documentId: string) => void;
    /** Data service for fetching document names. */
    dataService: IDataService;
    /** Optional extra class name for the root element. */
    className?: string;
}
/**
 * Fetches document display names for the given IDs and renders a horizontal
 * RadioGroup so the user can switch the active document.
 *
 * Hidden when documentIds.length < 2 (caller is responsible for the guard,
 * but the component also returns null defensively).
 */
export declare const DocumentSelector: React.FC<IDocumentSelectorProps>;
export default DocumentSelector;
//# sourceMappingURL=DocumentSelector.d.ts.map