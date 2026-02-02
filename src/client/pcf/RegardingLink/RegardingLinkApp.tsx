/**
 * RegardingLink React App Component
 *
 * Displays a navigation link to the parent (regarding) record.
 */

import * as React from "react";
import { Link, Text, makeStyles, tokens } from "@fluentui/react-components";
import { Open16Regular } from "@fluentui/react-icons";

// STUB: [CONFIG] - S007: Hardcoded entity type map - must match AssociationResolver ENTITY_CONFIGS (Task 020)
// Values correspond to sprk_event.sprk_regardingrecordtype optionset
const ENTITY_TYPE_MAP: Record<number, string> = {
    0: "sprk_project",
    1: "sprk_matter",
    2: "sprk_invoice",
    3: "sprk_analysis",
    4: "account",
    5: "contact",
    6: "sprk_workassignment",
    7: "sprk_budget"
};

interface RegardingLinkAppProps {
    regardingRecordType: number | null;
    regardingRecordId: string;
    regardingRecordName: string;
    version: string;
}

const useStyles = makeStyles({
    container: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalXS
    },
    link: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS
    },
    emptyState: {
        color: tokens.colorNeutralForeground3,
        fontStyle: "italic"
    },
    versionText: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground4,
        marginLeft: "auto"
    }
});

export const RegardingLinkApp: React.FC<RegardingLinkAppProps> = ({
    regardingRecordType,
    regardingRecordId,
    regardingRecordName,
    version
}) => {
    const styles = useStyles();

    const handleNavigate = () => {
        if (!regardingRecordId || regardingRecordType === null) return;

        const entityLogicalName = ENTITY_TYPE_MAP[regardingRecordType];
        if (!entityLogicalName) {
            console.error("[RegardingLink] Unknown entity type:", regardingRecordType);
            return;
        }

        // Navigate using Xrm.Navigation.openForm
        const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
        if (xrm?.Navigation?.openForm) {
            xrm.Navigation.openForm({
                entityName: entityLogicalName,
                entityId: regardingRecordId
            });
        } else {
            console.error("[RegardingLink] Xrm.Navigation.openForm not available");
        }
    };

    // No record selected - show em dash per acceptance criteria
    if (!regardingRecordId || !regardingRecordName) {
        return (
            <div className={styles.container}>
                <Text className={styles.emptyState}>—</Text>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            <Link className={styles.link} onClick={handleNavigate}>
                {regardingRecordName}
                <Open16Regular />
            </Link>
            <Text className={styles.versionText}>v{version}</Text>
        </div>
    );
};
