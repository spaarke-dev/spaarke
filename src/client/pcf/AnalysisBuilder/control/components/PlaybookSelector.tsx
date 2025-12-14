/**
 * PlaybookSelector Component
 *
 * Card-based grid selector for AI playbooks.
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 *
 * Features:
 * - Compact card design (no inline description)
 * - Info icon with Fluent V9 Popover for description
 */

import * as React from "react";
import {
    Card,
    CardHeader,
    Text,
    Spinner,
    makeStyles,
    tokens,
    mergeClasses,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Button
} from "@fluentui/react-components";
import {
    Lightbulb24Regular,
    Document24Regular,
    Certificate24Regular,
    Shield24Regular,
    Settings24Regular,
    Notebook24Regular,
    Info16Regular
} from "@fluentui/react-icons";
import { IPlaybookSelectorProps, IPlaybook } from "../types";

const useStyles = makeStyles({
    container: {
        paddingTop: "12px",
        paddingBottom: "12px",
        paddingLeft: "16px",
        paddingRight: "16px",
        borderBottomWidth: "1px",
        borderBottomStyle: "solid",
        borderBottomColor: tokens.colorNeutralStroke1,
        flexShrink: 0
    },
    label: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        marginBottom: "8px",
        display: "block"
    },
    scrollContainer: {
        display: "flex",
        overflowX: "auto",
        gap: "12px",
        paddingBottom: "4px",
        // Hide scrollbar but keep functionality
        scrollbarWidth: "thin",
        "::-webkit-scrollbar": {
            height: "4px"
        },
        "::-webkit-scrollbar-track": {
            backgroundColor: tokens.colorNeutralBackground3
        },
        "::-webkit-scrollbar-thumb": {
            backgroundColor: tokens.colorNeutralStroke1,
            borderRadius: "2px"
        }
    },
    card: {
        cursor: "pointer",
        flexShrink: 0,
        width: "130px",
        minWidth: "130px"
    },
    cardSelected: {
        backgroundColor: tokens.colorBrandBackground2
    },
    cardWrapper: {
        position: "relative"
    },
    cardContent: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        paddingTop: "6px",
        paddingBottom: "10px",
        paddingLeft: "6px",
        paddingRight: "6px",
        textAlign: "center"
    },
    infoButtonWrapper: {
        position: "absolute",
        top: "4px",
        right: "4px",
        zIndex: 1
    },
    infoButton: {
        minWidth: "24px",
        width: "24px",
        height: "24px"
    },
    icon: {
        width: "32px",
        height: "32px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        marginBottom: "6px",
        color: tokens.colorBrandForeground1
    },
    name: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        lineHeight: "1.3"
    },
    popoverContent: {
        maxWidth: "280px",
        paddingTop: "12px",
        paddingBottom: "12px",
        paddingLeft: "12px",
        paddingRight: "12px"
    },
    popoverTitle: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        marginBottom: "8px"
    },
    popoverDescription: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: "1.5"
    },
    loading: {
        display: "flex",
        justifyContent: "center",
        paddingTop: "24px",
        paddingBottom: "24px"
    },
    empty: {
        color: tokens.colorNeutralForeground3,
        textAlign: "center",
        paddingTop: "24px",
        paddingBottom: "24px"
    }
});

// Icon mapping
const iconMap: Record<string, React.ReactElement> = {
    "Lightbulb": <Lightbulb24Regular />,
    "DocumentText": <Document24Regular />,
    "Certificate": <Certificate24Regular />,
    "Shield": <Shield24Regular />,
    "Settings": <Settings24Regular />,
    "default": <Notebook24Regular />
};

function getIcon(iconName?: string): React.ReactElement {
    if (iconName && iconMap[iconName]) {
        return iconMap[iconName];
    }
    return iconMap["default"];
}

export const PlaybookSelector: React.FC<IPlaybookSelectorProps> = ({
    playbooks,
    selectedPlaybookId,
    onSelect,
    isLoading
}) => {
    const styles = useStyles();

    const handleCardClick = (playbook: IPlaybook): void => {
        onSelect(playbook);
    };

    const handleKeyDown = (event: React.KeyboardEvent, playbook: IPlaybook): void => {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            onSelect(playbook);
        }
    };

    if (isLoading) {
        return (
            <div className={styles.container}>
                <Text className={styles.label}>Select a Playbook</Text>
                <div className={styles.loading}>
                    <Spinner size="small" label="Loading playbooks..." />
                </div>
            </div>
        );
    }

    if (playbooks.length === 0) {
        return (
            <div className={styles.container}>
                <Text className={styles.label}>Select a Playbook</Text>
                <Text className={styles.empty}>No playbooks available</Text>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <Text className={styles.label}>Select a Playbook</Text>
            <div className={styles.scrollContainer}>
                {playbooks.map((playbook) => (
                    <Card
                        key={playbook.id}
                        className={mergeClasses(
                            styles.card,
                            selectedPlaybookId === playbook.id && styles.cardSelected
                        )}
                        onClick={() => handleCardClick(playbook)}
                        onKeyDown={(e) => handleKeyDown(e, playbook)}
                        tabIndex={0}
                        role="button"
                        aria-pressed={selectedPlaybookId === playbook.id}
                    >
                        {/* Info icon with Popover (Fluent V9 pattern) */}
                        {playbook.description && (
                            <Popover withArrow positioning="above">
                                <PopoverTrigger disableButtonEnhancement>
                                    <Button
                                        className={styles.infoButton}
                                        appearance="subtle"
                                        icon={<Info16Regular />}
                                        size="small"
                                        onClick={(e) => e.stopPropagation()}
                                        aria-label={`More info about ${playbook.name}`}
                                    />
                                </PopoverTrigger>
                                <PopoverSurface>
                                    <div className={styles.popoverContent}>
                                        <Text className={styles.popoverTitle}>{playbook.name}</Text>
                                        <Text className={styles.popoverDescription}>
                                            {playbook.description}
                                        </Text>
                                    </div>
                                </PopoverSurface>
                            </Popover>
                        )}
                        <CardHeader
                            header={
                                <div className={styles.cardContent}>
                                    <div className={styles.icon}>
                                        {getIcon(playbook.icon)}
                                    </div>
                                    <Text className={styles.name}>{playbook.name}</Text>
                                </div>
                            }
                        />
                    </Card>
                ))}
            </div>
        </div>
    );
};

export default PlaybookSelector;
