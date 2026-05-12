/**
 * useWriteMode - Automatic write mode selection (Stream vs Diff)
 *
 * Determines the appropriate presentation mode for AI-generated content based
 * on the type of operation being performed:
 *
 * - **Stream mode**: Tokens stream directly into the editor in real-time.
 *   Best for additions, expansions, and full re-analysis.
 * - **Diff mode**: The complete proposed revision is collected, then displayed
 *   in a DiffCompareView for Accept/Reject. Best for revisions and
 *   selection-based rewrites.
 *
 * The hook supports user override via /mode commands in the action menu.
 * Override persists for the session via sessionStorage (keyed by sessionId).
 *
 * @see ADR-012 - Shared Component Library (hook in @spaarke/ui-components)
 * @see Task 102 - Automatic Write Mode Selection
 *
 * @example
 * ```tsx
 * const { mode, source, setMode, resetToAuto, resolveMode } = useWriteMode({
 *   sessionId: session?.sessionId,
 * });
 *
 * // Resolve mode for an incoming AI operation:
 * const activeMode = resolveMode("revision"); // "diff" (auto) or user override
 *
 * // User picks /mode stream from action menu:
 * setMode("stream"); // persists for session
 *
 * // User picks /mode auto:
 * resetToAuto(); // clears override, reverts to automatic detection
 * ```
 */
import { useState, useCallback, useRef } from 'react';
/**
 * Command definitions for /mode action menu integration.
 *
 * Register these with the action menu system to enable user-facing
 * write mode controls:
 * - `/mode stream` - Force streaming mode
 * - `/mode diff` - Force diff review mode
 * - `/mode auto` - Revert to automatic detection
 */
export const WRITE_MODE_COMMANDS = [
    {
        id: 'mode_stream',
        label: 'Mode: Stream',
        description: 'Stream AI output directly into the editor in real-time',
        command: '/mode stream',
        category: 'settings',
    },
    {
        id: 'mode_diff',
        label: 'Mode: Diff Review',
        description: 'Show AI revisions in a side-by-side diff view for review',
        command: '/mode diff',
        category: 'settings',
    },
    {
        id: 'mode_auto',
        label: 'Mode: Auto',
        description: 'Automatically choose stream or diff based on the operation type',
        command: '/mode auto',
        category: 'settings',
    },
];
// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
/** sessionStorage key prefix for persisting user write mode overrides. */
const STORAGE_KEY_PREFIX = 'sprk-write-mode-override';
// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
/**
 * Build a sessionStorage key scoped to the given session ID.
 * Falls back to a global key when no session ID is provided.
 */
function buildStorageKey(sessionId) {
    return sessionId ? `${STORAGE_KEY_PREFIX}:${sessionId}` : STORAGE_KEY_PREFIX;
}
/**
 * Read the persisted user override from sessionStorage.
 * Returns null if no override is stored or sessionStorage is unavailable.
 */
function readPersistedOverride(sessionId) {
    try {
        const key = buildStorageKey(sessionId);
        const stored = sessionStorage.getItem(key);
        if (stored === 'stream' || stored === 'diff') {
            return stored;
        }
    }
    catch {
        // sessionStorage may be unavailable in some environments (e.g., sandboxed iframes)
    }
    return null;
}
/**
 * Persist a user override to sessionStorage, or remove it when null.
 */
function persistOverride(sessionId, mode) {
    try {
        const key = buildStorageKey(sessionId);
        if (mode === null) {
            sessionStorage.removeItem(key);
        }
        else {
            sessionStorage.setItem(key, mode);
        }
    }
    catch {
        // sessionStorage may be unavailable — fail silently
    }
}
/**
 * Deterministic auto-detection: map operation type to write mode.
 *
 * | Operation Type      | Write Mode | Rationale                                |
 * |---------------------|------------|------------------------------------------|
 * | addition            | stream     | New content benefits from live feedback   |
 * | revision            | diff       | Replacement needs review before applying  |
 * | selection-revision  | diff       | Selected text revision needs comparison   |
 * | reanalysis          | stream     | Full document replace streams in place    |
 * | unknown             | stream     | Safe default for unrecognized operations  |
 */
export function resolveAutoMode(operationType) {
    switch (operationType) {
        case 'addition':
            return 'stream';
        case 'revision':
            return 'diff';
        case 'selection-revision':
            return 'diff';
        case 'reanalysis':
            return 'stream';
        case 'unknown':
        default:
            return 'stream';
    }
}
// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------
/**
 * Hook for automatic write mode selection with user override capability.
 *
 * On mount, checks sessionStorage for a persisted user override (keyed by sessionId).
 * If found, that override is applied. Otherwise, the hook starts in auto mode
 * where resolveMode() determines the mode from the operation type.
 *
 * @param options - Configuration including default mode and session ID
 * @returns Write mode state, setters, and resolveMode function
 *
 * @example
 * ```tsx
 * const { mode, source, setMode, resetToAuto, resolveMode } = useWriteMode({
 *   sessionId: "abc-123",
 * });
 *
 * // When an AI operation starts:
 * const writeMode = resolveMode("revision"); // "diff" unless user overrode
 *
 * // Handle /mode commands from action menu:
 * function handleModeCommand(commandId: string) {
 *   switch (commandId) {
 *     case "mode_stream": setMode("stream"); break;
 *     case "mode_diff": setMode("diff"); break;
 *     case "mode_auto": resetToAuto(); break;
 *   }
 * }
 * ```
 */
export function useWriteMode(options = {}) {
    const { defaultMode = 'stream', sessionId } = options;
    // Read persisted override on initial render only
    const [userOverride, setUserOverride] = useState(() => {
        return readPersistedOverride(sessionId);
    });
    // Track the sessionId so we can detect changes
    const prevSessionIdRef = useRef(sessionId);
    // If sessionId changes, re-read the persisted override for the new session.
    // This handles the case where the user switches sessions within the same
    // component lifecycle.
    if (prevSessionIdRef.current !== sessionId) {
        prevSessionIdRef.current = sessionId;
        const newOverride = readPersistedOverride(sessionId);
        if (newOverride !== userOverride) {
            setUserOverride(newOverride);
        }
    }
    // Derive the current mode and source
    const mode = userOverride ?? defaultMode;
    const source = userOverride !== null ? 'user-override' : 'auto';
    /**
     * Set a user override for write mode.
     * Persists to sessionStorage for cross-render continuity within the session.
     */
    const setMode = useCallback((newMode) => {
        setUserOverride(newMode);
        persistOverride(sessionId, newMode);
    }, [sessionId]);
    /**
     * Clear the user override and revert to automatic detection.
     * Removes the persisted value from sessionStorage.
     */
    const resetToAuto = useCallback(() => {
        setUserOverride(null);
        persistOverride(sessionId, null);
    }, [sessionId]);
    /**
     * Resolve the write mode for a specific operation type.
     * Returns the user override if set, otherwise auto-detects from the
     * operation type using deterministic mapping.
     */
    const resolveMode = useCallback((operationType) => {
        if (userOverride !== null) {
            return userOverride;
        }
        return resolveAutoMode(operationType);
    }, [userOverride]);
    return {
        mode,
        source,
        setMode,
        resetToAuto,
        resolveMode,
    };
}
//# sourceMappingURL=useWriteMode.js.map