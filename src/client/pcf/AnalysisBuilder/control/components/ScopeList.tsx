/**
 * ScopeList Component
 *
 * Checkbox list for selecting scope items (actions, skills, knowledge, tools, output).
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 */

import * as React from "react";
import {
    Checkbox,
    Radio,
    RadioGroup,
    Text,
    Spinner,
    makeStyles,
    tokens,
    mergeClasses
} from "@fluentui/react-components";
import {
    Play24Regular,
    BrainCircuit24Regular,
    Library24Regular,
    Wrench24Regular,
    Document24Regular,
    Filter24Regular,
    Search24Regular,
    People24Regular,
    Emoji24Regular,
    Code24Regular,
    Globe24Regular,
    TextDescription24Regular,
    DocumentText24Regular,
    Certificate24Regular,
    Shield24Regular,
    Settings24Regular
} from "@fluentui/react-icons";
import { IScopeItem, IScopeListProps } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: "8px"
    },
    item: {
        display: "flex",
        alignItems: "flex-start",
        gap: "12px",
        paddingTop: "12px",
        paddingBottom: "12px",
        paddingLeft: "12px",
        paddingRight: "12px",
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        cursor: "pointer"
    },
    itemSelected: {
        backgroundColor: tokens.colorBrandBackground2
    },
    selector: {
        flexShrink: 0,
        marginTop: "2px"
    },
    icon: {
        flexShrink: 0,
        width: "32px",
        height: "32px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorBrandForeground1
    },
    content: {
        flex: 1,
        minWidth: 0
    },
    name: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        marginBottom: "2px"
    },
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: "1.4"
    },
    loading: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "48px",
        gap: "16px"
    },
    empty: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "48px",
        color: tokens.colorNeutralForeground3
    }
});

// Icon mapping
const iconMap: Record<string, React.ReactElement> = {
    "Play": <Play24Regular />,
    "Brain": <BrainCircuit24Regular />,
    "Library": <Library24Regular />,
    "Wrench": <Wrench24Regular />,
    "Document": <Document24Regular />,
    "Filter": <Filter24Regular />,
    "Search": <Search24Regular />,
    "People": <People24Regular />,
    "Emoji": <Emoji24Regular />,
    "Code": <Code24Regular />,
    "Globe": <Globe24Regular />,
    "TextDocument": <TextDescription24Regular />,
    "DocumentText": <DocumentText24Regular />,
    "ReportDocument": <Document24Regular />,
    "Certificate": <Certificate24Regular />,
    "Shield": <Shield24Regular />,
    "Settings": <Settings24Regular />
};

function getIcon(iconName?: string): React.ReactElement {
    if (iconName && iconMap[iconName]) {
        return iconMap[iconName];
    }
    return <Document24Regular />;
}

export function ScopeList<T extends IScopeItem>({
    items,
    onSelectionChange,
    isLoading,
    emptyMessage = "No items available",
    multiSelect = true
}: IScopeListProps<T>): React.ReactElement {
    const styles = useStyles();

    const handleCheckboxChange = (itemId: string, checked: boolean): void => {
        const currentSelected = items.filter(i => i.isSelected).map(i => i.id);

        let newSelected: string[];
        if (checked) {
            newSelected = [...currentSelected, itemId];
        } else {
            newSelected = currentSelected.filter(id => id !== itemId);
        }

        onSelectionChange(newSelected);
    };

    const handleRadioChange = (_event: unknown, data: { value: string }): void => {
        onSelectionChange([data.value]);
    };

    if (isLoading) {
        return (
            <div className={styles.loading}>
                <Spinner size="medium" label="Loading items..." />
            </div>
        );
    }

    if (items.length === 0) {
        return (
            <div className={styles.empty}>
                <Text>{emptyMessage}</Text>
            </div>
        );
    }

    // Single select uses RadioGroup
    if (!multiSelect) {
        const selectedValue = items.find(i => i.isSelected)?.id || "";

        return (
            <RadioGroup
                value={selectedValue}
                onChange={handleRadioChange}
                className={styles.container}
            >
                {items.map((item) => (
                    <div
                        key={item.id}
                        className={mergeClasses(
                            styles.item,
                            item.isSelected && styles.itemSelected
                        )}
                    >
                        <Radio
                            value={item.id}
                            className={styles.selector}
                        />
                        <div className={styles.icon}>
                            {getIcon(item.icon)}
                        </div>
                        <div className={styles.content}>
                            <Text className={styles.name}>{item.name}</Text>
                            {item.description && (
                                <Text className={styles.description}>
                                    {item.description}
                                </Text>
                            )}
                        </div>
                    </div>
                ))}
            </RadioGroup>
        );
    }

    // Multi-select uses Checkboxes
    return (
        <div className={styles.container}>
            {items.map((item) => (
                <div
                    key={item.id}
                    className={mergeClasses(
                        styles.item,
                        item.isSelected && styles.itemSelected
                    )}
                >
                    <Checkbox
                        checked={item.isSelected}
                        onChange={(_e, data) => handleCheckboxChange(item.id, !!data.checked)}
                        className={styles.selector}
                    />
                    <div className={styles.icon}>
                        {getIcon(item.icon)}
                    </div>
                    <div className={styles.content}>
                        <Text className={styles.name}>{item.name}</Text>
                        {item.description && (
                            <Text className={styles.description}>
                                {item.description}
                            </Text>
                        )}
                    </div>
                </div>
            ))}
        </div>
    );
}

export default ScopeList;
