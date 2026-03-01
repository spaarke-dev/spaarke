/**
 * BuilderLayout — Main layout for the Playbook Builder Code Page
 *
 * Wraps the PlaybookCanvas in a ReactFlowProvider and manages
 * playbook loading state. Future phases will add node palette,
 * properties panel, and toolbar.
 */

import { makeStyles, tokens, Spinner, Text } from "@fluentui/react-components";
import { ReactFlowProvider } from "@xyflow/react";
import { PlaybookCanvas } from "./canvas/PlaybookCanvas";
import { usePlaybookLoader } from "../hooks/usePlaybookLoader";

interface BuilderLayoutProps {
    playbookId: string;
}

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
    canvasArea: {
        flex: 1,
        position: "relative",
        overflow: "hidden",
    },
    loading: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        gap: tokens.spacingHorizontalM,
    },
    error: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        color: tokens.colorPaletteRedForeground1,
        gap: tokens.spacingVerticalM,
    },
});

export function BuilderLayout({ playbookId }: BuilderLayoutProps): JSX.Element {
    const styles = useStyles();
    const { isLoading, error, playbookName } = usePlaybookLoader(playbookId);

    if (isLoading) {
        return (
            <div className={styles.loading}>
                <Spinner size="medium" />
                <Text>Loading playbook...</Text>
            </div>
        );
    }

    if (error) {
        return (
            <div className={styles.error}>
                <Text size={400} weight="semibold">
                    Failed to load playbook
                </Text>
                <Text>{error}</Text>
            </div>
        );
    }

    return (
        <div className={styles.root}>
            {/* Canvas area — ReactFlowProvider required for useReactFlow() hook */}
            <ReactFlowProvider>
                <div className={styles.canvasArea}>
                    <PlaybookCanvas />
                </div>
            </ReactFlowProvider>
        </div>
    );
}
