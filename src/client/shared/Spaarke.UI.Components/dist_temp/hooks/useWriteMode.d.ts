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
/** Presentation mode for AI-generated content. */
export type WriteMode = 'stream' | 'diff';
/** Source of the current write mode decision. */
export type WriteModeSource = 'auto' | 'user-override';
/**
 * Type of AI operation, used for automatic mode resolution.
 *
 * - `addition` - New content generation (e.g., "write a new section about X")
 * - `revision` - Rewriting existing content (e.g., "rewrite this to be concise")
 * - `selection-revision` - Revising user-selected text (highlight-refine)
 * - `reanalysis` - Full document re-analysis with complete replacement
 * - `unknown` - Unrecognized operation (defaults to stream)
 */
export type OperationType = 'addition' | 'revision' | 'selection-revision' | 'reanalysis' | 'unknown';
/** Options for the useWriteMode hook. */
export interface UseWriteModeOptions {
    /** Default write mode when no auto-detection or override is active. Defaults to "stream". */
    defaultMode?: WriteMode;
    /** Chat session ID. Used to key sessionStorage persistence of user overrides. */
    sessionId?: string;
}
/** Return type for the useWriteMode hook. */
export interface UseWriteModeResult {
    /** Current write mode. Reflects user override if set, otherwise the default. */
    mode: WriteMode;
    /** Whether the current mode was determined automatically or by user override. */
    source: WriteModeSource;
    /**
     * Set a user override for write mode. Persists to sessionStorage for the
     * current session. Subsequent calls to resolveMode() will return this
     * override until resetToAuto() is called.
     */
    setMode: (mode: WriteMode) => void;
    /** Clear the user override and revert to automatic detection. */
    resetToAuto: () => void;
    /**
     * Resolve the write mode for a given operation type.
     * Returns the user override if set, otherwise auto-detects from the
     * operation type.
     *
     * @param operationType - The type of AI operation being performed
     * @returns The write mode to use for this operation
     */
    resolveMode: (operationType: OperationType) => WriteMode;
}
/**
 * Command definition for action menu integration.
 * These definitions describe the /mode commands that can be registered
 * with the action menu system.
 */
export interface WriteModeCommand {
    /** Command identifier (e.g., "mode_stream") */
    id: string;
    /** Display label in the action menu */
    label: string;
    /** Description shown in the action menu */
    description: string;
    /** The slash command text that triggers this command */
    command: string;
    /** Category for action menu grouping */
    category: 'settings';
}
/**
 * Command definitions for /mode action menu integration.
 *
 * Register these with the action menu system to enable user-facing
 * write mode controls:
 * - `/mode stream` - Force streaming mode
 * - `/mode diff` - Force diff review mode
 * - `/mode auto` - Revert to automatic detection
 */
export declare const WRITE_MODE_COMMANDS: ReadonlyArray<WriteModeCommand>;
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
export declare function resolveAutoMode(operationType: OperationType): WriteMode;
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
export declare function useWriteMode(options?: UseWriteModeOptions): UseWriteModeResult;
//# sourceMappingURL=useWriteMode.d.ts.map