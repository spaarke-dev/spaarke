/**
 * The playbook run widget (ui://spaarke/playbook-run.html; spaarkeai-mcp-bff-api-r2 task 037c, FR-16, NFR-09). It opens from the result of
 * `spaarke_run_playbook` or `spaarke_get_playbook_run` and shows, in a FluentProvider whose theme is the host's (ADR-021):
 *  - a header with the playbook's name (once the results tool has named it) and a state badge;
 *  - while the run is queued or running, a progress bar from the status tool's progress and counts, polled on a bounded schedule
 *    (`useRunController`);
 *  - once it has finished, the results of the app-only `spaarke_get_playbook_run_results` (called once): collapsible sections rendered as
 *    safe Markdown (`MarkdownText`: React text only), a "More in Spaarke" link when the server cut them, the link to the analysis in the
 *    Console, and the source documents, each with its own link;
 *  - plain-word empty, not-found and error states.
 * The widget holds no token and opens no connection (empty CSP): every read is a tool call through the host, and every link opens only
 * through the host's `openLink` (links are buttons, never anchors). Fluent v9 semantic tokens only; no colour is written here.
 */
import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Badge,
  Button,
  FluentProvider,
  Link,
  MessageBar,
  MessageBarBody,
  ProgressBar,
  Skeleton,
  SkeletonItem,
  Spinner,
  Text,
  makeStyles,
  tokens,
  type BadgeProps,
} from '@fluentui/react-components';
import { useMcpHost } from '../../shared/mcp/mcpHostContext';
import { hostThemeName, themeFromHostContext } from '../../shared/mcp/hostTheme';
import { useWidgetMcpTransport } from '../../shared/mcp/widgetMcpTransport';
import { MarkdownText } from './run/markdown';
import {
  outcomeLabel,
  progressLine,
  safeLink,
  type RunOutcome,
  type RunResults,
  type RunState,
} from './run/playbookRun';
import { useRunController, type RunView } from './run/useRunController';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
    overflowWrap: 'anywhere',
  },
  progress: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
  },
  sectionHeading: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
  cut: {
    marginTop: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
  },
  actions: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
  documents: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  documentList: {
    margin: 0,
    paddingLeft: tokens.spacingHorizontalXL,
  },
  subheading: {
    margin: 0,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
});

/** How long the widget waits for the result that opened it before it says none came (the tools answer within 30 s, FR-14). */
export const RESULT_WAIT_MS = 45_000;

const NO_RESULT = 'No playbook run arrived. Ask again in the chat.';
const LINK_NOT_OPENED = 'Your host did not open that link. Open the run from the Spaarke Console.';

const BADGE_COLOUR: Record<RunOutcome, BadgeProps['color']> = {
  queued: 'informative',
  running: 'brand',
  completed: 'success',
  failed: 'danger',
  cancelled: 'warning',
  interrupted: 'warning',
};

function stateOf(view: RunView): RunState | null {
  return view.kind === 'waiting' ? null : view.state;
}

function finishedLine(outcome: RunOutcome): string {
  switch (outcome) {
    case 'completed':
      return 'The run completed.';
    case 'cancelled':
      return 'The run was stopped before it finished.';
    case 'interrupted':
      return 'The run was interrupted (Spaarke restarted while it ran) and did not finish. Start it again in the chat.';
    default:
      return 'The run failed.';
  }
}

interface LinkOpener {
  open: (url: string | null) => void;
}

