/**
 * SearchDomainTabs -- Entity domain tab selector
 *
 * Renders a horizontal TabList (Fluent v9 subtle appearance) for switching
 * between the four search domains: Documents, Matters, Projects, Invoices.
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
    TabList,
    Tab,
    type SelectTabData,
    type SelectTabEvent,
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
    icon: React.ComponentType;
}

const DOMAIN_TABS: DomainTabConfig[] = [
    { id: "documents", label: "Documents", icon: DocumentMultipleRegular },
    { id: "matters", label: "Matters", icon: BriefcaseRegular },
    { id: "projects", label: "Projects", icon: TaskListSquareAddRegular },
    { id: "invoices", label: "Invoices", icon: ReceiptRegular },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        alignItems: "center",
    },
    tabList: {
        columnGap: tokens.spacingHorizontalXS,
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

    const handleTabSelect = useCallback(
        (_event: SelectTabEvent, data: SelectTabData) => {
            const newDomain = data.value as SearchDomain;
            onDomainChange(newDomain);
            onSearch(query, newDomain);
        },
        [onDomainChange, onSearch, query],
    );

    return (
        <div className={styles.root}>
            <TabList
                className={styles.tabList}
                appearance="subtle"
                selectedValue={activeDomain}
                onTabSelect={handleTabSelect}
                aria-label="Search domain selector"
            >
                {DOMAIN_TABS.map((tab) => (
                    <Tab
                        key={tab.id}
                        value={tab.id}
                        icon={<tab.icon />}
                    >
                        {tab.label}
                    </Tab>
                ))}
            </TabList>
        </div>
    );
};

export default SearchDomainTabs;
