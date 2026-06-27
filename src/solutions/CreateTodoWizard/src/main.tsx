import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
import { CreateTodoWizard } from "@spaarke/ui-components/components/CreateTodoWizard";
import type { IDataService } from "@spaarke/ui-components/types/serviceInterfaces";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// R4 task 100 (W-2) — post-wizard-close refetch BroadcastChannel contract.
//
// After a successful `sprk_todo` create we post a `{ type: SPRK_TODO_CREATED }`
// message on the SPRK_TODO_CHANNEL_NAME channel. The LegalWorkspace SmartTodo
// widget shim (`src/solutions/LegalWorkspace/src/sections/todo.registration.ts`)
// subscribes and invokes its captured `refetch` ref so the list refreshes
// without a page reload — closes UAT issue 1 from the 2026-06-18 widget-parity
// audit.
//
// Contract: constants MUST stay in lockstep with the shim's matching constants.
// They are intentionally inlined on both sides because the wizard Code Page
// does not depend on `@spaarke/smart-todo-components`; introducing a shared
// constants module just to share two strings would couple the wizard's
// minimal build graph to a peer package it otherwise doesn't need.
//
// Defensive: BroadcastChannel is widely supported in modern Chromium-based MDA
// runtimes; on the rare hostile sandbox where it isn't, the wrapper silently
// no-ops (the create still succeeds; only the cross-iframe refetch is missed,
// and a manual refresh recovers).
// ---------------------------------------------------------------------------

const SPRK_TODO_ENTITY = "sprk_todo";
const SPRK_TODO_CHANNEL_NAME = "sprk_todo:lifecycle";
const SPRK_TODO_CREATED = "sprk_todo:created";

/**
 * Wrap an `IDataService` so successful `sprk_todo` creates broadcast a
 * `sprk_todo:created` message on the shared BroadcastChannel. All other
 * operations pass through unmodified.
 *
 * @param inner - The underlying IDataService (e.g., XrmDataServiceAdapter).
 * @returns A wrapped IDataService instance.
 */
function wrapDataServiceForCreateBroadcast(inner: IDataService): IDataService {
  if (typeof BroadcastChannel === "undefined") {
    // No-op wrapper — host environment doesn't support BroadcastChannel.
    return inner;
  }

  return {
    ...inner,
    createRecord: async (entityName, data) => {
      const result = await inner.createRecord(entityName, data);
      if (entityName === SPRK_TODO_ENTITY && result) {
        try {
          const channel = new BroadcastChannel(SPRK_TODO_CHANNEL_NAME);
          try {
            channel.postMessage({ type: SPRK_TODO_CREATED, todoId: result });
          } finally {
            channel.close();
          }
        } catch (err) {
          // Non-fatal — the create succeeded; only the cross-iframe refetch
          // signal failed. Log + continue so the user still sees the success
          // screen.
          console.warn(
            "[CreateTodoWizard] Failed to broadcast sprk_todo:created — widget will need manual refresh",
            err,
          );
        }
      }
      return result;
    },
  };
}

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>(params.bffBaseUrl || "");

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

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
        console.error("[CreateTodoWizard] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  // R4 task 100 (W-2) — wrap the dataService so successful sprk_todo creates
  // broadcast on the shared BroadcastChannel for cross-iframe refetch wiring.
  const dataService = React.useMemo(
    () => wrapDataServiceForCreateBroadcast(createXrmDataService()),
    [],
  );
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  const handleClose = React.useCallback(() => {
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  const resolveSpeContainerId = React.useCallback(async (): Promise<string> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    if (!xrm?.WebApi?.retrieveRecord) throw new Error("Xrm.WebApi not available");
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    const user = await xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=_businessunitid_value");
    const buId = user["_businessunitid_value"] as string;
    if (!buId) throw new Error("Could not resolve business unit");
    const bu = await xrm.WebApi.retrieveRecord("businessunit", buId, "?$select=sprk_containerid");
    return (bu["sprk_containerid"] as string) || "";
  }, []);

  if (!isAuthReady) {
    return (
      <FluentProvider theme={theme} style={{ height: "100%" }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
          <span>Initializing...</span>
        </div>
      </FluentProvider>
    );
  }

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <CreateTodoWizard
        open={true}
        dataService={dataService}
        navigationService={navigationService}
        embedded={true}
        onClose={handleClose}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={resolvedBffBaseUrl}
        resolveSpeContainerId={resolveSpeContainerId}
      />
    </FluentProvider>
  );
}

const rootElement = document.getElementById("root");
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[CreateTodoWizard] Root element not found");
}
