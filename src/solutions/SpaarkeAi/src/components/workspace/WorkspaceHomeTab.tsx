/**
 * WorkspaceHomeTab.tsx — Home tab content for the SpaarkeAi WorkspacePane.
 *
 * Fetches the user's default workspace layout from
 * `GET /api/workspace/layouts/default` (per-request auth via `authenticatedFetch`
 * from `@spaarke/auth` — ADR-028) and renders it via the shared `WorkspaceShell`
 * component (ADR-012) using the canonical `buildDynamicWorkspaceConfig` hoisted in
 * task 067.
 *
 * Section content rendering strategy (task 067):
 *   - SpaarkeAi consumes the canonical builder from `@spaarke/ui-components`.
 *   - A LOCAL placeholder registry supplies one registration per known section ID
 *     (get-started, quick-summary, latest-updates, todo, documents, daily-briefing,
 *     matters, projects). Each placeholder renders a labelled message describing
 *     the section. This preserves the structural integrity of the user's layout
 *     while keeping legal-domain components (QuickSummaryRow, ActivityFeed,
 *     SmartToDo, DocumentsTab, DailyBriefingSection) inside LegalWorkspace per
 *     ADR-012 ("MUST NOT hard-code Dataverse entity names ... in shared lib").
 *   - See `projects/spaarke-ai-platform-unification-r3/notes/drafts/067-factory-inventory.md`
 *     for the architectural decision.
 *
 * Standards:
 *   - ADR-012: WorkspaceShell + builder consumed from `@spaarke/ui-components` barrel
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
import {
  RocketRegular,
  DataBarVerticalRegular,
  ClockRegular,
  CheckmarkCircleRegular,
  DocumentRegular,
  SparkleRegular,
  BriefcaseRegular,
  FolderRegular,
} from "@fluentui/react-icons";
import { authenticatedFetch, buildBffApiUrl } from "@spaarke/auth";
import {
  WorkspaceShell,
  buildDynamicWorkspaceConfig,
  SYSTEM_DEFAULT_LAYOUT_JSON,
} from "@spaarke/ui-components";
import type {
  WorkspaceConfig,
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
  LayoutJson,
} from "@spaarke/ui-components";
import { getBffBaseUrl } from "../../config/runtimeConfig";

// ---------------------------------------------------------------------------
// BFF DTO shape (mirror LegalWorkspace conventions)
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

// ---------------------------------------------------------------------------
// Section id → friendly label + icon map (foundational placeholders).
//
// These match the standalone LegalWorkspace section identities so the
// rendered structure is consistent across both surfaces. Section content
// bodies are placeholders — the legal-domain section components remain
// in LegalWorkspace (see notes/drafts/067-factory-inventory.md).
// ---------------------------------------------------------------------------

interface IPlaceholderSectionMeta {
  label: string;
  description: string;
  icon: SectionRegistration["icon"];
  defaultHeight: string;
  category: SectionRegistration["category"];
}

const PLACEHOLDER_SECTION_META: Record<string, IPlaceholderSectionMeta> = {
  "get-started": {
    label: "Get Started",
    description: "Quick-action cards for common workflows",
    icon: RocketRegular,
    defaultHeight: "200px",
    category: "overview",
  },
  "quick-summary": {
    label: "Quick Summary",
    description: "Key metrics at a glance",
    icon: DataBarVerticalRegular,
    defaultHeight: "180px",
    category: "overview",
  },
  "latest-updates": {
    label: "Latest Updates",
    description: "Recent activity feed with flagging",
    icon: ClockRegular,
    defaultHeight: "325px",
    category: "data",
  },
  todo: {
    label: "My To Do List",
    description: "Embedded smart to-do list",
    icon: CheckmarkCircleRegular,
    defaultHeight: "560px",
    category: "productivity",
  },
  documents: {
    label: "My Documents",
    description: "Recent documents with quick actions",
    icon: DocumentRegular,
    defaultHeight: "560px",
    category: "data",
  },
  "daily-briefing": {
    label: "Daily Briefing",
    description: "AI-curated highlights from your day",
    icon: SparkleRegular,
    defaultHeight: "325px",
    category: "ai",
  },
  matters: {
    label: "My Matters",
    description: "Active legal matters",
    icon: BriefcaseRegular,
    defaultHeight: "325px",
    category: "data",
  },
  projects: {
    label: "My Projects",
    description: "Active projects",
    icon: FolderRegular,
    defaultHeight: "325px",
    category: "data",
  },
};

/** Build a placeholder SectionRegistration for an unrecognised section ID. */
function buildFallbackMeta(id: string): IPlaceholderSectionMeta {
  const label = id
    .split("-")
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join(" ");
  return {
    label,
    description: `Section "${label}"`,
    icon: DocumentRegular,
    defaultHeight: "200px",
    category: "data",
  };
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
  return SYSTEM_DEFAULT_LAYOUT_JSON;
}

// ---------------------------------------------------------------------------
// Build a placeholder registry covering every section ID referenced in the
// layout JSON. Each registration produces a ContentSectionConfig whose body
// is a labelled placeholder paragraph (matching the pre-067 visual contract).
// ---------------------------------------------------------------------------

