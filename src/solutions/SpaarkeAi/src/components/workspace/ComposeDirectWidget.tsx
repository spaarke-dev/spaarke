/**
 * ComposeDirectWidget.tsx — WorkspaceWidgetProps → ComposeWorkspace adapter for
 * the `'compose'` DIRECT widget (spaarkeai-compose-r2 Wave 5).
 *
 * A Direct workspace widget factory receives only `WorkspaceWidgetProps`
 * (`{ data, widgetType, ... }`). `ComposeWorkspace`, however, needs a richer
 * prop set (`bffBaseUrl`, `tenantId`, `driveId`, `containerId`,
 * `initialDocumentRef`/`initialUploadRef`/`initialDraftRef`,
 * `enqueueComposeAction`) that the LegalWorkspace section shim
 * (`composeEditor.registration.ts`) supplies from `SectionFactoryContext` +
 * `useComposeLaunch()`. This adapter reproduces that mapping for the Direct
 * path: it builds a per-tab `ComposeLaunchContext` from `data.compose` (the
 * SAME seed translation `main.tsx`'s `SpaarkeAiWorkspaceRenderer` performs for
 * the layout path) and sources `bffBaseUrl` from `useAiSession()` (there is no
 * `SectionFactoryContext` in the Direct path).
 *
 * NOTE (spaarkeai-compose-r2 UNIFY — this IS now a live mount door): the earlier
 * "today NO open-path dispatches `widget_load{ widgetType:'compose' }`" note is
 * NO LONGER TRUE. `WorkspacePane`'s compose-launch auto-install (ribbon
 * `composeMode=editor`), `ConversationPane`'s `mountActiveSourceDocInCompose` +
 * `handleDocAction`, and the compose layout-reroute all dispatch a
 * `widget_load{ widgetType:'compose' }` that mounts THIS adapter. It coexists
 * with the LegalWorkspace layout mount (does NOT replace it).
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9), ADR-028
 * (auth via `@spaarke/auth`; no token props). No AI-internal types, no new
 * endpoint (ADR-013/039).
 */

import * as React from "react";
import { makeStyles } from "@fluentui/react-components";
import {
  ComposeWorkspace,
  ComposeLaunchContext,
  useComposeLaunch,
  useComposeActionBridge,
  useComposeToolbarActivation,
} from "@spaarke/compose-components";
import type { ComposeLaunchContextValue } from "@spaarke/compose-components";
import { useAiSession } from "@spaarke/ai-widgets";
import type { WorkspaceWidgetProps } from "@spaarke/ai-widgets";
import { resolveTenantIdSync } from "@spaarke/auth";
import { EntityCreationService, cleanGuid } from "@spaarke/ui-components";
import type { ComposeWidgetData, ComposeWidgetSeed } from "./composeWidgetData";

