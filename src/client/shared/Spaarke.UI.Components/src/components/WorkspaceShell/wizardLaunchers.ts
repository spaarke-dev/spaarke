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
// OOB size scale (spaarke-modal-system P7 task 090 — FR-11/FR-18). ONE source
// of truth for Xrm.Navigation.navigateTo dialog dimensions — see
// utils/adapters/oobModalSizes.ts. Do NOT reintroduce inline width/height
// percentage literals in this file.
// ---------------------------------------------------------------------------

import { OOB_MODAL_SIZES } from '../../utils/adapters/oobModalSizes';

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
export function resolveXrmNavigation(): any | null {
  if (typeof window === 'undefined') return null;

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

const DEFAULT_TARGET = 2 as const;
// `wizard` OOB size (60% × 70%) — sourced from oobModalSizes.ts, NOT an inline
// literal (spec FR-11/FR-18). Matches the seven Get-Started wizards below.
const DEFAULT_WIDTH = OOB_MODAL_SIZES.wizard.width;
const DEFAULT_HEIGHT = OOB_MODAL_SIZES.wizard.height;

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
          pageType: 'webresource',
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
    webresourceName: 'sprk_creatematterwizard',
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: 'Create New Matter',
  });
}

/**
 * Launch the Create Event wizard (`sprk_createeventwizard`).
 *
 * Added by spaarkeai-assistant-enhancements-r1 task 012 — this was the one
 * Get-Started surface `wizardLaunchers.ts` did NOT hoist (matter / project /
 * summarize / find-similar / work-assignment / playbook existed; event did not,
 * per surface-launch-mechanism §3 build-verify #2). The shared `CreateEventWizard`
 * component + `src/solutions/CreateEventWizard` Code Page (web resource
 * `sprk_createeventwizard`, confirmed against its `main.tsx`) already exist; this
 * is the missing direct-click launcher, matching the sibling call shape exactly.
 *
 * The `create-task` assistant hand-off routes here with a Task-subtype preset
 * carried in the envelope (see `surfaceLaunchRegistry.ts`), NOT on the URL.
 */
export function launchCreateEventWizard(options: BaseLauncherOptions): void {
  fireNavigateTo({
    webresourceName: 'sprk_createeventwizard',
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: 'Create New Event',
  });
}

/**
 * Launch the Create Project wizard (`sprk_createprojectwizard`).
 *
 * Source: WorkspaceGrid.tsx `handleOpenProjectWizard` (lines 244–269).
 */
