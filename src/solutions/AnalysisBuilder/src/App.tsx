/**
 * App.tsx — Analysis Builder Code Page
 *
 * 2-tab layout for creating AI analyses from a Document form context.
 * Tab 1: Select Playbook (card grid, locked scopes on selection)
 * Tab 2: Custom Scope (manual action/skills/knowledge/tools selection)
 *
 * Replaces the AnalysisBuilder PCF control (v2.9.2).
 */
import React from "react";
import {
  FluentProvider,
  TabList,
  Tab,
  Button,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import {
  PlaybookCardGrid,
  ScopeConfigurator,
  loadAllData,
  loadPlaybookScopes,
  createAndAssociate,
} from "@playbook/index";
import type {
  IPlaybook,
  IAction,
  ISkill,
  IKnowledge,
  ITool,
  IPlaybookScopes,
  IAnalysisConfig,
  IPlaybookData,
} from "@playbook/index";

// ---------------------------------------------------------------------------
// Declare global Xrm for Dataverse WebAPI and navigation
// ---------------------------------------------------------------------------

// eslint-disable-next-line @typescript-eslint/no-explicit-any
declare const Xrm: any;

// ---------------------------------------------------------------------------
// Resolve Xrm from parent/top frames (web resource dialogs run in iframes)
// ---------------------------------------------------------------------------

function resolveWebApi(): any {
  try {
    if (typeof Xrm !== "undefined" && Xrm?.WebApi?.retrieveMultipleRecords) return Xrm.WebApi;
  } catch { /* */ }
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.WebApi?.retrieveMultipleRecords) return p.WebApi;
  } catch { /* */ }
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.WebApi?.retrieveMultipleRecords) return t.WebApi;
  } catch { /* */ }
  return undefined;
}

function resolveXrmNavigation(): any {
  try {
    if (typeof Xrm !== "undefined" && Xrm?.Navigation) return Xrm.Navigation;
  } catch { /* */ }
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.Navigation) return p.Navigation;
  } catch { /* */ }
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.Navigation) return t.Navigation;
  } catch { /* */ }
  return undefined;
}

// ---------------------------------------------------------------------------
// URL param parsing
// ---------------------------------------------------------------------------

interface IDocumentContext {
  documentId: string;
  documentName: string;
  containerId: string;
  fileId: string;
  apiBaseUrl: string;
}

function parseUrlParams(): IDocumentContext {
  const params = new URLSearchParams(window.location.search);
  // Also check for Dataverse `data` parameter which encodes params as query string
  const dataParam = params.get("data");
  const effective = dataParam ? new URLSearchParams(dataParam) : params;

  return {
    documentId: effective.get("documentId") ?? "",
    documentName: effective.get("documentName") ?? "Document",
    containerId: effective.get("containerId") ?? "",
    fileId: effective.get("fileId") ?? "",
    apiBaseUrl: effective.get("apiBaseUrl") ?? "",
  };
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  tabBar: {
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke1,
    flexShrink: 0,
  },
  content: {
    flex: 1,
    overflow: "auto",
    paddingTop: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    minHeight: 0,
  },
  scopePreview: {
    marginTop: tokens.spacingVerticalL,
  },
  footer: {
    display: "flex",
    justifyContent: "flex-end",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  loading: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: 1,
  },
});

// ---------------------------------------------------------------------------
// Tab type
// ---------------------------------------------------------------------------

type BuilderTab = "playbook" | "custom";

// ---------------------------------------------------------------------------
// AppContent (inner component, requires FluentProvider context)
// ---------------------------------------------------------------------------

