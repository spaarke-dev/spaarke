import { makeStyles, tokens, Text } from "@fluentui/react-components";

interface BuilderLayoutProps {
    playbookId: string;
}

const useStyles = makeStyles({
    root: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
});

export function BuilderLayout({ playbookId }: BuilderLayoutProps): JSX.Element {
    const styles = useStyles();

    return (
        <div className={styles.root}>
            <Text size={500}>
                Playbook Builder — {playbookId || "No playbook ID provided"}
            </Text>
        </div>
    );
}
