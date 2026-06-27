import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import {
  useTheme,
  syncThemeFromDataverse,
  persistThemeToDataverse,
  getUserThemePreference,
  THEME_CHANGE_EVENT,
} from "@spaarke/ui-components";
import type { SectionRegistration } from "@spaarke/ui-components";
import { PageHeader } from "./components/Shell/PageHeader";
import { WorkspaceGrid } from "./components/Shell/WorkspaceGrid";
import type { WorkspaceHeaderState } from "./components/Shell/WorkspaceGrid";
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
  /**
   * Optional workspace layout ID for deep-linking — when provided, the embedded
   * `WorkspaceGrid` activates this layout on mount instead of the user's
   * pinned default. Added in Round 4 Fix 4 (2026-05-21) so SpaarkeAi's
   * `WorkspaceLayoutWidget` can open a chosen workspace by id.
   */
  initialWorkspaceId?: string;
  /**
   * When `true`, render in embedded mode: skip the internal `<PageHeader>`
   * (which carries LegalWorkspace's own workspace dropdown), skip the footer,
   * skip the outer `<FluentProvider>` (assumes a parent provider is present),
   * and skip cross-device theme sync side effects (already owned by the
   * embedding shell). Used by SpaarkeAi's `WorkspaceLayoutWidget` to host
   * the full workspace experience inside a tab without UI duplication.
   *
   * Standalone LegalWorkspace continues to use `embedded={false}` (default),
   * preserving FR-25 / NFR-10 (byte-identical bundle behaviour).
   */
  embedded?: boolean;
  /**
   * Optional custom section registry (R2 Option D, 2026-06-18). When omitted,
   * the default `SECTION_REGISTRY` is used (standalone LegalWorkspace behavior).
   * Embedding consumers (SpaarkeAi) pass a registry built via
   * `createLegalWorkspaceSectionRegistry({...})` to inject per-widget
   * customization (e.g. SpaarkeAi's `loadSpaarkeAiNotificationContext` for
   * Daily Briefing).
   *
   * Replaces the R2 task 002 module-mutation slot pattern — see
   * `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md`.
   */
  sections?: readonly SectionRegistration[];
}

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
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
});

export const LegalWorkspaceApp: React.FC<ILegalWorkspaceAppProps> = ({
  version,
  allocatedWidth,
  allocatedHeight,
  webApi,
  userId,
  initialWorkspaceId,
  embedded = false,
  sections,
}) => {
  const { theme } = useTheme();
  const styles = useStyles();

  const cleanUserId = userId?.replace(/[{}]/g, '') ?? '';

  // Workspace header state — pushed up from WorkspaceGrid via onHeaderReady.
  // Only consumed when `embedded=false` (the internal `<PageHeader>` renders
  // the dropdown). In embedded mode the host shell owns the workspace switcher.
  const [headerState, setHeaderState] = React.useState<WorkspaceHeaderState | null>(null);

  // Cross-device theme sync side effects — only when NOT embedded. The
  // embedding shell (e.g. SpaarkeAi) owns its own theme lifecycle and would
  // otherwise double-fire these effects.
  React.useEffect(() => {
    if (embedded) return;
    if (webApi && cleanUserId) {
      syncThemeFromDataverse(webApi, cleanUserId);
    }
  }, [embedded, webApi, cleanUserId]);

  // Persist theme changes to Dataverse (triggered by ThemeToggle or ribbon menu).
  // Skipped in embedded mode for the same reason as the sync effect above.
  React.useEffect(() => {
    if (embedded) return;
    if (!webApi || !cleanUserId) return;
    const handler = () => {
      persistThemeToDataverse(webApi, cleanUserId, getUserThemePreference());
    };
    window.addEventListener(THEME_CHANGE_EVENT, handler);
    return () => window.removeEventListener(THEME_CHANGE_EVENT, handler);
  }, [embedded, webApi, cleanUserId]);
  const buildDate = new Date().toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });

  // ---------------------------------------------------------------------
  // Tree assembly
  //
  // The inner tree (FeedTodoSyncProvider + WorkspaceGrid) is identical in
  // both modes — only the chrome differs. We assemble the inner subtree
  // first, then branch on `embedded` for the wrapper.
  // ---------------------------------------------------------------------

  const innerTree = (
    /*
     * FeedTodoSyncProvider is placed at the top of the app tree so that
     * Block 3 (ActivityFeed / FeedItemCard) and Block 4 (SmartToDo) both
     * share the same cross-block todo-lifecycle bus and receive change
     * notifications via subscribe() without prop-drilling. The provider
     * holds no persistence state — producers write to Dataverse and then
     * call notifyTodoChange(todoId, isActive). See FeedTodoSyncContext.tsx.
     */
    <FeedTodoSyncProvider>
      <div className={styles.root}>
        {/* Embedded mode skips the LegalWorkspace internal PageHeader.
            Standalone mode renders the full header with workspace dropdown. */}
        {!embedded && (
          <PageHeader
            activeLayout={headerState?.activeLayout}
            layouts={headerState?.layouts}
            onLayoutChange={headerState?.onLayoutChange}
            onEditClick={headerState?.onEditClick}
            onCreateClick={headerState?.onCreateClick}
            onDeleteClick={headerState?.onDeleteClick}
            onSetDefaultClick={headerState?.onSetDefaultClick}
          />
        )}
        <main className={styles.content}>
          <WorkspaceGrid
            allocatedWidth={allocatedWidth}
            allocatedHeight={allocatedHeight}
            webApi={webApi}
            userId={userId}
            initialWorkspaceId={initialWorkspaceId}
            embedded={embedded}
            sections={sections}
            onHeaderReady={!embedded ? setHeaderState : undefined}
          />
        </main>
        {/* Embedded mode skips the footer (tab content provides its own chrome). */}
        {!embedded && (
          <footer className={styles.footer}>
            <Text size={100} style={{ color: tokens.colorNeutralForeground4 }}>
              v{version} &bull; Built {buildDate}
            </Text>
          </footer>
        )}
      </div>
    </FeedTodoSyncProvider>
  );

  // Embedded mode skips the outer FluentProvider — the embedding shell
  // already wraps its tree in one and double-providers can subtly disrupt
  // tokens / theme propagation in nested portals.
  if (embedded) {
    return innerTree;
  }

  return <FluentProvider theme={theme}>{innerTree}</FluentProvider>;
};
