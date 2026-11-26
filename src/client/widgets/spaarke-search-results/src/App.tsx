/**
 * The widget shell: a FluentProvider whose theme is the host's (ADR-021) around the Console's `SearchResultsWidget` (spaarkeai-mcp-bff-api-r2
 * task 035, FR-16). One component, the Console's own, used as-is through its props: the hits come from the result of `spaarke_search_documents`
 * / `spaarke_find_records` that opened the widget, and further pages from the same tool through the shared widget MCP transport. The widget
 * holds no token and opens no connection (empty CSP): every read is a tool call through the host, and a link opens only through the host's
 * `openLink`. Fluent v9 semantic tokens only; no colour is written here.
 *
 * Nothing the widget cannot do is drawn:
 *  - a hit with no link has a plain title (the component links a title only when it has a `url`);
 *  - "Load more" appears only when the tool advised a next offset AND the host sent the call's arguments (a further call needs them);
 *  - the score badge the component always draws is hidden, because the tools expose no score (D-035-score).
 * Text from a hit is rendered by the component as React text, never HTML.
 */
import * as React from 'react';
import {
  Button,
  FluentProvider,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import SearchResultsWidget from '../../../shared/Spaarke.AI.Widgets/src/outputs/output-widgets/SearchResultsWidget';
import { useMcpHost } from '../../shared/mcp/mcpHostContext';
import { hostThemeName, themeFromHostContext } from '../../shared/mcp/hostTheme';
import { useWidgetMcpTransport } from '../../shared/mcp/widgetMcpTransport';
import {
  nextCallArguments,
  noticesFor,
  parseSearchResult,
  queryOf,
  safeLink,
  toResultItems,
  type SearchPage,
} from './search/searchResults';

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
  // The component always draws a relevance badge in each card's header action slot; the tools expose no score, so it is hidden.
  noScore: {
    '& .fui-CardHeader__action': {
      display: 'none',
    },
  },
  footer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingVerticalL,
  },
  ending: {
    color: tokens.colorNeutralForeground3,
  },
});

/** How long the widget waits for the result that opened it before it says none came (the tool answers within 30 s, FR-14). */
export const RESULT_WAIT_MS = 45_000;

const NO_RESULT = 'No search results arrived. Ask again in the chat.';
const LINK_NOT_OPENED = 'Your host did not open that link. Open the record from the Spaarke Console.';

const NO_PAGES: SearchPage[] = [];

interface MorePages {
  /** The opening result these pages continue; a new opening result starts the list again. */
  source: unknown;
  pages: SearchPage[];
}

