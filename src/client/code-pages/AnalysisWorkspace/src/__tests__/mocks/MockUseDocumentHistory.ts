/**
 * Mock module for @spaarke/ui-components/hooks/useDocumentHistory
 *
 * Returns a stable mock result for tests that don't exercise undo/redo history.
 */

export function useDocumentHistory(_editorRef: unknown) {
    return {
        undo: jest.fn(),
        redo: jest.fn(),
        pushVersion: jest.fn(),
        canUndo: false,
        canRedo: false,
        historyLength: 0,
    };
}
