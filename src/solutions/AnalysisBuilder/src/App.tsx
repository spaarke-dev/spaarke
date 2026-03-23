/**
 * App.tsx — Analysis Builder Code Page (Thin Wrapper)
 *
 * Delegates all UI and logic to PlaybookLibraryShell from the shared library.
 * Parses URL parameters and wires up Xrm service adapters.
 *
 * Replaces the former ~508 LOC monolith (UDSS-022).
 */

import * as React from "react";
import { PlaybookLibraryShell } from "@spaarke/ui-components/components/PlaybookLibraryShell";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";

export const App: React.FC = () => {
  const params = React.useMemo(() => parseDataParams(), []);
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>(
    params.apiBaseUrl || params.bffBaseUrl || ""
  );

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
        console.error("[AnalysisBuilder] Failed to initialize auth:", err);
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => { cancelled = true; };
  }, []);

  if (!isAuthReady) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
        <span>Initializing...</span>
      </div>
    );
  }

  return (
    <PlaybookLibraryShell
      entityType={params.entityType || "sprk_document"}
      entityId={params.documentId || params.entityId || ""}
      entityDisplayName={params.documentName}
      dataService={dataService}
      authenticatedFetch={authenticatedFetch}
      bffBaseUrl={resolvedBffBaseUrl}
      onClose={() => {
        try { window.close(); } catch { window.history.back(); }
      }}
    />
  );
};
