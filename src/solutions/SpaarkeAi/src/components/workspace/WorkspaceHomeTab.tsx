/**
 * WorkspaceHomeTab.tsx — Home tab content for the SpaarkeAi WorkspacePane.
 *
 * Fetches the user's default workspace layout from
 * `GET /api/workspace/layouts/default` (per-request auth via `authenticatedFetch`
 * from `@spaarke/auth` — ADR-028) and renders it via the shared `WorkspaceShell`
 * component (ADR-012). This component is wired in as the Home tab's resolved
 * React Component by `WorkspacePane.tsx` via `WorkspaceTabManager.ensureHomeTab()`.
 *
 * Foundational scope (task 030):
 *   - PaneHeader rendered at top is the responsibility of WorkspacePane.tsx
 *   - This component only renders the WorkspaceShell content for the Home tab.
 *   - Section bodies are rendered as placeholder content sections that describe
 *     the section id. Wave 2b / 2c tasks (034, 040, 041, 043) wire the actual
 *     section factories (Daily Briefing, Get Started, etc.) once the
 *     `SectionRegistration` factories from LegalWorkspace are accessible via
 *     a shared section registry. Until then, the Home tab faithfully reflects
 *     the user's layout STRUCTURE while leaving content body wiring for later.
 *
 * Why a thin foundational embed:
 *   - The full LegalWorkspace `WorkspaceGrid` consumes `SECTION_REGISTRY` and
 *     `buildDynamicWorkspaceConfig` from `src/solutions/LegalWorkspace/...`.
 *     SpaarkeAi does not depend on LegalWorkspace and lifting the section
 *     registry to the shared library is out of scope for this task (it would
 *     require touching many factories that the standalone LegalWorkspace owns —
 *     violating NFR-10 if not done carefully).
 *   - The foundational pattern landed here (fetch → parse → render via
 *     WorkspaceShell) is the correct shape; subsequent tasks replace the
 *     placeholder section body with real factories.
 *
 * Standards:
 *   - ADR-012: WorkspaceShell consumed from `@spaarke/ui-components` (no deep imports)
 *   - ADR-021: Fluent v9 tokens only (no hex / rgba)
 *   - ADR-022: React 19 functional component
 *   - ADR-028: All BFF calls via `authenticatedFetch`; no `accessToken` snapshots
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from "@fluentui/react-components";
import { authenticatedFetch, buildBffApiUrl } from "@spaarke/auth";
import { WorkspaceShell } from "@spaarke/ui-components";
import type {
  WorkspaceConfig,
  WorkspaceRowConfig,
  SectionConfig,
} from "@spaarke/ui-components";
import { getBffBaseUrl } from "../../config/runtimeConfig";

// ---------------------------------------------------------------------------
// BFF DTO + layout JSON shape (mirror LegalWorkspace conventions)
// ---------------------------------------------------------------------------

/** Client-side mirror of the BFF `WorkspaceLayoutDto` shape. */
interface WorkspaceLayoutDto {
  id: string;
  name: string;
  layoutTemplateId: string;
  /** Stringified `LayoutJson`. */
  sectionsJson: string;
  isDefault: boolean;
  sortOrder: number | null;
  isSystem: boolean;
}

/** A single row in the persisted layout JSON. */
interface LayoutJsonRow {
  id: string;
  columns: string;
  columnsSmall?: string;
  sections: string[];
}

/** Top-level layout JSON persisted in Dataverse `sprk_sectionsjson`. */
interface LayoutJson {
  schemaVersion: number;
  rows: LayoutJsonRow[];
}

/**
 * Minimal system-default layout used when the BFF is unreachable or returns no
 * default layout. Matches the shape used by the standalone LegalWorkspace shell
 * so visual parity is preserved at narrower widths.
 */
const FALLBACK_LAYOUT_JSON: LayoutJson = {
  schemaVersion: 1,
  rows: [
    { id: "row-1", columns: "1fr 1fr", sections: ["get-started", "quick-summary"] },
    { id: "row-2", columns: "1fr", sections: ["latest-updates"] },
    { id: "row-3", columns: "1fr 1fr", sections: ["todo", "documents"] },
  ],
};

// ---------------------------------------------------------------------------
// Section id → friendly label map (foundational placeholders)
// ---------------------------------------------------------------------------

const SECTION_LABELS: Record<string, string> = {
  "get-started": "Get Started",
  "quick-summary": "Quick Summary",
  "latest-updates": "Latest Updates",
  todo: "My To Do List",
  documents: "My Documents",
  matters: "My Matters",
  projects: "My Projects",
  "daily-briefing": "Daily Briefing",
};

/** Friendly label for a section id (capitalized fallback). */
function labelForSection(id: string): string {
  if (id in SECTION_LABELS) return SECTION_LABELS[id];
  return id
    .split("-")
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join(" ");
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    flex: 1,
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground2,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  loading: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    padding: tokens.spacingVerticalXXL,
  },
  errorBar: {
    marginBottom: tokens.spacingVerticalM,
  },
  placeholderBody: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Layout JSON parsing
// ---------------------------------------------------------------------------