function buildPlaceholderRegistry(
  layoutJson: LayoutJson,
  paddedClassName: string,
): SectionRegistration[] {
  const seenIds = new Set<string>();
  const registry: SectionRegistration[] = [];

  for (const row of layoutJson.rows) {
    for (const sectionId of row.sections) {
      if (seenIds.has(sectionId)) continue;
      seenIds.add(sectionId);

      const meta = PLACEHOLDER_SECTION_META[sectionId] ?? buildFallbackMeta(sectionId);

      const registration: SectionRegistration = {
        id: sectionId,
        label: meta.label,
        description: meta.description,
        icon: meta.icon,
        category: meta.category,
        defaultHeight: meta.defaultHeight,
        factory: (_ctx: SectionFactoryContext): ContentSectionConfig => ({
          id: sectionId,
          type: "content",
          title: meta.label,
          style: {},
          renderContent: () => (
            <div className={paddedClassName} data-testid={`home-section-${sectionId}`}>
              <Text size={200}>
                Section content for &ldquo;{meta.label}&rdquo; will render here.
              </Text>
            </div>
          ),
        }),
      };

      registry.push(registration);
    }
  }

  return registry;
}

// ---------------------------------------------------------------------------
// Build a SectionFactoryContext for SpaarkeAi.
//
// SpaarkeAi does not currently expose Xrm.WebApi / DataverseService to the
// workspace embed; the placeholder factories never call into context.webApi
// or context.service, so we supply safe no-op stubs. If a future task hoists
// legal-domain section components into shared lib, this context will need to
// be enriched with real platform handles.
// ---------------------------------------------------------------------------

function buildSpaarkeAiContext(bffBaseUrl: string): SectionFactoryContext {
  return {
    webApi: undefined as unknown,
    userId: "",
    service: undefined as unknown,
    bffBaseUrl,
    onNavigate: () => {
      /* SpaarkeAi navigation handled by host shell; placeholders never navigate */
    },
    onOpenWizard: () => {
      /* Placeholders never open wizards */
    },
    onBadgeCountChange: () => {
      /* Placeholders never report counts */
    },
    onRefetchReady: () => {
      /* Placeholders are static */
    },
    onExpandSection: undefined,
    onOpenDocumentsDialog: undefined,
    scope: "my",
    businessUnitId: undefined,
  };
}

// ---------------------------------------------------------------------------
// WorkspaceHomeTab component
// ---------------------------------------------------------------------------

/**
 * WorkspaceHomeTab — content for the WorkspacePane Home tab.
 *
 * Fetches the user's default workspace layout from the BFF and renders it
 * via the shared `WorkspaceShell` using the canonical `buildDynamicWorkspaceConfig`.
 * Falls back to the system-default layout (matching the standalone LegalWorkspace)
 * when the BFF is unreachable.
 *
 * Per ADR-028 the layout fetch is performed via `authenticatedFetch` from
 * `@spaarke/auth`; no `accessToken` is propagated as a prop or held in state.
 */
export const WorkspaceHomeTab: React.FC = () => {
  const styles = useStyles();

  const [layoutJson, setLayoutJson] = React.useState<LayoutJson | null>(null);
  const [bffBaseUrl, setBffBaseUrl] = React.useState<string>("");
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

      let resolvedBffBaseUrl: string;
      try {
        resolvedBffBaseUrl = getBffBaseUrl();
      } catch {
        // Runtime config not initialized — render fallback layout silently.
        if (!cancelled) {
          setLayoutJson(SYSTEM_DEFAULT_LAYOUT_JSON);
          setBffBaseUrl("");
          setIsLoading(false);
        }
        return;
      }

      try {
        const url = buildBffApiUrl(resolvedBffBaseUrl, "/workspace/layouts/default");
        const response = await authenticatedFetch(url);

        if (cancelled) return;

        if (!response.ok) {
          // Non-OK response: fall back to system default. We do not surface
          // an error UI here because the fallback is visually equivalent and
          // task 035 / FR-24 owns the error-telemetry path.
          console.warn(
            `[WorkspaceHomeTab] Default layout fetch returned ${response.status}; using fallback layout`,
          );
          setLayoutJson(SYSTEM_DEFAULT_LAYOUT_JSON);
          setBffBaseUrl(resolvedBffBaseUrl);
          setIsLoading(false);
          return;
        }

        const dto = (await response.json()) as WorkspaceLayoutDto;
        if (cancelled) return;

        setLayoutJson(parseLayoutJson(dto.sectionsJson));
        setBffBaseUrl(resolvedBffBaseUrl);
        setIsLoading(false);
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : "Unknown error";
        console.warn("[WorkspaceHomeTab] Layout fetch failed, using fallback:", message);
        setErrorMessage(message);
        setLayoutJson(SYSTEM_DEFAULT_LAYOUT_JSON);
        setBffBaseUrl(resolvedBffBaseUrl);
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
  // Build placeholder registry, factory context, and the WorkspaceConfig via
  // the canonical builder (hoisted in task 067).
  // -------------------------------------------------------------------------

  const placeholderRegistry = buildPlaceholderRegistry(layoutJson, styles.placeholderBody);
  const factoryContext = buildSpaarkeAiContext(bffBaseUrl);
  const config: WorkspaceConfig = buildDynamicWorkspaceConfig(
    layoutJson,
    placeholderRegistry,
    factoryContext,
  );

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