// ---------------------------------------------------------------------------
// FIX #6 (spaarkeai-compose-r2) — bounded height host for the DIRECT mount.
// ---------------------------------------------------------------------------
// The Compose toolbar-pin fix previously lived in the LegalWorkspace layout-door
// section shim, which the Direct-widget flip BYPASSES. This host div re-establishes
// the bounded flex-column chain at the Direct layer: the WorkspacePane tab content
// host is already bounded (WorkspaceTabManagerComponent `content` + `widgetWrapper`
// height:100%), so this fills it (`height:100%`) as a `minHeight:0` flex column.
// That gives ComposeWorkspace.root (height:100% + minHeight:0) a definite-height flex
// parent, so its `editorSlot` (flex:1; minHeight:0) resolves and ComposeEditor's
// `editorSurface` (flex:1; overflow:auto) becomes THE scroll region — keeping the
// sticky ComposeFormatToolbar pinned instead of scrolling away with an outer container.
const useStyles = makeStyles({
  host: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    minHeight: 0,
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// Seed → ComposeLaunchContext translation (mirror of main.tsx SpaarkeAiWorkspaceRenderer)
// ---------------------------------------------------------------------------

/**
 * Translate a tab's `widgetData.compose` seed into a `ComposeLaunchContextValue`.
 * The three door shapes are mutually exclusive and checked in the same order as
 * the layout renderer: draft → upload → stored document. Returns `null` when the
 * seed carries no mountable identity (the widget then inherits any ambient
 * launch context, or renders the Compose empty state).
 */
export function buildLaunchFromSeed(
  seed: ComposeWidgetSeed | undefined
): ComposeLaunchContextValue | null {
  if (!seed) return null;

  // DEF-08 AI-drafted full document (Part A ledgerRef+sessionId, or Part B html).
  if (
    seed.draft &&
    ((typeof seed.draft.ledgerRef === "string" &&
      seed.draft.ledgerRef.length > 0 &&
      typeof seed.draft.sessionId === "string" &&
      seed.draft.sessionId.length > 0) ||
      (typeof seed.draft.html === "string" && seed.draft.html.length > 0))
  ) {
    return {
      composeMode: "editor",
      document: null,
      driveId: "",
      draft: {
        ledgerRef: seed.draft.ledgerRef,
        sessionId: seed.draft.sessionId,
        html: seed.draft.html,
        fileName: seed.draft.fileName ?? undefined,
      },
      activeWorkType: seed.activeWorkType,
    };
  }

  // FR-03 transient Assistant-upload (no SPE pointer; create-on-save).
  if (
    seed.upload &&
    typeof seed.upload.sessionId === "string" &&
    seed.upload.sessionId.length > 0 &&
    typeof seed.upload.sessionFileId === "string" &&
    seed.upload.sessionFileId.length > 0
  ) {
    return {
      composeMode: "editor",
      document: null,
      driveId: "",
      upload: {
        sessionId: seed.upload.sessionId,
        sessionFileId: seed.upload.sessionFileId,
        fileName: seed.upload.fileName ?? undefined,
      },
      activeWorkType: seed.activeWorkType,
    };
  }

  // Stored document (SPE pointer).
  if (typeof seed.speDriveItemId === "string" && seed.speDriveItemId.length > 0) {
    return {
      composeMode: "editor",
      document: {
        speDriveItemId: seed.speDriveItemId,
        sprkDocumentId: seed.sprkDocumentId,
        fileName: seed.fileName ?? undefined,
      },
      driveId: seed.speDriveId ?? "",
      activeWorkType: seed.activeWorkType,
    };
  }

  return null;
}

// ---------------------------------------------------------------------------
// ComposeDirectMount — reads the (provided or ambient) launch context + maps to
// ComposeWorkspace props. Mirror of ComposeSectionMount in composeEditor.registration.ts,
// with bffBaseUrl sourced from useAiSession() (no SectionFactoryContext here).
// ---------------------------------------------------------------------------

interface ComposeDirectMountProps {
  /** spaarkeai-compose-r2 (multi-Compose-tab): the workspace tab id this editor is mounted in. */
  workspaceTabId?: string;
  /** spaarkeai-compose-r2 (multi-Compose-tab): whether this tab is the active (visible) tab. */
  isActiveTab?: boolean;
  /**
   * agreements-r1 task 033 (FR-17): the wizard-minted ANALYSIS-OWNED session to open the stored
   * document ON (`ComposeWidgetSeed.composeSessionId`). Threaded as `<ComposeWorkspace
   * initialSessionId>` so the BFF Load's FR-29/FR-33 resume path (`?sessionId=` +
   * `IsSameCrossVersionBinding` on the document GUID) RESUMES it as the document session — chat
   * session ≡ document session, the same coincidence the upload-mount door has by construction.
   * Absent for every pre-existing seed → `""` (the exact prior wire shape — server mints as before).
   */
  initialSessionId?: string;
}

const ComposeDirectMount: React.FC<ComposeDirectMountProps> = ({
  workspaceTabId,
  isActiveTab = true,
  initialSessionId,
}) => {
  const { bffBaseUrl } = useAiSession();
  const composeLaunch = useComposeLaunch();
  // FR-13: forward the Assistant serial-dispatch queue ONLY when the bridge is
  // present AND a host dispatcher is registered — else omit so the inline AI
  // toolbar falls back to its own dispatcher.
  const bridge = useComposeActionBridge();
  const tenantId = resolveTenantIdSync();

  // Activate the inline AI toolbar (reads GET /api/ai/capabilities?surface=compose).
  useComposeToolbarActivation({ bffBaseUrl });

  // FR-05 create-on-save: resolve the user's BU SPE container. UAT-11 (2026-08-18): the resolution
  // is now a REUSABLE function returning a discriminated outcome, so a transient-create Save can RETRY
  // it (below, threaded to ComposeWorkspace `resolveContainer`) instead of the mount-only one-shot that
  // left `containerId` undefined — and a dishonest "no container configured" save error — whenever Xrm
  // wasn't ready yet, a transient 401, or a Dataverse query fault hit at mount time.
  const resolveContainer = React.useCallback(async (): Promise<{
    containerId?: string;
    outcome: "resolved" | "no-container" | "unavailable";
  }> => {
    try {
      // The SpaarkeAi code page runs in an iframe where Xrm lives on the PARENT/TOP window, not the
      // iframe's own globalThis — use the SAME fallback every other SpaarkeAi Xrm consumer uses
      // (WorkspacePane, ManageWorkspacesPane, usePlaybookOptions, main.tsx).
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      const xrm = w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm;
      const rawUserId: string | undefined = xrm?.Utility?.getGlobalContext?.().userSettings?.userId;
      const webApi = xrm?.WebApi;
      if (!rawUserId || !webApi) return { outcome: "unavailable" }; // no Dataverse host / not ready
      const userId = cleanGuid(rawUserId);
      const defaults = await EntityCreationService.resolveUserBuDefaults(webApi, userId);
      // A completed query with no container id = the BU genuinely has none; a container id = resolved.
      return defaults.containerId
        ? { containerId: defaults.containerId, outcome: "resolved" }
        : { outcome: "no-container" };
    } catch (err) {
      // eslint-disable-next-line no-console
      console.warn("[ComposeDirectWidget] BU container resolution failed:", err);
      return { outcome: "unavailable" }; // transient — the save-path retry can recover it
    }
  }, []);

  const [containerId, setContainerId] = React.useState<string | undefined>(undefined);
  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      const result = await resolveContainer();
      if (!cancelled && result.containerId) setContainerId(result.containerId);
    })();
    return () => {
      cancelled = true;
    };
  }, [resolveContainer]);

  return React.createElement(ComposeWorkspace, {
    bffBaseUrl,
    driveId: composeLaunch?.driveId ?? "",
    tenantId,
    containerId,
    resolveContainer,
    onCreateOnSaveComplete: composeLaunch?.onCreateOnSaveComplete,
    initialDocumentRef: composeLaunch?.document ?? null,
    initialUploadRef: composeLaunch?.upload ?? null,
    initialDraftRef: composeLaunch?.draft ?? null,
    // task 033 (FR-17): resume the wizard-minted Analysis-owned session when the seed carries one
    // (see ComposeDirectMountProps.initialSessionId); "" preserves the exact pre-033 wire shape.
    initialSessionId: initialSessionId ?? "",
    enqueueComposeAction: bridge?.hasDispatcher ? bridge.enqueue : undefined,
    // spaarkeai-compose-r2 (multi-Compose-tab): thread the tab id + active flag so this editor
    // tab-scopes its active-document registration and only the ACTIVE tab claims the session doc.
    workspaceTabId,
    isActiveTab,
    // ai-advanced-capabilities-analysis-hub-r1 task 041 (FR-13): forward the launch's active
    // work type so getToolsForSurface scopes the inline AI toolbar (e.g. Agreement Review).
    activeWorkType: composeLaunch?.activeWorkType,
  });
};

