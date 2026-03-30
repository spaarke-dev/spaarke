import * as React from "react";
import { tokens, Spinner } from "@fluentui/react-components";
import { WorkspaceShell } from "@spaarke/ui-components";
import type { SectionFactoryContext, NavigateTarget, DialogOptions } from "@spaarke/ui-components";
import { useDataverseService } from "../../hooks/useDataverseService";
import { useWorkspaceLayouts } from "../../hooks/useWorkspaceLayouts";
import { navigateToEntityList } from "../../utils/navigation";
import {
  createPlaybookHandlers,
} from "../GetStarted/ActionCardHandlers";
import { buildWorkspaceConfig } from "../../workspaceConfig";
import { buildDynamicWorkspaceConfig } from "../../workspace/buildDynamicWorkspaceConfig";
import { SECTION_REGISTRY } from "../../sectionRegistry";
import { WorkspaceHeader } from "../WorkspaceHeader";
import type { WorkspaceLayoutSummary } from "../WorkspaceHeader";
import type { IWebApi } from "../../types/xrm";
import { getBffBaseUrl } from "../../config/runtimeConfig";
import {
  WorkspaceSkeleton,
  PersonalizeBanner,
  FetchErrorBar,
} from "../WorkspaceLoadingStates";

// ---------------------------------------------------------------------------
// Lazy-loaded dialog components (bundle-size optimization — Task 033)
//
// These dialogs are modal overlays only opened on user interaction.
// Using React.lazy() defers their JavaScript from the initial bundle chunk.
// The PCF platform bundles as a single file (esbuild/webpack), so lazy chunks
// are emitted separately and loaded on first open.
//
// Suspense fallback: Fluent Spinner centred in a fixed overlay so the layout
// does not shift while the chunk loads.
// ---------------------------------------------------------------------------

const LazyGetStartedExpandDialog = React.lazy(() =>
  import("../GetStarted/GetStartedExpandDialog").then((m) => ({
    default: m.GetStartedExpandDialog,
  }))
);

const LazyQuickSummaryDashboardDialog = React.lazy(() =>
  import("../QuickSummary/QuickSummaryDashboardDialog").then((m) => ({
    default: m.QuickSummaryDashboardDialog,
  }))
);

const LazyCloseProjectDialog = React.lazy(
  () => import("../CreateProject/CloseProjectDialog")
);

// ---------------------------------------------------------------------------
// Suspense fallback: Fluent Spinner shown while lazy chunk loads
// ---------------------------------------------------------------------------

const DialogLoadingFallback: React.FC = () => (
  <div
    style={{
      position: "fixed",
      inset: 0,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      backgroundColor: tokens.colorNeutralStroke1,
      zIndex: 1000,
    }}
    aria-live="polite"
    aria-label="Loading dialog"
  >
    <Spinner size="large" label="Loading..." labelPosition="below" />
  </div>
);

/** Workspace header state passed up to the PageHeader via onHeaderReady. */
export interface WorkspaceHeaderState {
  activeLayout: WorkspaceLayoutSummary;
  layouts: WorkspaceLayoutSummary[];
  onLayoutChange: (layoutId: string) => void;
  onEditClick: () => void;
  onCreateClick: () => void;
}

