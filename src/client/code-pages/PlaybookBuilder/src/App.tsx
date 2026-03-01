import { makeStyles, tokens, Spinner, Text } from "@fluentui/react-components";
import { useAuth } from "./hooks/useAuth";
import { BuilderLayout } from "./components/BuilderLayout";

interface AppProps {
    playbookId: string;
}

const useStyles = makeStyles({
    loading: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        gap: tokens.spacingHorizontalM,
    },
    error: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorPaletteRedForeground1,
        gap: tokens.spacingVerticalM,
    },
});

export function App({ playbookId }: AppProps): JSX.Element {
    const styles = useStyles();
    const auth = useAuth();

    if (auth.isLoading) {
        return (
            <div className={styles.loading}>
                <Spinner size="medium" />
                <Text>Initializing...</Text>
            </div>
        );
    }

    if (auth.error) {
        return (
            <div className={styles.error}>
                <Text size={400} weight="semibold">Authentication Error</Text>
                <Text>{auth.error}</Text>
            </div>
        );
    }

    return <BuilderLayout playbookId={playbookId} />;
}
