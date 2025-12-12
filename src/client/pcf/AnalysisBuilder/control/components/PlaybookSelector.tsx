/**
 * PlaybookSelector Component
 *
 * Card-based grid selector for AI playbooks.
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 */

import * as React from "react";
import {
    Card,
    CardHeader,
    Text,
    Spinner,
    makeStyles,
    tokens,
    mergeClasses
} from "@fluentui/react-components";
import {
    Lightbulb24Regular,
    Document24Regular,
    Certificate24Regular,
    Shield24Regular,
    Settings24Regular,
    BookTemplate24Regular
} from "@fluentui/react-icons";
import { IPlaybookSelectorProps, IPlaybook } from "../types";

const useStyles = makeStyles({
    container: {
        padding: "16px 24px",
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`
    },
    label: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        marginBottom: "12px",
        display: "block"
    },
    grid: {
        display: "grid",
        gridTemplateColumns: "repeat(auto-fill, minmax(160px, 1fr))",
        gap: "12px"
    },
    card: {
        cursor: "pointer",
        transition: "all 0.15s ease",
        ":hover": {
            borderColor: tokens.colorBrandStroke1
        }
    },
    cardSelected: {
        borderColor: tokens.colorBrandStroke1,
        backgroundColor: tokens.colorBrandBackground2
    },
    cardContent: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        padding: "12px",
        textAlign: "center" as const
    },
    icon: {
        width: "40px",
        height: "40px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        marginBottom: "8px",
        color: tokens.colorBrandForeground1
    },
    name: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        marginTop: "4px"
    },
    loading: {
        display: "flex",
        justifyContent: "center",
        padding: "24px"
    },
    empty: {
        color: tokens.colorNeutralForeground3,
        textAlign: "center" as const,
        padding: "24px"
    }
});

// Icon mapping
const iconMap: Record<string, React.ReactElement> = {
    "Lightbulb": <Lightbulb24Regular />,
    "DocumentText": <Document24Regular />,
    "Certificate": <Certificate24Regular />,
    "Shield": <Shield24Regular />,
    "Settings": <Settings24Regular />,
    "default": <BookTemplate24Regular />
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
            <div className={styles.grid}>
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
                        <CardHeader
                            header={
                                <div className={styles.cardContent}>
                                    <div className={styles.icon}>
                                        {getIcon(playbook.icon)}
                                    </div>
                                    <Text className={styles.name}>{playbook.name}</Text>
                                    {playbook.description && (
                                        <Text className={styles.description}>
                                            {playbook.description}
                                        </Text>
                                    )}
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