export interface IWorkspaceGridProps {
  allocatedWidth: number;
  allocatedHeight: number;
  /** Xrm.WebApi reference from PCF framework context, forwarded to data-bound blocks */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /** Optional workspace layout ID for deep-linking (from URL data parameter) */
  initialWorkspaceId?: string;
  /** Called when workspace header data is ready — parent renders it in PageHeader toolbar. */
  onHeaderReady?: (state: WorkspaceHeaderState) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const WorkspaceGrid: React.FC<IWorkspaceGridProps> = ({
  allocatedWidth: _allocatedWidth,
  allocatedHeight: _allocatedHeight,
  webApi,
  userId,
  onHeaderReady,
  initialWorkspaceId,
}) => {
  // -------------------------------------------------------------------------
  // DataverseService for DocumentsTab
  // -------------------------------------------------------------------------

  const service = useDataverseService(webApi);

  // -------------------------------------------------------------------------
  // Dynamic workspace layouts (BFF API)
  // -------------------------------------------------------------------------

  const {
    layouts: workspaceLayouts,
    activeLayout,
    activeLayoutJson,
    isLoading: isLayoutsLoading,
    status: layoutStatus,
    error: layoutError,
    setActiveLayoutById,
    refetch: refetchLayouts,
  } = useWorkspaceLayouts(initialWorkspaceId);

  // -------------------------------------------------------------------------
  // Record counts for section badges
  // -------------------------------------------------------------------------

  const [feedCount, setFeedCount] = React.useState<number>(0);
  const [todoCount, setTodoCount] = React.useState<number>(0);
  const [docCount, setDocCount] = React.useState<number>(0);

  // SmartToDo refetch ref — used to sync workspace Kanban after dialog close
  const todoRefetchRef = React.useRef<(() => void) | null>(null);
  const handleTodoRefetchReady = React.useCallback((refetch: () => void) => {
    todoRefetchRef.current = refetch;
  }, []);

  // Activity Feed refetch ref — used to sync after Event wizard close
  const feedRefetchRef = React.useRef<(() => void) | null>(null);
  const handleFeedRefetchReady = React.useCallback((refetch: () => void) => {
    feedRefetchRef.current = refetch;
  }, []);

  // Documents refetch ref — used to refresh docs grid
  const docRefetchRef = React.useRef<(() => void) | null>(null);
  const handleDocRefetchReady = React.useCallback((refetch: () => void) => {
    docRefetchRef.current = refetch;
  }, []);

  // Open all updates → navigates to sprk_event entity list dialog
  const handleOpenAllUpdates = React.useCallback(() => {
    navigateToEntityList("sprk_event", "9399ba21-2e17-f111-8343-7c1e520aa4df");
  }, []);

  // -------------------------------------------------------------------------
  // Get Started expand dialog state
  // -------------------------------------------------------------------------

  const [isExpandOpen, setIsExpandOpen] = React.useState(false);
  const handleExpandClick = React.useCallback(() => setIsExpandOpen(true), []);
  const handleExpandClose = React.useCallback(() => setIsExpandOpen(false), []);

  // -------------------------------------------------------------------------
  // Quick Summary Dashboard dialog state
  // -------------------------------------------------------------------------

  const [isDashboardOpen, setIsDashboardOpen] = React.useState(false);
  const handleDashboardOpen = React.useCallback(() => setIsDashboardOpen(true), []);
  const handleDashboardClose = React.useCallback(() => setIsDashboardOpen(false), []);

  // -------------------------------------------------------------------------
  // Create New Matter — opens Code Page dialog via navigateTo (UDSS-009)
  // -------------------------------------------------------------------------

  const handleOpenWizard = React.useCallback(async () => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (!xrm?.Navigation?.navigateTo) return;

      await xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_creatematterwizard",
          data: `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`,
        },
        {
          target: 2,
          width: { value: 60, unit: "%" },
          height: { value: 70, unit: "%" },
          title: "Create New Matter",
        }
      );
    } catch {
      // User cancelled or dialog error — ignore
    }
  }, []);

  // -------------------------------------------------------------------------
  // Create New Project — opens Code Page dialog via navigateTo (UDSS-009)
  // -------------------------------------------------------------------------

  const handleOpenProjectWizard = React.useCallback(async () => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (!xrm?.Navigation?.navigateTo) return;

      await xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_createprojectwizard",
          data: `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`,
        },
        {
          target: 2,
          width: { value: 60, unit: "%" },
          height: { value: 70, unit: "%" },
          title: "Create New Project",
        }
      );
    } catch {
      // User cancelled or dialog error — ignore
    }
  }, []);

  // -------------------------------------------------------------------------
  // Summarize Files — opens Code Page dialog via navigateTo (UDSS-017)
  // -------------------------------------------------------------------------

  const handleOpenSummarize = React.useCallback(async (documentIds?: string[]) => {
    try {
      const bffParam = `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`;
      const data = documentIds ? `documentIds=${documentIds.join(",")}&${bffParam}` : bffParam;
      await (window as any).Xrm?.Navigation?.navigateTo(
        { pageType: "webresource", webresourceName: "sprk_summarizefileswizard", data },
        { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" }, title: "Summarize Files" }
      );
      docRefetchRef.current?.();
    } catch {
      docRefetchRef.current?.();
    }
  }, []);

  // -------------------------------------------------------------------------
  // Find Similar — opens Code Page dialog via navigateTo (UDSS-017)
  // -------------------------------------------------------------------------

  const handleOpenFindSimilar = React.useCallback(async (documentId?: string, containerId?: string) => {
    try {
      const data = `documentId=${documentId || ""}&containerId=${containerId || ""}&bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`;
      await (window as any).Xrm?.Navigation?.navigateTo(
        { pageType: "webresource", webresourceName: "sprk_findsimilar", data },
        { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" }, title: "Find Similar Documents" }
      );
      docRefetchRef.current?.();
    } catch {
      docRefetchRef.current?.();
    }
  }, []);

  // -------------------------------------------------------------------------
  // Create New Event — opens Code Page dialog via navigateTo (UDSS-017)
  // -------------------------------------------------------------------------

  const handleOpenEventWizard = React.useCallback(async () => {
    try {
      await (window as any).Xrm?.Navigation?.navigateTo(
        { pageType: "webresource", webresourceName: "sprk_createeventwizard", data: `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}` },
        { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" }, title: "Create New Event" }
      );
      feedRefetchRef.current?.();
    } catch {
      feedRefetchRef.current?.();
    }
  }, []);

  // -------------------------------------------------------------------------
  // Create New To Do — opens Code Page dialog via navigateTo (UDSS-017)
  // -------------------------------------------------------------------------

  const handleOpenTodoWizard = React.useCallback(async () => {
    try {
      await (window as any).Xrm?.Navigation?.navigateTo(
        { pageType: "webresource", webresourceName: "sprk_createtodowizard", data: `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}` },
        { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" }, title: "Create New To Do" }
      );
      todoRefetchRef.current?.();
    } catch {
      todoRefetchRef.current?.();
    }
  }, []);

  // -------------------------------------------------------------------------
  // Create Work Assignment — opens Code Page dialog via navigateTo (UDSS-017)
  // -------------------------------------------------------------------------

  const handleOpenWorkAssignmentWizard = React.useCallback(async () => {
    try {
      await (window as any).Xrm?.Navigation?.navigateTo(
        { pageType: "webresource", webresourceName: "sprk_createworkassignmentwizard", data: `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}` },
        { target: 2, width: { value: 60, unit: "%" }, height: { value: 70, unit: "%" }, title: "Create Work Assignment" }
      );
    } catch {
      // User cancelled or dialog error — ignore
    }
  }, []);

  // -------------------------------------------------------------------------
  // Close Project dialog state
  // -------------------------------------------------------------------------

  const [closeProjectContext, setCloseProjectContext] = React.useState<{
    projectId: string;
    projectName: string;
    containerId?: string;
  } | null>(null);

  const handleOpenCloseProjectDialog = React.useCallback(
    (projectId: string, projectName: string, containerId?: string) => {
      setCloseProjectContext({ projectId, projectName, containerId });
    },
    []
  );

  const handleCloseProjectDialog = React.useCallback(() => {
    setCloseProjectContext(null);
  }, []);

  // Expose the close-project dialog opener on the window so it can be triggered
  // from Xrm ribbon commands or command bar buttons on the project form.
  // Usage: window.__SPAARKE_OPEN_CLOSE_PROJECT__(projectId, projectName, containerId?)
  React.useEffect(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).__SPAARKE_OPEN_CLOSE_PROJECT__ = handleOpenCloseProjectDialog;
    return () => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      delete (window as any).__SPAARKE_OPEN_CLOSE_PROJECT__;
    };
  }, [handleOpenCloseProjectDialog]);

  // -------------------------------------------------------------------------
  // Playbook handlers for email-compose & meeting-schedule cards (UDSS-024)
  // Opens Playbook Library Code Page via Xrm.Navigation.navigateTo
  // -------------------------------------------------------------------------

  const playbookHandlers = React.useMemo(() => {
    let bffUrl = "";
    try {
      bffUrl = getBffBaseUrl();
    } catch {
      // Runtime config not yet initialized — playbook handlers will be inert
    }
    return createPlaybookHandlers({
      onDialogClose: () => {
        feedRefetchRef.current?.();
        todoRefetchRef.current?.();
      },
      bffBaseUrl: bffUrl,
    });
  }, []);

  // -------------------------------------------------------------------------
  // To Do and Documents: open full list dialog handlers
  // -------------------------------------------------------------------------

  const handleOpenTodoDialog = React.useCallback(() => {
    // Open the workspace web resource in dialog mode with full Kanban + inline detail panel
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (xrm?.Navigation?.navigateTo) {
        xrm.Navigation.navigateTo(
          {
            pageType: "webresource",
            webresourceName: "sprk_corporateworkspace",
            data: "mode=todo",
          },
          { target: 2, width: { value: 90, unit: "%" }, height: { value: 90, unit: "%" } }
        ).then(() => {
          // Dialog closed — refetch workspace Kanban to sync any changes made in dialog
          todoRefetchRef.current?.();
        }).catch(() => { /* user cancelled or navigation failed */ });
      }
    } catch (err) {
      console.error("[WorkspaceGrid] Failed to open To Do dialog:", err);
    }
  }, []);

  const handleOpenDocumentsDialog = React.useCallback(() => {
    navigateToEntityList("sprk_document", "1ab4a862-3317-f111-8342-7c1e525abd8b");
  }, []);

  // Open DocumentUploadWizard Code Page dialog (Integration Pattern C — frame-walking)
  const handleAddDocument = React.useCallback(async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any =
      (window as any)?.Xrm ??
      (window.parent as any)?.Xrm ??
      (window.top as any)?.Xrm;
    if (!xrm?.Navigation?.navigateTo) {
      console.warn("[WorkspaceGrid] Xrm.Navigation not available");
      return;
    }

    // Resolve container ID from business unit
    let containerId = "";
    try {
      const userSettings = xrm.Utility.getGlobalContext().userSettings;
      const uid = userSettings.userId.replace(/[{}]/g, "");
      const user = await xrm.WebApi.retrieveRecord(
        "systemuser", uid, "?$select=_businessunitid_value"
      );
      const buId = user["_businessunitid_value"] as string;
      if (buId) {
        const bu = await xrm.WebApi.retrieveRecord(
          "businessunit", buId, "?$select=sprk_containerid"
        );
        containerId = (bu["sprk_containerid"] as string) ?? "";
      }
    } catch (err) {
      console.warn("[WorkspaceGrid] Failed to resolve container ID:", err);
    }
    if (!containerId) {
      console.error("[WorkspaceGrid] No container ID available");
      return;
    }

    // Detect theme
    let theme = "light";
    try {
      const bodyBg = window.getComputedStyle(document.body).backgroundColor;
      const rgbMatch = bodyBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
      if (rgbMatch) {
        const luminance =
          0.299 * parseInt(rgbMatch[1]) +
          0.587 * parseInt(rgbMatch[2]) +
          0.114 * parseInt(rgbMatch[3]);
        if (luminance < 128) theme = "dark";
      }
    } catch { /* ignore */ }

    const dataString =
      "parentEntityType=sprk_document" +
      "&parentEntityId=" +
      "&parentEntityName=" +
      "&containerId=" + containerId +
      "&theme=" + theme +
      "&bffBaseUrl=" + encodeURIComponent(getBffBaseUrl());

    try {
      await xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_documentuploadwizard",
          data: encodeURIComponent(dataString),
        },
        {
          target: 2,
          width: { value: 60, unit: "%" },
          height: { value: 70, unit: "%" },
        }
      );
    } catch (error: any) {
      if (error?.errorCode !== 2) {
        console.error("[WorkspaceGrid] Upload dialog error:", error);
      }
    }
  }, []);

  // -------------------------------------------------------------------------
  // Full card click handler map: Create Matter (wizard) + 6 Analysis Builder
  // -------------------------------------------------------------------------

  const cardClickHandlers = React.useMemo(
    () => ({
      ...playbookHandlers,
      // Explicit handlers AFTER spread to prevent overwrite
      "create-new-matter": handleOpenWizard,
      "create-new-project": handleOpenProjectWizard,
      "summarize-new-files": handleOpenSummarize,
      "find-similar": handleOpenFindSimilar,
      "assign-to-counsel": handleOpenWorkAssignmentWizard,
    }),
    [playbookHandlers, handleOpenWizard, handleOpenProjectWizard, handleOpenSummarize, handleOpenFindSimilar, handleOpenWorkAssignmentWizard]
  );

  // -------------------------------------------------------------------------
  // Stable callbacks for toolbar refetch buttons (avoid re-building config)
  // -------------------------------------------------------------------------

  const handleTodoRefetch = React.useCallback(() => {
    todoRefetchRef.current?.();
  }, []);

  const handleDocRefetch = React.useCallback(() => {
    docRefetchRef.current?.();
  }, []);

  // -------------------------------------------------------------------------
  // SectionFactoryContext — standard context for dynamic section factories
  // -------------------------------------------------------------------------

  const handleNavigate = React.useCallback((target: NavigateTarget) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any =
      (window as any)?.Xrm ??
      (window.parent as any)?.Xrm ??
      (window.top as any)?.Xrm;

    if (target.type === "view" && target.viewId) {
      navigateToEntityList(target.entity, target.viewId);
    } else if (target.type === "record" && xrm?.Navigation?.openForm) {
      xrm.Navigation.openForm({
        entityName: target.entity,
        entityId: target.id,
      });
    } else if (target.type === "url" && xrm?.Navigation?.openUrl) {
      xrm.Navigation.openUrl(target.url);
    }
  }, []);

  const handleOpenWizardGeneric = React.useCallback(
    async (webResourceName: string, data?: string, options?: DialogOptions) => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm: any =
          (window as any)?.Xrm ??
          (window.parent as any)?.Xrm ??
          (window.top as any)?.Xrm;
        if (!xrm?.Navigation?.navigateTo) return;

        const bffParam = `bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`;
        const fullData = data ? `${data}&${bffParam}` : bffParam;

        await xrm.Navigation.navigateTo(
          {
            pageType: "webresource",
            webresourceName: webResourceName,
            data: fullData,
          },
          {
            target: 2,
            width: options?.width ?? { value: 60, unit: "%" },
            height: options?.height ?? { value: 70, unit: "%" },
          },
        );
      } catch {
        // User cancelled or dialog error — ignore
      }
    },
    [],
  );

  const factoryContext = React.useMemo<SectionFactoryContext>(() => {
    let bffBaseUrl = "";
    try {
      bffBaseUrl = getBffBaseUrl();
    } catch {
      // Not initialized yet — sections that need BFF will handle gracefully
    }

    return {
      webApi,
      userId,
      service,
      bffBaseUrl,
      onNavigate: handleNavigate,
      onOpenWizard: handleOpenWizardGeneric,
      onBadgeCountChange: (/* count — handled per-section via factory */) => {},
      onRefetchReady: (/* refetch — handled per-section via factory */) => {},
    };
  }, [webApi, userId, service, handleNavigate, handleOpenWizardGeneric]);

  // -------------------------------------------------------------------------
  // Build dynamic WorkspaceConfig from active layout + SECTION_REGISTRY
  // Falls back to old buildWorkspaceConfig on error for graceful degradation
  // -------------------------------------------------------------------------

  const workspaceConfig = React.useMemo(() => {
    try {
      return buildDynamicWorkspaceConfig(
        activeLayoutJson,
        SECTION_REGISTRY,
        factoryContext,
      );
    } catch (err) {
      console.warn(
        "[WorkspaceGrid] Dynamic config build failed, falling back to static config:",
        err,
      );
      // Fallback: use the old static buildWorkspaceConfig
      return buildWorkspaceConfig({
        webApi,
        userId,
        service,
        feedCount,
        todoCount,
        docCount,
        onTodoRefetch: handleTodoRefetch,
        onDocRefetch: handleDocRefetch,
        onTodoRefetchReady: handleTodoRefetchReady,
        onFeedRefetchReady: handleFeedRefetchReady,
        onDocRefetchReady: handleDocRefetchReady,
        onFeedCountChange: setFeedCount,
        onTodoCountChange: setTodoCount,
        onDocCountChange: setDocCount,
        onExpandClick: handleExpandClick,
        onDashboardOpen: handleDashboardOpen,
        onOpenAllUpdates: handleOpenAllUpdates,
        onCreateEvent: handleOpenEventWizard,
        onOpenTodoWizard: handleOpenTodoWizard,
        onOpenTodoDialog: handleOpenTodoDialog,
        onAddDocument: handleAddDocument,
        onOpenDocumentsDialog: handleOpenDocumentsDialog,
        cardClickHandlers,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    activeLayoutJson,
    factoryContext,
    webApi,
    userId,
    service,
    feedCount,
    todoCount,
    docCount,
  ]);

  // -------------------------------------------------------------------------
  // WorkspaceHeader layout summaries (map BFF DTOs to header props)
  // -------------------------------------------------------------------------

  const headerLayouts = React.useMemo<WorkspaceLayoutSummary[]>(
    () =>
      workspaceLayouts.map((l) => ({
        id: l.id,
        name: l.name,
        isSystem: l.isSystem,
      })),
    [workspaceLayouts],
  );

  const headerActiveLayout = React.useMemo<WorkspaceLayoutSummary>(
    () =>
      activeLayout
        ? { id: activeLayout.id, name: activeLayout.name, isSystem: activeLayout.isSystem }
        : { id: "system-default", name: "Corporate Workspace", isSystem: true },
    [activeLayout],
  );

  const handleLayoutChange = React.useCallback(
    (layoutId: string) => {
      setActiveLayoutById(layoutId);
    },
    [setActiveLayoutById],
  );

  const handleEditLayout = React.useCallback(() => {
    if (!activeLayout) return;
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (!xrm?.Navigation?.navigateTo) return;

      const mode = activeLayout.isSystem ? "saveAs" : "edit";

      // For saveAs mode, pass the system layout data so the wizard can pre-populate
      // all three steps (template, sections, name) from the source layout.
      let dataParams = `mode=${mode}&layoutId=${activeLayout.id}&bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`;
      if (mode === "saveAs") {
        dataParams += `&layoutTemplateId=${encodeURIComponent(activeLayout.layoutTemplateId)}`;
        dataParams += `&sectionsJson=${encodeURIComponent(activeLayout.sectionsJson)}`;
        dataParams += `&name=${encodeURIComponent(activeLayout.name)}`;
      }

      xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_workspacelayoutwizard",
          data: dataParams,
        },
        {
          target: 2,
          width: { value: 80, unit: "%" },
          height: { value: 80, unit: "%" },
          title: mode === "saveAs" ? "Save As New Workspace" : "Edit Workspace",
        },
      ).then(() => {
        // Wizard closed — refetch layouts in case changes were saved
        refetchLayouts();
      }).catch(() => { /* user cancelled */ });
    } catch {
      // Navigation not available
    }
  }, [activeLayout, refetchLayouts]);

  const handleCreateLayout = React.useCallback(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      if (!xrm?.Navigation?.navigateTo) return;

      xrm.Navigation.navigateTo(
        {
          pageType: "webresource",
          webresourceName: "sprk_workspacelayoutwizard",
          data: `mode=create&bffBaseUrl=${encodeURIComponent(getBffBaseUrl())}`,
        },
        {
          target: 2,
          width: { value: 80, unit: "%" },
          height: { value: 80, unit: "%" },
          title: "Create New Workspace",
        },
      ).then(() => {
        refetchLayouts();
      }).catch(() => { /* user cancelled */ });
    } catch {
      // Navigation not available
    }
  }, [refetchLayouts]);

  // -------------------------------------------------------------------------
  // Push header state to parent (PageHeader renders the dropdown + gear)
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    onHeaderReady?.({
      activeLayout: headerActiveLayout,
      layouts: headerLayouts,
      onLayoutChange: handleLayoutChange,
      onEditClick: handleEditLayout,
      onCreateClick: handleCreateLayout,
    });
  }, [headerActiveLayout, headerLayouts, handleLayoutChange, handleEditLayout, handleCreateLayout, onHeaderReady]);

  // -------------------------------------------------------------------------
  // Layout — WorkspaceShell renders the full grid
  // -------------------------------------------------------------------------

  return (
    <>
      {/* ----- Loading state: skeleton grid while fetching layouts ----- */}
      {layoutStatus === "loading" && <WorkspaceSkeleton />}

      {/* ----- Error state: fallback layout + warning bar ----- */}
      {layoutStatus === "error" && (
        <>
          <FetchErrorBar />
          <WorkspaceShell config={workspaceConfig} />
        </>
      )}

      {/* ----- First visit: system default + personalize banner ----- */}
      {layoutStatus === "first-visit" && (
        <>
          <PersonalizeBanner />
          <WorkspaceShell config={workspaceConfig} />
        </>
      )}

      {/* ----- Loaded: normal workspace rendering ----- */}
      {layoutStatus === "loaded" && <WorkspaceShell config={workspaceConfig} />}

      {/* GetStarted expand dialog — shows all 7 action cards in a grid */}
      {isExpandOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyGetStartedExpandDialog
            open={isExpandOpen}
            onClose={handleExpandClose}
            onCardClick={cardClickHandlers}
          />
        </React.Suspense>
      )}

      {/* Quick Summary Dashboard dialog — Coming Soon placeholder */}
      {isDashboardOpen && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyQuickSummaryDashboardDialog
            open={isDashboardOpen}
            onClose={handleDashboardClose}
          />
        </React.Suspense>
      )}

      {/* Close Secure Project confirmation dialog — triggered via ribbon command or
          window.__SPAARKE_OPEN_CLOSE_PROJECT__(projectId, projectName, containerId?).
          Lazy-loaded: chunk only fetched on first user interaction. */}
      {closeProjectContext !== null && (
        <React.Suspense fallback={<DialogLoadingFallback />}>
          <LazyCloseProjectDialog
            open={closeProjectContext !== null}
            projectId={closeProjectContext.projectId}
            projectName={closeProjectContext.projectName}
            containerId={closeProjectContext.containerId}
            onClose={handleCloseProjectDialog}
          />
        </React.Suspense>
      )}
    </>
  );
};
