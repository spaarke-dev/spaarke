/**
 * wizardLaunchers.ts
 *
 * Shared Xrm.Navigation.navigateTo launchers for the seven Get Started wizards
 * used by both LegalWorkspace and SpaarkeAi. Hoisted in Round 4 Fix 2 (task 085)
 * to STOP the parallel-implementation bug — previously SpaarkeAi had its own
 * `launchCodePagePopup` helper (Round 3 task 068) and `launchAssignWorkWizard`
 * (task 045) and widget-load dispatchers (tasks 043/044) that subtly diverged
 * from the proven LegalWorkspace WorkspaceGrid.tsx call shape, causing the
 * popups not to render reliably from the SpaarkeAi Context pane.
 *
 * Source of truth (verbatim navigateTo shape):
 *   `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`
 *     - handleOpenWizard               → sprk_creatematterwizard
 *     - handleOpenProjectWizard        → sprk_createprojectwizard
 *     - handleOpenSummarize            → sprk_summarizefileswizard
 *     - handleOpenFindSimilar          → sprk_findsimilar
 *     - handleOpenWorkAssignmentWizard → sprk_createworkassignmentwizard
 *   `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCardHandlers.ts`
 *     - openPlaybookIntent             → sprk_playbooklibrary (intent=...)
 *
 * Each launcher matches LegalWorkspace's call shape exactly:
 *   - pageType:        "webresource"
 *   - target:          2 (modal dialog)
 *   - width / height:  60% / 70%
 *   - data:            "bffBaseUrl=<encoded>" (plus call-specific params)
 *   - title:           per-wizard label (e.g. "Create New Matter")
 *
 * ADR-012: shared library — both LegalWorkspace and SpaarkeAi consume these.
 * ADR-028: `bffBaseUrl` is a base URL only (NOT a token). The wizard Code Page
 *          authenticates via `@spaarke/auth` after it loads.
 * FR-25 / NFR-10: LegalWorkspace's WorkspaceGrid.tsx may continue using its own
 *          local handlers — this module is OPT-IN so the standalone LegalWorkspace
 *          path remains byte-identical to the pre-fix baseline.
 *
 * Frame-walking Xrm resolution:
 *   The previous SpaarkeAi `launchCodePagePopup` only checked `window.Xrm`.
 *   The widgets that worked (CreateProjectWizardWidget, FindSimilarWizardWidget)
 *   used a frame-walking resolver. We adopt the frame-walking resolver here so
 *   the launcher works regardless of which iframe layer is calling — Code Page
 *   nested under Power Apps host, PCF nested under model-driven form, or any
 *   future deeper nesting.
 *
 * @see projects/spaarke-ai-platform-unification-r3/tasks/085-wizard-launcher-reuse.poml
 */

// ---------------------------------------------------------------------------
// Internal: Xrm.Navigation feature detection (frame-walking)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Walks `window`, `window.parent`, `window.top` looking for an Xrm.Navigation
 * with `navigateTo`. Returns `null` in non-host environments (Vite dev, jsdom).
 *
 * This is the same resolver used by the widget-mount path that already worked
 * (CreateProjectWizardWidget, FindSimilarWizardWidget) — hoisted here so the
 * direct-click path uses the same resolver.
 */
