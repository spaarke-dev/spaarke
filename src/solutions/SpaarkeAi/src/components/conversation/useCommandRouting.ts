/**
 * useCommandRouting — R6 Pillar 8 Command Router integration (hard slashes +
 * reference resolution at the outbound-body seam), extracted from
 * ConversationPane.tsx by ai-architecture-redesign-r1 task 045 (FR-P3-06
 * thin-host decomposition).
 *
 * `handleDecorateOutboundBody` is the SINGLE seam through which the R6 tasks
 * 081 (hard slashes) and 083 (references) dispatch: it runs INSIDE SprkChat's
 * handleSend, between body construction and stream start. Hard slashes
 * execute client-side and return null → cancel the BFF send. References
 * attach `resolvedReferences`. Natural-language input passes through
 * unchanged (NFR-11).
 *
 * FR-P2-05 hard cutover (task 034): soft slashes carry NO intent-bias wire
 * field — soft-slash text enters the agent-turn loop like any NL utterance.
 * Turning the closed soft-slash vocabulary into Click-path deterministic
 * launchers (dispatchConsumer by binding id) requires a capability-discovery
 * READ endpoint mapping slash → Binding GUID per environment (client-hardcoded
 * GUIDs are not portable; ADR-039 keeps resolution vocabulary server-owned).
 * That server surface does not exist as of task 045 — disposition recorded in
 * the task-045 report (client-only task; server wire-contract changes are
 * out of boundary).
 */

import * as React from "react";
import { usePaneEventBus } from "@spaarke/ai-widgets/events";
import type { IChatMessage } from "@spaarke/ui-components";
import { buildBffApiUrl } from "@spaarke/auth";
import { parse as parseCommandIntent } from "./CommandRouter";
import {
  executeHardSlash,
  defaultTelemetrySink,
  defaultDownloadBlob,
  type ExecutorContext as HardSlashExecutorContext,
  type ConversationMessage as HardSlashConversationMessage,
} from "./HardSlashExecutor";
import ReferenceResolver, {
  createScopeFetch,
  createFileLookupFromSessionMap,
  type ResolverContext,
} from "./ReferenceResolver";

export interface CommandRoutingDeps {
  bffBaseUrl: string;
  authenticatedFetch: (input: string, init?: RequestInit) => Promise<Response>;
  chatSessionId: string | null;
  setChatSessionId: (id: string) => void;
  /** Active entity context (matter scoping + resolver context). */
  entityContext: {
    entityType: string;
    entityId: string;
    entityName?: string | null;
    matterId?: string | null;
  } | null;
  /** Single-slot Assistant-message injection (hard-slash result lines). */
  inject: (message: IChatMessage) => void;
  /** Playbook Library opener (`/playbooks` hard slash, browse mode). */
  openLibraryModal: (sessionAttachmentIds: string[]) => void;
}

export interface CommandRoutingController {
  helpPanelOpen: boolean;
  setHelpPanelOpen: (open: boolean) => void;
  /** SprkChat remount key — `/clear` increments to wipe the message list. */
  sprkChatRemountKey: number;
  /**
   * Header "New session" affordance (G-P3 UAT round-4 R4-5, 2026-07-07):
   * remounts SprkChat by bumping the remount key. The caller clears the
   * persisted chat-session id FIRST (`clearChatSession()` from
   * `useAiSession()`), so the remounted SprkChat mounts with
   * `sessionId=undefined` and mints a fresh session — the same recovery leg
   * the session-stale path uses. Scoped to the conversation pane by design
   * (does NOT dispatch `session_reset` — workspace tabs and context survive).
   */
  startNewSession: () => void;
  handleDecorateOutboundBody: (
    body: Record<string, unknown>
  ) => Promise<Record<string, unknown> | null>;
  /** SprkChat `onMessagesChange` — keeps the `/export` transcript ref current. */
  noteMessagesChanged: (messages: IChatMessage[]) => void;
  /** Workspace `tab_change` tracking — `/pin` reads the focused tab id. */
  noteTabFocus: (tabId: string | null) => void;
}

