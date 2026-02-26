/**
 * SavedSearchSelector — Saved search dropdown
 *
 * Standard Fluent v9 Dropdown with "Saved Searches" bold label,
 * matching the style of other filter dropdowns in the pane.
 *
 * Shows:
 *   - 4 default system searches (hardcoded)
 *   - Personal saved searches from useSavedSearches hook
 *
 * @see notes/spikes/gridconfiguration-schema.md — ViewSelector incompatibility
 * @see spec.md Section 6.1 — toolbar layout
 */

import React, { useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Dropdown,
    Option,
    OptionGroup,
    Label,
    Spinner,
} from "@fluentui/react-components";
import type { SavedSearch, SearchDomain, SearchFilters } from "../types";

// =============================================
// Default Searches
// =============================================

const DEFAULT_FILTERS: SearchFilters = {
    documentTypes: [],
    fileTypes: [],
    matterTypes: [],
    dateRange: { from: null, to: null },
    threshold: 50,
    searchMode: "rrf",
};

const DEFAULT_SEARCHES: SavedSearch[] = [
    {
        id: "default-all-documents",
        name: "All Documents",
        searchDomain: "documents" as SearchDomain,
        query: "",
        filters: DEFAULT_FILTERS,
        viewMode: "grid",
        columns: ["name", "similarity", "documentType", "parentEntity", "modified"],
        sortColumn: "similarity",
        sortDirection: "desc",
    },
    {
        id: "default-all-matters",
        name: "All Matters",
        searchDomain: "matters" as SearchDomain,
        query: "",
        filters: DEFAULT_FILTERS,
        viewMode: "grid",
        columns: ["name", "confidenceScore", "organizations", "people"],
        sortColumn: "confidenceScore",
        sortDirection: "desc",
    },
    {
        id: "default-recent",
        name: "Recent Documents",
        searchDomain: "documents" as SearchDomain,
        query: "",
        filters: {
            ...DEFAULT_FILTERS,
            dateRange: {
                from: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10),
                to: null,
            },
        },
        viewMode: "grid",
        columns: ["name", "similarity", "documentType", "parentEntity", "modified"],
        sortColumn: "modified",
        sortDirection: "desc",
    },
    {
        id: "default-high-similarity",
        name: "High Similarity",
        searchDomain: "documents" as SearchDomain,
        query: "",
        filters: { ...DEFAULT_FILTERS, threshold: 75 },
        viewMode: "grid",
        columns: ["name", "similarity", "documentType", "parentEntity", "modified"],
        sortColumn: "similarity",
        sortDirection: "desc",
    },
];

// =============================================
// Props
// =============================================

export interface SavedSearchSelectorProps {
    /** Personal saved searches from useSavedSearches hook. */
    savedSearches: SavedSearch[];
    /** Currently active search name (or null). */
    currentSearchName: string | null;
    /** Called when user selects a saved search. */
    onSelectSavedSearch: (search: SavedSearch) => void;
    /** Called when user clicks "Save Current Search". */
    onSaveCurrentSearch: () => void;
    /** Whether saved searches are loading. */
    isLoading: boolean;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    label: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    dropdown: {
        width: "100%",
    },
    loading: {
        padding: tokens.spacingVerticalS,
        display: "flex",
        justifyContent: "center",
    },
});

// =============================================
// Component
// =============================================

export const SavedSearchSelector: React.FC<SavedSearchSelectorProps> = ({
    savedSearches,
    currentSearchName,
    onSelectSavedSearch,
    isLoading,
}) => {
    const styles = useStyles();

    // Build a lookup map: id → SavedSearch (both defaults + personal)
    const allSearches = useMemo(() => {
        const map = new Map<string, SavedSearch>();
        for (const s of DEFAULT_SEARCHES) map.set(s.id!, s);
        for (const s of savedSearches) map.set(s.id!, s);
        return map;
    }, [savedSearches]);

    const handleOptionSelect = useCallback(
        (_event: unknown, data: { optionValue?: string }) => {
            if (!data.optionValue) return;
            const search = allSearches.get(data.optionValue);
            if (search) onSelectSavedSearch(search);
        },
        [allSearches, onSelectSavedSearch],
    );

    // Find the currently selected option value (search ID)
    const selectedOptions = useMemo(() => {
        if (!currentSearchName) return [];
        for (const [id, s] of allSearches) {
            if (s.name === currentSearchName) return [id];
        }
        return [];
    }, [currentSearchName, allSearches]);

    return (
        <div className={styles.container}>
            <Label className={styles.label}>Saved Searches</Label>
            {isLoading ? (
                <div className={styles.loading}>
                    <Spinner size="tiny" label="Loading..." />
                </div>
            ) : (
                <Dropdown
                    className={styles.dropdown}
                    size="small"
                    placeholder="Select a saved search..."
                    value={currentSearchName ?? ""}
                    selectedOptions={selectedOptions}
                    onOptionSelect={handleOptionSelect}
                    aria-label="Saved searches"
                >
                    <OptionGroup label="Default Searches">
                        {DEFAULT_SEARCHES.map((search) => (
                            <Option key={search.id} value={search.id!}>
                                {search.name}
                            </Option>
                        ))}
                    </OptionGroup>
                    {savedSearches.length > 0 && (
                        <OptionGroup label="My Searches">
                            {savedSearches.map((search) => (
                                <Option key={search.id} value={search.id!}>
                                    {search.name}
                                </Option>
                            ))}
                        </OptionGroup>
                    )}
                </Dropdown>
            )}
        </div>
    );
};

export default SavedSearchSelector;