export function launchCreateProjectWizard(options: BaseLauncherOptions): void {
  fireNavigateTo({
    webresourceName: 'sprk_createprojectwizard',
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: 'Create New Project',
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
      ? `documentIds=${options.documentIds.join(',')}&${bffParam}`
      : bffParam;
  fireNavigateTo({
    webresourceName: 'sprk_summarizefileswizard',
    data,
    title: 'Summarize Files',
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
  const documentIdPart = options.documentId ?? '';
  const containerIdPart = options.containerId ?? '';
  const data = `documentId=${documentIdPart}&containerId=${containerIdPart}&bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`;
  fireNavigateTo({
    webresourceName: 'sprk_findsimilar',
    data,
    title: 'Find Similar Documents',
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
    webresourceName: 'sprk_createworkassignmentwizard',
    data: `bffBaseUrl=${encodeURIComponent(options.bffBaseUrl)}`,
    title: 'Create Work Assignment',
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
    webresourceName: 'sprk_playbooklibrary',
    data,
    title: options.title ?? 'Playbook Library',
  });
}

// ---------------------------------------------------------------------------
// Promise-returning navigate primitives (task 012 — the hand-off return path)
// ---------------------------------------------------------------------------
//
// The fire-and-forget `fireNavigateTo` above swallows the `navigateTo` promise
// (the shipped Get-Started launchers don't need an outcome). The Assistant →
// surface hand-off (task 012) DOES need it: the `navigateTo` promise resolving
// is the "surface is done" signal (design §1 step 8 / build-verify #2), at which
// point the Assistant reads the outcome the surface wrote to sessionStorage.
// These primitives expose that promise. They REUSE the same frame-walking
// `resolveXrmNavigation` + dialog options as the fire-and-forget path — no
// parallel launch mechanism (CLAUDE.md §11).

/** Common shape of a `navigateTo` result the hand-off cares about. */
export interface NavigateToOutcome {
  /**
   * `false` when no Xrm host was reachable (Vite dev / jsdom) — the caller
   * degrades to "draft shown, nothing opened" rather than treating it as a
   * committed launch. `true` once `navigateTo` was actually invoked.
   */
  readonly launched: boolean;
  /**
   * `true` when the modal was cancelled/dismissed (the `navigateTo` promise
   * rejected). Never carries the raw rejection detail (ADR-019).
   */
  readonly cancelled?: boolean;
  /**
   * The saved-entity reference returned by an `entityrecord` navigation when a
   * record was created/saved (OOB-form return path). Absent for web resources
   * (those return their outcome via the sessionStorage result envelope instead).
   */
  readonly savedEntityReference?: { readonly id: string; readonly entityType?: string; readonly name?: string };
}

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Launch a web-resource surface (a Create*Wizard Code Page) and RESOLVE when the
 * modal closes. Used by the hand-off orchestrator so it can read the surface's
 * outcome from sessionStorage after close. `data` carries the hand-off id +
 * bffBaseUrl (the payload rides sessionStorage, not the URL — design §2).
 */
export async function navigateToWebResourceSurfaceAsync(params: NavigateToParams): Promise<NavigateToOutcome> {
  const nav = resolveXrmNavigation();
  if (nav === null) {
    return { launched: false };
  }
  try {
    await nav.navigateTo(
      { pageType: 'webresource', webresourceName: params.webresourceName, data: params.data },
      { target: DEFAULT_TARGET, width: DEFAULT_WIDTH, height: DEFAULT_HEIGHT, title: params.title }
    );
    return { launched: true };
  } catch {
    // User cancel / dialog error — the outcome (if any) is in sessionStorage.
    return { launched: true, cancelled: true };
  }
}

/**
 * Options for launching an OOB entity record surface in a modal (design §3 /
 * MODAL-DECISION-CRITERIA — the To Do route).
 *
 * Consolidated by smart-todo-r5 task 031 (spec FR-11 — "one code path for
 * create + open"): this is now the SOLE OOB `sprk_todo` main-form launcher,
 * serving BOTH the CREATE surface (`entityId` absent, task 030) and the OPEN
 * surface (`entityId` present, task 031). Only sizing/position/outcome-shape
 * differ per branch — see `navigateToEntityRecordSurfaceAsync` below.
 */
export interface EntityRecordSurfaceParams {
  /** Entity logical name (e.g. `sprk_todo`). */
  entityName: string;
  /**
   * Existing record ID to OPEN (present = open at the `record` OOB size,
   * 85%×85%, centered — task 031 / FR-11). Absent = CREATE a new record at
   * the `createForm` OOB size (70%×80% — task 030 / FR-10). Braces are
   * stripped internally (`{...}` registry-format GUIDs are accepted).
   */
  entityId?: string;
  /**
   * Dialog title. Optional — when omitted, no `title` navOption is sent
   * (matches the pre-consolidation OPEN call sites, which never set a title;
   * the CREATE call site always supplies one).
   */
  title?: string;
  /**
   * Default field values to pre-populate the create form (thinner OOB pre-seed —
   * OOB forms accept default-value params only, design §3). Attribute-logical-name
   * keyed. Optional. Only meaningful on the CREATE branch (no current caller
   * combines this with `entityId`).
   */
  defaultValues?: Record<string, unknown>;
}

/**
 * Launch an OOB entity record surface (`pageType:'entityrecord'`) in a modal —
 * CREATE mode when `params.entityId` is absent (`createForm` OOB size,
 * 70%×80%, no position override), OPEN-EXISTING mode when present (`record`
 * OOB size, 85%×85%, `position: 1` centered — per
 * `.claude/patterns/ui/record-modal-selection.md` Layout 1). REUSES the same
 * frame-walking resolver + modal-option shape for both branches — the ONE
 * `sprk_todo` OOB main-form launcher (task 031, spec FR-11).
 *
 * Outcome-shape parity (task 031 step 2 research — Microsoft Learn
 * `Xrm.Navigation.navigateTo` reference, "Return value" section, verified
 * 2026-08-16): "An object is passed only if the `pageType` = `entityRecord`
 * and you opened the form in CREATE mode." An existing-record OPEN always
 * resolves with NO result object on dialog close, whether the user saved or
 * cancelled — there is no `savedEntityReference` to inspect and no reliable
 * way to distinguish save-vs-cancel from the resolve value alone. So the two
 * branches populate `NavigateToOutcome` differently:
 *   - CREATE: `savedEntityReference` present on save; `cancelled: true` when
 *     the resolve carries no reference (unchanged from pre-031 behavior).
 *   - OPEN: a successful resolve (dialog closed, no error) returns a plain
 *     `{ launched: true }` — no `cancelled` flag, no `savedEntityReference`.
 *     Callers that need a post-open refresh signal (task 033) MUST refresh
 *     unconditionally on `launched: true` rather than gating on `cancelled`.
 *   - Both branches: a rejected/erroring promise still returns
 *     `{ launched: true, cancelled: true }` (dialog error, not a normal
 *     save/cancel close).
 */
export async function navigateToEntityRecordSurfaceAsync(
  params: EntityRecordSurfaceParams
): Promise<NavigateToOutcome> {
  const nav = resolveXrmNavigation();
  if (nav === null) {
    return { launched: false };
  }
  const isOpenExisting = typeof params.entityId === 'string' && params.entityId.length > 0;
  const pageInput: Record<string, unknown> = {
    pageType: 'entityrecord',
    entityName: params.entityName,
  };
  if (isOpenExisting) {
    // Strip braces per the pre-031 SmartTodoApp.tsx convention — registry
    // format `{...}` GUIDs are accepted by callers, `entityId` wants bare.
    pageInput.entityId = (params.entityId as string).replace(/[{}]/g, '');
  }
  // OOB pre-seed: `data` on an entityrecord pageInput pre-populates form
  // fields with default values (the documented createFromEntity/default-value seam).
  if (params.defaultValues && Object.keys(params.defaultValues).length > 0) {
    pageInput.data = params.defaultValues;
  }
  const navOptions: Record<string, unknown> = isOpenExisting
    ? {
        // `record` OOB size (85% × 85%) + centered position — Layout 1
        // (record-modal-selection.md), task 031 / spec FR-11.
        target: DEFAULT_TARGET,
        position: 1,
        width: OOB_MODAL_SIZES.record.width,
        height: OOB_MODAL_SIZES.record.height,
      }
    : {
        // `createForm` OOB size (70% × 80%) — this launches an OOB entity
        // CREATE form (`pageType: 'entityrecord'`, no existing entityId), the
        // exact scenario `createForm` was named for (spec FR-11), NOT the
        // `wizard` (60% × 70%) webresource size the rest of this module uses.
        // Verified zero existing callers depend on the prior 60%×70% fallback
        // (task 090 repo-wide grep) — see notes/task-090-completion.md.
        target: DEFAULT_TARGET,
        width: OOB_MODAL_SIZES.createForm.width,
        height: OOB_MODAL_SIZES.createForm.height,
      };
  if (params.title !== undefined) {
    navOptions.title = params.title;
  }
  try {
    const result: any = await nav.navigateTo(pageInput, navOptions);
    if (isOpenExisting) {
      // See outcome-shape-parity note above — an existing-record open never
      // carries savedEntityReference; a clean resolve just means "closed".
      return { launched: true };
    }
    // entityrecord CREATE resolves with `{ savedEntityReference: [{ id, entityType, name }] }`
    // when the user saved; undefined/empty when they cancelled.
    const ref = Array.isArray(result?.savedEntityReference) ? result.savedEntityReference[0] : undefined;
    if (ref?.id) {
      return {
        launched: true,
        savedEntityReference: { id: String(ref.id).replace(/[{}]/g, ''), entityType: ref.entityType, name: ref.name },
      };
    }
    return { launched: true, cancelled: true };
  } catch {
    return { launched: true, cancelled: true };
  }
}

/* eslint-enable @typescript-eslint/no-explicit-any */