const AppContent: React.FC = () => {
  const styles = useStyles();
  const docContext = React.useMemo(() => parseUrlParams(), []);

  // --- Data state ---
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [playbooks, setPlaybooks] = React.useState<IPlaybook[]>([]);
  const [actions, setActions] = React.useState<IAction[]>([]);
  const [skills, setSkills] = React.useState<ISkill[]>([]);
  const [knowledge, setKnowledge] = React.useState<IKnowledge[]>([]);
  const [tools, setTools] = React.useState<ITool[]>([]);

  // --- Selection state ---
  const [activeTab, setActiveTab] = React.useState<BuilderTab>("playbook");
  const [selectedPlaybook, setSelectedPlaybook] = React.useState<IPlaybook | null>(null);
  const [playbookScopes, setPlaybookScopes] = React.useState<IPlaybookScopes | null>(null);

  // Custom scope selection (Tab 2)
  const [selectedActionIds, setSelectedActionIds] = React.useState<string[]>([]);
  const [selectedSkillIds, setSelectedSkillIds] = React.useState<string[]>([]);
  const [selectedKnowledgeIds, setSelectedKnowledgeIds] = React.useState<string[]>([]);
  const [selectedToolIds, setSelectedToolIds] = React.useState<string[]>([]);

  // --- Execution state ---
  const [isExecuting, setIsExecuting] = React.useState(false);

  // --- Resolve Xrm.WebApi once ---
  const webApi = React.useMemo(() => resolveWebApi(), []);

  // --- Load data on mount ---
  React.useEffect(() => {
    if (!webApi) {
      setError("Dataverse WebAPI not available. Please open this page from within Dynamics 365.");
      setIsLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const data: IPlaybookData = await loadAllData(webApi);
        if (cancelled) return;
        setPlaybooks(data.playbooks);
        setActions(data.actions);
        setSkills(data.skills);
        setKnowledge(data.knowledge);
        setTools(data.tools);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Failed to load data");
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [webApi]);

  // --- Playbook selection handler ---
  const handlePlaybookSelect = React.useCallback(async (playbook: IPlaybook) => {
    setSelectedPlaybook(playbook);
    try {
      const scopes = await loadPlaybookScopes(webApi, playbook.id);
      setPlaybookScopes(scopes);
    } catch (err) {
      console.error("[AnalysisBuilder] Failed to load playbook scopes:", err);
      setPlaybookScopes(null);
    }
  }, [webApi]);

  // --- Tab change handler ---
  const handleTabSelect = React.useCallback(
    (_event: unknown, data: { value: unknown }) => {
      setActiveTab(data.value as BuilderTab);
    },
    []
  );

  // --- Can execute? ---
  const canExecute = React.useMemo(() => {
    if (!docContext.documentId) return false;
    if (activeTab === "playbook") {
      return selectedPlaybook !== null && playbookScopes !== null;
    }
    // Custom scope: need at least an action
    return selectedActionIds.length > 0;
  }, [activeTab, selectedPlaybook, playbookScopes, selectedActionIds, docContext.documentId]);

  // --- Run Analysis handler ---
  const handleExecute = React.useCallback(async () => {
    if (!canExecute) return;
    setIsExecuting(true);
    setError(null);

    try {
      let config: IAnalysisConfig;

      if (activeTab === "playbook" && selectedPlaybook && playbookScopes) {
        config = {
          documentId: docContext.documentId,
          documentName: docContext.documentName,
          playbookId: selectedPlaybook.id,
          actionId: playbookScopes.actionIds[0] ?? "",
          skillIds: playbookScopes.skillIds,
          knowledgeIds: playbookScopes.knowledgeIds,
          toolIds: playbookScopes.toolIds,
        };
      } else {
        config = {
          documentId: docContext.documentId,
          documentName: docContext.documentName,
          actionId: selectedActionIds[0] ?? "",
          skillIds: selectedSkillIds,
          knowledgeIds: selectedKnowledgeIds,
          toolIds: selectedToolIds,
        };
      }

      const analysisId = await createAndAssociate(webApi, config);

      // Navigate to Analysis Workspace form
      const nav = resolveXrmNavigation();
      if (nav) {
        nav.navigateTo(
          {
            pageType: "entityrecord",
            entityName: "sprk_analysis",
            entityId: analysisId,
          },
          { target: 1 }
        );
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create analysis");
    } finally {
      setIsExecuting(false);
    }
  }, [canExecute, activeTab, selectedPlaybook, playbookScopes, selectedActionIds, selectedSkillIds, selectedKnowledgeIds, selectedToolIds, docContext, webApi]);

  // --- Cancel handler ---
  const handleCancel = React.useCallback(() => {
    try {
      window.close();
    } catch {
      // Fallback: navigate back
      window.history.back();
    }
  }, []);

  // --- Render ---

  if (isLoading) {
    return (
      <div className={styles.root}>
        <div className={styles.loading}>
          <Spinner size="large" label="Loading playbooks..." />
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.header}>
        <Text size={500} weight="semibold">New Analysis</Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          {docContext.documentName}
        </Text>
      </div>

      {/* Tab bar */}
      <div className={styles.tabBar}>
        <TabList selectedValue={activeTab} onTabSelect={handleTabSelect} size="medium">
          <Tab value="playbook">Select Playbook</Tab>
          <Tab value="custom">Custom Scope</Tab>
        </TabList>
      </div>

      {/* Error bar */}
      {error && (
        <MessageBar intent="error" style={{ margin: tokens.spacingVerticalS }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Content area */}
      <div className={styles.content}>
        {activeTab === "playbook" ? (
          <>
            <PlaybookCardGrid
              playbooks={playbooks}
              selectedId={selectedPlaybook?.id}
              onSelect={handlePlaybookSelect}
              isLoading={false}
            />
            {selectedPlaybook && playbookScopes && (
              <div className={styles.scopePreview}>
                <ScopeConfigurator
                  actions={actions}
                  skills={skills}
                  knowledge={knowledge}
                  tools={tools}
                  selectedActionIds={playbookScopes.actionIds}
                  selectedSkillIds={playbookScopes.skillIds}
                  selectedKnowledgeIds={playbookScopes.knowledgeIds}
                  selectedToolIds={playbookScopes.toolIds}
                  onActionChange={() => {}}
                  onSkillChange={() => {}}
                  onKnowledgeChange={() => {}}
                  onToolChange={() => {}}
                  readOnly
                />
              </div>
            )}
          </>
        ) : (
          <ScopeConfigurator
            actions={actions}
            skills={skills}
            knowledge={knowledge}
            tools={tools}
            selectedActionIds={selectedActionIds}
            selectedSkillIds={selectedSkillIds}
            selectedKnowledgeIds={selectedKnowledgeIds}
            selectedToolIds={selectedToolIds}
            onActionChange={setSelectedActionIds}
            onSkillChange={setSelectedSkillIds}
            onKnowledgeChange={setSelectedKnowledgeIds}
            onToolChange={setSelectedToolIds}
          />
        )}
      </div>

      {/* Footer */}
      <div className={styles.footer}>
        <Button appearance="secondary" onClick={handleCancel} disabled={isExecuting}>
          Cancel
        </Button>
        <Button appearance="primary" onClick={handleExecute} disabled={!canExecute || isExecuting}>
          {isExecuting ? "Creating..." : "Run Analysis"}
        </Button>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// App (outer component with FluentProvider + theme)
// ---------------------------------------------------------------------------

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);

  React.useEffect(() => {
    return setupThemeListener(() => setTheme(resolveTheme()));
  }, []);

  return (
    <FluentProvider theme={theme}>
      <AppContent />
    </FluentProvider>
  );
};