function Results({ results, opener }: { results: RunResults; opener: LinkOpener }) {
  const styles = useStyles();
  const { sections, documents } = results;

  return (
    <>
      {results.outcome !== 'completed' ? <Text>{finishedLine(results.outcome)}</Text> : null}

      {results.results === 'available' && sections.length > 0 ? (
        <Accordion multiple collapsible defaultOpenItems={['section-0']}>
          {sections.map((section, index) => (
            <AccordionItem key={index} value={`section-${index}`}>
              <AccordionHeader as="h3" className={styles.sectionHeading}>
                {section.heading ?? (sections.length === 1 ? 'Results' : `Part ${index + 1}`)}
              </AccordionHeader>
              <AccordionPanel>
                <MarkdownText text={section.text} />
                {section.truncated ? (
                  <Text as="p" size={200} className={styles.cut}>
                    This section was shortened. The full text is in Spaarke.
                  </Text>
                ) : null}
              </AccordionPanel>
            </AccordionItem>
          ))}
        </Accordion>
      ) : null}

      {results.results === 'none' || (results.results === 'available' && sections.length === 0) ? (
        <Text>
          {results.outcome === 'completed'
            ? 'The run finished but saved no results to show here.'
            : 'There are no results to show for this run.'}
        </Text>
      ) : null}

      {results.results === 'unsaved' ? (
        <MessageBar intent="warning">
          <MessageBarBody>
            The run finished but its results could not be saved. Open the analysis in Spaarke or run it again.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {results.truncated ? (
        <MessageBar intent="info">
          <MessageBarBody>Some of the results were left out to keep this short.</MessageBarBody>
        </MessageBar>
      ) : null}

      <div className={styles.actions}>
        {results.moreLink ? (
          <Button appearance="primary" onClick={() => opener.open(results.moreLink)}>
            More in Spaarke
          </Button>
        ) : null}
        {results.consoleLink ? (
          <Button
            appearance={results.moreLink ? 'secondary' : 'primary'}
            onClick={() => opener.open(results.consoleLink)}
          >
            Open in Spaarke
          </Button>
        ) : null}
      </div>

      {documents.length > 0 ? (
        <section className={styles.documents} aria-label="Source documents">
          <h3 className={styles.subheading}>Source documents</h3>
          <ul className={styles.documentList}>
            {documents.map(document => (
              <li key={document.documentId}>
                {document.link ? (
                  <Link as="button" onClick={() => opener.open(document.link)}>
                    {document.name}
                  </Link>
                ) : (
                  <Text>{document.name}</Text>
                )}
              </li>
            ))}
          </ul>
          {results.moreDocuments > 0 ? (
            <Text as="p" size={200}>
              {`And ${results.moreDocuments} more ${results.moreDocuments === 1 ? 'document' : 'documents'}.`}
            </Text>
          ) : null}
        </section>
      ) : null}
    </>
  );
}

function Shell({ resultWaitMs = RESULT_WAIT_MS }: { resultWaitMs?: number }) {
  const styles = useStyles();
  const { isConnected, error, openLink, toolResult } = useMcpHost();
  const transport = useWidgetMcpTransport();
  const { view, checkAgain, retryResults } = useRunController(isConnected ? toolResult : null, transport);
  const [linkError, setLinkError] = React.useState(false);
  const [gaveUp, setGaveUp] = React.useState(false);

  React.useEffect(() => setLinkError(false), [toolResult]);

  // A widget opened with no result at all says so after a while instead of spinning for ever.
  React.useEffect(() => {
    if (!isConnected || view.kind !== 'waiting') {
      setGaveUp(false);
      return undefined;
    }
    const timer = setTimeout(() => setGaveUp(true), resultWaitMs);
    return () => clearTimeout(timer);
  }, [isConnected, view.kind, resultWaitMs]);

  const opener = React.useMemo<LinkOpener>(
    () => ({
      open: (value: string | null) => {
        const url = safeLink(value);
        if (!url) return;
        void (async () => {
          let opened = false;
          try {
            opened = await openLink(url);
          } catch {
            opened = false;
          }
          setLinkError(!opened);
        })();
      },
    }),
    [openLink]
  );

  const state = stateOf(view);
  const playbookName = view.kind === 'results' ? view.results.playbookName : null;
  const outcome = view.kind === 'results' ? view.results.outcome : state?.outcome;

  let body: React.ReactNode;
  if (error) {
    body = (
      <Text role="alert" className={styles.error}>
        The widget could not connect to the host.
      </Text>
    );
  } else if (!isConnected) {
    body = <Spinner size="tiny" label="Connecting to the host" labelPosition="after" />;
  } else if (view.kind === 'waiting') {
    body = gaveUp ? (
      <Text role="alert" className={styles.error}>
        {NO_RESULT}
      </Text>
    ) : (
      <Spinner size="tiny" label="Waiting for the playbook run" labelPosition="after" />
    );
  } else if (view.kind === 'polling' || view.kind === 'stopped') {
    const { state: running } = view;
    body = (
      <div className={styles.progress}>
        <ProgressBar
          aria-label="Run progress"
          thickness="large"
          value={running.progress === null ? undefined : running.progress / 100}
        />
        <Text role="status" size={200} className={styles.muted}>
          {progressLine(running)}
        </Text>
        {view.kind === 'stopped' ? (
          <>
            <Text>
              {view.reason === 'cap'
                ? 'The run is still going after 50 minutes, so this widget has stopped checking. Check again, or open it in Spaarke.'
                : 'Spaarke did not answer for a while, so this widget has stopped checking.'}
            </Text>
            <div className={styles.actions}>
              <Button appearance="primary" onClick={checkAgain}>
                Check again
              </Button>
              {running.consoleLink ? (
                <Button appearance="secondary" onClick={() => opener.open(running.consoleLink)}>
                  Open in Spaarke
                </Button>
              ) : null}
            </div>
          </>
        ) : (
          <Text size={200} className={styles.muted}>
            The run continues in Spaarke. This updates by itself.
          </Text>
        )}
      </div>
    );
  } else if (view.kind === 'loading-results') {
    body = (
      <Skeleton aria-label="Loading the results">
        <SkeletonItem size={16} />
        <SkeletonItem size={16} />
        <SkeletonItem size={16} />
      </Skeleton>
    );
  } else if (view.kind === 'failed') {
    body = (
      <>
        <Text role="alert" className={view.failure.kind === 'not-found' ? undefined : styles.error}>
          {view.failure.message}
        </Text>
        {view.canRetry ? (
          <div className={styles.actions}>
            <Button appearance="secondary" onClick={retryResults}>
              Try again
            </Button>
          </div>
        ) : null}
      </>
    );
  } else if (view.results.results === 'pending') {
    body = (
      <>
        <Text>The run has finished and is still saving its results.</Text>
        <div className={styles.actions}>
          <Button appearance="primary" onClick={retryResults}>
            Check again
          </Button>
        </div>
      </>
    );
  } else {
    body = <Results results={view.results} opener={opener} />;
  }

  return (
    <main className={styles.root} aria-label="Spaarke playbook run">
      <header className={styles.header}>
        <h2 className={styles.title}>{playbookName ?? 'Playbook run'}</h2>
        {outcome && view.kind !== 'failed' ? (
          <Badge appearance="tint" color={BADGE_COLOUR[outcome]}>
            {outcomeLabel(outcome)}
          </Badge>
        ) : null}
      </header>
      <div className={styles.progress}>{body}</div>
      {linkError ? (
        <MessageBar intent="warning">
          <MessageBarBody>{LINK_NOT_OPENED}</MessageBarBody>
        </MessageBar>
      ) : null}
    </main>
  );
}

export function App({ resultWaitMs }: { resultWaitMs?: number } = {}) {
  const { hostContext } = useMcpHost();
  return (
    <FluentProvider theme={themeFromHostContext(hostContext)} data-theme={hostThemeName(hostContext)}>
      <Shell resultWaitMs={resultWaitMs} />
    </FluentProvider>
  );
}
