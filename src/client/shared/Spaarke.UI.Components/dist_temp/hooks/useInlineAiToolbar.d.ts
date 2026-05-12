/**
 * useInlineAiToolbar - Selection-tracking hook for the InlineAiToolbar
 *
 * Monitors the document for text selection changes and computes the
 * position and visibility state for the InlineAiToolbar floating container.
 * Debounces at 200ms to avoid flooding during drag-select operations.
 *
 * Usage:
 * ```tsx
 * const editorContainerRef = useRef<HTMLDivElement>(null);
 * const { visible, position, selectedText, actions } = useInlineAiToolbar({
 *   editorContainerRef,
 * });
 *
 * return (
 *   <>
 *     <div ref={editorContainerRef}>...</div>
 *     <InlineAiToolbar
 *       visible={visible}
 *       position={position}
 *       actions={actions}
 *       onAction={handleAction}
 *     />
 *   </>
 * );
 * ```
 *
 * Constraints:
 * - MUST NOT import Xrm or ComponentFramework (ADR-012)
 * - Selection detection uses only the browser Selection API and React refs
 * - Debounce MUST be ≤ 200ms (spec-NFR-01)
 *
 * @see InlineAiToolbar component
 * @see useInlineAiActions — companion hook for dispatching actions
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 */
import { type InlineAiAction } from '../components/InlineAiToolbar/inlineAiToolbar.types';
export interface UseInlineAiToolbarOptions {
    /**
     * Ref to the container element that hosts the editable content.
     * The toolbar will only appear when the selection is within this element.
     */
    editorContainerRef: React.RefObject<HTMLElement | null>;
    /**
     * Override the default set of inline actions shown in the toolbar.
     * Defaults to DEFAULT_INLINE_ACTIONS from inlineAiToolbar.types.
     */
    actions?: InlineAiAction[];
}
export interface UseInlineAiToolbarResult {
    /** Whether the toolbar should be visible */
    visible: boolean;
    /** Absolute pixel position for the floating toolbar container */
    position: {
        top: number;
        left: number;
    };
    /** The text that was selected when the toolbar became visible */
    selectedText: string;
    /** The set of actions to render in the toolbar */
    actions: InlineAiAction[];
}
/**
 * Tracks text selection within an editor container and computes toolbar
 * visibility and positioning.
 *
 * Returns toolbar state: visible, position (top/left), selectedText, and the
 * action list. The toolbar is positioned above the selection bounding rect at
 * a fixed vertical offset.
 *
 * @param options - Configuration options
 * @returns Toolbar display state
 */
export declare function useInlineAiToolbar(options: UseInlineAiToolbarOptions): UseInlineAiToolbarResult;
//# sourceMappingURL=useInlineAiToolbar.d.ts.map