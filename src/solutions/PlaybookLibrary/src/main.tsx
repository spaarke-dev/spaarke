/**
 * Playbook Library - Code Page Entry Point
 *
 * Unified Code Page that replaces both PlaybookLibrary and AnalysisBuilder.
 * Accepts the union of parameters from both former Code Pages:
 *
 *   intent        — pre-selected playbook intent (AnalysisBuilder / PlaybookLibrary)
 *   documentId    — single document ID (AnalysisBuilder)
 *   documentIds   — comma-separated list of document IDs (future multi-doc use)
 *   documentName  — document display name (AnalysisBuilder)
 *   apiBaseUrl    — BFF API base URL alias (AnalysisBuilder)
 *   bffBaseUrl    — BFF API base URL (PlaybookLibrary)
 *   entityType    — context entity type (both)
 *   entityId      — context entity GUID (both)
 *
 * Routing logic:
 *   - If `intent` is passed  → pre-select that playbook (intent mode)
 *   - If only `documentId` is passed without `intent` → browse/select playbook view
 *   - `documentId` is mapped to `entityId` when `entityType` is not provided
 *     (backwards-compat with AnalysisBuilder callers that only pass documentId)
 *
 * Close behavior auto-detection:
 *   - If `window.opener` is truthy OR the URL contains `target=2` from a navigateTo
 *     dialog launch → use Xrm INavigationService.closeDialog (Dataverse modal)
 *   - Otherwise → fallback to window.close() then window.history.back()
 *     (supports AnalysisBuilder callers that open as a regular navigation)
 *
 * Web resource name: sprk_playbooklibrary
 * Display name: Playbook Library
 *
 * Note: Standalone web resource (not PCF) → React 18 with native useId() support
 * required by Fluent UI v9.
 *
 * @see ADR-006 - Code Pages for standalone dialogs
 * @see ADR-012 - Shared components from @spaarke/ui-components
 */

import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { PlaybookLibraryShell } from "@spaarke/ui-components/components/PlaybookLibraryShell";
import {
  resolveRuntimeConfig,
  initAuth,
  authenticatedFetch,
} from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Close behavior helpers
// ---------------------------------------------------------------------------

/**
 * Determine whether we are running inside a Dataverse navigateTo dialog
 * (target: 2) or as a standalone navigation.
 *
 * Heuristics (in order of reliability):
 *  1. window.opener is truthy → opened as a popup/dialog by Dataverse
 *  2. window.parent !== window → running in an iframe (Dataverse UCIv2 modal)
 *  3. URL search contains a `data` param → Xrm data envelope present
 *
 * When any heuristic matches we prefer the Xrm closeDialog path.
 */
function detectIsDialogContext(): boolean {
  try {
    if (window.opener) return true;
    if (window.parent !== window) return true;
    if (new URLSearchParams(window.location.search).has("data")) return true;
  } catch {
    // ignore cross-origin access errors
  }
  return false;
}

