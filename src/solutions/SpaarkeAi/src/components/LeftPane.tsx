/**
 * LeftPane.tsx — Left pane composite for the SpaarkeAi three-pane layout.
 *
 * Composes ChatPanel (SprkChat interaction) and ChatHistoryPanel (session history)
 * into a single left pane slot. Provides a toggle button to switch between chat
 * and history views within the same pane.
 *
 * Layout modes:
 *   - "chat" (default): shows ChatPanel (SprkChat) filling the full pane height
 *   - "history": shows ChatHistoryPanel (session list with search)
 *
 * The toggle button sits in a tab-style header bar above the active panel.
 * ThreePaneLayout handles pane collapsing to a narrow strip via its own mechanism.
 *
 * @see ADR-021 — Fluent UI v9 semantic tokens only (no hardcoded colors)
 * @see ADR-022 — React 19, functional components
 */

import * as React from "react";
import { makeStyles, tokens, Button, Text } from "@fluentui/react-components";
import { ChatRegular, HistoryRegular } from "@fluentui/react-icons";
import { ChatPanel } from "./ChatPanel";
import { ChatHistoryPanel } from "./ChatHistoryPanel";

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  tabBar: {
    flexShrink: 0,
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground1,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: "2px",
    paddingBottom: "0px",
    gap: tokens.spacingHorizontalXS,
    minHeight: "36px",
  },
  tabButton: {
    // Use Fluent v9 Button appearance="subtle" — active tab uses a bottom border indicator
    borderRadius: "0px",
    height: "36px",
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
  },
  tabButtonActive: {
    borderBottomWidth: "2px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorBrandStroke1,
    color: tokens.colorBrandForeground1,
  },
  content: {
    flex: 1,
    minHeight: 0,
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// LeftPane
// ---------------------------------------------------------------------------

type LeftPaneView = "chat" | "history";

/**
 * LeftPane — left slot of ThreePaneLayout for the SpaarkeAi Code Page.
 *
 * Renders a tab bar with "Chat" and "History" tabs that toggle between
 * the ChatPanel (SprkChat) and ChatHistoryPanel (session list) components.
 * Defaults to the chat view on mount.
 */
export function LeftPane(): React.JSX.Element {
  const styles = useStyles();
  const [activeView, setActiveView] = React.useState<LeftPaneView>("chat");

  return (
    <div className={styles.root}>
      {/* Tab bar — Chat / History toggle */}
      <div className={styles.tabBar} role="tablist" aria-label="AI Chat navigation">
        <Button
          appearance="subtle"
          role="tab"
          aria-selected={activeView === "chat"}
          icon={<ChatRegular />}
          className={
            activeView === "chat"
              ? `${styles.tabButton} ${styles.tabButtonActive}`
              : styles.tabButton
          }
          onClick={() => setActiveView("chat")}
          size="small"
        >
          <Text size={200} weight={activeView === "chat" ? "semibold" : "regular"}>
            Chat
          </Text>
        </Button>

        <Button
          appearance="subtle"
          role="tab"
          aria-selected={activeView === "history"}
          icon={<HistoryRegular />}
          className={
            activeView === "history"
              ? `${styles.tabButton} ${styles.tabButtonActive}`
              : styles.tabButton
          }
          onClick={() => setActiveView("history")}
          size="small"
        >
          <Text size={200} weight={activeView === "history" ? "semibold" : "regular"}>
            History
          </Text>
        </Button>
      </div>

      {/* Active panel content */}
      <div
        className={styles.content}
        role="tabpanel"
        aria-label={activeView === "chat" ? "AI Chat" : "Chat History"}
      >
        {activeView === "chat" ? <ChatPanel /> : <ChatHistoryPanel />}
      </div>
    </div>
  );
}
