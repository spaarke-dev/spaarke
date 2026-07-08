/**
 * useSelectionChip — "Refine this?" selection-chip state (AIPU2-101),
 * extracted from ConversationPane.tsx by ai-architecture-redesign-r1 task 045
 * (FR-P3-06 thin-host decomposition).
 *
 * Subscribes to the `workspace` PaneEventBus channel:
 *   - `selection_changed` — shows/clears the chip (null/empty text clears).
 *   - `tab_change`        — forwarded to the command-routing controller so
 *     the `/pin` hard slash knows the focused tab (render-free).
 *
 * Clicking the chip injects the selection as a refinement predefined prompt;
 * refinement prompts clear when a session is established.
 */

import * as React from "react";
import { usePaneEvent } from "@spaarke/ai-widgets";
import type { WorkspacePaneEvent } from "@spaarke/ai-widgets";

export interface SelectionChipState {
  /** Full text the user selected in the workspace widget. */
  selectedText: string;
  /** Human-readable origin label from the widget (e.g. "Document viewer"). */
  contextLabel: string;
}

export interface RefinementPrompt {
  key: string;
  label: string;
  prompt: string;
}

export interface SelectionChipDeps {
  /** Focused-tab tracking sink (useCommandRouting.noteTabFocus). */
  noteTabFocus: (tabId: string | null) => void;
}

export interface SelectionChipController {
  selectionChip: SelectionChipState | null;
  refinementPrompts: RefinementPrompt[];
  handleChipClick: () => void;
  handleChipDismiss: (e: React.MouseEvent) => void;
  /** Session-created reset — the suggestion chip is no longer needed. */
  clearRefinementPrompts: () => void;
}

export function useSelectionChip(deps: SelectionChipDeps): SelectionChipController {
  const { noteTabFocus } = deps;

  const [selectionChip, setSelectionChip] = React.useState<SelectionChipState | null>(null);
  const [refinementPrompts, setRefinementPrompts] = React.useState<RefinementPrompt[]>([]);

  usePaneEvent("workspace", (event: WorkspacePaneEvent): void => {
    if (event.type === "tab_change") {
      noteTabFocus(event.tabId ?? null);
      return;
    }

    if (event.type !== "selection_changed") return;

    if (event.selectedText == null || event.selectedText.length === 0) {
      // Selection cleared — hide the chip.
      setSelectionChip(null);
    } else {
      setSelectionChip({
        selectedText: event.selectedText,
        contextLabel: event.contextLabel ?? event.widgetType ?? "Workspace",
      });
    }
  });

  /**
   * Inject the selected text as a refinement context prompt, then dismiss the
   * chip (the predefined prompt chip in SprkChat now carries the selection).
   */
  const handleChipClick = React.useCallback((): void => {
    if (selectionChip === null) return;

    const { selectedText, contextLabel } = selectionChip;
    const truncated = selectedText.length > 80 ? `${selectedText.slice(0, 77)}…` : selectedText;

    setRefinementPrompts([
      {
        key: "refine-selection",
        label: `Refine: "${truncated}"`,
        prompt: `Refine this from ${contextLabel}: "${selectedText}"`,
      },
    ]);
    setSelectionChip(null);
  }, [selectionChip]);

  const handleChipDismiss = React.useCallback((e: React.MouseEvent): void => {
    e.stopPropagation();
    setSelectionChip(null);
  }, []);

  const clearRefinementPrompts = React.useCallback((): void => {
    setRefinementPrompts([]);
  }, []);

  // Stable controller identity (Step 9.5 review) — changes only with state.
  return React.useMemo(
    () => ({
      selectionChip,
      refinementPrompts,
      handleChipClick,
      handleChipDismiss,
      clearRefinementPrompts,
    }),
    [selectionChip, refinementPrompts, handleChipClick, handleChipDismiss, clearRefinementPrompts]
  );
}
