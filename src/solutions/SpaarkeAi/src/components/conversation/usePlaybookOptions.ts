/**
 * usePlaybookOptions — chat-routing-redesign-r1 task 117b playbook_options
 * handlers (FR-49/50/51), extracted from ConversationPane.tsx by
 * ai-architecture-redesign-r1 task 045 (FR-P3-06 thin-host decomposition).
 *
 * On a `playbook_options` SSE event the host appends a structured Assistant
 * message (SprkChatMessageRenderer renders candidate link buttons + an Open
 * Library link). Candidate clicks POST `/api/ai/playbook-dispatch/execute`;
 * the Library link opens the `sprk_playbooklibrary` Code Page modal via
 * Xrm.Navigation (frame-walk resolution).
 *
 * NOTE (task 045 survey): the server-side `playbook_options` emitter was
 * retired with the dispatcher stack (task 035 / FR-P2-06) and the
 * `/playbook-dispatch/execute` endpoint was never implemented — this client
 * leg is currently dormant wiring kept for the SprkChat prop contract.
 * Flagged as a Track-B / FR-P4-01 deletion candidate in the task-045 notes;
 * NOT deleted here (046/Track-B own the widget/SSE-surface pruning).
 *
 * ADR-015: log counts/booleans only. ADR-028: `authenticatedFetch` only.
 */

import * as React from "react";
import type { IChatMessage } from "@spaarke/ui-components";

export interface PlaybookOptionsPayloadShape {
  candidates: Array<{
    playbookId: string;
    playbookCode: string;
    displayName: string;
    confidence: number;
    reason: string;
  }>;
  libraryModalCta: boolean;
  sessionAttachmentIds: string[];
  rerankInvoked: boolean;
  rerankReason?: string | null;
}

export interface PlaybookOptionsDeps {
  bffBaseUrl: string;
  authenticatedFetch: (input: string, init?: RequestInit) => Promise<Response>;
  chatSessionId: string | null;
  /** Single-slot Assistant-message injection. */
  inject: (message: IChatMessage) => void;
  /** Most recent outbound user message (dispatcher `originalMessage`). */
  getLastSentMessage: () => string;
}

export interface PlaybookOptionsHandlers {
  handlePlaybookOptions: (payload: PlaybookOptionsPayloadShape) => void;
  handleSelectPlaybook: (playbookId: string, sessionAttachmentIds: string[]) => void;
  handleOpenLibraryModal: (sessionAttachmentIds: string[]) => void;
}

export function usePlaybookOptions(deps: PlaybookOptionsDeps): PlaybookOptionsHandlers {
  const { bffBaseUrl, authenticatedFetch, chatSessionId, inject, getLastSentMessage } = deps;

  const handlePlaybookOptions = React.useCallback(
    (payload: PlaybookOptionsPayloadShape): void => {
      // ADR-015 telemetry: ONLY counts + boolean signals — never payload contents.
      console.log(
        "[ConversationPane] playbook_options received — candidates:%d libraryModalCta:%s rerankInvoked:%s",
        payload.candidates.length,
        payload.libraryModalCta,
        payload.rerankInvoked
      );

      inject({
        role: "Assistant",
        // Fallback text in case the renderer falls back to markdown (defensive).
        content:
          payload.candidates.length > 0
            ? "Which playbook would you like me to use?"
            : "I couldn't find a confident match for your files.",
        timestamp: new Date().toISOString(),
        metadata: {
          responseType: "playbook_options",
          data: {
            candidates: payload.candidates,
            libraryModalCta: payload.libraryModalCta,
            sessionAttachmentIds: payload.sessionAttachmentIds,
            rerankInvoked: payload.rerankInvoked,
            rerankReason: payload.rerankReason ?? null,
          },
        },
      });
    },
    [inject]
  );

  const handleSelectPlaybook = React.useCallback(
    (playbookId: string, sessionAttachmentIds: string[]): void => {
      void (async () => {
        try {
          const url = `${bffBaseUrl.replace(/\/$/, "")}/api/ai/playbook-dispatch/execute`;
          const response = await authenticatedFetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              playbookId,
              sessionAttachmentIds,
              originalMessage: getLastSentMessage(),
              sessionId: chatSessionId ?? null,
            }),
          });
          if (!response.ok) {
            console.error(
              "[ConversationPane] playbook-dispatch failed — status:%d",
              response.status
            );
            inject({
              role: "Assistant",
              content:
                response.status === 404
                  ? "I'm not able to run that playbook yet — the dispatcher endpoint is still being wired up."
                  : "I couldn't start that playbook. Please try again.",
              timestamp: new Date().toISOString(),
            });
          }
        } catch (err) {
          // Log structurally only — error objects can leak headers/URLs.
          console.error(
            "[ConversationPane] playbook-dispatch threw:",
            err instanceof Error ? err.name : "unknown"
          );
        }
      })();
    },
    [authenticatedFetch, bffBaseUrl, chatSessionId, inject, getLastSentMessage]
  );

  /**
   * Open the `sprk_playbooklibrary` Code Page modal (target: 2). Follows the
   * proven Xrm frame-walk + navigateTo pattern (ADR-021/ADR-028 precedent).
   * Also invoked by the `/playbooks` hard slash (browse mode, empty ids).
   */
  const handleOpenLibraryModal = React.useCallback((sessionAttachmentIds: string[]): void => {
    let nav: { navigateTo?: (...args: unknown[]) => Promise<unknown> } | null = null;
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const w = window as any;
      const xrm = w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
      nav = xrm?.Navigation ?? null;
    } catch {
      nav = null;
    }

    if (!nav?.navigateTo) {
      console.warn(
        "[ConversationPane] Open Library: Xrm.Navigation unavailable — running outside Dataverse host."
      );
      return;
    }

    const parts: string[] = [];
    if (sessionAttachmentIds.length > 0) {
      parts.push(`sessionAttachmentIds=${encodeURIComponent(sessionAttachmentIds.join(","))}`);
    }
    const data = parts.join("&");

    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (nav.navigateTo as any)(
        {
          pageType: "webresource",
          webresourceName: "sprk_playbooklibrary",
          data,
        },
        {
          target: 2,
          width: { value: 85, unit: "%" },
          height: { value: 85, unit: "%" },
          title: "Playbook Library",
        }
      ).catch?.((err: unknown) => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const code = (err as any)?.errorCode;
        // errorCode 2 = user-cancelled (modal closed); ignore.
        if (code !== 2) {
          console.warn("[ConversationPane] Open Library: navigateTo error:", code ?? "unknown");
        }
      });
    } catch (err) {
      console.warn(
        "[ConversationPane] Open Library: navigateTo threw synchronously:",
        err instanceof Error ? err.name : "unknown"
      );
    }
  }, []);

  // Stable controller identity (Step 9.5 review).
  return React.useMemo(
    () => ({ handlePlaybookOptions, handleSelectPlaybook, handleOpenLibraryModal }),
    [handlePlaybookOptions, handleSelectPlaybook, handleOpenLibraryModal]
  );
}
