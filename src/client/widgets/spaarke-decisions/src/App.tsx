/**
 * The widget shell: a FluentProvider whose theme is the host's (ADR-021), around the Decide lane (task 031) and the Decision wizard
 * (task 032). The lane raises `onOpenWizard` for a row or a review step; the shell numbers the request and hands it to `WizardDialog`, which
 * shows the shared wizard in the frame (dialog, then master-detail, then a link to the Console). A recorded decision makes the lane read
 * itself again in place, so the row leaves it and the count drops. Fluent v9 semantic tokens only; no colour is written here.
 */
import * as React from 'react';
import { FluentProvider, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { createWorklistService, type WorklistData } from '@spaarke/ui-components/components/Worklist/worklistService';
import { DecideLane, type OpenWizardEvent } from './lane/DecideLane';
import { consoleBaseUrlOf } from './lane/billingLinkOut';
import { createDecideLaneFetch } from './lane/decideLaneFetch';
import { useMcpHost } from './mcp/mcpHostContext';
import { hostThemeName, themeFromHostContext } from './theme/hostTheme';
import { WizardDialog, type WizardRequest } from './wizard/WizardDialog';

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

  // The wizard opens on the lane as the user saw it (the lane reports each read), and a recorded decision reads the lane again.
  const lane = React.useRef<WorklistData | null>(null);
  const getLane = React.useCallback(() => lane.current, []);
  const onData = React.useCallback((data: WorklistData | null) => {
    lane.current = data;
  }, []);
  const [request, setRequest] = React.useState<WizardRequest | null>(null);
  const requests = React.useRef(0);
  const [reloadToken, setReloadToken] = React.useState(0);

  const openWizard = React.useCallback(
    (event: OpenWizardEvent) => {
      onOpenWizard?.(event);
      requests.current += 1;
      setRequest({ event, seq: requests.current });
    },
    [onOpenWizard]
  );
  const onRecorded = React.useCallback(() => setReloadToken(n => n + 1), []);
  const consoleBaseUrl = consoleBaseUrlOf(toolResult);

  return (
    <main className={styles.root} aria-label="Spaarke decisions">
      {error ? (
        <Text role="alert" className={styles.error}>
          The widget could not connect to the host.
        </Text>
      ) : isConnected ? (
        <WizardDialog request={request} getLane={getLane} consoleBaseUrl={consoleBaseUrl} onRecorded={onRecorded}>
          <DecideLane
            service={service}
            consoleBaseUrl={consoleBaseUrl}
            openLink={openLink}
            onOpenWizard={openWizard}
            reloadToken={reloadToken}
            onData={onData}
          />
        </WizardDialog>
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
