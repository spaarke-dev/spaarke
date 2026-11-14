/**
 * The widget shell (task 030). A FluentProvider whose theme is the host's (ADR-021), around a placeholder: the Decide lane lands in
 * task 031 and the Decision wizard in task 032. Fluent v9 semantic tokens only; no colour is written here.
 */
import { FluentProvider, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { useMcpHost } from './mcp/mcpHostContext';
import { hostThemeName, themeFromHostContext } from './theme/hostTheme';

const useStyles = makeStyles({
  root: {
    padding: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
  },
});

function Shell() {
  const styles = useStyles();
  const { isConnected, error } = useMcpHost();

  return (
    <main className={styles.root} aria-label="Spaarke decisions">
      <Text as="h1" size={500} weight="semibold">
        Decide
      </Text>
      {error ? (
        <Text role="alert" className={styles.error}>
          The widget could not connect to the host.
        </Text>
      ) : isConnected ? (
        <Text className={styles.muted}>Items that need your decision will appear here.</Text>
      ) : (
        <Spinner size="tiny" label="Connecting to the host" labelPosition="after" />
      )}
    </main>
  );
}

export function App() {
  const { hostContext } = useMcpHost();
  return (
    <FluentProvider theme={themeFromHostContext(hostContext)} data-theme={hostThemeName(hostContext)}>
      <Shell />
    </FluentProvider>
  );
}
