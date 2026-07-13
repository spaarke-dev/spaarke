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
 * NOTE (additive-coexistence, honest per plan §Wave 5 item 3): today NO
 * open-path dispatches `widget_load{ widgetType:'compose' }` — the layout path
 * remains the only mount door. This adapter makes the Direct registration a
 * genuine, mountable capability (so the registry entry is real, not a stub) and
 * confirms the Compose widget CAN receive what it needs on the Direct path when
 * the SpaarkeAi providers (`ComposeActionBridgeProvider`, auth/session) are in
 * the tree — it does NOT replace the layout mount.
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
    };
  }

  return null;
}

// ---------------------------------------------------------------------------
// ComposeDirectMount — reads the (provided or ambient) launch context + maps to
// ComposeWorkspace props. Mirror of ComposeSectionMount in composeEditor.registration.ts,
// with bffBaseUrl sourced from useAiSession() (no SectionFactoryContext here).
// ---------------------------------------------------------------------------

const ComposeDirectMount: React.FC = () => {
  const { bffBaseUrl } = useAiSession();
  const composeLaunch = useComposeLaunch();
  // FR-13: forward the Assistant serial-dispatch queue ONLY when the bridge is
  // present AND a host dispatcher is registered — else omit so the inline AI
  // toolbar falls back to its own dispatcher.
  const bridge = useComposeActionBridge();
  const tenantId = resolveTenantIdSync();

  // Activate the inline AI toolbar (reads GET /api/ai/capabilities?surface=compose).
  useComposeToolbarActivation({ bffBaseUrl });

  // FR-05 create-on-save: resolve the user's BU SPE container once on mount.
  const [containerId, setContainerId] = React.useState<string | undefined>(undefined);
  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (globalThis as any).Xrm;
        const rawUserId: string | undefined =
          xrm?.Utility?.getGlobalContext?.().userSettings?.userId;
        const webApi = xrm?.WebApi;
        if (!rawUserId || !webApi) return; // no Dataverse host — create-on-save container unavailable
        const userId = cleanGuid(rawUserId);
        const defaults = await EntityCreationService.resolveUserBuDefaults(webApi, userId);
        if (!cancelled) setContainerId(defaults.containerId);
      } catch (err) {
        // eslint-disable-next-line no-console
        console.warn("[ComposeDirectWidget] BU container resolution failed:", err);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return React.createElement(ComposeWorkspace, {
    bffBaseUrl,
    driveId: composeLaunch?.driveId ?? "",
    tenantId,
    containerId,
    onCreateOnSaveComplete: composeLaunch?.onCreateOnSaveComplete,
    initialDocumentRef: composeLaunch?.document ?? null,
    initialUploadRef: composeLaunch?.upload ?? null,
    initialDraftRef: composeLaunch?.draft ?? null,
    initialSessionId: "",
    enqueueComposeAction: bridge?.hasDispatcher ? bridge.enqueue : undefined,
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
}) => {
  const styles = useStyles();
  const launch = buildLaunchFromSeed(data?.compose);
  const mount = React.createElement(ComposeDirectMount);
  const inner = launch
    ? React.createElement(ComposeLaunchContext.Provider, { value: launch }, mount)
    : mount;
  // FIX #6 — wrap in the bounded flex-column host so the editor's scroll region resolves and the
  // sticky format toolbar stays pinned under the Direct mount (see the useStyles comment above).
  return React.createElement("div", { className: styles.host }, inner);
};

ComposeDirectWidget.displayName = "ComposeDirectWidget";

export default ComposeDirectWidget;
