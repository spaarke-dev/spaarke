/**
 * SearchDomainTabs -- Entity domain tab selector (2x2 grid)
 *
 * Renders a 2x2 grid of toggle buttons for switching between the four search
 * domains: Documents, Matters, Projects, Invoices.
 *
 * Selecting a tab updates the active domain and triggers a new search
 * against the selected domain.
 *
 * @see ADR-021 for Fluent UI v9 design system requirements
 * @see types/index.ts for SearchDomain type definition
 */

import { useCallback } from "react";
import {
    makeStyles,
    tokens,
    ToggleButton,
} from "@fluentui/react-components";
import {
    DocumentMultipleRegular,
    BriefcaseRegular,
    TaskListSquareAddRegular,
    ReceiptRegular,
} from "@fluentui/react-icons";
import type { SearchDomain } from "../types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SearchDomainTabsProps {
    /** Currently active search domain */
    activeDomain: SearchDomain;
    /** Callback when the user switches domain tabs */
    onDomainChange: (domain: SearchDomain) => void;
    /** Current search query (forwarded to onSearch on tab change) */
    query: string;
    /** Callback to trigger a new search after domain change */
    onSearch: (query: string, domain: SearchDomain) => void;
}

// ---------------------------------------------------------------------------
// Tab Configuration
// ---------------------------------------------------------------------------

interface DomainTabConfig {
    id: SearchDomain;
    label: string;
    icon: React.ReactElement;
}

const DOMAIN_TABS: DomainTabConfig[] = [
    { id: "documents", label: "Documents", icon: <DocumentMultipleRegular /> },
    { id: "matters", label: "Matters", icon: <BriefcaseRegular /> },
    { id: "projects", label: "Projects", icon: <TaskListSquareAddRegular /> },
    { id: "invoices", label: "Invoices", icon: <ReceiptRegular /> },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    grid: {
        display: "grid",
        gridTemplateColumns: "1fr 1fr",
        gap: tokens.spacingHorizontalXS,
    },
    button: {
        justifyContent: "flex-start",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SearchDomainTabs: React.FC<SearchDomainTabsProps> = ({
    activeDomain,
    onDomainChange,
    query,
    onSearch,
}) => {
    const styles = useStyles();

    const handleClick = useCallback(
        (domain: SearchDomain) => () => {
            onDomainChange(domain);
            onSearch(query, domain);
        },
        [onDomainChange, onSearch, query],
    );

    return (
        <div className={styles.grid} role="tablist" aria-label="Search domain selector">
            {DOMAIN_TABS.map((tab) => (
                <ToggleButton
                    key={tab.id}
                    className={styles.button}
                    checked={activeDomain === tab.id}
                    onClick={handleClick(tab.id)}
                    icon={tab.icon}
                    size="small"
                    appearance={activeDomain === tab.id ? "primary" : "subtle"}
                    aria-label={`Search ${tab.label}`}
                >
                    {tab.label}
                </ToggleButton>
            ))}
        </div>
    );
};

export default SearchDomainTabs;
