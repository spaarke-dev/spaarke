import * as React from "react";
import {
  FluentProvider,
  tokens,
  Text,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { BuProvider, useBuContext } from "./contexts/BuContext";
import { AppShell, type SpeAdminPage } from "./components/layout/AppShell";
import { DashboardPage } from "./components/dashboard/DashboardPage";
import { FileBrowserPage } from "./components/files/FileBrowserPage";
import { SettingsPage } from "./components/settings/SettingsPage";
import { AuditLogPage } from "./components/audit/AuditLogPage";
import { SearchPage } from "./components/search/SearchPage";
import { SecurityPage } from "./components/security/SecurityPage";
import { RecycleBinPage } from "./components/recycle-bin/RecycleBinPage";
import { ContainerTypesPage } from "./components/container-types/ContainerTypesPage";
import { ContainerTypeDetail } from "./components/container-types/ContainerTypeDetail";

// SPE Admin App — root application component
// Wraps the app in FluentProvider with dynamic theme resolution (ADR-021)
// Parses URL parameters passed via Xrm.Navigation.navigateTo data param

// ─────────────────────────────────────────────────────────────────────────────
// URL Parameter Parsing
// ─────────────────────────────────────────────────────────────────────────────

/**
 * URL parameters passed to the SPE Admin App via navigateTo.
 * All values are optional — the app functions without them (shows BU selector).
 */
export interface SpeAdminParams {
  /** Pre-selected environment config ID (GUID) — skips environment selection step */
  configId: string | null;
  /** Pre-selected Business Unit ID (GUID) — narrows the container scope */
  buId: string | null;
}

/**
 * Parse the `data` query parameter from the URL.
 *
 * Dataverse passes custom data via:
 *   ?data=key1%3Dvalue1%26key2%3Dvalue2
 *
 * The `data` value is URL-encoded once, and its content is a
 * key=value&key=value string.
 */
function parseDataParams(): Record<string, string> {
  try {
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
  } catch {
    // URL parsing failed — return empty params
    return {};
  }
}

/**
 * Extract typed SPE Admin parameters from the Dataverse data param.
 */
function parseSpeAdminParams(): SpeAdminParams {
  const data = parseDataParams();
  return {
    configId: data["configId"] ?? null,
    buId: data["buId"] ?? null,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// App Component
// ─────────────────────────────────────────────────────────────────────────────

const APP_VERSION = "1.0.0";

// ─────────────────────────────────────────────────────────────────────────────
// AppContent — inner component that consumes BuContext for page routing
// ─────────────────────────────────────────────────────────────────────────────

interface AppContentProps {
  params: SpeAdminParams;
  activePage: SpeAdminPage;
  onNavigate: (page: SpeAdminPage) => void;
}

/**
 * AppContent renders the active page component.
 * Separated from App so it can use useBuContext (which requires BuProvider).
 *
 * fileBrowserContainerId / fileBrowserContainerName:
 *   Set by the ContainersPage (task SPE-033) when the user opens a container.
 *   Passed into FileBrowserPage so it knows which container to browse.
 */
const AppContent: React.FC<AppContentProps> = ({
  params,
  activePage,
  onNavigate,
}) => {
  const { selectedConfig } = useBuContext();

  // Container selected for browsing — set when user opens a container from ContainersPage.
  // ContainersPage (task SPE-033) will call onOpenContainer to populate this state.
  const [fileBrowserContainerId, setFileBrowserContainerId] = React.useState<
    string | undefined
  >(undefined);
  const [fileBrowserContainerName, setFileBrowserContainerName] =
    React.useState<string | undefined>(undefined);

  // Container type selected for detail panel — set when user clicks a row in ContainerTypesPage.
  const [detailContainerTypeId, setDetailContainerTypeId] = React.useState<string | null>(null);

  /** Called by ContainersPage when the user opens a container for browsing. */
  const handleOpenContainerInBrowser = React.useCallback(
    (containerId: string, containerName?: string) => {
      setFileBrowserContainerId(containerId);
      setFileBrowserContainerName(containerName);
      onNavigate("file-browser");
    },
    [onNavigate]
  );

  // Use configId from the selected config, or fall back to URL param
  const configId =
    selectedConfig?.id ?? params.configId ?? undefined;

  return (
    <AppShell
      activePage={activePage}
      onNavigate={onNavigate}
      version={APP_VERSION}
    >
      {/*
       * Page routing — renders the appropriate page based on activePage.
       * DashboardPage:        task SPE-032
       * FileBrowserPage:      task SPE-036
       * SettingsPage:         task SPE-038
       * AuditLogPage:         task SPE-037
       * ContainersPage:       task SPE-033 (placeholder still shown)
       * ContainerTypesPage:   task SPE-061
       */}
      {activePage === "dashboard" ? (
        <DashboardPage />
      ) : activePage === "file-browser" ? (
        <FileBrowserPage
          containerId={fileBrowserContainerId}
          configId={configId}
          containerName={fileBrowserContainerName}
        />
      ) : activePage === "settings" ? (
        <SettingsPage />
      ) : activePage === "audit-log" ? (
        <AuditLogPage />
      ) : activePage === "search" ? (
        <SearchPage />
      ) : activePage === "security" ? (
        <SecurityPage />
      ) : activePage === "recycle-bin" ? (
        <RecycleBinPage />
      ) : activePage === "container-types" ? (
        <>
          <ContainerTypesPage onOpenDetail={setDetailContainerTypeId} />
          <ContainerTypeDetail
            containerTypeId={detailContainerTypeId}
            onClose={() => setDetailContainerTypeId(null)}
          />
        </>
      ) : (
        // Placeholder for pages not yet implemented (e.g. containers)
        // The onOpenContainerInBrowser callback is available for ContainersPage (task SPE-033)
        // to hook into once implemented.
        <div
          style={{
            padding: tokens.spacingVerticalXL,
            display: "flex",
            flexDirection: "column",
            gap: tokens.spacingVerticalM,
          }}
          data-open-container-handler={String(!!handleOpenContainerInBrowser)}
        >
          <Text size={500} weight="semibold">
            {activePage.charAt(0).toUpperCase() +
              activePage.slice(1).replace("-", " ")}
          </Text>
          <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
            SPE Admin App — page content for &quot;{activePage}&quot; will be
            added in subsequent tasks.
            {params.configId ? ` (configId: ${params.configId})` : ""}
            {params.buId ? ` (buId: ${params.buId})` : ""}
          </Text>
        </div>
      )}
    </AppShell>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// App — root component with theme and provider setup
// ─────────────────────────────────────────────────────────────────────────────

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);

  // Parse URL parameters once on mount (stable across renders)
  const params = React.useMemo(() => parseSpeAdminParams(), []);

  // Active navigation page
  const [activePage, setActivePage] = React.useState<SpeAdminPage>("dashboard");

  // Theme listener — responds to Dataverse theme changes and system changes
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      {/*
       * BuProvider manages BU/config/environment selection state.
       * Placed inside FluentProvider so context can access Fluent theme if needed.
       * initialBuId and initialConfigId seed from URL params (Xrm.Navigation.navigateTo data).
       * Task 028 — BuContext state management.
       */}
      <BuProvider initialBuId={params.buId} initialConfigId={params.configId}>
        {/*
         * AppContent is separated so it can use useBuContext (requires BuProvider).
         * It handles page routing and passing BU/config state to page components.
         */}
        <AppContent
          params={params}
          activePage={activePage}
          onNavigate={setActivePage}
        />
      </BuProvider>
    </FluentProvider>
  );
};
