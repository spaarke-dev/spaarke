import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { getWebApi, getUserId } from "./services/xrmProvider";
import { PageHeader } from "./components/Shell/PageHeader";
import { WorkspaceGrid } from "./components/Shell/WorkspaceGrid";
import { SmartToDo } from "./components/SmartToDo/SmartToDo";
import { FeedTodoSyncProvider } from "./contexts/FeedTodoSyncContext";
import type { IWebApi } from "./types/xrm";

/**
 * Parse the `data` query parameter from the URL.
 * Dataverse passes custom data via `?data=key1%3Dvalue1%26key2%3Dvalue2`.
 */
function parseDataParams(): Record<string, string> {
  const params = new URLSearchParams(window.location.search);
  const raw = params.get("data") ?? "";
  const result: Record<string, string> = {};
  if (!raw) return result;
  const decoded = decodeURIComponent(raw);
  for (const pair of decoded.split("&")) {
    const [key, ...rest] = pair.split("=");
    if (key) result[key.trim()] = rest.join("=").trim();
  }
  return result;
}

const APP_VERSION = "1.0.2";

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
  },
  content: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    minHeight: 0,
    padding: tokens.spacingVerticalXL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },
  footer: {
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    display: "flex",
    justifyContent: "flex-end",
  },
  error: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    padding: "40px",
    textAlign: "center",
  },
});

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);
  const styles = useStyles();

  // Resolve Xrm dependencies once on mount
  const webApi = React.useMemo<IWebApi | null>(() => getWebApi(), []);
  const userId = React.useMemo(() => getUserId(), []);

  // Check for dialog mode (e.g. ?data=mode%3Dtodo)
  const dataParams = React.useMemo(() => parseDataParams(), []);
  const mode = dataParams.mode ?? "";

  // Theme listener
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  const buildDate = new Date().toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });

  // Guard: Xrm not available (local dev or misconfigured Custom Page)
  if (!webApi) {
    return (
      <FluentProvider theme={theme} style={{ height: "100%" }}>
        <div className={styles.error}>
          <Text size={400}>
            Xrm.WebApi is not available. This page must be loaded inside a
            Dataverse Model-Driven App Custom Page.
          </Text>
        </div>
      </FluentProvider>
    );
  }

  // Dialog mode: render only the requested section (full, with side pane)
  if (mode === "todo") {
    return (
      <FluentProvider theme={theme} style={{ height: "100%" }}>
        <FeedTodoSyncProvider webApi={webApi}>
          <div className={styles.root}>
            <SmartToDo webApi={webApi} userId={userId} useDialogForDetail />
          </div>
        </FeedTodoSyncProvider>
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <FeedTodoSyncProvider webApi={webApi}>
        <div className={styles.root}>
          <PageHeader />
          <main className={styles.content}>
            <WorkspaceGrid
              allocatedWidth={0}
              allocatedHeight={0}
              webApi={webApi}
              userId={userId}
            />
          </main>
          <footer className={styles.footer}>
            <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>
              v{APP_VERSION} &bull; Built {buildDate}
            </Text>
          </footer>
        </div>
      </FeedTodoSyncProvider>
    </FluentProvider>
  );
};
