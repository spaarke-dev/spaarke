import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { useTheme } from "./hooks/useTheme";
import { PageHeader } from "./components/Shell/PageHeader";
import { WorkspaceGrid } from "./components/Shell/WorkspaceGrid";
import { FeedTodoSyncProvider } from "./contexts/FeedTodoSyncContext";
import type { IWebApi } from "./types/xrm";

export interface ILegalWorkspaceAppProps {
  version: string;
  allocatedWidth: number;
  allocatedHeight: number;
  /** Xrm.WebApi reference from PCF framework context, used for Dataverse queries */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
}

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    minHeight: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  content: {
    flex: "1 1 auto",
    padding: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
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
});

export const LegalWorkspaceApp: React.FC<ILegalWorkspaceAppProps> = ({
  version,
  allocatedWidth,
  allocatedHeight,
  webApi,
  userId,
}) => {
  const { theme } = useTheme();
  const styles = useStyles();
  const buildDate = new Date().toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });

  return (
    <FluentProvider theme={theme}>
      {/*
       * FeedTodoSyncProvider is placed at the top of the app tree so that
       * Block 3 (ActivityFeed / FeedItemCard) and Block 4 (SmartToDo) both
       * share the same flag state instance and receive cross-block updates
       * via the subscribe() mechanism without prop-drilling.
       */}
      <FeedTodoSyncProvider webApi={webApi}>
        <div className={styles.root}>
          <PageHeader />
          <main className={styles.content}>
            <WorkspaceGrid
              allocatedWidth={allocatedWidth}
              allocatedHeight={allocatedHeight}
              webApi={webApi}
              userId={userId}
            />
          </main>
          <footer className={styles.footer}>
            <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>
              v{version} &bull; Built {buildDate}
            </Text>
          </footer>
        </div>
      </FeedTodoSyncProvider>
    </FluentProvider>
  );
};
