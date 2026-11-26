/**
 * The widget shell: a FluentProvider whose theme is the host's (ADR-021) around the Console's `DailyBriefingApp` (spaarkeai-mcp-bff-api-r2
 * task 034, FR-09 / FR-16). One component, the Console's own; it is given a `BriefingDataService` over the shared widget MCP transport, so its
 * briefing comes from the `spaarke_get_briefing` tool through the host and it never reaches a network, a token or Xrm (task 010's seam).
 * What the host did not supply is hidden by the component, not stubbed here: record links (no `openRecord`), the Preferences menu (no
 * preferences service), inline To Do, dismiss, email share and document preview. Fluent v9 semantic tokens only; no colour is written here.
 */
import * as React from 'react';
import {
  FluentProvider,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { DailyBriefingApp } from '@spaarke/daily-briefing-components/components/DailyBriefingApp';
import type { BriefingNavigationHandler } from '@spaarke/daily-briefing-components/services/briefingDataService';
import { useMcpHost } from '../../shared/mcp/mcpHostContext';
import { hostThemeName, themeFromHostContext } from '../../shared/mcp/hostTheme';
import { useWidgetMcpTransport } from '../../shared/mcp/widgetMcpTransport';
import { OpeningResult, createBriefingMcpService } from './briefing/briefingMcpService';

const useStyles = makeStyles({
  root: {
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  status: {
    padding: tokens.spacingHorizontalL,
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
  },
});

// DailyBriefingApp takes its params from the Dataverse page it is hosted in; a widget has none.
const NO_PARAMS: Record<string, string> = {};

/** The one notice the widget adds to the Console's component: the tool trimmed this briefing to fit its result limit. */
export function omittedNotice(omitted: number): string {
  const what = omitted === 1 ? '1 highlight was' : `${omitted} highlights were`;
  return `${what} left out; open the Spaarke Console for all of them.`;
}

function Shell({ openingWaitMs }: { openingWaitMs?: number }) {
  const styles = useStyles();
  const { isConnected, error, openLink, toolResult } = useMcpHost();
  const transport = useWidgetMcpTransport();

  // The result that opened the widget is the briefing the host already has: the first read takes it instead of generating another.
  const [opening] = React.useState(() => new OpeningResult());
  React.useEffect(() => opening.offer(toolResult), [opening, toolResult]);

  // How many highlights the tool left out of the briefing now shown (0: none). Set by every read, by the service below.
  const [omitted, setOmitted] = React.useState(0);

  // Supplied on the component's FIRST render (it decides injected vs Xrm when it mounts), so both are built before it is shown.
  const dataService = React.useMemo(
    () => createBriefingMcpService({ transport, opening, openingWaitMs, onOmittedHighlights: setOmitted }),
    [transport, opening, openingWaitMs]
  );
  const navigation = React.useMemo<BriefingNavigationHandler>(
    () => ({
      openUrl: async url => {
        await openLink(url);
      },
    }),
    [openLink]
  );

  return (
    <main className={styles.root} aria-label="Spaarke daily briefing">
      {error ? (
        <Text role="alert" className={mergeClasses(styles.status, styles.error)}>
          The widget could not connect to the host.
        </Text>
      ) : isConnected ? (
        <>
          {omitted > 0 ? (
            <MessageBar intent="warning">
              <MessageBarBody>{omittedNotice(omitted)}</MessageBarBody>
            </MessageBar>
          ) : null}
          <DailyBriefingApp params={NO_PARAMS} dataService={dataService} navigation={navigation} />
        </>
      ) : (
        <Spinner className={styles.status} size="tiny" label="Connecting to the host" labelPosition="after" />
      )}
    </main>
  );
}

export function App({ openingWaitMs }: { openingWaitMs?: number } = {}) {
  const { hostContext } = useMcpHost();
  return (
    <FluentProvider theme={themeFromHostContext(hostContext)} data-theme={hostThemeName(hostContext)}>
      <Shell openingWaitMs={openingWaitMs} />
    </FluentProvider>
  );
}