// ---------------------------------------------------------------------------
// App component
// ---------------------------------------------------------------------------

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>(
    // Accept both apiBaseUrl (AnalysisBuilder) and bffBaseUrl (PlaybookLibrary)
    params.apiBaseUrl || params.bffBaseUrl || ""
  );

  // Theme listener
  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  // Auth initialisation
  React.useEffect(() => {
    let cancelled = false;
    async function initialize(): Promise<void> {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
          tenantId: config.tenantId || undefined,
          proactiveRefresh: true,
        });
        if (!cancelled) {
          setResolvedBffBaseUrl(config.bffBaseUrl);
          setIsAuthReady(true);
        }
      } catch (err) {
        console.error("[PlaybookLibrary] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => {
      cancelled = true;
    };
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(
    () => createXrmNavigationService(),
    []
  );

  // -------------------------------------------------------------------------
  // Resolve merged parameters — union of PlaybookLibrary + AnalysisBuilder
  // -------------------------------------------------------------------------

  // documentIds — comma-separated list of document GUIDs (new multi-doc path).
  // When present and contains 2+ IDs the shell renders a document selector.
  const resolvedDocumentIds = React.useMemo((): string[] | undefined => {
    const raw = params.documentIds as string | undefined;
    if (!raw) return undefined;
    const ids = raw
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    return ids.length >= 1 ? ids : undefined;
  }, [params.documentIds]);

  // entityType / entityId — direct params take precedence.
  // When only documentId is provided (AnalysisBuilder compat), treat the
  // document as the entity so PlaybookLibraryShell has a valid entityId.
  //
  // When documentIds is provided, entityId defaults to the first ID in the
  // list (the shell will manage the active selection internally).
  const resolvedEntityType =
    params.entityType ||
    (params.documentId || resolvedDocumentIds ? "sprk_document" : "");

  const resolvedEntityId =
    params.entityId ||
    params.documentId ||
    resolvedDocumentIds?.[0] ||
    "";

  // documentName maps to entityDisplayName in the shell
  const entityDisplayName = params.documentName || undefined;

  // intent — present when launched from a "Run Analysis" action button
  const intent = params.intent || undefined;

  // PlaybookLibraryShell mode:
  //   'intent' → a specific playbook is pre-selected (intent was provided)
  //   'browse' → show the full card grid (no pre-selection)
  const shellMode: "browse" | "intent" = intent ? "intent" : "browse";

  // -------------------------------------------------------------------------
  // Close behavior auto-detection
  // -------------------------------------------------------------------------

  const isDialogContext = React.useMemo(() => detectIsDialogContext(), []);

  const handleClose = React.useCallback(() => {
    if (isDialogContext) {
      // Xrm navigateTo modal path (PlaybookLibrary original behaviour)
      try {
        navigationService.closeDialog({ confirmed: true });
        return;
      } catch (err) {
        console.warn(
          "[PlaybookLibrary] closeDialog via Xrm failed, falling back:",
          err
        );
      }
    }
    // Fallback: window.close() then history.back()
    // (AnalysisBuilder original behaviour for non-modal launches)
    try {
      window.close();
    } catch {
      window.history.back();
    }
  }, [isDialogContext, navigationService]);

  // -------------------------------------------------------------------------
  // onComplete — open Analysis Workspace then close this dialog
  // -------------------------------------------------------------------------

  const handleComplete = React.useCallback(
    ({ analysisId }: { analysisId: string }) => {
      // Navigate to Analysis Workspace in the parent/host frame so it opens
      // as a new dialog on top of the Dataverse page (not nested inside this one).
      const dataParam = resolvedEntityId
        ? `analysisId=${analysisId}&documentId=${resolvedEntityId}`
        : `analysisId=${analysisId}`;

      try {
        // Walk up frames to find a Xrm.Navigation that can open a new dialog.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const frames: Window[] = [];
        try {
          if (window.parent && window.parent !== window) frames.push(window.parent);
        } catch { /* cross-origin */ }
        try {
          if (window.top && window.top !== window && window.top !== window.parent)
            frames.push(window.top!);
        } catch { /* cross-origin */ }

        let launched = false;
        for (const frame of frames) {
          try {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const xrmNav = (frame as any).Xrm?.Navigation;
            if (xrmNav?.navigateTo) {
              xrmNav.navigateTo(
                {
                  pageType: "webresource",
                  webresourceName: "sprk_AnalysisWorkspace",
                  data: dataParam,
                },
                {
                  target: 2,
                  width: { value: 95, unit: "%" },
                  height: { value: 95, unit: "%" },
                }
              );
              launched = true;
              break;
            }
          } catch { /* try next frame */ }
        }

        if (!launched) {
          console.warn(
            "[PlaybookLibrary] Could not find Xrm.Navigation in parent frames to open Analysis Workspace"
          );
        }
      } catch (err) {
        console.warn("[PlaybookLibrary] Failed to open Analysis Workspace:", err);
      }

      // Close the Playbook Library dialog
      handleClose();
    },
    [resolvedEntityId, handleClose]
  );

  // -------------------------------------------------------------------------
  // Loading gate
  // -------------------------------------------------------------------------

  if (!isAuthReady) {
    return (
      <FluentProvider theme={theme} style={{ height: "100%" }}>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            height: "100%",
          }}
        >
          <span>Initializing...</span>
        </div>
      </FluentProvider>
    );
  }

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <PlaybookLibraryShell
        dataService={dataService}
        entityType={resolvedEntityType}
        entityId={resolvedEntityId}
        entityDisplayName={entityDisplayName}
        embedded={true}
        onClose={handleClose}
        onComplete={handleComplete}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={resolvedBffBaseUrl}
        mode={shellMode}
        {...(resolvedDocumentIds ? { documentIds: resolvedDocumentIds } : {})}
        {...(intent ? { intent } : {})}
      />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// Mount
// ---------------------------------------------------------------------------

const rootElement = document.getElementById("root");

if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[PlaybookLibrary] Root element not found");
}