function Shell({ resultWaitMs = RESULT_WAIT_MS }: { resultWaitMs?: number }) {
  const styles = useStyles();
  const { isConnected, error, openLink, toolResult, toolInput } = useMcpHost();
  const transport = useWidgetMcpTransport();

  const opening = React.useMemo(
    () => (toolResult === null || toolResult === undefined ? null : parseSearchResult(toolResult)),
    [toolResult]
  );

  const [more, setMore] = React.useState<MorePages>({ source: undefined, pages: [] });
  const [loading, setLoading] = React.useState(false);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [linkError, setLinkError] = React.useState(false);
  const [gaveUp, setGaveUp] = React.useState(false);

  // A new opening result is a new search: forget the pages and notices of the last one.
  const extra = more.source === toolResult ? more.pages : NO_PAGES;
  React.useEffect(() => {
    setLoadError(null);
    setLinkError(false);
  }, [toolResult]);

  // A widget opened with no result at all says so after a while instead of spinning for ever.
  React.useEffect(() => {
    if (!isConnected || opening) return undefined;
    const timer = setTimeout(() => setGaveUp(true), resultWaitMs);
    return () => clearTimeout(timer);
  }, [isConnected, opening, resultWaitMs]);

  const first = opening?.status === 'page' ? opening.page : null;
  const pages = React.useMemo(() => (first ? [first, ...extra] : []), [first, extra]);
  const last = pages.length ? pages[pages.length - 1] : null;
  const hits = React.useMemo(() => pages.flatMap(page => page.hits), [pages]);
  const shown = React.useMemo(() => {
    const seen = new Set<string>();
    return hits.filter(hit => (seen.has(hit.id) ? false : (seen.add(hit.id), true)));
  }, [hits]);

  const next = last && last.nextOffset !== null ? nextCallArguments(last.kind, toolInput, last.nextOffset) : null;
  const notices = last
    ? noticesFor(
        last,
        pages.some(page => page.partial),
        next !== null
      )
    : { incomplete: [], ending: null };

  const loadMore = React.useCallback(async () => {
    if (!next || loading) return;
    setLoading(true);
    setLoadError(null);
    const result = await transport.callTool(next.tool, next.args);
    const text = result.content?.find(block => block.type === 'text')?.text;
    const parsed = parseSearchResult(result.structuredContent, { isError: result.isError, message: text });
    if (parsed.status === 'page') {
      setMore(previous => ({
        source: toolResult,
        pages: [...(previous.source === toolResult ? previous.pages : []), parsed.page],
      }));
    } else {
      setLoadError(parsed.message);
    }
    setLoading(false);
  }, [next, loading, transport, toolResult]);

  // A link opens only through the host. The component renders a real anchor for a hit with a link (`target="_blank"`); a click on any anchor
  // inside the list is taken here, never left to the frame, and handed to the host's openLink.
  const openThroughHost = React.useCallback(
    async (event: React.SyntheticEvent) => {
      const target = event.target as Element | null;
      const anchor = target && typeof target.closest === 'function' ? target.closest('a') : null;
      if (!anchor) return;
      event.preventDefault();
      const url = safeLink(anchor.getAttribute('href'));
      if (!url) return;
      let opened = false;
      try {
        opened = await openLink(url);
      } catch {
        opened = false;
      }
      setLinkError(!opened);
    },
    [openLink]
  );

  const data = React.useMemo(() => ({ query: queryOf(toolInput), results: toResultItems(shown) }), [toolInput, shown]);

  let body: React.ReactNode;
  if (error) {
    body = (
      <Text role="alert" className={mergeClasses(styles.status, styles.error)}>
        The widget could not connect to the host.
      </Text>
    );
  } else if (!isConnected) {
    body = <Spinner className={styles.status} size="tiny" label="Connecting to the host" labelPosition="after" />;
  } else if (opening?.status === 'error') {
    body = (
      <Text role="alert" className={mergeClasses(styles.status, styles.error)}>
        {opening.message}
      </Text>
    );
  } else if (!first) {
    body = gaveUp ? (
      <Text role="alert" className={mergeClasses(styles.status, styles.error)}>
        {NO_RESULT}
      </Text>
    ) : (
      <Spinner className={styles.status} size="tiny" label="Waiting for the search results" labelPosition="after" />
    );
  } else {
    body = (
      <>
        {notices.incomplete.map(notice => (
          <MessageBar key={notice} intent="warning">
            <MessageBarBody>{notice}</MessageBarBody>
          </MessageBar>
        ))}
        {linkError ? (
          <MessageBar intent="warning">
            <MessageBarBody>{LINK_NOT_OPENED}</MessageBarBody>
          </MessageBar>
        ) : null}
        {/* Delegated: a click or middle-click on a hit's link is routed to the host (Enter on a focused link is a click). */}
        <div onClick={openThroughHost} onAuxClick={openThroughHost}>
          <SearchResultsWidget data={data} className={styles.noScore} />
        </div>
        <div className={styles.footer}>
          {loadError ? (
            <MessageBar intent="error">
              <MessageBarBody>{loadError}</MessageBarBody>
            </MessageBar>
          ) : null}
          {next ? (
            <Button appearance="secondary" disabled={loading} onClick={loadMore}>
              {loading ? 'Loading' : 'Load more'}
            </Button>
          ) : null}
          {loading ? <Spinner size="tiny" label="Loading more results" labelPosition="after" /> : null}
          {notices.ending ? (
            <Text size={200} className={styles.ending}>
              {notices.ending}
            </Text>
          ) : null}
        </div>
      </>
    );
  }

  return (
    <main className={styles.root} aria-label="Spaarke search results">
      {body}
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
