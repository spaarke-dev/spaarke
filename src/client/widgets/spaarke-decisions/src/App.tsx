/**
 * The widget shell: a FluentProvider whose theme is the host's (ADR-021), around the Decide lane (task 031). The Decision wizard lands in
 * task 032, which passes `onOpenWizard`. Fluent v9 semantic tokens only; no colour is written here.
 */
import * as React from 'react';
import { FluentProvider, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { createWorklistService } from '@spaarke/ui-components/components/Worklist/worklistService';
import { DecideLane, type OpenWizardEvent } from './lane/DecideLane';
import { consoleBaseUrlOf } from './lane/billingLinkOut';
import { createDecideLaneFetch } from './lane/decideLaneFetch';
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
  error: {
    color: tokens.colorPaletteRedForeground1,
  },
});

function Shell({ onOpenWizard }: { onOpenWizard?: (event: OpenWizardEvent) => void }) {
  const styles = useStyles();
  const { isConnected, error, callTool, openLink, toolResult } = useMcpHost();

  // The shared service over the MCP shim: no origin, no token; every read is a `callServerTool` through the host. The base URL is empty
  // because the address is never sent anywhere, only mapped to a tool.
  const service = React.useMemo(
    () => createWorklistService({ fetch: createDecideLaneFetch(callTool), baseUrl: '' }),
    [callTool]
  );

  return (
    <main className={styles.root} aria-label="Spaarke decisions">
      {error ? (
        <Text role="alert" className={styles.error}>
          The widget could not connect to the host.
        </Text>
      ) : isConnected ? (
        <DecideLane
          service={service}
          consoleBaseUrl={consoleBaseUrlOf(toolResult)}
          openLink={openLink}
          onOpenWizard={onOpenWizard}
        />
      ) : (
        <Spinner size="tiny" label="Connecting to the host" labelPosition="after" />
      )}
    </main>
  );
}

export function App({ onOpenWizard }: { onOpenWizard?: (event: OpenWizardEvent) => void } = {}) {
  const { hostContext } = useMcpHost();
  return (
    <FluentProvider theme={themeFromHostContext(hostContext)} data-theme={hostThemeName(hostContext)}>
      <Shell onOpenWizard={onOpenWizard} />
    </FluentProvider>
  );
}
