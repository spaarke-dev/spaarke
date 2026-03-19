/**
 * PlaybookLibraryPage — SPA page that renders the PlaybookLibraryShell.
 *
 * Route pattern: /playbooks/:entityType/:entityId  (browse mode)
 *                /playbooks/:entityType/:entityId?intent=<intent>  (intent mode)
 *
 * Bridges the shared PlaybookLibraryShell into the Power Pages SPA context by:
 *   1. Parsing route params (entityType, entityId) and optional intent query param
 *   2. Creating an authenticated fetch wrapper backed by MSAL token acquisition
 *   3. Instantiating a BFF-backed IDataService via createBffDataService
 *   4. Rendering PlaybookLibraryShell with onClose/onComplete callbacks
 *
 * Authentication: MSAL (Entra B2B) via acquireBffToken — same as all other SPA pages.
 * Data access: BFF API proxy at /api/dataverse/ — no direct Dataverse calls.
 *
 * @see PlaybookLibraryShell in @spaarke/ui-components
 * @see ADR-012 (Shared Component Library), ADR-013 (AI Architecture)
 */

import * as React from "react";
import { useParams, useSearchParams, useNavigate } from "react-router-dom";
import {
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
} from "@fluentui/react-components";
import { PlaybookLibraryShell } from "@spaarke/ui-components/components/PlaybookLibraryShell";
import { createBffDataService } from "@spaarke/ui-components/utils/adapters/bffDataServiceAdapter";
import type { AuthenticatedFetch } from "@spaarke/ui-components/utils/adapters/bffDataServiceAdapter";
import { acquireBffToken } from "../auth/msal-auth";
import { BFF_API_URL } from "../config";
import { PageContainer, NavigationBar } from "../components";
import type { BreadcrumbNavItem } from "../components";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  shellContainer: {
    flex: "1 1 auto",
    display: "flex",
    flexDirection: "column",
    minHeight: "500px",
    overflow: "hidden",
  },
  centered: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flex: 1,
    padding: tokens.spacingVerticalXXL,
  },
});

// ---------------------------------------------------------------------------
// Authenticated fetch factory
// ---------------------------------------------------------------------------

/**
 * Creates an authenticated fetch function that attaches an MSAL-acquired
 * Bearer token to every outgoing request. This matches the AuthenticatedFetch
 * signature expected by createBffDataService.
 *
 * On 401, acquires a fresh token and retries once (mirrors bff-client.ts logic).
 */
function createAuthenticatedFetch(): AuthenticatedFetch {
  return async (url: string, init?: RequestInit): Promise<Response> => {
    const token = await acquireBffToken();
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
      ...(init?.headers as Record<string, string> | undefined),
    };

    const response = await fetch(url, { ...init, headers });

    // Retry once on 401 with a fresh token
    if (response.status === 401) {
      const freshToken = await acquireBffToken();
      const retryHeaders: Record<string, string> = {
        ...headers,
        Authorization: `Bearer ${freshToken}`,
      };
      return fetch(url, { ...init, headers: retryHeaders });
    }

    return response;
  };
}

// ---------------------------------------------------------------------------
// Route param types
// ---------------------------------------------------------------------------

interface PlaybookRouteParams {
  entityType: string;
  entityId: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PlaybookLibraryPage: React.FC = () => {
  const styles = useStyles();
  const navigate = useNavigate();
  const { entityType, entityId } = useParams<keyof PlaybookRouteParams>() as PlaybookRouteParams;
  const [searchParams] = useSearchParams();

  const intent = searchParams.get("intent") ?? undefined;

  // Validate required route params
  if (!entityType || !entityId) {
    return (
      <PageContainer>
        <div className={styles.centered}>
          <MessageBar intent="error">
            <MessageBarBody>
              Missing required route parameters. Expected: /playbooks/:entityType/:entityId
            </MessageBarBody>
          </MessageBar>
        </div>
      </PageContainer>
    );
  }

  // Memoize the authenticated fetch and data service so they remain stable
  // across re-renders (no dependency on changing values).
  const authenticatedFetch = React.useMemo(() => createAuthenticatedFetch(), []);

  const dataService = React.useMemo(
    () => createBffDataService(authenticatedFetch, BFF_API_URL),
    [authenticatedFetch]
  );

  // Determine display mode based on intent query param
  const mode: "browse" | "intent" = intent ? "intent" : "browse";

  // Breadcrumb navigation (href-based for HashRouter — NavigationBar uses <a href>)
  const breadcrumbs: BreadcrumbNavItem[] = React.useMemo(
    () => [
      { label: "Home", href: "#/" },
      { label: "Playbook Library" },
    ],
    []
  );

  // --- Callbacks ---

  const handleClose = React.useCallback(() => {
    // Navigate back; if there is browser history go back, otherwise go home
    if (window.history.length > 1) {
      navigate(-1);
    } else {
      navigate("/");
    }
  }, [navigate]);

  const handleComplete = React.useCallback(
    (result: { analysisId: string }) => {
      console.log("[PlaybookLibraryPage] Analysis created:", result.analysisId);
      // Navigate back after successful creation
      if (window.history.length > 1) {
        navigate(-1);
      } else {
        navigate("/");
      }
    },
    [navigate]
  );

  return (
    <PageContainer>
      <NavigationBar items={breadcrumbs} />

      <div className={styles.shellContainer}>
        <PlaybookLibraryShell
          entityType={entityType}
          entityId={entityId}
          mode={mode}
          intent={intent}
          dataService={dataService}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={BFF_API_URL}
          onClose={handleClose}
          onComplete={handleComplete}
          title={intent ? `Run: ${intent}` : "Playbook Library"}
        />
      </div>
    </PageContainer>
  );
};

export default PlaybookLibraryPage;
