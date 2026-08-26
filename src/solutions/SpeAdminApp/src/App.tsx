import * as React from "react";
import {
  FluentProvider,
  tokens,
  Text,
} from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { BuProvider, useBuContext } from "./contexts/BuContext";
import { AppShell, type SpeAdminPage } from "./components/layout/AppShell";
import { PageErrorBoundary } from "./components/layout/PageErrorBoundary";
import { useResizablePane, PaneSplitter } from "./components/layout/ResizablePane";
import { DashboardPage } from "./components/dashboard/DashboardPage";
import { FileBrowserPage } from "./components/files/FileBrowserPage";
import { SettingsPage } from "./components/settings/SettingsPage";
import { AuditLogPage } from "./components/audit/AuditLogPage";
import { SearchPage } from "./components/search/SearchPage";
import { SecurityPage } from "./components/security/SecurityPage";
import { RecycleBinPage } from "./components/recycle-bin/RecycleBinPage";
import { ContainersPage } from "./components/containers/ContainersPage";
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
  /**
   * Page to open on load, when the app is deep-linked rather than launched cold.
   *
   * 🔴 Added 2026-08-24 to fix a real defect: Search's "Manage Permissions" opened a new tab at
   * `?page=containers&containerId=…`, but nothing ever read either parameter — `activePage` was
   * hard-initialised to "dashboard". A tab DID open, so the action looked like it worked; it just
   * silently landed on the Dashboard every time. Same shape as the rest of this project's defects:
   * an upper layer reading a dropped value as a benign default.
   */
  page: SpeAdminPage | null;
  /** Container whose detail panel should be open on load. Pairs with `page: "containers"`. */
  containerId: string | null;
}

/** The pages a deep link may target — the runtime guard behind the `SpeAdminPage` type. */
const DEEP_LINKABLE_PAGES: readonly SpeAdminPage[] = [
  "dashboard",
  "containers",
  "container-types",
  "file-browser",
  "search",
  "recycle-bin",
  "security",
  "audit-log",
  "settings",
];

/**
 * Narrows an arbitrary string to a `SpeAdminPage`.
 *
 * An unrecognised value falls back to null (→ Dashboard) rather than being cast through. A bad
 * `?page=` in a hand-edited URL is not worth a blank screen.
 */
function toSpeAdminPage(value: string | null | undefined): SpeAdminPage | null {
  if (!value) return null;
  const match = DEEP_LINKABLE_PAGES.find((p) => p === value);
  return match ?? null;
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

  /*
   * `page` / `containerId` are read from BOTH sources, top-level query string first.
   *
   * Dataverse launches the app with everything packed into the `data` param, but in-app deep links
   * (Search → Manage Permissions) build a plain `?page=…&containerId=…` on the current URL. Reading
   * only the `data` bag is precisely why those links did nothing.
   */
  let query: URLSearchParams;
  try {
    query = new URLSearchParams(window.location.search);
  } catch {
    query = new URLSearchParams();
  }

  return {
    configId: data["configId"] ?? null,
    buId: data["buId"] ?? null,
    page: toSpeAdminPage(query.get("page") ?? data["page"]),
    containerId: query.get("containerId") ?? data["containerId"] ?? null,
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

  /** Drag-to-resize state for the Container Types detail pane (UAT round 6). */
  const typeDetailPane = useResizablePane({ defaultHeight: 340, minHeight: 160 });

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
      {/*
       * Wraps the routed page only — never the shell. A render crash inside one page must not take
       * the nav rail and pickers down with it, because navigating away is the operator's escape
       * hatch. Before this boundary existed, any such crash unmounted the entire app and showed a
       * blank white page (UAT 2026-08-25, Audit Log).
       */}
      <PageErrorBoundary pageKey={activePage}>
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
      ) : activePage === "containers" ? (
        <ContainersPage
          onOpenContainer={handleOpenContainerInBrowser}
          // Deep link (e.g. Search → Manage Permissions) names the container to open.
          initialDetailContainerId={params.containerId}
        />
      ) : activePage === "container-types" ? (
        /*
         * Master-detail, stacked (UAT 2026-08-26). The detail pane used to be a fixed 440px
         * overlay on the right with a modal backdrop; it is now a docked bottom pane. This flex
         * column is what makes the list SHRINK when the pane opens instead of being covered by it
         * — the inner wrapper gives ContainerTypesPage a correctly-sized box for its `height: 100%`
         * to resolve against, and ContainerTypeDetail renders null when nothing is selected, so the
         * list gets the full height back on close.
         */
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            height: "100%",
            overflow: "hidden",
          }}
        >
          <div style={{ flex: "1 1 auto", minHeight: 0, overflow: "hidden" }}>
            <ContainerTypesPage onOpenDetail={setDetailContainerTypeId} />
          </div>
          {/* Splitter renders only alongside the pane it resizes. */}
          {detailContainerTypeId !== null && (
            <PaneSplitter
              label="container type details"
              height={typeDetailPane.height}
              onPointerDown={typeDetailPane.onPointerDown}
              onKeyDown={typeDetailPane.onKeyDown}
              isDragging={typeDetailPane.isDragging}
            />
          )}
          <ContainerTypeDetail
            containerTypeId={detailContainerTypeId}
            onClose={() => setDetailContainerTypeId(null)}
            paneHeight={typeDetailPane.height}
          />
        </div>
      ) : (
        // Placeholder for any remaining pages not yet implemented
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
      </PageErrorBoundary>
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

  // Active navigation page — seeded from `?page=` so a deep link lands where it says it will.
  const [activePage, setActivePage] = React.useState<SpeAdminPage>(
    params.page ?? "dashboard",
  );

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
