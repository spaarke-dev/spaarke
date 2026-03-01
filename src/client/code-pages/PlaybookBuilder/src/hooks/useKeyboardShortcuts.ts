/**
 * useKeyboardShortcuts — Keyboard shortcut handler for the Playbook Builder.
 *
 * Shortcuts:
 *   Ctrl+S         — Save playbook
 *   Delete/Backspace — Delete selected node
 *   Escape         — Deselect node
 *   Ctrl+A         — Toggle AI assistant
 */

import { useEffect, useCallback } from "react";
import { useCanvasStore } from "../stores/canvasStore";
import { useAiAssistantStore } from "../stores/aiAssistantStore";

interface UseKeyboardShortcutsOptions {
    onSave: () => void;
}

export function useKeyboardShortcuts({ onSave }: UseKeyboardShortcutsOptions): void {
    const selectedNodeId = useCanvasStore((s) => s.selectedNodeId);
    const removeNode = useCanvasStore((s) => s.removeNode);
    const selectNode = useCanvasStore((s) => s.selectNode);
    const nodes = useCanvasStore((s) => s.nodes);
    const isAiModalOpen = useAiAssistantStore((s) => s.isModalOpen);
    const openAiModal = useAiAssistantStore((s) => s.openModal);
    const closeAiModal = useAiAssistantStore((s) => s.closeModal);

    const handleKeyDown = useCallback(
        (event: KeyboardEvent) => {
            const target = event.target as HTMLElement;
            const isInputFocused =
                target.tagName === "INPUT" ||
                target.tagName === "TEXTAREA" ||
                target.tagName === "SELECT" ||
                target.isContentEditable;

            // Ctrl+S — Save (always active)
            if ((event.ctrlKey || event.metaKey) && event.key === "s") {
                event.preventDefault();
                onSave();
                return;
            }

            // Ctrl+A — Toggle AI assistant (when not in input)
            if ((event.ctrlKey || event.metaKey) && event.key === "a" && !isInputFocused) {
                event.preventDefault();
                if (isAiModalOpen) {
                    closeAiModal();
                } else {
                    openAiModal();
                }
                return;
            }

            // Skip remaining shortcuts when focused on input elements
            if (isInputFocused) return;

            // Delete/Backspace — Delete selected node (not start node)
            if (
                (event.key === "Delete" || event.key === "Backspace") &&
                selectedNodeId
            ) {
                const selectedNode = nodes.find((n) => n.id === selectedNodeId);
                if (selectedNode && selectedNode.data.type !== "start") {
                    event.preventDefault();
                    removeNode(selectedNodeId);
                }
                return;
            }

            // Escape — Deselect
            if (event.key === "Escape") {
                if (selectedNodeId) {
                    selectNode(null);
                }
                return;
            }
        },
        [
            onSave,
            selectedNodeId,
            removeNode,
            selectNode,
            nodes,
            isAiModalOpen,
            openAiModal,
            closeAiModal,
        ],
    );

    useEffect(() => {
        window.addEventListener("keydown", handleKeyDown);
        return () => window.removeEventListener("keydown", handleKeyDown);
    }, [handleKeyDown]);
}