export function useCommandRouting(deps: CommandRoutingDeps): CommandRoutingController {
  const {
    bffBaseUrl,
    authenticatedFetch,
    chatSessionId,
    setChatSessionId,
    entityContext,
    inject,
    openLibraryModal,
  } = deps;

  const [helpPanelOpen, setHelpPanelOpen] = React.useState<boolean>(false);
  // R6 hotfix 2026-06-19 (UAT): `/clear` remounts SprkChat to wipe its
  // internal message list (the BFF session DELETE clears server-side state).
  const [sprkChatRemountKey, setSprkChatRemountKey] = React.useState<number>(0);

  // Render-free refs: `/export` transcript + `/pin` focused-tab tracking.
  const messagesRef = React.useRef<IChatMessage[]>([]);
  const focusedTabIdRef = React.useRef<string | null>(null);

  const noteMessagesChanged = React.useCallback((messages: IChatMessage[]): void => {
    messagesRef.current = messages;
  }, []);

  const noteTabFocus = React.useCallback((tabId: string | null): void => {
    focusedTabIdRef.current = tabId;
  }, []);

  // R6 task 081: HardSlashExecutor needs the full bus instance to dispatch on
  // multiple channels (public-barrel seam).
  const paneEventBus = usePaneEventBus();

  const hardSlashContext = React.useMemo<HardSlashExecutorContext>(
    () => ({
      bffBaseUrl,
      authenticatedFetch,
      sessionId: chatSessionId ?? "",
      paneEventBus,
      setHelpOpen: setHelpPanelOpen,
      clearLocalConversation: () => {
        setSprkChatRemountKey((k) => k + 1);
      },
      createNewSession: async (): Promise<string | null> => {
        // R6 closeout (task 097): POST /api/ai/chat/sessions (empty body) to
        // mint a fresh session; push the id into AiSessionProvider so the
        // remounted SprkChat continues with it.
        try {
          const url = buildBffApiUrl(bffBaseUrl, "/api/ai/chat/sessions");
          const response = await authenticatedFetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({}),
          });
          if (!response.ok) return null;
          const json = (await response.json()) as { sessionId?: string };
          const newId =
            typeof json?.sessionId === "string" && json.sessionId.length > 0
              ? json.sessionId
              : null;
          if (newId !== null) {
            setChatSessionId(newId);
          }
          return newId;
        } catch {
          return null;
        }
      },
      // R6 task 097b — `/export` transcript snapshot (user + assistant only).
      getConversationHistory: (): HardSlashConversationMessage[] =>
        messagesRef.current
          .filter((m) => m.role === "User" || m.role === "Assistant")
          .map((m) => ({
            role: m.role === "User" ? "user" : "assistant",
            content: m.content,
            timestamp: m.timestamp,
          })),
      // R6 task 097c — most-recently-focused workspace tab id (`/pin`).
      getFocusedTabId: (): string | null => focusedTabIdRef.current,
      activeMatterId: entityContext?.matterId ?? null,
      downloadBlob: defaultDownloadBlob,
      // R7 task 094 / FR-18 — `/playbooks` opens the Library (browse mode,
      // no pre-filter per task 093 audit Q6).
      openLibraryModal: (): void => {
        openLibraryModal([]);
      },
      telemetry: defaultTelemetrySink,
    }),
    [
      bffBaseUrl,
      authenticatedFetch,
      chatSessionId,
      entityContext,
      paneEventBus,
      setChatSessionId,
      openLibraryModal,
    ]
  );

  const referenceResolverContext = React.useMemo<ResolverContext>(
    () => ({
      // TODO(task 084): thread real tenantId once host exposes it; empty
      // string turns OFF the resolver's caching (degraded) but resolution works.
      tenantId: "",
      sessionId: chatSessionId ?? "",
      entityContext: entityContext
        ? {
            entityType: entityContext.entityType,
            entityId: entityContext.entityId,
            displayName: entityContext.entityName ?? entityContext.entityType,
          }
        : undefined,
      openTabs: [],
      scopeFetch: createScopeFetch(bffBaseUrl, authenticatedFetch),
      fileLookup: createFileLookupFromSessionMap(new Map()),
    }),
    [bffBaseUrl, authenticatedFetch, chatSessionId, entityContext]
  );

  const handleDecorateOutboundBody = React.useCallback(
    async (body: Record<string, unknown>): Promise<Record<string, unknown> | null> => {
      const msg = typeof body.message === "string" ? body.message : "";
      const intent = parseCommandIntent(msg);

      if (intent.isHardSlash) {
        try {
          const result = await executeHardSlash(intent, hardSlashContext);
          if (result.message) {
            inject({
              role: "Assistant",
              content: result.message,
              timestamp: new Date().toISOString(),
            });
          }
        } catch (err) {
          console.error("[R6 Pillar 8] HardSlashExecutor failed:", err);
        }
        return null;
      }

      // Soft slashes pass through undecorated (FR-P2-05 hard cutover) — the
      // utterance enters the agent-turn loop.
      let decorated: Record<string, unknown> = body;

      if (intent.references.length > 0) {
        try {
          const resolved = await ReferenceResolver.resolveAll(
            intent.references,
            referenceResolverContext
          );
          decorated = { ...decorated, resolvedReferences: resolved };
        } catch (err) {
          console.error("[R6 Pillar 8] ReferenceResolver failed:", err);
        }
      }

      return decorated;
    },
    [hardSlashContext, referenceResolverContext, inject]
  );

  // R4-5 (2026-07-07): header "New session" — remount SprkChat so a cleared
  // persisted id resolves to a brand-new session on mount.
  const startNewSession = React.useCallback((): void => {
    setSprkChatRemountKey((k) => k + 1);
  }, []);

  // Stable controller identity (Step 9.5 review) — changes only with state.
  return React.useMemo(
    () => ({
      helpPanelOpen,
      setHelpPanelOpen,
      sprkChatRemountKey,
      startNewSession,
      handleDecorateOutboundBody,
      noteMessagesChanged,
      noteTabFocus,
    }),
    [
      helpPanelOpen,
      setHelpPanelOpen,
      sprkChatRemountKey,
      startNewSession,
      handleDecorateOutboundBody,
      noteMessagesChanged,
      noteTabFocus,
    ]
  );
}