// ---------------------------------------------------------------------------
// ComposeDirectWidget — the WorkspaceWidgetComponent-shaped default export.
// ---------------------------------------------------------------------------

/**
 * Direct workspace widget for `widgetType: 'compose'`. Wraps `ComposeWorkspace`
 * so it can be resolved by `WorkspaceWidgetRegistry` and mounted as a workspace
 * tab. When `data.compose` carries a seed, a per-tab `ComposeLaunchContext`
 * provider is installed so the editor opens on that document; otherwise the
 * ambient launch context (if any) or the Compose empty state is used.
 */
export const ComposeDirectWidget: React.FC<WorkspaceWidgetProps<ComposeWidgetData>> = ({
  data,
  tabId,
  isActiveTab,
}) => {
  const styles = useStyles();
  const launch = buildLaunchFromSeed(data?.compose);
  const mount = React.createElement(ComposeDirectMount, {
    workspaceTabId: tabId,
    isActiveTab,
    // task 033 (FR-17): the wizard hand-off's Analysis-owned session (absent on every other seed).
    initialSessionId: data?.compose?.composeSessionId,
  });
  const inner = launch
    ? React.createElement(ComposeLaunchContext.Provider, { value: launch }, mount)
    : mount;
  // FIX #6 — wrap in the bounded flex-column host so the editor's scroll region resolves and the
  // sticky format toolbar stays pinned under the Direct mount (see the useStyles comment above).
  return React.createElement("div", { className: styles.host }, inner);
};

ComposeDirectWidget.displayName = "ComposeDirectWidget";

export default ComposeDirectWidget;
