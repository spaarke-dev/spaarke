/**
 * usePlaybookSelection — cross-pane playbook selection state (AIPU2-102),
 * extracted from ConversationPane.tsx by ai-architecture-redesign-r1 task 045
 * (FR-P3-06 thin-host decomposition).
 *
 * PlaybookGalleryWidget dispatches `playbook-selected` on the `conversation`
 * PaneEventBus channel. This hook:
 *   1. Persists the playbookId to AiSessionProvider (sessionStorage-backed).
 *   2. Advances the shell stage: welcome → loading (Stage 1 → Stage 2).
 *   3. Tracks the active playbook name for the header strip.
 *   4. Shows a brief confirmation toast (auto-dismissed after 3 s).
 * Also handles legacy `playbook_change` (in-SprkChat switch) to keep session
 * state in sync, and the "Change playbook" reset (direct `reset()` + a
 * `session_reset` workspace-bus broadcast — both paths must agree).
 */

import * as React from "react";
import { usePaneEvent } from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";

/** Duration (ms) the confirmation toast strip is visible after selection. */
const TOAST_DURATION_MS = 3000;

export interface PlaybookSelectionDeps {
  setPlaybookId: (id: string) => void;
  /** Shell stage transition: welcome → loading. */
  toLoading: () => void;
  /** Shell stage reset (Stage 1 / gallery). */
  reset: () => void;
  /** `workspace`-channel PaneEventBus publisher (session_reset broadcast). */
  dispatch: (channel: "workspace", event: WorkspacePaneEvent) => void;
}

export interface PlaybookSelectionState {
  /** Playbook name for the header strip; null when none selected. */
  activePlaybookName: string | null;
  /** Toast strip name; null when hidden. */
  toastPlaybookName: string | null;
  /** "Change playbook" button handler — back to Stage 1. */
  handleChangePlaybook: () => void;
  /** SprkChat `onPlaybookChange` — persist in-chat playbook switches. */
  handlePlaybookChange: (newPlaybookId: string) => void;
}

export function usePlaybookSelection(deps: PlaybookSelectionDeps): PlaybookSelectionState {
  const { setPlaybookId, toLoading, reset, dispatch } = deps;

  const [activePlaybookName, setActivePlaybookName] = React.useState<string | null>(null);
  const [toastPlaybookName, setToastPlaybookName] = React.useState<string | null>(null);
  const toastTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  usePaneEvent("conversation", (event) => {
    if (event.type === "playbook-selected") {
      const { playbookId: newId, playbookName: newName } = event;
      if (!newId) return;

      setPlaybookId(newId);
      toLoading();
      setActivePlaybookName(newName ?? newId);

      if (toastTimerRef.current !== null) {
        clearTimeout(toastTimerRef.current);
      }
      setToastPlaybookName(newName ?? newId);
      toastTimerRef.current = setTimeout(() => {
        setToastPlaybookName(null);
        toastTimerRef.current = null;
      }, TOAST_DURATION_MS);
    } else if (event.type === "playbook_change") {
      // Legacy in-SprkChat playbook switch — keep session state in sync.
      if (event.playbookId) {
        setPlaybookId(event.playbookId);
        setActivePlaybookName(event.playbookName ?? event.playbookId);
      }
    }
  });

  // Cleanup toast timer on unmount.
  React.useEffect(() => {
    return () => {
      if (toastTimerRef.current !== null) {
        clearTimeout(toastTimerRef.current);
      }
    };
  }, []);

  const handleChangePlaybook = React.useCallback(() => {
    setActivePlaybookName(null);
    setToastPlaybookName(null);
    if (toastTimerRef.current !== null) {
      clearTimeout(toastTimerRef.current);
      toastTimerRef.current = null;
    }
    // Direct reset — updates ShellStageContext immediately; bus broadcast —
    // ShellStageManager's subscriber also resets SessionState (belt-and-braces).
    reset();
    dispatch("workspace", { type: "session_reset" });
  }, [reset, dispatch]);

  const handlePlaybookChange = React.useCallback(
    (newPlaybookId: string) => {
      setPlaybookId(newPlaybookId);
    },
    [setPlaybookId]
  );

  // Stable controller identity (Step 9.5 review) — changes only with state.
  return React.useMemo(
    () => ({ activePlaybookName, toastPlaybookName, handleChangePlaybook, handlePlaybookChange }),
    [activePlaybookName, toastPlaybookName, handleChangePlaybook, handlePlaybookChange]
  );
}