function resolveXrmNavigation(): any | null {
  if (typeof window === "undefined") return null;

  const frames: Window[] = [window];
  try {
    if (window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin — skip */
  }
  try {
    if (window.top && window.top !== window) frames.push(window.top);
  } catch {
    /* cross-origin — skip */
  }

  for (const frame of frames) {
    try {
      const nav = (frame as any).Xrm?.Navigation;
      if (nav?.navigateTo) {
        return nav;
      }
    } catch {
      /* cross-origin — skip */
    }
  }
  return null;
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Common dialog options (matches LegalWorkspace WorkspaceGrid.tsx verbatim)
// ---------------------------------------------------------------------------

const DEFAULT_TARGET: 2 = 2;
const DEFAULT_WIDTH = { value: 60, unit: "%" as const };
const DEFAULT_HEIGHT = { value: 70, unit: "%" as const };

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Common option shape for all launchers. `bffBaseUrl` is REQUIRED — the wizard
 * Code Page needs it to resolve its own MSAL-authenticated BFF calls.
 */
export interface BaseLauncherOptions {
  /**
   * Spaarke BFF base URL (e.g. `https://spe-api-dev-67e2xz.azurewebsites.net`).
   * Sourced from the caller's runtime config (`getBffBaseUrl()` in
   * LegalWorkspace or SpaarkeAi's `src/config/runtimeConfig.ts`).
   *
   * Per ADR-028 this is a base URL only, NOT a token.
   */
  bffBaseUrl: string;
}

export interface SummarizeFilesLauncherOptions extends BaseLauncherOptions {
  /** Optional preselected document IDs to summarise. */
  documentIds?: string[];
}

export interface FindSimilarLauncherOptions extends BaseLauncherOptions {
  /** Optional preselected source document. */
  documentId?: string;
  /** Optional SharePoint Embedded container ID. */
  containerId?: string;
}

export interface PlaybookIntentLauncherOptions extends BaseLauncherOptions {
  /** Playbook Library intent identifier (e.g. `email-compose`, `meeting-schedule`). */
  intent: string;
  /** Optional dialog title override. Defaults to "Playbook Library". */
  title?: string;
}

// ---------------------------------------------------------------------------
// Internal helper — fire-and-forget navigateTo with swallowed cancel/error
// ---------------------------------------------------------------------------

interface NavigateToParams {
  webresourceName: string;
  data: string;
  title: string;
}

function fireNavigateTo({ webresourceName, data, title }: NavigateToParams): void {
  const nav = resolveXrmNavigation();
  if (nav === null) {
    return; // Non-host environment (Vite dev, jsdom) — silent no-op.
  }
  try {
    nav
      .navigateTo(
        {
          pageType: "webresource",
          webresourceName,
          data,
        },
        {
          target: DEFAULT_TARGET,
          width: DEFAULT_WIDTH,
          height: DEFAULT_HEIGHT,
          title,
        }
      )
      .catch(() => {
        // Intentional: user cancel / dialog error — ignore (matches
        // WorkspaceGrid.tsx's try/await/catch swallow precedent).
      });
  } catch {
    /* xrm getter threw — silent */
  }
}

// ---------------------------------------------------------------------------
// Public launchers — one per wizard
// ---------------------------------------------------------------------------

/**
 * Launch the Create Matter wizard (`sprk_creatematterwizard`).
 *
 * Source: WorkspaceGrid.tsx `handleOpenWizard` (lines 213–238).
 */
export function launchCreateMatterWizard(options: BaseLauncherOptions): void {
  fireNavigateTo({
    webresourceName: "sprk_creatematterwizard",
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: "Create New Matter",
  });
}

/**
 * Launch the Create Project wizard (`sprk_createprojectwizard`).
 *
 * Source: WorkspaceGrid.tsx `handleOpenProjectWizard` (lines 244–269).
 */
export function launchCreateProjectWizard(options: BaseLauncherOptions): void {
  fireNavigateTo({
    webresourceName: "sprk_createprojectwizard",
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: "Create New Project",
  });
}

/**
 * Launch the Summarize Files wizard (`sprk_summarizefileswizard`).
 *
 * Source: WorkspaceGrid.tsx `handleOpenSummarize` (lines 275–287).
 */
export function launchSummarizeFilesWizard(options: SummarizeFilesLauncherOptions): void {
  const bffParam = `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`;
  const data =
    options.documentIds && options.documentIds.length > 0
      ? `documentIds=${options.documentIds.join(",")}&${bffParam}`
      : bffParam;
  fireNavigateTo({
    webresourceName: "sprk_summarizefileswizard",
    data,
    title: "Summarize Files",
  });
}

/**
 * Launch the Find Similar Documents dialog (`sprk_findsimilar`).
 *
 * Source: WorkspaceGrid.tsx `handleOpenFindSimilar` (lines 293–304).
 *
 * NOTE: LegalWorkspace's original handler emits empty `documentId=` and
 * `containerId=` params when nothing is preselected. We replicate that shape
 * exactly so the Find Similar Code Page's data-parsing path matches the
 * proven LegalWorkspace baseline.
 */
export function launchFindSimilarWizard(options: FindSimilarLauncherOptions): void {
  const documentIdPart = options.documentId ?? "";
  const containerIdPart = options.containerId ?? "";
  const data = `documentId=${documentIdPart}&containerId=${containerIdPart}&bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`;
  fireNavigateTo({
    webresourceName: "sprk_findsimilar",
    data,
    title: "Find Similar Documents",
  });
}

/**
 * Launch the Create Work Assignment wizard (`sprk_createworkassignmentwizard`).
 *
 * Source: WorkspaceGrid.tsx `handleOpenWorkAssignmentWizard` (lines 342–351).
 *
 * Supersedes the package-local `launchAssignWorkWizard` from task 045 — that
 * helper is deleted in Round 4 Fix 2 because (a) its frame-detection only
 * looked at `window.Xrm`, missing nested iframe cases, and (b) keeping two
 * launchers is exactly the parallel-implementation issue this fix removes.
 */
export function launchAssignWorkWizard(options: BaseLauncherOptions): void {
  fireNavigateTo({
    webresourceName: "sprk_createworkassignmentwizard",
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: "Create Work Assignment",
  });
}

/**
 * Launch the Playbook Library with a specific intent
 * (`sprk_playbooklibrary?intent=<intent>`).
 *
 * Source: ActionCardHandlers.ts `openPlaybookIntent` (lines 77–94).
 *
 * Used for the `email-compose` and `meeting-schedule` Get Started cards.
 */
export function launchPlaybookIntent(options: PlaybookIntentLauncherOptions): void {
  const bffParam = `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`;
  const data = `intent=${options.intent}&${bffParam}`;
  fireNavigateTo({
    webresourceName: "sprk_playbooklibrary",
    data,
    title: options.title ?? "Playbook Library",
  });
}
