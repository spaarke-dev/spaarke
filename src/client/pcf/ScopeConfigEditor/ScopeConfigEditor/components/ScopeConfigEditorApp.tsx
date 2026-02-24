/**
 * ScopeConfigEditorApp
 *
 * Root component that auto-detects the entity logical name and renders
 * the appropriate editor variant:
 *   sprk_analysisaction      → ActionEditor
 *   sprk_analysisskill      → SkillEditor
 *   sprk_analysisknowledge  → KnowledgeSourceEditor
 *   sprk_analysistool       → ToolEditor
 *
 * ADR-021: All styling via makeStyles / Fluent v9 tokens. No hardcoded colors.
 * ADR-022: React 16 APIs only. No createRoot.
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    MessageBar,
    MessageBarBody,
    Text,
} from "@fluentui/react-components";
import { ActionEditor } from "./ActionEditor";
import { SkillEditor } from "./SkillEditor";
import { KnowledgeSourceEditor } from "./KnowledgeSourceEditor";
import { ToolEditor } from "./ToolEditor";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IScopeConfigEditorAppProps {
    /** Dataverse entity logical name (e.g., "sprk_analysisaction") */
    entityLogicalName: string;
    /** Current field value from the bound property */
    fieldValue: string;
    /** BFF API base URL for handler discovery */
    apiBaseUrl: string;
    /** Callback when value changes — propagates to PCF output */
    onValueChange: (newValue: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        boxSizing: "border-box",
        minHeight: "200px",
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
    unknownEntity: {
        padding: tokens.spacingVerticalM,
    },
    versionFooter: {
        marginTop: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalXS,
        ...shorthands.borderTop("1px", "solid", tokens.colorNeutralStroke2),
        color: tokens.colorNeutralForeground4,
        fontSize: tokens.fontSizeBase100,
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ScopeConfigEditorApp: React.FC<IScopeConfigEditorAppProps> = ({
    entityLogicalName,
    fieldValue,
    apiBaseUrl,
    onValueChange,
}) => {
    const styles = useStyles();

    const renderEditor = (): React.ReactElement => {
        const entity = entityLogicalName.toLowerCase();

        if (entity === "sprk_analysisaction") {
            return (
                <ActionEditor
                    value={fieldValue}
                    onChange={onValueChange}
                />
            );
        }

        if (entity === "sprk_analysisskill") {
            return (
                <SkillEditor
                    value={fieldValue}
                    onChange={onValueChange}
                />
            );
        }

        if (entity === "sprk_analysisknowledge") {
            return (
                <KnowledgeSourceEditor
                    value={fieldValue}
                    onChange={onValueChange}
                />
            );
        }

        if (entity === "sprk_analysistool") {
            return (
                <ToolEditor
                    value={fieldValue}
                    apiBaseUrl={apiBaseUrl}
                    onChange={onValueChange}
                />
            );
        }

        // Fallback: show informational message
        return (
            <div className={styles.unknownEntity}>
                <MessageBar intent="warning">
                    <MessageBarBody>
                        ScopeConfigEditor: unknown entity type &quot;{entityLogicalName}&quot;.
                        Expected one of: sprk_analysisaction, sprk_analysisskill, sprk_analysisknowledge, sprk_analysistool.
                    </MessageBarBody>
                </MessageBar>
            </div>
        );
    };

    return (
        <div className={styles.root}>
            {renderEditor()}
            <Text className={styles.versionFooter}>
                v1.2.6 &bull; Built 2026-02-24
            </Text>
        </div>
    );
};
