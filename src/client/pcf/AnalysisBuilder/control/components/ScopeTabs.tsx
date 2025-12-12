/**
 * ScopeTabs Component
 *
 * Tab navigation for analysis scope configuration.
 * Shows tabs with selection counts.
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 */

import * as React from "react";
import {
    TabList,
    Tab,
    Badge,
    makeStyles,
    tokens
} from "@fluentui/react-components";
import {
    Play24Regular,
    BrainCircuit24Regular,
    Library24Regular,
    Wrench24Regular,
    Document24Regular
} from "@fluentui/react-icons";
import { IScopeTabsProps, ScopeTabId } from "../types";

const useStyles = makeStyles({
    container: {
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        paddingLeft: "24px",
        paddingRight: "24px"
    },
    tab: {
        display: "flex",
        alignItems: "center",
        gap: "8px"
    },
    badge: {
        marginLeft: "4px"
    }
});

// Icon mapping for tabs
const tabIcons: Record<ScopeTabId, React.ReactElement> = {
    action: <Play24Regular />,
    skills: <BrainCircuit24Regular />,
    knowledge: <Library24Regular />,
    tools: <Wrench24Regular />,
    output: <Document24Regular />
};

export const ScopeTabs: React.FC<IScopeTabsProps> = ({
    activeTab,
    tabs,
    onTabChange
}) => {
    const styles = useStyles();

    const handleTabSelect = (_event: unknown, data: { value: unknown }): void => {
        onTabChange(data.value as ScopeTabId);
    };

    return (
        <div className={styles.container}>
            <TabList
                selectedValue={activeTab}
                onTabSelect={handleTabSelect}
                size="medium"
            >
                {tabs.map((tab) => (
                    <Tab
                        key={tab.id}
                        value={tab.id}
                        icon={tabIcons[tab.id]}
                    >
                        <span className={styles.tab}>
                            {tab.label}
                            {tab.count > 0 && (
                                <Badge
                                    appearance="filled"
                                    color="brand"
                                    size="small"
                                    className={styles.badge}
                                >
                                    {tab.count}
                                </Badge>
                            )}
                        </span>
                    </Tab>
                ))}
            </TabList>
        </div>
    );
};

export default ScopeTabs;