function parseLayoutJson(sectionsJson: string): LayoutJson {
  try {
    const parsed = JSON.parse(sectionsJson) as LayoutJson;
    if (parsed && typeof parsed.schemaVersion === "number" && Array.isArray(parsed.rows)) {
      return parsed;
    }
  } catch (err) {
    console.warn("[WorkspaceHomeTab] Failed to parse sectionsJson:", err);
  }
  return FALLBACK_LAYOUT_JSON;
}

// ---------------------------------------------------------------------------
// Build a foundational WorkspaceConfig from a LayoutJson
//
// Foundational behaviour: each section in the layout JSON becomes a
// placeholder `content` section that names the section id. Later tasks
// replace this with the real section factory output (Daily Briefing,
// Get Started cards, Latest Updates feed, To Do kanban, Documents grid).
// ---------------------------------------------------------------------------

function buildFoundationalConfig(
  layout: LayoutJson,
  paddedClassName: string,
): WorkspaceConfig {
  const sections: SectionConfig[] = [];
  const seenSectionIds = new Set<string>();

  for (const row of layout.rows) {
    for (const sectionId of row.sections) {
      if (seenSectionIds.has(sectionId)) continue;
      seenSectionIds.add(sectionId);
      sections.push({
        id: sectionId,
        type: "content",
        title: labelForSection(sectionId),
        renderContent: () => (
          <div className={paddedClassName} data-testid={`home-section-${sectionId}`}>
            <Text size={200}>
              Section content for &ldquo;{labelForSection(sectionId)}&rdquo; will render here.
            </Text>
          </div>
        ),
      });
    }
  }

  const rows: WorkspaceRowConfig[] = layout.rows.map((r) => ({
    id: r.id,
    sectionIds: r.sections,
    gridTemplateColumns: r.columns,
    gridTemplateColumnsSmall: r.columnsSmall,
  }));

  return {
    layout: "rows",
    rows,
    sections,
  };
}

// ---------------------------------------------------------------------------
// WorkspaceHomeTab component
// ---------------------------------------------------------------------------

/**
 * WorkspaceHomeTab — content for the WorkspacePane Home tab.
 *
 * Fetches the user's default workspace layout from the BFF and renders it
 * via the shared `WorkspaceShell`. Falls back to the system-default layout
 * (matching the standalone LegalWorkspace) when the BFF is unreachable.
 *
 * Per ADR-028 the layout fetch is performed via `authenticatedFetch` from
 * `@spaarke/auth`; no `accessToken` is propagated as a prop or held in state.
 */
export const WorkspaceHomeTab: React.FC = () => {
  const styles = useStyles();

  const [layoutJson, setLayoutJson] = React.useState<LayoutJson | null>(null);
  const [isLoading, setIsLoading] = React.useState(true);
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

  // -------------------------------------------------------------------------
  // Fetch default layout via authenticatedFetch (ADR-028)
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    let cancelled = false;

    (async () => {
      setIsLoading(true);
      setErrorMessage(null);

      let bffBaseUrl: string;
      try {
        bffBaseUrl = getBffBaseUrl();
      } catch {
        // Runtime config not initialized — render fallback layout silently.
        if (!cancelled) {
          setLayoutJson(FALLBACK_LAYOUT_JSON);
          setIsLoading(false);
        }
        return;
      }

      try {
        const url = buildBffApiUrl(bffBaseUrl, "/workspace/layouts/default");
        const response = await authenticatedFetch(url);

        if (cancelled) return;

        if (!response.ok) {
          // Non-OK response: fall back to system default. We do not surface
          // an error UI here because the fallback is visually equivalent and
          // task 035 / FR-24 owns the error-telemetry path.
          console.warn(
            `[WorkspaceHomeTab] Default layout fetch returned ${response.status}; using fallback layout`,
          );
          setLayoutJson(FALLBACK_LAYOUT_JSON);
          setIsLoading(false);
          return;
        }

        const dto = (await response.json()) as WorkspaceLayoutDto;
        if (cancelled) return;

        setLayoutJson(parseLayoutJson(dto.sectionsJson));
        setIsLoading(false);
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : "Unknown error";
        console.warn("[WorkspaceHomeTab] Layout fetch failed, using fallback:", message);
        setErrorMessage(message);
        setLayoutJson(FALLBACK_LAYOUT_JSON);
        setIsLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  // -------------------------------------------------------------------------
  // Loading state
  // -------------------------------------------------------------------------

  if (isLoading || layoutJson === null) {
    return (
      <div className={styles.loading} data-testid="home-tab-loading">
        <Spinner size="medium" label="Loading workspace..." />
      </div>
    );
  }

  // -------------------------------------------------------------------------
  // Render WorkspaceShell with foundational config
  // -------------------------------------------------------------------------

  const config = buildFoundationalConfig(layoutJson, styles.placeholderBody);

  return (
    <div className={styles.root} data-testid="home-tab-root">
      {errorMessage ? (
        <MessageBar intent="warning" className={styles.errorBar}>
          <MessageBarBody>
            <MessageBarTitle>Couldn&rsquo;t load your workspace</MessageBarTitle>
            Showing the default layout. {errorMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}
      <WorkspaceShell config={config} />
    </div>
  );
};

WorkspaceHomeTab.displayName = "WorkspaceHomeTab";
